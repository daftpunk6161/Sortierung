using System.Text.Json;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Behavioral and contract-shape tests retained from the historical "tracker batch 4" set.
/// All source-mirror assertions were removed in Block A
/// of test-suite-remediation-plan-2026-04-25.md.
/// </summary>
public sealed class TrackerAllFindingsBatch4RedTests
{
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
    public void Fin02_OperationResult_Collections_MustBeReadOnlyForConsumers()
    {
        var type = typeof(OperationResult);
        Assert.Equal(typeof(IReadOnlyDictionary<string, object>), type.GetProperty(nameof(OperationResult.Meta))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<string>), type.GetProperty(nameof(OperationResult.Warnings))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyDictionary<string, double>), type.GetProperty(nameof(OperationResult.Metrics))!.PropertyType);
        Assert.Equal(typeof(IReadOnlyList<string>), type.GetProperty(nameof(OperationResult.Artifacts))!.PropertyType);
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
    public void I18n06_Defaults_MustUseAutoLocaleAndTheme()
    {
        using var defaultsDoc = JsonDocument.Parse(File.ReadAllText(FindRepoFile("data", "defaults.json")));
        var root = defaultsDoc.RootElement;

        Assert.Equal("auto", root.GetProperty("theme").GetString());
        Assert.Equal("auto", root.GetProperty("locale").GetString());
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
