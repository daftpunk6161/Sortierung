using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Conversion;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

public sealed class CriticalPathRegressionTests : IDisposable
{
    private readonly string _root;
    private readonly string _stateRoot;

    public CriticalPathRegressionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.CriticalPaths", Guid.NewGuid().ToString("N"));
        _stateRoot = Path.Combine(Path.GetTempPath(), "Romulus.CriticalPaths.State", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_stateRoot);
    }

    public void Dispose()
    {
        TryDelete(_root);
        TryDelete(_stateRoot);
    }

    [Fact]
    public async Task CliAnalyzeSubcommand_DuplicateLibrary_EmitsCanonicalJsonAndDoesNotMoveSources()
    {
        var first = CreateFile("Analyze Game (USA).zip", "same-game-a");
        var second = CreateFile("Analyze Game (Europe).zip", "same-game-b");

        using var overrides = CreateCliPathOverrides();
        var (exit, stdout, _) = await ProgramTestRunner.RunSubcommandAsync(() =>
            InvokePrivateCliSubcommandAsync("SubcommandAnalyzeAsync", new CliRunOptions
            {
                Roots = [_root],
                Mode = RunConstants.ModeDryRun,
                EnableDat = false,
                EnableDatExplicit = true,
                Extensions = [".zip"],
                ExtensionsExplicit = true
            }));

        Assert.Equal(0, exit);
        Assert.True(File.Exists(first));
        Assert.True(File.Exists(second));
        Assert.False(Directory.Exists(Path.Combine(_root, RunConstants.WellKnownFolders.TrashRegionDedupe)));

        using var doc = JsonDocument.Parse(stdout);
        var json = doc.RootElement;
        Assert.True(json.TryGetProperty("healthScore", out var healthScore));
        Assert.InRange(healthScore.GetInt32(), 0, 100);
        Assert.True(json.TryGetProperty("totalFiles", out var totalFiles));
        Assert.Equal(2, totalFiles.GetInt32());
        Assert.True(json.TryGetProperty("duplicates", out var duplicates));
        Assert.True(duplicates.GetInt32() > 0);
        Assert.True(json.TryGetProperty("winners", out var winners));
        Assert.True(winners.GetInt32() > 0);
    }

    [Fact]
    public async Task CliExplainSubcommand_FilterUsesProjectedConsoleAndGameKey()
    {
        CreateFile("Explain Game (USA).zip", "same-game-a");
        CreateFile("Explain Game (Japan).zip", "same-game-b");

        using var overrides = CreateCliPathOverrides();
        var baseOptions = new CliRunOptions
        {
            Roots = [_root],
            Mode = RunConstants.ModeDryRun,
            EnableDat = false,
            EnableDatExplicit = true,
            Extensions = [".zip"],
            ExtensionsExplicit = true
        };

        var (exit, stdout, _) = await ProgramTestRunner.RunSubcommandAsync(() =>
            CliProgram.SubcommandExplainAsync(baseOptions));

        Assert.Equal(0, exit);
        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;
        var count = root.GetProperty("count").GetInt32();
        Assert.True(count > 0);

        var firstExplanation = root.GetProperty("explanations").EnumerateArray().First();
        var consoleKey = firstExplanation.GetProperty("consoleKey").GetString();
        var gameKey = firstExplanation.GetProperty("gameKey").GetString();
        Assert.False(string.IsNullOrWhiteSpace(consoleKey));
        Assert.False(string.IsNullOrWhiteSpace(gameKey));
        Assert.True(firstExplanation.GetProperty("scores").GetArrayLength() > 0);

        var filteredOptions = new CliRunOptions
        {
            Roots = baseOptions.Roots,
            Mode = baseOptions.Mode,
            EnableDat = baseOptions.EnableDat,
            EnableDatExplicit = baseOptions.EnableDatExplicit,
            Extensions = new HashSet<string>(baseOptions.Extensions, StringComparer.OrdinalIgnoreCase),
            ExtensionsExplicit = baseOptions.ExtensionsExplicit,
            ConsoleKey = consoleKey,
            GameKey = gameKey
        };
        var (filteredExit, filteredStdout, _) = await ProgramTestRunner.RunSubcommandAsync(() =>
            CliProgram.SubcommandExplainAsync(filteredOptions));

        Assert.Equal(0, filteredExit);
        using var filteredDoc = JsonDocument.Parse(filteredStdout);
        Assert.Equal(1, filteredDoc.RootElement.GetProperty("count").GetInt32());
        var filtered = filteredDoc.RootElement.GetProperty("explanations").EnumerateArray().Single();
        Assert.Equal(consoleKey, filtered.GetProperty("consoleKey").GetString());
        Assert.Equal(gameKey, filtered.GetProperty("gameKey").GetString());
    }

    [Fact]
    public void ConversionBatch_MoveLossyPlanWithoutToken_ThrowsBeforeExecutorRuns()
    {
        var source = CreateFile("lossy.iso", "source");
        var executor = new RecordingConversionExecutor();
        var converter = CreateLossyPlanningConverter(executor);
        var options = CreateMoveConversionOptions(acceptDataLossToken: null);

        var error = Assert.Throws<InvalidOperationException>(() =>
            ConversionPhaseHelper.ExecuteBatch(
                [new ConversionPhaseHelper.ConversionWorkItem(0, source, "PSP", false, false)],
                converter,
                options,
                CreatePipelineContext(options),
                "files",
                CancellationToken.None));

        Assert.Contains("AcceptDataLossToken", error.Message, StringComparison.Ordinal);
        Assert.Equal(0, executor.ExecuteCount);
        Assert.True(File.Exists(source));
        Assert.False(File.Exists(Path.ChangeExtension(source, ".cso")));
    }

    [Fact]
    public void ConversionBatch_MoveLossyPlanWithMatchingToken_ExecutesAndTrashesSourceAfterVerifiedOutput()
    {
        var source = CreateFile("accepted-lossy.iso", "source");
        var token = ConversionLossyTokenPolicy.ComputeAcceptDataLossToken(
        [
            new ConversionLossyPlanItem(source, "iso", "cso")
        ]);
        var executor = new RecordingConversionExecutor();
        var converter = CreateLossyPlanningConverter(executor);
        var options = CreateMoveConversionOptions(token);

        var result = ConversionPhaseHelper.ExecuteBatch(
            [new ConversionPhaseHelper.ConversionWorkItem(0, source, "PSP", false, false)],
            converter,
            options,
            CreatePipelineContext(options),
            "files",
            CancellationToken.None);

        var target = Path.ChangeExtension(source, ".cso");
        var trashedSource = Path.Combine(_root, RunConstants.WellKnownFolders.TrashConverted, Path.GetFileName(source));
        Assert.Equal(1, executor.ExecuteCount);
        Assert.Equal(1, result.Converted);
        Assert.Equal(0, result.Errors);
        Assert.True(File.Exists(target));
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(trashedSource));
    }

    [Fact]
    public void FileSystemAdapter_CopyFile_UnsafeDestinationsAreRejectedBeforeSourceIsTouched()
    {
        var source = CreateFile("copy-source.rom", "payload");
        var fs = new FileSystemAdapter();

        Assert.Throws<InvalidOperationException>(() =>
            fs.CopyFile(source, Path.Combine(_root, "copy-target.zip:evil"), overwrite: false));
        Assert.Throws<InvalidOperationException>(() =>
            fs.CopyFile(source, Path.Combine(_root, "NUL.zip"), overwrite: false));

        Assert.True(File.Exists(source));
        Assert.Equal("payload", File.ReadAllText(source));
        Assert.False(File.Exists(Path.Combine(_root, "copy-target.zip")));
    }

    [Fact]
    public void FileSystemAdapter_GetFilesSafe_PreCancelledTokenStopsBeforeScan()
    {
        CreateFile("cancel-a.zip", "a");
        var fs = new FileSystemAdapter();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            fs.GetFilesSafe(_root, [".zip"], cts.Token));
    }

    [Fact]
    public void DatRepository_LoadDatPayload_CacheHitDoesNotParseChangedDiskDat()
    {
        var datPath = Path.Combine(_root, "cached.dat");
        File.WriteAllText(datPath, """
        <?xml version="1.0"?>
        <datafile>
          <game name="DiskGame">
            <rom name="disk.rom" sha1="diskhash" />
          </game>
        </datafile>
        """);
        var cachedPayload = new CachedDatPayload
        {
            Games = new Dictionary<string, List<Dictionary<string, string>>>(StringComparer.OrdinalIgnoreCase)
            {
                ["CachedGame"] =
                [
                    new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["name"] = "cached.rom",
                        ["hash"] = "cachedhash",
                        ["hashType"] = "SHA1"
                    }
                ]
            }
        };
        var cache = new RecordingDatEntryCache(hitPayload: cachedPayload);
        var dat = new DatRepositoryAdapter(cache: cache);

        var payload = dat.LoadDatPayload(datPath, "SHA1");

        Assert.Same(cachedPayload, payload);
        Assert.Equal(1, cache.TryGetCount);
        Assert.Equal(0, cache.SetCount);
        Assert.True(payload.Games.ContainsKey("CachedGame"));
        Assert.False(payload.Games.ContainsKey("DiskGame"));
    }

    [Fact]
    public void DatRepository_LoadDatPayload_CacheWriteFailureLogsButReturnsParsedPayload()
    {
        var datPath = Path.Combine(_root, "cache-write-fails.dat");
        File.WriteAllText(datPath, """
        <?xml version="1.0"?>
        <datafile>
          <game name="ParsedGame">
            <rom name="parsed.rom" sha1="parsedhash" />
          </game>
        </datafile>
        """);
        var logs = new List<string>();
        var cache = new RecordingDatEntryCache(throwOnSet: true);
        var dat = new DatRepositoryAdapter(log: logs.Add, cache: cache);

        var payload = dat.LoadDatPayload(datPath, "SHA1");

        Assert.True(payload.Games.ContainsKey("ParsedGame"));
        Assert.Equal(1, cache.TryGetCount);
        Assert.Equal(1, cache.SetCount);
        Assert.Contains(logs, line => line.Contains("Cache-Write", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RunEnvironmentBuilder_SettingsDatRootWithDatFilesBeatsCatalogStateAndConventionalRoots()
    {
        var dataDir = Path.Combine(_root, "data");
        var conventionalRoot = Path.Combine(dataDir, "dats");
        var settingsRoot = Path.Combine(_root, "settings-dats");
        var stateRoot = Path.Combine(_root, "state-dats");
        Directory.CreateDirectory(conventionalRoot);
        Directory.CreateDirectory(settingsRoot);
        Directory.CreateDirectory(stateRoot);

        File.WriteAllText(Path.Combine(conventionalRoot, "conventional.dat"), "conventional");
        File.WriteAllText(Path.Combine(settingsRoot, "settings.dat"), "settings");
        var stateDatPath = Path.Combine(stateRoot, "state.dat");
        File.WriteAllText(stateDatPath, "state");

        var statePath = Path.Combine(_root, "dat-catalog-state.json");
        DatCatalogStateService.SaveState(statePath, new DatCatalogState
        {
            Entries = new Dictionary<string, DatLocalInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["state"] = new DatLocalInfo
                {
                    InstalledDate = new DateTime(2026, 5, 17, 12, 0, 0, DateTimeKind.Utc),
                    FileSha256 = "hash",
                    FileSizeBytes = 5,
                    LocalPath = stateDatPath
                }
            }
        });

        var resolution = RunEnvironmentBuilder.ResolveEffectiveDatRoot(
            runOptionDatRoot: null,
            settingsDatRoot: settingsRoot,
            dataDir: dataDir,
            statePath: statePath);

        Assert.Equal(RunEnvironmentBuilder.DatRootResolutionSource.Settings, resolution.Source);
        Assert.Equal(Path.GetFullPath(settingsRoot), resolution.Path);
    }

    [Fact]
    public void ExternalProcessGuard_AlreadyExitedProcessUsesNoopLeaseAndDoesNotLeakTracking()
    {
        using var process = StartShortLivedProcess();
        Assert.True(process.WaitForExit(5000));
        var trackedBefore = ExternalProcessGuard.GetTrackedProcessCountForTests();

        using (ExternalProcessGuard.Track(process, "critical-path-test"))
        {
            Assert.Equal(trackedBefore, ExternalProcessGuard.GetTrackedProcessCountForTests());
        }

        Assert.Equal(trackedBefore, ExternalProcessGuard.GetTrackedProcessCountForTests());
    }

    private IDisposable CreateCliPathOverrides()
        => CliProgram.SetTestPathOverrides(new CliPathOverrides
        {
            CollectionDbPath = Path.Combine(_stateRoot, "collection.db"),
            AuditSigningKeyPath = Path.Combine(_stateRoot, "audit-signing.key")
        });

    private static async Task<int> InvokePrivateCliSubcommandAsync(string methodName, CliRunOptions options)
    {
        var method = typeof(CliProgram).GetMethod(methodName, BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, [options]);
        return await ((Task<int>)result!).ConfigureAwait(false);
    }

    private string CreateFile(string relativePath, string content)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private RunOptions CreateMoveConversionOptions(string? acceptDataLossToken)
        => new()
        {
            Roots = [_root],
            Mode = RunConstants.ModeMove,
            Extensions = [".iso"],
            AcceptDataLossToken = acceptDataLossToken,
            AuditPath = Path.Combine(_stateRoot, "conversion-audit.csv")
        };

    private PipelineContext CreatePipelineContext(RunOptions options)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(new FileSystemAdapter(), keyFilePath: Path.Combine(_stateRoot, "conversion-audit.key")),
            Metrics = metrics
        };
    }

    private static FormatConverterAdapter CreateLossyPlanningConverter(RecordingConversionExecutor executor)
        => new(
            new NoopToolRunner(),
            bestFormats: null,
            registry: null,
            planner: new LossyPlanner(),
            executor: executor,
            allowReviewRequiredPlans: true);

    private static ConversionPlan BuildLossyPlan(string sourcePath, string consoleKey, string sourceExtension)
    {
        var capability = new ConversionCapability
        {
            SourceExtension = sourceExtension,
            TargetExtension = ".cso",
            Tool = new ToolRequirement { ToolName = "fake-lossy" },
            Command = "compress",
            ApplicableConsoles = null,
            ResultIntegrity = SourceIntegrity.Lossy,
            Lossless = false,
            Cost = 1,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = ConversionCondition.None
        };

        return new ConversionPlan
        {
            SourcePath = sourcePath,
            ConsoleKey = consoleKey,
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossy,
            Safety = ConversionSafety.Acceptable,
            Steps =
            [
                new ConversionStep
                {
                    Order = 0,
                    InputExtension = sourceExtension,
                    OutputExtension = ".cso",
                    Capability = capability,
                    IsIntermediate = false
                }
            ]
        };
    }

    private static Process StartShortLivedProcess()
    {
        if (OperatingSystem.IsWindows())
        {
            return Process.Start(new ProcessStartInfo
            {
                FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
                Arguments = "/c exit 0",
                CreateNoWindow = true,
                UseShellExecute = false
            })!;
        }

        return Process.Start(new ProcessStartInfo
        {
            FileName = "/bin/sh",
            Arguments = "-c true",
            UseShellExecute = false
        })!;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
        }
    }

    private sealed class LossyPlanner : IConversionPlanner
    {
        public ConversionPlan Plan(string sourcePath, string consoleKey, string sourceExtension)
            => BuildLossyPlan(sourcePath, consoleKey, sourceExtension);

        public IReadOnlyList<ConversionPlan> PlanBatch(IReadOnlyList<(string Path, string ConsoleKey, string Extension)> candidates)
            => candidates.Select(candidate => Plan(candidate.Path, candidate.ConsoleKey, candidate.Extension)).ToArray();
    }

    private sealed class RecordingConversionExecutor : IConversionExecutor
    {
        public int ExecuteCount { get; private set; }

        public ConversionResult Execute(
            ConversionPlan plan,
            Action<ConversionStep, ConversionStepResult>? onStepComplete = null,
            CancellationToken cancellationToken = default)
        {
            ExecuteCount++;
            cancellationToken.ThrowIfCancellationRequested();

            var targetPath = Path.ChangeExtension(plan.SourcePath, plan.FinalTargetExtension);
            File.WriteAllBytes(targetPath, [9, 10, 11, 12]);
            var stepResult = new ConversionStepResult(0, targetPath, true, VerificationStatus.Verified, null, 1);
            onStepComplete?.Invoke(plan.Steps[0], stepResult);

            return new ConversionResult(plan.SourcePath, targetPath, ConversionOutcome.Success)
            {
                Plan = plan,
                SourceIntegrity = plan.SourceIntegrity,
                Safety = plan.Safety,
                VerificationResult = VerificationStatus.Verified,
                DurationMs = 1
            };
        }
    }

    private sealed class NoopToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => null;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => throw new InvalidOperationException("Tool execution is not expected in this test.");

        public ToolResult InvokeProcess(
            string filePath,
            string[] arguments,
            ToolRequirement? requirement,
            string? errorLabel,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
            => throw new InvalidOperationException("Tool execution is not expected in this test.");

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => throw new InvalidOperationException("Tool execution is not expected in this test.");
    }

    private sealed class RecordingDatEntryCache(
        CachedDatPayload? hitPayload = null,
        bool throwOnSet = false) : IDatEntryCache
    {
        public int TryGetCount { get; private set; }
        public int SetCount { get; private set; }

        public bool TryGet(string datPath, string hashType, out CachedDatPayload payload)
        {
            TryGetCount++;
            if (hitPayload is not null)
            {
                payload = hitPayload;
                return true;
            }

            payload = new CachedDatPayload();
            return false;
        }

        public void Set(string datPath, string hashType, CachedDatPayload payload)
        {
            SetCount++;
            if (throwOnSet)
                throw new IOException("forced cache write failure");
        }
    }
}
