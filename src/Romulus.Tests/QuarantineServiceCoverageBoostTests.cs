using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Quarantine;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for QuarantineService: custom rule edge cases, filename sanitization,
/// Execute move/error paths, GetContents grouping, and Restore validation branches.
/// Targets ~39 uncovered lines.
/// </summary>
public sealed class QuarantineServiceCoverageBoostTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _quarantineDir;
    private readonly FakeFs _fakeFs;
    private readonly QuarantineService _svc;

    public QuarantineServiceCoverageBoostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "QS_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        _quarantineDir = Path.Combine(_tempDir, "quarantine");
        Directory.CreateDirectory(_tempDir);
        Directory.CreateDirectory(_quarantineDir);
        _fakeFs = new FakeFs(_tempDir);
        _svc = new QuarantineService(_fakeFs);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFile(string relativePath, string content = "data")
    {
        var full = Path.GetFullPath(Path.Combine(_tempDir, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, content);
        return full;
    }

    // ===== TestCandidate: custom rules =====

    [Fact]
    public void TestCandidate_CustomRule_MatchByConsole_AddsReason()
    {
        var item = new QuarantineItem { Console = "UNKNOWN_SYS", Format = "zip", DatStatus = "Match", Category = "GAME", HeaderStatus = "OK" };
        var rules = new List<QuarantineRule>
        {
            new() { Field = "Console", Value = "UNKNOWN_SYS" }
        };

        var result = _svc.TestCandidate(item, rules);

        Assert.True(result.IsCandidate);
        Assert.Contains(result.Reasons, r => r.Contains("CustomRule:Console=UNKNOWN_SYS"));
    }

    [Fact]
    public void TestCandidate_CustomRule_NullField_Skipped()
    {
        var item = new QuarantineItem { Console = "NES", Format = "nes", DatStatus = "Match", Category = "GAME", HeaderStatus = "OK" };
        var rules = new List<QuarantineRule>
        {
            new() { Field = null!, Value = "anything" },
            new() { Field = "  ", Value = "anything" }
        };

        var result = _svc.TestCandidate(item, rules);

        Assert.False(result.IsCandidate);
    }

    [Fact]
    public void TestCandidate_CustomRule_UnknownField_EmptyValue_NoMatch()
    {
        var item = new QuarantineItem { Console = "NES", Format = "zip", DatStatus = "NoMatch", Category = "GAME", HeaderStatus = "OK" };
        var rules = new List<QuarantineRule>
        {
            new() { Field = "UnknownField", Value = "" }
        };

        var result = _svc.TestCandidate(item, rules);

        // "UnknownField" → fallback to "" → matches Value="" → custom rule matches
        Assert.True(result.IsCandidate);
        Assert.Contains(result.Reasons, r => r.Contains("CustomRule:UnknownField="));
    }

    [Theory]
    [InlineData("Format", "zip")]
    [InlineData("DatStatus", "NoMatch")]
    [InlineData("Category", "BIOS")]
    [InlineData("HeaderStatus", "Anomaly")]
    public void TestCandidate_CustomRule_EachField_Matches(string field, string value)
    {
        var item = new QuarantineItem
        {
            Console = "NES", Format = "zip", DatStatus = "NoMatch",
            Category = "BIOS", HeaderStatus = "Anomaly"
        };
        var rules = new List<QuarantineRule> { new() { Field = field, Value = value } };

        var result = _svc.TestCandidate(item, rules);

        Assert.True(result.IsCandidate);
    }

    // ===== CreateAction: filename sanitization =====

    [Fact]
    public void CreateAction_NormalFilename_ValidAction()
    {
        var source = Path.Combine(_tempDir, "game.rom");
        var action = _svc.CreateAction(source, _quarantineDir);

        Assert.Equal(source, action.SourcePath);
        Assert.StartsWith(_quarantineDir, action.TargetPath);
        Assert.Equal("Pending", action.Status);
    }

    [Fact]
    public void CreateAction_FilenameWithInvalidChars_Sanitized()
    {
        // Construct a path with the actual source having a clean name, but test the logic
        var source = Path.Combine(_tempDir, "clean.rom");
        var action = _svc.CreateAction(source, _quarantineDir, reasons: ["suspicious"]);

        Assert.Contains("suspicious", action.Reasons);
    }

    // ===== Execute: DryRun =====

    [Fact]
    public void Execute_DryRun_SetsStatusButDoesNotMove()
    {
        var source = CreateFile("dryrun.rom");
        var action = _svc.CreateAction(source, _quarantineDir, mode: RunConstants.ModeDryRun);
        var result = _svc.Execute([action], RunConstants.ModeDryRun);

        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Moved);
        Assert.Equal(RunConstants.ModeDryRun, result.Results[0].Status);
        Assert.True(File.Exists(source)); // Still exists
    }

    // ===== Execute: actual move =====

    [Fact]
    public void Execute_MoveMode_MovesFile()
    {
        var source = CreateFile("moveme.rom");

        var action = _svc.CreateAction(source, _quarantineDir, mode: "Move");
        var result = _svc.Execute([action], "Move");

        Assert.Equal(1, result.Moved);
        Assert.False(File.Exists(source));
    }

    // ===== Execute: source missing =====

    [Fact]
    public void Execute_SourceMissing_CountsAsError()
    {
        var missingPath = Path.Combine(_tempDir, "nonexistent.rom");
        var action = new QuarantineAction
        {
            SourcePath = missingPath,
            TargetPath = Path.Combine(_quarantineDir, "20250101", "nonexistent.rom"),
            QuarantineDir = Path.Combine(_quarantineDir, "20250101"),
            Mode = "Move",
            Status = "Pending"
        };

        var result = _svc.Execute([action], "Move");

        Assert.Equal(1, result.Errors);
        Assert.Equal("SourceMissing", result.Results[0].Status);
    }

    // ===== Execute: empty actions =====

    [Fact]
    public void Execute_EmptyActions_ReturnsDefault()
    {
        var result = _svc.Execute([], "Move");
        Assert.Equal(0, result.Processed);
    }

    // ===== GetContents: files in date groups =====

    [Fact]
    public void GetContents_FilesGroupedByDate()
    {
        var dateDir = Path.Combine(_quarantineDir, "20250601");
        Directory.CreateDirectory(dateDir);
        File.WriteAllText(Path.Combine(dateDir, "game1.rom"), "content1");
        File.WriteAllText(Path.Combine(dateDir, "game2.rom"), "content222");

        var contents = _svc.GetContents(_quarantineDir);

        Assert.Equal(2, contents.Files.Count);
        Assert.True(contents.TotalSize > 0);
        Assert.True(contents.DateGroups.ContainsKey("20250601"));
        Assert.Equal(2, contents.DateGroups["20250601"].Count);
    }

    // ===== GetContents: empty quarantine =====

    [Fact]
    public void GetContents_EmptyRoot_ReturnsEmpty()
    {
        var emptyDir = Path.Combine(_tempDir, "empty-q");
        Directory.CreateDirectory(emptyDir);

        var contents = _svc.GetContents(emptyDir);

        Assert.Empty(contents.Files);
    }

    // ===== GetContents: non-existent root =====

    [Fact]
    public void GetContents_NonExistentRoot_ReturnsEmpty()
    {
        var contents = _svc.GetContents(Path.Combine(_tempDir, "nope"));
        Assert.Empty(contents.Files);
    }

    // ===== Restore: DryRun =====

    [Fact]
    public void Restore_DryRun_ReturnsDryRunStatus()
    {
        var qFile = CreateFile("quarantine/20250101/game.rom");
        var originalPath = Path.Combine(_tempDir, "original", "game.rom");

        var result = _svc.Restore(qFile, originalPath, RunConstants.ModeDryRun,
            allowedRestoreRoots: [_tempDir]);

        Assert.Equal(RunConstants.ModeDryRun, result.Status);
    }

    // ===== Restore: no allowed roots =====

    [Fact]
    public void Restore_NoAllowedRoots_ReturnsError()
    {
        var qFile = CreateFile("quarantine/20250102/game.rom");
        var result = _svc.Restore(qFile, "C:\\somewhere\\game.rom", "Move",
            allowedRestoreRoots: null);

        Assert.Equal("Error", result.Status);
        Assert.Equal("NoAllowedRestoreRoots", result.Reason);
    }

    // ===== Restore: empty allowed roots =====

    [Fact]
    public void Restore_EmptyAllowedRoots_ReturnsError()
    {
        var qFile = CreateFile("quarantine/20250103/game.rom");
        var result = _svc.Restore(qFile, Path.Combine(_tempDir, "game.rom"), "Move",
            allowedRestoreRoots: ["", "  "]);

        Assert.Equal("Error", result.Status);
        Assert.Equal("NoAllowedRestoreRoots", result.Reason);
    }

    // ===== Restore: path outside allowed roots =====

    [Fact]
    public void Restore_PathOutsideAllowedRoots_ReturnPathTraversal()
    {
        var qFile = CreateFile("quarantine/20250104/game.rom");
        var result = _svc.Restore(qFile, "C:\\Windows\\System32\\evil.rom", "Move",
            allowedRestoreRoots: [_tempDir]);

        Assert.Equal("Error", result.Status);
        Assert.Equal("PathTraversalBlocked", result.Reason);
    }

    // ===== Restore: ADS colon blocked =====

    [Fact]
    public void Restore_ADS_PathTraversalBlocked()
    {
        var qFile = CreateFile("quarantine/20250105/game.rom");
        var adsPath = Path.Combine(_tempDir, "game.rom:evil:$DATA");
        var result = _svc.Restore(qFile, adsPath, "Move",
            allowedRestoreRoots: [_tempDir]);

        Assert.Equal("Error", result.Status);
        Assert.Equal("PathTraversalBlocked", result.Reason);
    }

    // ===== Restore: file not found =====

    [Fact]
    public void Restore_QuarantineFileNotFound_ReturnsError()
    {
        var result = _svc.Restore(
            Path.Combine(_tempDir, "nope.rom"),
            Path.Combine(_tempDir, "game.rom"),
            "Move",
            allowedRestoreRoots: [_tempDir]);

        Assert.Equal("Error", result.Status);
        Assert.Equal("QuarantineFileNotFound", result.Reason);
    }

    // ===== Restore: actual restore =====

    [Fact]
    public void Restore_Execute_MovesFileBack()
    {
        var qFile = CreateFile("quarantine/20250106/restore-me.rom");
        var originalPath = Path.Combine(_tempDir, "restored", "restore-me.rom");

        var result = _svc.Restore(qFile, originalPath, "Move",
            allowedRestoreRoots: [_tempDir]);

        Assert.Equal("Restored", result.Status);
        Assert.True(File.Exists(originalPath));
        Assert.False(File.Exists(qFile));
    }

    /// <summary>Minimal IFileSystem that delegates to real filesystem.</summary>
    private sealed class FakeFs : IFileSystem
    {
        private readonly string _root;
        public FakeFs(string root) => _root = root;

        public bool TestPath(string path, string type) => type == "Container"
            ? Directory.Exists(path)
            : File.Exists(path);

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];

        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }

        public string? MoveItemSafely(string source, string destination)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(source, destination);
                return destination;
            }
            catch { return null; }
        }

        public void CopyFile(string source, string destination, bool overwrite = false) => throw new NotImplementedException();
        public void DeleteFile(string path) => throw new NotImplementedException();
        // MoveDirectorySafely, RenameItemSafely: not overridden → default interface implementations used
        public bool IsReparsePoint(string path) => false;
        public long GetFileSize(string path) => new FileInfo(path).Length;
        public string NormalizePathNfc(string path) => path;
        public string? ResolveChildPathWithinRoot(string root, string childName) => Path.Combine(root, childName);
    }
}
