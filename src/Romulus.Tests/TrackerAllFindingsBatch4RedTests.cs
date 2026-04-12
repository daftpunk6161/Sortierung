using System.Reflection;
using System.Text.Json;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class TrackerAllFindingsBatch4RedTests
{
    [Fact]
    public void Sec09_FileSystemAdapter_MustUseSourceSnapshotValidationForMove()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "FileSystem", "FileSystemAdapter.cs"));
        Assert.Contains("ValidateSourceSnapshotBeforeMove", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Di04_RunService_MustNotFallbackToNewRunEnvironmentFactory()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.UI.Wpf", "Services", "RunService.cs"));
        Assert.DoesNotContain("?? new RunEnvironmentFactory()", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Di05_FeatureCommandService_MustNotFallbackToNewInfrastructureServices()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.UI.Wpf", "Services", "FeatureCommandService.cs"));
        Assert.DoesNotContain("?? new FileSystemAdapter()", source, StringComparison.Ordinal);
        Assert.DoesNotContain("?? new AuditCsvStore(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Err04_Infrastructure_MustUseILoggerAbstractions()
    {
        var datSource = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Dat", "DatSourceService.cs"));
        var toolRunner = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Tools", "ToolRunnerAdapter.cs"));
        var watch = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Watch", "WatchFolderService.cs"));

        Assert.Contains("ILogger<DatSourceService>", datSource, StringComparison.Ordinal);
        Assert.Contains("ILogger<ToolRunnerAdapter>", toolRunner, StringComparison.Ordinal);
        Assert.Contains("ILogger<WatchFolderService>", watch, StringComparison.Ordinal);
    }

    [Fact]
    public void Err05_ExternalCalls_MustHaveRetryPolicyForHttpAndToolExecution()
    {
        var datSource = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Dat", "DatSourceService.cs"));
        var toolRunner = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Tools", "ToolRunnerAdapter.cs"));

        Assert.Contains("ExecuteHttpWithRetryAsync", datSource, StringComparison.Ordinal);
        Assert.Contains("RunProcessWithRetry", toolRunner, StringComparison.Ordinal);
    }

    [Fact]
    public void Err07_OperationErrorResponse_MustExposeProblemDetailsFields()
    {
        var type = typeof(OperationErrorResponse);
        Assert.NotNull(type.GetProperty("Type"));
        Assert.NotNull(type.GetProperty("Title"));
        Assert.NotNull(type.GetProperty("Status"));
        Assert.NotNull(type.GetProperty("Detail"));
        Assert.NotNull(type.GetProperty("Instance"));
    }

    [Fact]
    public void Orc02_WatchFolderService_MustContainDeletedDirectoryRecoveryFlow()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Watch", "WatchFolderService.cs"));
        Assert.Contains("_configuredRoots", source, StringComparison.Ordinal);
        Assert.Contains("TryRecoverWatchers", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Orc06_PhaseMetricsCollector_AutoComplete_MustBeExplicitlyMarked()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Metrics", "PhaseMetricsCollector.cs"));
        Assert.Contains("AutoCompleted", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Fin02_OperationResult_Collections_MustBeReadOnlyForConsumers()
    {
        var type = typeof(OperationResult);
        Assert.Equal(typeof(IReadOnlyDictionary<string, object>), type.GetProperty(nameof(OperationResult.Meta))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<string>), type.GetProperty(nameof(OperationResult.Warnings))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyDictionary<string, double>), type.GetProperty(nameof(OperationResult.Metrics))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<string>), type.GetProperty(nameof(OperationResult.Artifacts))!.PropertyType);
    }

    [Fact]
    public void Fin03_QuarantineModels_MustUseInitOnlyProperties()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Contracts", "Models", "QuarantineModels.cs"));
        Assert.DoesNotContain("{ get; set; }", source, StringComparison.Ordinal);
        Assert.Contains("{ get; init; }", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Fin06_I18n07_Cli_MustUseCentralJsonSerializerWithoutAdHocIndentedOptions()
    {
        var cliProgram = File.ReadAllText(FindRepoFile("src", "Romulus.CLI", "Program.cs"));
        var cliSubcommands = File.ReadAllText(FindRepoFile("src", "Romulus.CLI", "Program.Subcommands.AnalysisAndDat.cs"));

        Assert.DoesNotContain("new JsonSerializerOptions { WriteIndented = true }", cliProgram, StringComparison.Ordinal);
        Assert.DoesNotContain("new JsonSerializerOptions { WriteIndented = true }", cliSubcommands, StringComparison.Ordinal);
        Assert.Contains("CliOutputWriter.SerializeJson", cliProgram, StringComparison.Ordinal);
    }

    [Fact]
    public void Core04_DeduplicatePhase_MustIncludeUnknownAndNonGameGroups()
    {
        var options = new RunOptions { Roots = ["C:\\roms"], Extensions = [".zip"] };
        var phase = new DeduplicatePipelinePhase();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = new NoopFileSystem(),
            AuditStore = new NoopAuditStore(),
            Metrics = new Romulus.Infrastructure.Metrics.PhaseMetricsCollector()
        };
        context.Metrics.Initialize();

        var input = new[]
        {
            new RomCandidate
            {
                MainPath = "C:\\roms\\unknown-a.zip",
                GameKey = "mystery",
                ConsoleKey = "PSX",
                Category = FileCategory.Unknown
            },
            new RomCandidate
            {
                MainPath = "C:\\roms\\unknown-b.zip",
                GameKey = "mystery",
                ConsoleKey = "PSX",
                Category = FileCategory.Unknown
            },
            new RomCandidate
            {
                MainPath = "C:\\roms\\junk-only.zip",
                GameKey = "junk-only",
                ConsoleKey = "PSX",
                Category = FileCategory.Junk
            }
        };

        var output = phase.Execute(input, context, CancellationToken.None);
        Assert.Contains(output.GameGroups, g => string.Equals(g.GameKey, "mystery", StringComparison.OrdinalIgnoreCase));
        Assert.DoesNotContain(output.GameGroups, g => string.Equals(g.GameKey, "junk-only", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Core06_ClassificationIoResolver_MustNotReflectIntoInfrastructure()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Core", "Classification", "ClassificationIoResolver.cs"));
        Assert.DoesNotContain("Type.GetType(\"Romulus.Infrastructure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Core07_SetParserIoResolver_MustNotReflectIntoInfrastructure()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Core", "SetParsing", "SetParserIoResolver.cs"));
        Assert.DoesNotContain("Type.GetType(\"Romulus.Infrastructure", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Api06_ApiRunConfigurationMapper_MustUsePrecomputedPropertyNameSet()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Api", "ApiRunConfigurationMapper.cs"));
        Assert.Contains("CollectPropertyNames", source, StringComparison.Ordinal);
    }

    [Fact]
    public void Test06_GuiViewModelTests_MustBeSplitBelowThreeThousandLines()
    {
        var lineCount = File.ReadAllLines(FindRepoFile("src", "Romulus.Tests", "GuiViewModelTests.cs")).Length;
        Assert.True(lineCount <= 3000, $"GuiViewModelTests.cs has {lineCount} lines and must be split.");
    }

    [Fact]
    public void I18n06_Defaults_MustUseAutoLocaleAndTheme()
    {
        using var defaultsDoc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "defaults.json")));
        var root = defaultsDoc.RootElement;

        Assert.Equal("auto", root.GetProperty("theme").GetString());
        Assert.Equal("auto", root.GetProperty("locale").GetString());
    }

    [Fact]
    public void I18n06_SettingsLoader_MustResolveAutoLocaleAndThemeFromSystem()
    {
        var source = File.ReadAllText(FindRepoFile("src", "Romulus.Infrastructure", "Configuration", "SettingsLoader.cs"));
        Assert.Contains("ResolveSystemTheme", source, StringComparison.Ordinal);
        Assert.Contains("ResolveSystemLocale", source, StringComparison.Ordinal);
    }

    private static string FindRepoFile(params string[] segments)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var repoRoot = Directory.GetParent(dataDir)?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved.");
        return Path.Combine(new[] { repoRoot }.Concat(segments).ToArray());
    }

    private sealed class NoopFileSystem : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => false;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null) => Array.Empty<string>();
        public IReadOnlyList<string> ConsumeScanWarnings() => Array.Empty<string>();
        public string? MoveItemSafely(string sourcePath, string destinationPath) => null;
        public string? MoveItemSafely(string sourcePath, string destinationPath, bool overwrite) => null;
        public string? MoveItemSafely(string sourcePath, string destinationPath, string allowedRoot) => null;
        public string? RenameItemSafely(string sourcePath, string newFileName) => null;
        public bool MoveDirectorySafely(string sourcePath, string destinationPath) => false;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => null;
        public bool IsReparsePoint(string path) => false;
        public long? GetAvailableFreeSpace(string path) => null;
        public void DeleteFile(string path)
        {
        }

        public void WriteAllText(string path, string content)
        {
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
        {
        }
    }

    private sealed class NoopAuditStore : IAuditStore
    {
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
        }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
        }

        public bool TestMetadataSidecar(string auditCsvPath) => false;

        public void Flush(string auditCsvPath)
        {
        }
    }
}
