using System.Text;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave7TestGapRegressionTests : IDisposable
{
    private readonly string _tempDir;

    public Wave7TestGapRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_W7_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best-effort cleanup.
        }
    }

    [Fact]
    public void F009_DryRunAndMove_KeepParity_ForCoreSummaryAndSelections()
    {
        var dryRoot = Path.Combine(_tempDir, "f009-dry");
        var moveRoot = Path.Combine(_tempDir, "f009-move");
        Directory.CreateDirectory(dryRoot);
        Directory.CreateDirectory(moveRoot);

        SeedParityDataset(dryRoot);
        SeedParityDataset(moveRoot);

        var fs = new FileSystemAdapter();
        var dry = new RunOrchestrator(fs, new NoOpAuditStore()).Execute(new RunOptions
        {
            Roots = [dryRoot],
            Extensions = [".zip"],
            Mode = RunConstants.ModeDryRun,
            PreferRegions = ["US", "EU", "JP"],
            RemoveJunk = false
        });

        var execute = new RunOrchestrator(fs, new NoOpAuditStore()).Execute(new RunOptions
        {
            Roots = [moveRoot],
            Extensions = [".zip"],
            Mode = RunConstants.ModeMove,
            PreferRegions = ["US", "EU", "JP"],
            RemoveJunk = false
        });

        Assert.Equal(dry.TotalFilesScanned, execute.TotalFilesScanned);
        Assert.Equal(dry.GroupCount, execute.GroupCount);
        Assert.Equal(dry.WinnerCount, execute.WinnerCount);
        Assert.Equal(dry.LoserCount, execute.LoserCount);
        Assert.Equal(dry.UnknownCount, execute.UnknownCount);

        var dryWinners = dry.DedupeGroups
            .Select(g => Path.GetFileName(g.Winner.MainPath))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var executeWinners = execute.DedupeGroups
            .Select(g => Path.GetFileName(g.Winner.MainPath))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        Assert.Equal(dryWinners, executeWinners);
    }

    [Fact]
    public void F010_ConversionExecutor_TimeoutFailure_CleansIntermediateArtifacts()
    {
        var sourcePath = CreateFile("f010-game.cso", "source-bytes");
        var sourceDir = Path.GetDirectoryName(sourcePath)!;
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var intermediatePath = Path.Combine(sourceDir, $"{baseName}.tmp.step1.iso");
        var stagedFinalPath = Path.Combine(sourceDir, $"{baseName}.tmp.final.step2.chd");

        var plan = new ConversionPlan
        {
            SourcePath = sourcePath,
            ConsoleKey = "PSP",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps =
            [
                MakeStep(0, ".cso", ".iso", "tool-a", intermediate: true),
                MakeStep(1, ".iso", ".chd", "tool-b")
            ]
        };

        var sut = new ConversionExecutor([new TimeoutOnSecondStepInvoker()]);

        var result = sut.Execute(plan);

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("timeout", result.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        Assert.False(File.Exists(intermediatePath));
        Assert.False(File.Exists(stagedFinalPath));
    }

    [Fact]
    public void F011_RollbackVerify_CorruptAuditRow_IsSkippedWithoutAbortingValidRows()
    {
        var root = Path.Combine(_tempDir, "f011-root");
        var trash = Path.Combine(root, "_TRASH_DEDUPE");
        var keyPath = Path.Combine(_tempDir, "f011-signing.key");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trash);

        var oldPath = Path.Combine(root, "game.zip");
        var newPath = Path.Combine(trash, "game.zip");
        File.WriteAllText(newPath, "data");

        var auditPath = Path.Combine(_tempDir, "f011-audit.csv");
        var csv = new StringBuilder()
            .AppendLine("RootPath,OldPath,NewPath,Action")
            .AppendLine($"{root},{oldPath},{newPath},MOVE")
            .AppendLine($"{root},\"{oldPath},{newPath},MOVE")
            .ToString();
        File.WriteAllText(auditPath, csv, Encoding.UTF8);

        var auditStore = new AuditCsvStore(keyFilePath: keyPath);
        auditStore.WriteMetadataSidecar(auditPath, new Dictionary<string, object>
        {
            ["AllowedRestoreRoots"] = new[] { root },
            ["AllowedCurrentRoots"] = new[] { root }
        });

        var result = RollbackService.VerifyTrashIntegrity(auditPath, [root], keyPath);

        Assert.True(result.DryRun);
        Assert.Equal(2, result.TotalRows);
        Assert.Equal(1, result.EligibleRows);
        Assert.Equal(1, result.DryRunPlanned);
        Assert.Equal(1, result.SkippedUnsafe);
    }

    [Fact]
    public void F012_RollbackVerify_MissingTrashFile_IsCountedAsMissingDestination()
    {
        var root = Path.Combine(_tempDir, "f012-root");
        var trash = Path.Combine(root, "_TRASH_DEDUPE");
        var keyPath = Path.Combine(_tempDir, "f012-signing.key");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trash);

        var oldPath = Path.Combine(root, "missing.zip");
        var newPath = Path.Combine(trash, "missing.zip");

        var auditPath = Path.Combine(_tempDir, "f012-audit.csv");
        File.WriteAllText(
            auditPath,
            "RootPath,OldPath,NewPath,Action\n" +
            $"{root},{oldPath},{newPath},MOVE\n",
            Encoding.UTF8);

        var auditStore = new AuditCsvStore(keyFilePath: keyPath);
        auditStore.WriteMetadataSidecar(auditPath, new Dictionary<string, object>
        {
            ["AllowedRestoreRoots"] = new[] { root },
            ["AllowedCurrentRoots"] = new[] { root }
        });

        var result = RollbackService.VerifyTrashIntegrity(auditPath, [root], keyPath);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.TotalRows);
        Assert.Equal(1, result.EligibleRows);
        Assert.Equal(0, result.DryRunPlanned);
        Assert.Equal(1, result.SkippedMissingDest);
        Assert.Equal(1, result.Failed);
    }

    [Fact]
    public void F019_ArchiveHashService_BlocksTraversalEntries_Before7zExtractionRuns()
    {
        var archivePath = Path.Combine(_tempDir, "f019-payload.7z");
        File.WriteAllBytes(archivePath, [0x37, 0x7A, 0xBC, 0xAF]);

        var runner = new TraversalListing7zRunner();
        var service = new ArchiveHashService(runner);

        var hashes = service.GetArchiveHashes(archivePath, "SHA1");

        Assert.Empty(hashes);
        Assert.Equal(0, runner.ExtractInvocationCount);
    }

    [Fact]
    public void F020_FormatAndVersionScoring_HandleBoundaryCasesDeterministically()
    {
        var regionScore = FormatScorer.GetRegionScore("MARS", []);
        Assert.Equal(200, regionScore);

        var versionScorer = new VersionScorer();
        var sixSegments = versionScorer.GetVersionScore("Game (v1.2.3.4.5.6)");
        var sevenSegments = versionScorer.GetVersionScore("Game (v1.2.3.4.5.6.7)");

        Assert.True(sevenSegments > sixSegments,
            "A version with an extra trailing segment must remain deterministically higher.");
    }

    [Fact]
    public void F021_AllowedRootPolicy_BlocksPathsEnteringDirectorySymlinkBoundary()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var root = Path.Combine(_tempDir, "f021-root");
        var outside = Path.Combine(_tempDir, "f021-outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        var outsideFile = Path.Combine(outside, "game.rom");
        File.WriteAllText(outsideFile, "x");

        var linkDir = Path.Combine(root, "linked");

        try
        {
            Directory.CreateSymbolicLink(linkDir, outside);
        }
        catch
        {
            return;
        }

        var pathViaLink = Path.Combine(linkDir, "game.rom");
        var policy = new AllowedRootPathPolicy([root]);

        Assert.False(policy.IsPathAllowed(pathViaLink));
    }

    [Fact]
    public void F017_FeatureCommandService_ProfileMessages_UseLocalizationKeys()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/Services/FeatureCommandService.cs");

        Assert.DoesNotContain("Kein Benutzerprofil ausgewaehlt.", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Profil gespeichert:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Profil konnte nicht gespeichert werden:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Workflow/Profil geladen:", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kein Workflow\"", source, StringComparison.Ordinal);
        Assert.DoesNotContain("\"kein Profil\"", source, StringComparison.Ordinal);

        Assert.Contains("_vm.Loc[\"Cmd.ProfileDeleteNoSelection\"]", source, StringComparison.Ordinal);
        Assert.Contains("_vm.Loc.Format(\"Cmd.ProfileSaved\"", source, StringComparison.Ordinal);
        Assert.Contains("_vm.Loc.Format(\"Cmd.ProfileSaveFailed\"", source, StringComparison.Ordinal);
        Assert.Contains("_vm.Loc.Format(\"Cmd.ProfileLoaded\"", source, StringComparison.Ordinal);
        Assert.Contains("_vm.Loc[\"Cmd.ProfileLoadedNoWorkflow\"]", source, StringComparison.Ordinal);
        Assert.Contains("_vm.Loc[\"Cmd.ProfileLoadedNoProfile\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void F017_ProfileLocalizationKeys_ArePresentInAllWave7Locales()
    {
        var requiredKeys = new[]
        {
            "Cmd.ProfileDeleteNoSelection",
            "Cmd.ProfileSaved",
            "Cmd.ProfileSaveFailed",
            "Cmd.ProfileLoaded",
            "Cmd.ProfileLoadedNoWorkflow",
            "Cmd.ProfileLoadedNoProfile"
        };

        foreach (var locale in new[] { "de.json", "en.json", "fr.json" })
        {
            var path = Path.Combine(FindRepositoryRoot(), "data", "i18n", locale);
            var payload = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
            Assert.NotNull(payload);

            foreach (var key in requiredKeys)
            {
                Assert.True(payload!.TryGetValue(key, out var value), $"Missing key '{key}' in locale '{locale}'.");
                Assert.Equal(JsonValueKind.String, value.ValueKind);
                Assert.False(string.IsNullOrWhiteSpace(value.GetString()), $"Empty value for key '{key}' in locale '{locale}'.");
            }
        }
    }

    // F018_WatchFolderService_UsesNamedDefaultsAndBufferConstants,
    // F018_ParallelHasher_UsesNamedThreadAndBatchThresholdConstants,
    // F018_ScheduleService_UsesNamedDefaultPollIntervalConstant: removed
    // (per testing.instructions.md - pinned literal const-name strings,
    //  no behavioural assertion, broke on any harmless rename).

    [Fact]
    public void F016_ApiRedPhaseTests_UseAdaptivePollingInsteadOfFixed50msSleepLoops()
    {
        var source = ReadSource("src/Romulus.Tests/ApiRedPhaseTests.cs");

        Assert.DoesNotContain("await Task.Delay(50);", source, StringComparison.Ordinal);
    }

    [Fact]
    public void F016_TestSuite_DoesNotUseFixedSleepForRateLimiterAndArchiveTimingChecks()
    {
        var auditSource = ReadSource("src/Romulus.Tests/AuditFindingsFixTests.cs");
        var block4Source = ReadSource("src/Romulus.Tests/Block4_RobustnessTests.cs");
        var coverageSource = ReadSource("src/Romulus.Tests/ApiCoverageBoostTests.cs");

        Assert.DoesNotContain("Thread.Sleep(100);", auditSource, StringComparison.Ordinal);
        Assert.DoesNotContain("System.Threading.Thread.Sleep(10);", block4Source, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(50);", coverageSource, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(20);", coverageSource, StringComparison.Ordinal);
    }

    [Fact]
    public void F016_RunOrderingTests_AvoidFixed25msDelays()
    {
        var apiIntegrationSource = ReadSource("src/Romulus.Tests/ApiIntegrationTests.cs");
        var runManagerSource = ReadSource("src/Romulus.Tests/RunManagerTests.cs");

        Assert.DoesNotContain("await Task.Delay(25);", apiIntegrationSource, StringComparison.Ordinal);
        Assert.DoesNotContain("await Task.Delay(25);", runManagerSource, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        var root = FindRepositoryRoot();
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(fullPath);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not resolve repository root from test context.");
    }

    private static ConversionStep MakeStep(int order, string inputExt, string outputExt, string toolName, bool intermediate = false)
        => new()
        {
            Order = order,
            InputExtension = inputExt,
            OutputExtension = outputExt,
            IsIntermediate = intermediate,
            Capability = new ConversionCapability
            {
                SourceExtension = inputExt,
                TargetExtension = outputExt,
                Tool = new ToolRequirement { ToolName = toolName },
                Command = "convert",
                Verification = VerificationMethod.FileExistenceCheck,
                Lossless = true,
                ResultIntegrity = SourceIntegrity.Lossless,
                Cost = 1
            }
        };

    private void SeedParityDataset(string root)
    {
        File.WriteAllText(Path.Combine(root, "Mega Game (US).zip"), "us");
        File.WriteAllText(Path.Combine(root, "Mega Game (EU).zip"), "eu");
        File.WriteAllText(Path.Combine(root, "Mystery [h].zip"), "mystery");
    }

    private string CreateFile(string fileName, string content)
    {
        var path = Path.Combine(_tempDir, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private sealed class TimeoutOnSecondStepInvoker : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability)
            => capability.Tool.ToolName is "tool-a" or "tool-b";

        public ToolInvocationResult Invoke(
            string sourcePath,
            string targetPath,
            ConversionCapability capability,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (capability.Tool.ToolName.Equals("tool-b", StringComparison.OrdinalIgnoreCase))
            {
                return new ToolInvocationResult(
                    false,
                    null,
                    -1,
                    null,
                    "tool-timeout",
                    5000,
                    VerificationStatus.NotAttempted);
            }

            File.WriteAllText(targetPath, "intermediate-output");
            return new ToolInvocationResult(
                true,
                targetPath,
                0,
                "ok",
                null,
                10,
                VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability)
            => VerificationStatus.Verified;
    }

    private sealed class TraversalListing7zRunner : IToolRunner
    {
        public int ExtractInvocationCount { get; private set; }

        public string? FindTool(string toolName)
            => toolName.Equals("7z", StringComparison.OrdinalIgnoreCase) ? "fake-7z.exe" : null;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            if (arguments.Length > 0 && arguments[0] == "l")
            {
                var output = string.Join('\n',
                [
                    "Listing archive: payload.7z",
                    "",
                    "----------",
                    "Path = ..\\..\\outside\\evil.bin",
                    "Size = 12",
                    "",
                    "Path = game.bin",
                    "Size = 42"
                ]);

                return new ToolResult(0, output, true);
            }

            if (arguments.Length > 0 && arguments[0] == "x")
            {
                ExtractInvocationCount++;
                return new ToolResult(0, string.Empty, true);
            }

            return new ToolResult(1, string.Empty, false);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => InvokeProcess(sevenZipPath, arguments);
    }

    private sealed class NoOpAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
        }

        public bool TestMetadataSidecar(string auditCsvPath) => false;

        public void Flush(string auditCsvPath)
        {
        }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => [];

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
        }
    }
}
