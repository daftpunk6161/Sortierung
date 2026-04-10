using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for Contracts: IFileSystem default implementations,
/// IToolRunner default overloads, OperationResult branches, and model constructors.
/// Targets ~65 uncovered lines (IFileSystem 40 + IToolRunner 19 + models 6).
/// </summary>
public sealed class ContractsCoverageBoostTests : IDisposable
{
    private readonly string _tempDir;

    public ContractsCoverageBoostTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Con_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFile(string name, string content = "data")
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    // ==========================================
    // IFileSystem default implementations
    // ==========================================

    [Fact]
    public void FileExists_NullOrWhitespace_ReturnsFalse()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.False(fs.FileExists(null!));
        Assert.False(fs.FileExists(""));
        Assert.False(fs.FileExists("  "));
    }

    [Fact]
    public void FileExists_ExistingFile_ReturnsTrue()
    {
        var file = CreateFile("exists.rom");
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.True(fs.FileExists(file));
    }

    [Fact]
    public void FileExists_NonExistent_ReturnsFalse()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.False(fs.FileExists(Path.Combine(_tempDir, "nope.rom")));
    }

    [Fact]
    public void DirectoryExists_NullOrWhitespace_ReturnsFalse()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.False(fs.DirectoryExists(null!));
        Assert.False(fs.DirectoryExists(""));
        Assert.False(fs.DirectoryExists("  "));
    }

    [Fact]
    public void DirectoryExists_ExistingDir_ReturnsTrue()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.True(fs.DirectoryExists(_tempDir));
    }

    [Fact]
    public void DirectoryExists_NonExistent_ReturnsFalse()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.False(fs.DirectoryExists(Path.Combine(_tempDir, "nope")));
    }

    [Fact]
    public void GetDirectoryFiles_Default_ReturnsEmpty()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        var result = fs.GetDirectoryFiles(_tempDir, "*");
        Assert.Empty(result);
    }

    // ===== MoveItemSafely with allowedRoot =====

    [Fact]
    public void MoveItemSafely_WithinAllowedRoot_Succeeds()
    {
        var src = CreateFile("move-src.rom");
        var dest = Path.Combine(_tempDir, "sub", "move-dest.rom");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);

        IFileSystem fs = new MinimalFileSystem(_tempDir);
        var result = fs.MoveItemSafely(src, dest, _tempDir);

        Assert.NotNull(result);
    }

    [Fact]
    public void MoveItemSafely_OutsideAllowedRoot_ReturnsNull()
    {
        var src = CreateFile("move-outside.rom");
        var outsideDest = Path.Combine(Path.GetTempPath(), "outside-" + Guid.NewGuid().ToString("N")[..4], "file.rom");

        IFileSystem fs = new MinimalFileSystem(_tempDir);
        var result = fs.MoveItemSafely(src, outsideDest, _tempDir);

        Assert.Null(result);
    }

    [Fact]
    public void MoveItemSafely_EmptyAllowedRoot_Throws()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Throws<ArgumentException>(() =>
            fs.MoveItemSafely("src", "dest", ""));
    }

    // ===== RenameItemSafely (default) =====

    [Fact]
    public void RenameItemSafely_Default_ValidName_Calls()
    {
        var src = CreateFile("rename-me.rom");
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        var result = fs.RenameItemSafely(src, "renamed.rom");
        // The default implementation composes path and calls MoveItemSafely
        Assert.NotNull(result);
    }

    [Fact]
    public void RenameItemSafely_Default_NullSource_ReturnsNull()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Null(fs.RenameItemSafely(null!, "name"));
        Assert.Null(fs.RenameItemSafely("", "name"));
    }

    [Fact]
    public void RenameItemSafely_Default_NullNewName_ReturnsNull()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Null(fs.RenameItemSafely("source", null!));
        Assert.Null(fs.RenameItemSafely("source", ""));
    }

    [Fact]
    public void RenameItemSafely_Default_PathSegment_Throws()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Throws<InvalidOperationException>(() =>
            fs.RenameItemSafely("source.rom", "sub\\evil.rom"));
    }

    [Fact]
    public void RenameItemSafely_Default_ADS_Throws()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Throws<InvalidOperationException>(() =>
            fs.RenameItemSafely("source.rom", "evil:$DATA"));
    }

    [Fact]
    public void RenameItemSafely_Default_InvalidChars_Throws()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Throws<InvalidOperationException>(() =>
            fs.RenameItemSafely("source.rom", "evil<>.rom"));
    }

    // ===== MoveDirectorySafely (default) =====

    [Fact]
    public void MoveDirectorySafely_Default_NullSource_ReturnsFalse()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.False(fs.MoveDirectorySafely(null!, "dest"));
        Assert.False(fs.MoveDirectorySafely("", "dest"));
    }

    [Fact]
    public void MoveDirectorySafely_Default_NullDest_ReturnsFalse()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.False(fs.MoveDirectorySafely("source", null!));
        Assert.False(fs.MoveDirectorySafely("source", ""));
    }

    [Fact]
    public void MoveDirectorySafely_Default_TraversalInDest_Throws()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Throws<InvalidOperationException>(() =>
            fs.MoveDirectorySafely(_tempDir, Path.Combine(_tempDir, "..", "evil")));
    }

    [Fact]
    public void MoveDirectorySafely_Default_SamePath_Throws()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        Assert.Throws<InvalidOperationException>(() =>
            fs.MoveDirectorySafely(_tempDir, _tempDir));
    }

    [Fact]
    public void MoveDirectorySafely_Default_NonExistentSource_ReturnsFalse()
    {
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        var result = fs.MoveDirectorySafely(Path.Combine(_tempDir, "nope"), Path.Combine(_tempDir, "dest"));
        Assert.False(result);
    }

    [Fact]
    public void MoveDirectorySafely_Default_ExistingSource_ReturnsFalse()
    {
        var srcDir = Path.Combine(_tempDir, "existing-src");
        Directory.CreateDirectory(srcDir);
        IFileSystem fs = new MinimalFileSystem(_tempDir);
        // Default implementation returns false (no actual I/O)
        var result = fs.MoveDirectorySafely(srcDir, Path.Combine(_tempDir, "dest-dir"));
        Assert.False(result);
    }

    // ==========================================
    // IToolRunner default overloads
    // ==========================================

    [Fact]
    public void InvokeProcess_WithRequirement_DelegatesToFullOverload()
    {
        IToolRunner runner = new MinimalToolRunner();
        var result = runner.InvokeProcess("tool.exe", ["--help"], new ToolRequirement { ToolName = "test" }, "label");

        // Default 5-arg overload returns error because requirement is non-null
        Assert.NotNull(result);
        Assert.False(result.Success);
    }

    [Fact]
    public void InvokeProcess_WithTimeout_DelegatesToFullOverload()
    {
        IToolRunner runner = new MinimalToolRunner();
        var result = runner.InvokeProcess("tool.exe", ["--help"], "label", TimeSpan.FromSeconds(5), CancellationToken.None);

        Assert.NotNull(result);
        Assert.False(result.Success);
    }

    [Fact]
    public void InvokeProcess_WithCancellation_DelegatesToFullOverload()
    {
        var cts = new CancellationTokenSource();
        IToolRunner runner = new MinimalToolRunner();
        var result = runner.InvokeProcess("tool.exe", ["--help"], "label", null, cts.Token);

        Assert.NotNull(result);
        Assert.False(result.Success);
    }

    [Fact]
    public void InvokeProcess_NoAdvancedFeatures_DelegatesToBasic()
    {
        IToolRunner runner = new MinimalToolRunner();
        var result = runner.InvokeProcess("tool.exe", ["--help"], requirement: null, "label", null, CancellationToken.None);

        // No requirement, no timeout, no cancellable token → delegates to basic overload
        Assert.NotNull(result);
        Assert.True(result.Success);
    }

    // ==========================================
    // OperationResult branches
    // ==========================================

    [Fact]
    public void OperationResult_Ok_HasCorrectDefaults()
    {
        var result = OperationResult.Ok("works", 42);
        Assert.Equal("ok", result.Status);
        Assert.False(result.ShouldReturn);
        Assert.Equal("OK", result.Outcome);
        Assert.Equal(42, result.Value);
    }

    [Fact]
    public void OperationResult_Completed_Status()
    {
        var result = OperationResult.Completed("done");
        Assert.Equal("completed", result.Status);
        Assert.False(result.ShouldReturn);
        Assert.Equal("OK", result.Outcome);
    }

    [Fact]
    public void OperationResult_Skipped_Outcome()
    {
        var result = OperationResult.Skipped("not needed");
        Assert.Equal("skipped", result.Status);
        Assert.Equal("SKIP", result.Outcome);
        Assert.False(result.ShouldReturn);
    }

    [Fact]
    public void OperationResult_Blocked_ShouldReturn()
    {
        var result = OperationResult.Blocked("path issue");
        Assert.Equal("blocked", result.Status);
        Assert.True(result.ShouldReturn);
        Assert.Equal("ERROR", result.Outcome);
    }

    [Fact]
    public void OperationResult_Error_ShouldReturn()
    {
        var result = OperationResult.Error("failed");
        Assert.Equal("error", result.Status);
        Assert.True(result.ShouldReturn);
        Assert.Equal("ERROR", result.Outcome);
    }

    [Fact]
    public void OperationResult_MetaAndWarnings_Mutable()
    {
        var result = OperationResult.Ok();
        result.Meta["key"] = "value";
        result.Warnings.Add("warning-1");
        result.Metrics["time"] = 1.5;
        result.Artifacts.Add("report.html");

        Assert.Single(result.Meta);
        Assert.Single(result.Warnings);
        Assert.Single(result.Metrics);
        Assert.Single(result.Artifacts);
    }

    // ==========================================
    // Model construction coverage
    // ==========================================

    [Fact]
    public void RunHistoryEntry_DefaultProperties()
    {
        var entry = new RunHistoryEntry();
        Assert.Equal("", entry.FileName);
        Assert.Equal("", entry.Mode);
        Assert.Equal("", entry.Status);
        Assert.Empty(entry.Roots);
        Assert.Equal(0, entry.FileCount);
    }

    [Fact]
    public void ScanIndexEntry_DefaultProperties()
    {
        var entry = new ScanIndexEntry();
        Assert.Equal("", entry.Path);
        Assert.Equal("", entry.Fingerprint);
        Assert.Null(entry.Hash);
    }

    [Fact]
    public void ConversionReviewEntry_Construction()
    {
        var entry = new ConversionReviewEntry("rom.iso", ".chd", "size>4GB");
        Assert.Equal("rom.iso", entry.SourcePath);
        Assert.Equal(".chd", entry.TargetExtension);
        Assert.Equal("size>4GB", entry.SafetyReason);
    }

    [Fact]
    public void ConversionStepResult_Construction()
    {
        var step = new ConversionStepResult(1, "out.chd", true, VerificationStatus.Verified, null, 500);
        Assert.Equal(1, step.StepOrder);
        Assert.True(step.Success);
        Assert.Equal(VerificationStatus.Verified, step.Verification);
        Assert.Null(step.ErrorReason);
    }

    [Fact]
    public void ConversionStepResult_FailedStep()
    {
        var step = new ConversionStepResult(2, "", false, VerificationStatus.VerifyFailed, "disk full", 100);
        Assert.False(step.Success);
        Assert.Equal("disk full", step.ErrorReason);
    }

    // ==========================================
    // Minimal test doubles
    // ==========================================

    private sealed class MinimalFileSystem : IFileSystem
    {
        private readonly string _root;
        public MinimalFileSystem(string root) => _root = root;

        public bool TestPath(string literalPath, string pathType = "Any")
        {
            if (string.IsNullOrWhiteSpace(literalPath)) return false;
            return pathType switch
            {
                "Leaf" => File.Exists(literalPath),
                "Container" => Directory.Exists(literalPath),
                _ => File.Exists(literalPath) || Directory.Exists(literalPath)
            };
        }

        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? ext = null)
            => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];

        public string? MoveItemSafely(string src, string dest)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                File.Move(src, dest);
                return dest;
            }
            catch { return null; }
        }

        public void CopyFile(string s, string d, bool o = false) { }
        public void DeleteFile(string p) { }
        // RenameItemSafely: not overridden → default interface implementation is used
        // MoveDirectorySafely: not overridden → default interface implementation is used

        public bool IsReparsePoint(string path) => false;
        public long GetFileSize(string path) => new FileInfo(path).Length;
        public string NormalizePathNfc(string path) => path;
        public string? ResolveChildPathWithinRoot(string root, string child) => Path.Combine(root, child);
    }

    private sealed class MinimalToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => null;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(0, "ok", true);

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "ok", true);
    }
}
