using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for PipelinePhaseHelpers: CreateAuditRow, GetConversionOutputPaths,
/// audit append methods, and TryMovePathToConvertedTrash edge cases.
/// </summary>
public sealed class PipelinePhaseHelpersCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public PipelinePhaseHelpersCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "PipelineHelpers_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ═══ CreateAuditRow ═══════════════════════════════════════════════

    [Fact]
    public void CreateAuditRow_NoAuditPath_ReturnsNull()
    {
        var options = MakeOptions(auditPath: null);

        var row = PipelinePhaseHelpers.CreateAuditRow(options, @"C:\roms\game.zip", @"C:\roms\out.chd", "CONVERT");

        Assert.Null(row);
    }

    [Fact]
    public void CreateAuditRow_PathOutsideRoots_ReturnsNull()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var options = MakeOptions(roots: [root], auditPath: Path.Combine(_tempDir, "audit.csv"));

        var row = PipelinePhaseHelpers.CreateAuditRow(options, @"C:\other\game.zip", null, "CONVERT");

        Assert.Null(row);
    }

    [Fact]
    public void CreateAuditRow_ValidArguments_ReturnsPopulatedRow()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "game.zip");
        var target = Path.Combine(root, "game.chd");
        var options = MakeOptions(roots: [root], auditPath: Path.Combine(_tempDir, "audit.csv"));

        var row = PipelinePhaseHelpers.CreateAuditRow(options, source, target, "CONVERT", "GAME", "", "format-convert:chdman");

        Assert.NotNull(row);
        Assert.Equal(root, row!.RootPath);
        Assert.Equal(source, row.OldPath);
        Assert.Equal(target, row.NewPath);
        Assert.Equal("CONVERT", row.Action);
        Assert.Equal("GAME", row.Category);
        Assert.Equal("format-convert:chdman", row.Reason);
    }

    [Fact]
    public void CreateAuditRow_NullTargetPath_StoresEmptyString()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var options = MakeOptions(roots: [root], auditPath: Path.Combine(_tempDir, "audit.csv"));

        var row = PipelinePhaseHelpers.CreateAuditRow(options, Path.Combine(root, "game.zip"), null, "CONVERT_ERROR");

        Assert.NotNull(row);
        Assert.Equal(string.Empty, row!.NewPath);
    }

    // ═══ GetConversionOutputPaths ═════════════════════════════════════

    [Fact]
    public void GetConversionOutputPaths_DeduplicatesAndPreservesOrder()
    {
        var result = new ConversionResult("source.iso", @"C:\out\game.chd", ConversionOutcome.Success)
        {
            AdditionalTargetPaths = [@"C:\out\game.chd", @"C:\out\game.cue", @"C:\out\game.cue", @"C:\out\game.bin"]
        };

        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);

        Assert.Equal(3, paths.Count);
        Assert.Equal(@"C:\out\game.chd", paths[0]);
        Assert.Equal(@"C:\out\game.cue", paths[1]);
        Assert.Equal(@"C:\out\game.bin", paths[2]);
    }

    [Fact]
    public void GetConversionOutputPaths_EmptyTargetPath_SkipsIt()
    {
        var result = new ConversionResult("source.iso", null, ConversionOutcome.Success)
        {
            AdditionalTargetPaths = [@"C:\out\game.chd"]
        };

        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);

        Assert.Single(paths);
        Assert.Equal(@"C:\out\game.chd", paths[0]);
    }

    [Fact]
    public void GetConversionOutputPaths_AllEmpty_ReturnsEmptyList()
    {
        var result = new ConversionResult("source.iso", "", ConversionOutcome.Success)
        {
            AdditionalTargetPaths = ["", "  "]
        };

        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);

        Assert.Empty(paths);
    }

    // ═══ Audit Append Methods ═════════════════════════════════════════

    [Fact]
    public void AppendConversionAudit_WritesToAuditStore()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "game.iso");
        var target = Path.Combine(root, "game.chd");
        var options = MakeOptions(roots: [root], auditPath: Path.Combine(_tempDir, "audit.csv"));
        var audit = new TrackingAudit();
        var ctx = MakeContext(options, audit);

        PipelinePhaseHelpers.AppendConversionAudit(ctx, options, source, target, "chdman");

        Assert.Single(audit.Rows);
        Assert.Equal(RunConstants.AuditActions.Convert, audit.Rows[0].Action);
        Assert.Contains("format-convert:chdman", audit.Rows[0].Reason);
    }

    [Fact]
    public void AppendConversionSourceAudit_WritesToAuditStore()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var options = MakeOptions(roots: [root], auditPath: Path.Combine(_tempDir, "audit.csv"));
        var audit = new TrackingAudit();
        var ctx = MakeContext(options, audit);

        PipelinePhaseHelpers.AppendConversionSourceAudit(ctx, options, Path.Combine(root, "game.iso"), Path.Combine(root, "_TRASH_CONVERTED", "game.iso"), "GAME", "converted-source");

        Assert.Single(audit.Rows);
        Assert.Equal(RunConstants.AuditActions.ConvertSource, audit.Rows[0].Action);
    }

    [Fact]
    public void AppendConversionFailedAudit_WritesToAuditStore()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var options = MakeOptions(roots: [root], auditPath: Path.Combine(_tempDir, "audit.csv"));
        var audit = new TrackingAudit();
        var ctx = MakeContext(options, audit);

        PipelinePhaseHelpers.AppendConversionFailedAudit(ctx, options, Path.Combine(root, "game.iso"), Path.Combine(root, "game.chd"), "chdman");

        Assert.Single(audit.Rows);
        Assert.Equal("CONVERT_FAILED", audit.Rows[0].Action);
        Assert.Contains("verify-failed:chdman", audit.Rows[0].Reason);
    }

    [Fact]
    public void AppendConversionErrorAudit_WritesToAuditStore()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var options = MakeOptions(roots: [root], auditPath: Path.Combine(_tempDir, "audit.csv"));
        var audit = new TrackingAudit();
        var ctx = MakeContext(options, audit);

        PipelinePhaseHelpers.AppendConversionErrorAudit(ctx, options, Path.Combine(root, "game.iso"), "tool-crashed");

        Assert.Single(audit.Rows);
        Assert.Equal("CONVERT_ERROR", audit.Rows[0].Action);
        Assert.Contains("convert-error:tool-crashed", audit.Rows[0].Reason);
    }

    [Fact]
    public void AppendConversionAudit_NoAuditPath_DoesNothing()
    {
        var options = MakeOptions(auditPath: null);
        var audit = new TrackingAudit();
        var ctx = MakeContext(options, audit);

        PipelinePhaseHelpers.AppendConversionAudit(ctx, options, @"C:\roms\game.iso", @"C:\roms\game.chd", "chdman");

        Assert.Empty(audit.Rows);
    }

    // ═══ TryMovePathToConvertedTrash ══════════════════════════════════

    [Fact]
    public void TryMovePathToConvertedTrash_PathOutsideRoots_ReturnsFalse()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var options = MakeOptions(roots: [root]);
        var ctx = MakeContext(options);

        var ok = PipelinePhaseHelpers.TryMovePathToConvertedTrash(ctx, options, @"C:\other\game.iso", out var dest, out var reason);

        Assert.False(ok);
        Assert.Null(dest);
        Assert.Contains("path-not-within-allowed-roots", reason!);
    }

    [Fact]
    public void TryMovePathToConvertedTrash_ValidPath_MovesToTrashConverted()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "game.iso");
        File.WriteAllText(source, "x");

        var expectedTrash = Path.Combine(root, RunConstants.WellKnownFolders.TrashConverted, "game.iso");
        var fs = new MinimalFs();
        fs.MoveResults[source] = expectedTrash;

        var options = MakeOptions(roots: [root]);
        var ctx = MakeContext(options, fs: fs);

        var ok = PipelinePhaseHelpers.TryMovePathToConvertedTrash(ctx, options, source, out var dest, out var reason);

        Assert.True(ok);
        Assert.Equal(expectedTrash, dest);
        Assert.Null(reason);
    }

    [Fact]
    public void TryMovePathToConvertedTrash_WithCustomTrashRoot_UsesTrashRoot()
    {
        var root = Path.Combine(_tempDir, "roms");
        var trashRoot = Path.Combine(_tempDir, "custom-trash");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trashRoot);
        var source = Path.Combine(root, "game.iso");
        File.WriteAllText(source, "x");

        var expectedTrash = Path.Combine(trashRoot, RunConstants.WellKnownFolders.TrashConverted, "game.iso");
        var fs = new MinimalFs();
        fs.MoveResults[source] = expectedTrash;
        fs.ResolvedPaths[trashRoot] = expectedTrash;

        var options = MakeOptions(roots: [root], trashRoot: trashRoot);
        var ctx = MakeContext(options, fs: fs);

        var ok = PipelinePhaseHelpers.TryMovePathToConvertedTrash(ctx, options, source, out var dest, out _);

        Assert.True(ok);
        Assert.Equal(expectedTrash, dest);
    }

    [Fact]
    public void TryMovePathToConvertedTrash_MoveReturnsNull_ReturnsFalse()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "game.iso");
        File.WriteAllText(source, "x");

        var options = MakeOptions(roots: [root]);
        var ctx = MakeContext(options, fs: new MinimalFs());

        var ok = PipelinePhaseHelpers.TryMovePathToConvertedTrash(ctx, options, source, out _, out var reason);

        Assert.False(ok);
        Assert.Contains("move-to-trash-failed", reason!);
    }

    // ═══ MoveConvertedSourceToTrash ═══════════════════════════════════

    [Fact]
    public void MoveConvertedSourceToTrash_NullOrEmptyConvertedPath_ReturnsNull()
    {
        var options = MakeOptions();
        var ctx = MakeContext(options);

        Assert.Null(PipelinePhaseHelpers.MoveConvertedSourceToTrash(ctx, options, @"C:\roms\game.iso", null));
        Assert.Null(PipelinePhaseHelpers.MoveConvertedSourceToTrash(ctx, options, @"C:\roms\game.iso", string.Empty));
    }

    [Fact]
    public void MoveConvertedSourceToTrash_ConvertedFileDoesNotExist_ReturnsNull()
    {
        var options = MakeOptions();
        var ctx = MakeContext(options);

        Assert.Null(PipelinePhaseHelpers.MoveConvertedSourceToTrash(ctx, options, @"C:\roms\game.iso", Path.Combine(_tempDir, "nonexistent.chd")));
    }

    [Fact]
    public void MoveConvertedSourceToTrash_ValidConvertedFile_ReturnsTrashPath()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "game.iso");
        var converted = Path.Combine(root, "game.chd");
        File.WriteAllText(source, "x");
        File.WriteAllText(converted, "y");

        var expectedTrash = Path.Combine(root, RunConstants.WellKnownFolders.TrashConverted, "game.iso");
        var fs = new MinimalFs();
        fs.MoveResults[source] = expectedTrash;

        var options = MakeOptions(roots: [root]);
        var ctx = MakeContext(options, fs: fs);

        var result = PipelinePhaseHelpers.MoveConvertedSourceToTrash(ctx, options, source, converted);

        Assert.Equal(expectedTrash, result);
    }

    // ═══ FindRootForPath Edge Cases ═══════════════════════════════════

    [Fact]
    public void FindRootForPath_MultipleRoots_MatchesFirstContainingRoot()
    {
        var parent = Path.Combine(_tempDir, "roms");
        var child = Path.Combine(_tempDir, "roms", "psx");
        Directory.CreateDirectory(child);

        var filePath = Path.Combine(child, "game.bin");

        // First-match: parent listed first → returns parent
        var match = PipelinePhaseHelpers.FindRootForPath(filePath, [parent, child]);
        Assert.Equal(parent, match);

        // If child is listed first → returns child
        var matchChild = PipelinePhaseHelpers.FindRootForPath(filePath, [child, parent]);
        Assert.Equal(child, matchChild);
    }

    [Fact]
    public void FindRootForPath_NoMatchingRoot_ReturnsNull()
    {
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);

        Assert.Null(PipelinePhaseHelpers.FindRootForPath(@"D:\other\game.zip", [root]));
    }

    // ═══ Helpers ══════════════════════════════════════════════════════

    private static RunOptions MakeOptions(
        string[]? roots = null,
        string? auditPath = null,
        string? trashRoot = null)
    {
        return new RunOptions
        {
            Roots = roots ?? [@"C:\roms"],
            AuditPath = auditPath,
            TrashRoot = trashRoot,
            Extensions = [".iso", ".chd", ".zip"]
        };
    }

    private static PipelineContext MakeContext(RunOptions options, TrackingAudit? audit = null, MinimalFs? fs = null)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = fs ?? new MinimalFs(),
            AuditStore = audit ?? new TrackingAudit(),
            Metrics = metrics,
            OnProgress = _ => { }
        };
    }

    private sealed class MinimalFs : IFileSystem
    {
        public Dictionary<string, string> MoveResults { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> ResolvedPaths { get; } = new(StringComparer.OrdinalIgnoreCase);

        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return path;
        }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null) => [];
        public string? MoveItemSafely(string sourcePath, string destinationPath) =>
            MoveResults.TryGetValue(sourcePath, out var r) ? r : null;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            if (ResolvedPaths.TryGetValue(rootPath, out var resolved))
                return resolved;
            return Path.GetFullPath(Path.Combine(rootPath, relativePath));
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }
        public void CopyFile(string source, string dest, bool overwrite = false) => File.Copy(source, dest, overwrite);
    }

    private sealed class TrackingAudit : IAuditStore
    {
        public List<AuditEntry> Rows { get; } = [];

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
        {
            Rows.Add(new AuditEntry(action, oldPath, newPath, reason));
        }

        public void Flush(string auditCsvPath) { }
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false) => [];
    }

    private sealed record AuditEntry(string Action, string OldPath, string NewPath, string Reason);
}
