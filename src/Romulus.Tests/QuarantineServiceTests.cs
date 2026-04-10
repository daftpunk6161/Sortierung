using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Quarantine;
using Xunit;

namespace Romulus.Tests;

public class QuarantineServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly QuarantineService _svc;
    private readonly FakeQuarantineFs _fs;

    public QuarantineServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"romqt_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _fs = new FakeQuarantineFs(_tempDir);
        _svc = new QuarantineService(_fs);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // --- TestCandidate ---

    [Fact]
    public void TestCandidate_UnknownConsoleAndFormat_IsCandidate()
    {
        var item = new QuarantineItem { Console = "Unknown", Format = "Unknown" };
        var result = _svc.TestCandidate(item);
        Assert.True(result.IsCandidate);
        Assert.Contains("UnknownConsoleAndFormat", result.Reasons);
    }

    [Fact]
    public void TestCandidate_EmptyConsoleAndFormat_IsCandidate()
    {
        var item = new QuarantineItem();
        var result = _svc.TestCandidate(item);
        Assert.True(result.IsCandidate);
    }

    [Fact]
    public void TestCandidate_KnownConsole_NotCandidate()
    {
        var item = new QuarantineItem { Console = "PS1", Format = "CHD", Category = "GAME" };
        var result = _svc.TestCandidate(item);
        Assert.False(result.IsCandidate);
    }

    [Fact]
    public void TestCandidate_NoDatMatchAndNotGame_IsCandidate()
    {
        var item = new QuarantineItem { Console = "PS1", Format = "BIN", DatStatus = "NoMatch", Category = "JUNK" };
        var result = _svc.TestCandidate(item);
        Assert.True(result.IsCandidate);
        Assert.Contains("NoDatMatchAndNotGame", result.Reasons);
    }

    [Fact]
    public void TestCandidate_HeaderAnomaly_IsCandidate()
    {
        var item = new QuarantineItem { Console = "SNES", Format = "SFC", HeaderStatus = "Anomaly" };
        var result = _svc.TestCandidate(item);
        Assert.True(result.IsCandidate);
        Assert.Contains("HeaderAnomaly", result.Reasons);
    }

    [Fact]
    public void TestCandidate_HeaderCorrupted_IsCandidate()
    {
        var item = new QuarantineItem { Console = "SNES", Format = "SFC", HeaderStatus = "Corrupted" };
        var result = _svc.TestCandidate(item);
        Assert.True(result.IsCandidate);
    }

    [Fact]
    public void TestCandidate_CustomRule_Matches()
    {
        var item = new QuarantineItem { Console = "NES", Format = "NES", Category = "GAME" };
        var rules = new List<QuarantineRule> { new() { Field = "Category", Value = "GAME" } };
        var result = _svc.TestCandidate(item, rules);
        Assert.True(result.IsCandidate);
        Assert.Contains(result.Reasons, r => r.StartsWith("CustomRule:"));
    }

    [Fact]
    public void TestCandidate_MultipleReasons()
    {
        var item = new QuarantineItem { Console = "Unknown", Format = "Unknown", HeaderStatus = "Corrupted" };
        var result = _svc.TestCandidate(item);
        Assert.True(result.IsCandidate);
        Assert.True(result.Reasons.Count >= 2);
    }

    // --- CreateAction ---

    [Fact]
    public void CreateAction_GeneratesCorrectPaths()
    {
        var action = _svc.CreateAction(@"C:\Roms\bad.zip", @"C:\Quarantine", new[] { "HeaderAnomaly" }.ToList());
        Assert.Equal(@"C:\Roms\bad.zip", action.SourcePath);
        Assert.Contains("bad.zip", action.TargetPath);
        Assert.Equal("Pending", action.Status);
        Assert.Single(action.Reasons);
    }

    [Fact]
    public void CreateAction_TargetHasDateDirectory()
    {
        var action = _svc.CreateAction(@"C:\Roms\file.bin", @"C:\Q");
        var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
        Assert.Contains(dateStr, action.QuarantineDir);
    }

    // --- Execute ---

    [Fact]
    public void Execute_DryRun_NoFileMoved()
    {
        var action = _svc.CreateAction(@"C:\Roms\test.zip", @"C:\Q");
        var result = _svc.Execute(new[] { action }, "DryRun");
        Assert.Equal(1, result.Processed);
        Assert.Equal(0, result.Moved);
        Assert.Equal("DryRun", result.Results[0].Status);
    }

    [Fact]
    public void Execute_EmptyActions_ReturnsZero()
    {
        var result = _svc.Execute(Array.Empty<QuarantineAction>());
        Assert.Equal(0, result.Processed);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void Execute_Move_FileExists()
    {
        var srcFile = Path.Combine(_tempDir, "test.zip");
        File.WriteAllText(srcFile, "data");

        var qRoot = Path.Combine(_tempDir, "quarantine");
        var action = _svc.CreateAction(srcFile, qRoot);
        var result = _svc.Execute(new[] { action }, "Move");

        Assert.Equal(1, result.Moved);
        Assert.Equal("Moved", result.Results[0].Status);
    }

    [Fact]
    public void Execute_Move_SourceMissing()
    {
        var action = _svc.CreateAction(Path.Combine(_tempDir, "nonexistent.zip"), Path.Combine(_tempDir, "q"));
        var result = _svc.Execute(new[] { action }, "Move");
        Assert.Equal(1, result.Errors);
        Assert.Equal("SourceMissing", result.Results[0].Status);
    }

    // --- GetContents ---

    [Fact]
    public void GetContents_EmptyDir_ReturnsEmpty()
    {
        var contents = _svc.GetContents(Path.Combine(_tempDir, "empty_quarantine"));
        Assert.Empty(contents.Files);
        Assert.Equal(0, contents.TotalSize);
    }

    [Fact]
    public void GetContents_WithFiles_ReturnsGrouped()
    {
        var qRoot = Path.Combine(_tempDir, "q");
        var dateDir = Path.Combine(qRoot, "20240101");
        Directory.CreateDirectory(dateDir);
        File.WriteAllText(Path.Combine(dateDir, "a.zip"), "test");
        File.WriteAllText(Path.Combine(dateDir, "b.zip"), "testdata");

        var contents = _svc.GetContents(qRoot);
        Assert.Equal(2, contents.Files.Count);
        Assert.True(contents.TotalSize > 0);
        Assert.Single(contents.DateGroups);
        Assert.True(contents.DateGroups.ContainsKey("20240101"));
    }

    // --- Restore ---

    [Fact]
    public void Restore_DryRun_NoMove()
    {
        var file = Path.Combine(_tempDir, "qfile.bin");
        File.WriteAllText(file, "data");
        var result = _svc.Restore(file, Path.Combine(_tempDir, "original.bin"), "DryRun", [_tempDir]);
        Assert.Equal("DryRun", result.Status);
        Assert.True(File.Exists(file)); // still in quarantine
    }

    [Fact]
    public void Restore_FileNotFound()
    {
        var result = _svc.Restore(Path.Combine(_tempDir, "gone.bin"), @"C:\orig.bin", allowedRestoreRoots: [@"C:\"]);
        Assert.Equal("Error", result.Status);
        Assert.Equal("QuarantineFileNotFound", result.Reason);
    }

    [Fact]
    public void Restore_NoAllowedRoots_ReturnsError()
    {
        var file = Path.Combine(_tempDir, "qfile_noroots.bin");
        File.WriteAllText(file, "data");

        var resultNull = _svc.Restore(file, Path.Combine(_tempDir, "original.bin"), "DryRun");
        Assert.Equal("Error", resultNull.Status);
        Assert.Equal("NoAllowedRestoreRoots", resultNull.Reason);

        var resultEmpty = _svc.Restore(file, Path.Combine(_tempDir, "original.bin"), "DryRun", []);
        Assert.Equal("Error", resultEmpty.Status);
        Assert.Equal("NoAllowedRestoreRoots", resultEmpty.Reason);
    }

    [Fact]
    public void Restore_WithAllowedRoot_InsideRoot_AllowsDryRun()
    {
        var file = Path.Combine(_tempDir, "qfile2.bin");
        File.WriteAllText(file, "data");

        var allowedRoot = Path.Combine(_tempDir, "restore-root");
        Directory.CreateDirectory(allowedRoot);
        var originalPath = Path.Combine(allowedRoot, "original.bin");

        var result = _svc.Restore(file, originalPath, "DryRun", [allowedRoot]);

        Assert.Equal("DryRun", result.Status);
        Assert.NotEqual("PathTraversalBlocked", result.Reason);
    }

    [Fact]
    public void Restore_WithAllowedRoot_OutsideRoot_Blocked()
    {
        var file = Path.Combine(_tempDir, "qfile3.bin");
        File.WriteAllText(file, "data");

        var allowedRoot = Path.Combine(_tempDir, "restore-root");
        Directory.CreateDirectory(allowedRoot);
        var outsidePath = Path.Combine(_tempDir, "outside", "original.bin");

        var result = _svc.Restore(file, outsidePath, "DryRun", [allowedRoot]);

        Assert.Equal("Error", result.Status);
        Assert.Equal("PathTraversalBlocked", result.Reason);
    }

    [Theory]
    [InlineData(@"\\server\share\restore\file.bin")]
    [InlineData(@"C:/Windows/System32/drivers/etc/hosts")]
    [InlineData(@"C:\outside\restore.bin:ads")]
    public void Restore_WithAllowedRoot_UnsafeTargetVariants_Blocked(string unsafeTarget)
    {
        var file = Path.Combine(_tempDir, "qfile4.bin");
        File.WriteAllText(file, "data");

        var allowedRoot = Path.Combine(_tempDir, "restore-root");
        Directory.CreateDirectory(allowedRoot);

        var result = _svc.Restore(file, unsafeTarget, "DryRun", [allowedRoot]);

        Assert.Equal("Error", result.Status);
        Assert.Equal("PathTraversalBlocked", result.Reason);
    }

    // --- Fake FileSystem ---

    private sealed class FakeQuarantineFs : IFileSystem
    {
        private readonly string _root;
        public FakeQuarantineFs(string root) => _root = root;

        public bool TestPath(string path, string type)
        {
            return type switch
            {
                "Leaf" => File.Exists(path),
                "Container" => Directory.Exists(path),
                _ => File.Exists(path) || Directory.Exists(path)
            };
        }

        public string EnsureDirectory(string path)
        {
            if (!Directory.Exists(path))
                Directory.CreateDirectory(path);
            return path;
        }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null)
        {
            if (!Directory.Exists(root)) return Array.Empty<string>();
            var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            if (extensions != null)
            {
                var extSet = new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase);
                files = files.Where(f => extSet.Contains(Path.GetExtension(f))).ToArray();
            }
            return files;
        }

        public string? MoveItemSafely(string source, string destination)
        {
            var dir = Path.GetDirectoryName(destination);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);
            File.Move(source, destination);
            return destination;
        }

        public string ResolveChildPathWithinRoot(string root, string childPath)
        {
            var resolved = Path.GetFullPath(Path.Combine(root, childPath));
            if (!resolved.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Path traversal detected");
            return resolved;
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }
}
