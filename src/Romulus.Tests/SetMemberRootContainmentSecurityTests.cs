using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Security regression tests for SEC-MOVE-06 and SEC-CONV-08.
///
/// SEC-MOVE-06: Set member paths parsed from CUE/GDI/CCD/M3U descriptors must be
///   validated against configured roots before any move operation. A crafted
///   descriptor referencing "..\..\important.dat" could cause files outside
///   allowed roots to be trashed.
///
/// SEC-CONV-08: Before trashing the source file after conversion, the converted
///   output path must be verified as a real file (not a reparse point/junction).
///   A manipulated conversion output path could be a junction pointing elsewhere,
///   which would make the system believe conversion succeeded while the actual
///   output is somewhere else — and the source gets erroneously trashed.
/// </summary>
public sealed class SetMemberRootContainmentSecurityTests
{
    // ═══════════════════════════════════════════════════════════════════
    // SEC-MOVE-06: FindRootForPath unit tests (pure logic, no I/O)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindRootForPath_FileInsideRoot_ReturnsRoot()
    {
        var roots = new[] { @"C:\Roms" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms\NES\game.zip", roots);
        Assert.Equal(@"C:\Roms", result);
    }

    [Fact]
    public void FindRootForPath_FileOutsideAllRoots_ReturnsNull()
    {
        var roots = new[] { @"C:\Roms" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"D:\Other\game.zip", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindRootForPath_TraversalAttempt_ReturnsNull()
    {
        // A crafted CUE descriptor might resolve to a path outside the root via ..
        var roots = new[] { @"C:\Roms\NES" };
        // Path.GetFullPath resolves the traversal → C:\important.dat
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms\NES\..\..\important.dat", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindRootForPath_SeparatorGuard_PreventsRomsOtherMatch()
    {
        // SEC-MOVE-01: C:\Roms must NOT match C:\Roms-Other\game.zip
        var roots = new[] { @"C:\Roms" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms-Other\game.zip", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindRootForPath_MultipleRoots_PicksCorrectOne()
    {
        var roots = new[] { @"C:\Roms\NES", @"C:\Roms\SNES", @"D:\Backups" };
        Assert.Equal(@"C:\Roms\SNES", PipelinePhaseHelpers.FindRootForPath(@"C:\Roms\SNES\game.sfc", roots));
        Assert.Equal(@"D:\Backups", PipelinePhaseHelpers.FindRootForPath(@"D:\Backups\sub\file.zip", roots));
        Assert.Null(PipelinePhaseHelpers.FindRootForPath(@"E:\other\file.zip", roots));
    }

    [Fact]
    public void FindRootForPath_CaseInsensitive()
    {
        var roots = new[] { @"C:\ROMS" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\roms\nes\game.zip", roots);
        Assert.Equal(@"C:\ROMS", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEC-MOVE-06: Set member root containment integration scenario
    // These tests verify the guard pattern used in MovePipelinePhase
    // and ConversionPhaseHelper.MoveSetMembersToTrash.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void SetMemberOutsideRoot_WouldBeSkipped_ByGuardPattern()
    {
        // Simulate what MovePipelinePhase and ConversionPhaseHelper do:
        // For each set member, check FindRootForPath. If null → skip.
        var roots = new[] { @"C:\Roms\PSX" };
        var setMembers = new[]
        {
            @"C:\Roms\PSX\game.bin",           // inside root → allowed
            @"C:\Roms\PSX\..\..\boot.ini",     // traversal → outside root
            @"D:\ImportantData\file.dat",       // different drive → outside
        };

        var allowed = new List<string>();
        var skipped = new List<string>();

        foreach (var member in setMembers)
        {
            var memberRoot = PipelinePhaseHelpers.FindRootForPath(member, roots);
            if (memberRoot is null)
                skipped.Add(member);
            else
                allowed.Add(member);
        }

        Assert.Single(allowed);
        Assert.Contains(@"C:\Roms\PSX\game.bin", allowed);
        Assert.Equal(2, skipped.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEC-CONV-08: MoveConvertedSourceToTrash reparse point guard
    // Integration test with real filesystem
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveConvertedSourceToTrash_NonExistentConvertedPath_SourceNotMoved()
    {
        // When convertedPath doesn't exist, source must NOT be trashed
        using var temp = new TempDir();
        var sourceFile = temp.CreateFile("source.cue", "FILE track01.bin BINARY");
        var context = CreateContext(temp.Root);
        var options = CreateOptions(temp.Root);

        PipelinePhaseHelpers.MoveConvertedSourceToTrash(
            context, options, sourceFile, @"C:\nonexistent\output.chd");

        // Source must still be in place
        Assert.True(File.Exists(sourceFile));
    }

    [Fact]
    public void MoveConvertedSourceToTrash_ValidConvertedFile_SourceIsTrashed()
    {
        using var temp = new TempDir();
        var sourceFile = temp.CreateFile("source.cue", "FILE track01.bin BINARY");
        var convertedFile = temp.CreateFile("output.chd", "converted content");
        var context = CreateContext(temp.Root);
        var options = CreateOptions(temp.Root);

        PipelinePhaseHelpers.MoveConvertedSourceToTrash(
            context, options, sourceFile, convertedFile);

        // Source should be trashed (moved away)
        Assert.False(File.Exists(sourceFile));
    }

    [Fact]
    public void MoveConvertedSourceToTrash_SourceOutsideRoots_NotTrashed()
    {
        // Even if convertedPath is valid, if source is outside roots → skip
        using var temp = new TempDir();
        var otherDir = Path.Combine(temp.Root, "other");
        Directory.CreateDirectory(otherDir);
        var sourceFile = Path.Combine(otherDir, "source.cue");
        File.WriteAllText(sourceFile, "data");
        var convertedFile = temp.CreateFile("output.chd", "converted");

        var rootDir = Path.Combine(temp.Root, "roms");
        Directory.CreateDirectory(rootDir);
        var context = CreateContext(rootDir);
        var options = CreateOptions(rootDir);

        PipelinePhaseHelpers.MoveConvertedSourceToTrash(
            context, options, sourceFile, convertedFile);

        // Source must still exist — it's outside configured roots
        Assert.True(File.Exists(sourceFile));
    }

    // ═══════════════════════════════════════════════════════════════════
    // SEC-CONV-08: Reparse point guard captures warning
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveConvertedSourceToTrash_EmptyConvertedPath_SourceNotMoved()
    {
        using var temp = new TempDir();
        var sourceFile = temp.CreateFile("source.cue", "data");
        var context = CreateContext(temp.Root);
        var options = CreateOptions(temp.Root);

        PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, options, sourceFile, "");

        Assert.True(File.Exists(sourceFile));
    }

    [Fact]
    public void MoveConvertedSourceToTrash_NullConvertedPath_SourceNotMoved()
    {
        using var temp = new TempDir();
        var sourceFile = temp.CreateFile("source.cue", "data");
        var context = CreateContext(temp.Root);
        var options = CreateOptions(temp.Root);

        PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, options, sourceFile, null);

        Assert.True(File.Exists(sourceFile));
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static PipelineContext CreateContext(string root)
    {
        var warnings = new List<string>();
        return new PipelineContext
        {
            Options = CreateOptions(root),
            FileSystem = new FileSystemAdapter(),
            AuditStore = new NoOpAuditStore(),
            Metrics = new PhaseMetricsCollector(),
            OnProgress = msg => warnings.Add(msg),
        };
    }

    private static RunOptions CreateOptions(string root)
    {
        return new RunOptions
        {
            Roots = [root],
            TrashRoot = "",
            AuditPath = "",
        };
    }

    private sealed class NoOpAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false) => [];
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "") { }
        public void Flush(string auditCsvPath) { }
    }

    private sealed class TempDir : IDisposable
    {
        public string Root { get; }

        public TempDir()
        {
            Root = Path.Combine(Path.GetTempPath(), "SEC_TEST_" + Guid.NewGuid().ToString("N")[..8]);
            Directory.CreateDirectory(Root);
        }

        public string CreateFile(string name, string content)
        {
            var path = Path.Combine(Root, name);
            File.WriteAllText(path, content);
            return path;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root))
                Directory.Delete(Root, true);
        }
    }
}
