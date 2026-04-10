using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

public sealed class SafetyValidatorTests
{
    // =========================================================================
    //  Profile Tests
    // =========================================================================

    [Fact]
    public void GetProfile_Conservative_IsStrict()
    {
        var profile = SafetyValidator.GetProfile("Conservative");
        Assert.True(profile.Strict);
        Assert.Contains("Desktop", profile.ProtectedPathsText);
        Assert.Contains("Dokumente", profile.ProtectedPathsText);
    }

    [Fact]
    public void GetProfile_Unknown_FallsBackToBalanced()
    {
        var profile = SafetyValidator.GetProfile("NonExistent");
        Assert.Equal("Balanced", profile.Name);
    }

    // =========================================================================
    //  NormalizePath Tests
    // =========================================================================

    [Fact]
    public void NormalizePath_Null_ReturnsNull()
        => Assert.Null(SafetyValidator.NormalizePath(null));

    [Fact]
    public void NormalizePath_Whitespace_ReturnsNull()
        => Assert.Null(SafetyValidator.NormalizePath("  "));

    [Fact]
    public void NormalizePath_ValidPath_ReturnsFullPath()
    {
        var result = SafetyValidator.NormalizePath(@"C:\Temp\test");
        Assert.NotNull(result);
        Assert.True(Path.IsPathRooted(result));
        Assert.Equal(Path.GetFullPath(@"C:\Temp\test"), result);
    }

    // =========================================================================
    //  ValidateSandbox Tests
    // =========================================================================

    [Fact]
    public void ValidateSandbox_InvalidRoot_BlockerReported()
    {
        var validator = new SafetyValidator(new StubToolRunner(), new StubFs());
        var result = validator.ValidateSandbox(
            roots: [":::invalid:::path:::"],
            strictSafety: false);

        Assert.Equal("blocked", result.Status);
        Assert.Equal(1, result.BlockerCount);
        Assert.Contains(result.Blockers, b =>
            b.Contains("does not exist", StringComparison.OrdinalIgnoreCase) ||
            b.Contains("Invalid", StringComparison.OrdinalIgnoreCase));
        Assert.Single(result.PathChecks);
        Assert.Equal("blocked", result.PathChecks[0].Status);
    }

    [Fact]
    public void ValidateSandbox_DriveRoot_BlockerReported()
    {
        var validator = new SafetyValidator(new StubToolRunner(), new StubFs());
        // Use a path that resolves to a drive root
        var result = validator.ValidateSandbox(
            roots: [@"C:\"],
            strictSafety: false);

        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Blockers, b => b.Contains("drive root", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSandbox_NoExtensions_WarningIssued()
    {
        var dir = Path.Combine(Path.GetTempPath(), "safety_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var validator = new SafetyValidator(new StubToolRunner(), new StubFs());
            var result = validator.ValidateSandbox(roots: [dir], extensions: null);
            Assert.Contains(result.Warnings, w => w.Contains("extension", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ValidateSandbox_DatEnabledNoRoot_Warning()
    {
        var dir = Path.Combine(Path.GetTempPath(), "safety_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var validator = new SafetyValidator(new StubToolRunner(), new StubFs());
            var result = validator.ValidateSandbox(
                roots: [dir],
                useDat: true,
                datRoot: null,
                extensions: [".zip"]);
            Assert.Contains(result.Warnings, w => w.Contains("DAT", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    [Fact]
    public void ValidateSandbox_ValidRoot_StatusOk()
    {
        var dir = Path.Combine(Path.GetTempPath(), "safety_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            var validator = new SafetyValidator(new StubToolRunner(), new StubFs());
            var result = validator.ValidateSandbox(roots: [dir], extensions: [".zip"]);
            Assert.Equal("ok", result.Status);
            Assert.Equal(0, result.BlockerCount);
            Assert.Equal(0, result.WarningCount);
            Assert.Single(result.PathChecks);
            Assert.Equal("ok", result.PathChecks[0].Status);
            Assert.Empty(result.Blockers);
        }
        finally
        {
            Directory.Delete(dir, true);
        }
    }

    // =========================================================================
    //  IsProtectedSystemPath Tests
    // =========================================================================

    [Theory]
    [InlineData(@"C:\Windows")]
    [InlineData(@"C:\WINDOWS")]
    [InlineData(@"C:\Windows\System32")]
    public void IsProtectedSystemPath_SystemPaths_ReturnsTrue(string path)
        => Assert.True(SafetyValidator.IsProtectedSystemPath(path));

    [Theory]
    [InlineData(@"C:\Program Files")]
    [InlineData(@"C:\Program Files (x86)")]
    public void IsProtectedSystemPath_ProgramFiles_ReturnsTrue(string path)
        => Assert.True(SafetyValidator.IsProtectedSystemPath(path));

    [Theory]
    [InlineData(@"C:\Users\TestUser\ROMs")]
    [InlineData(@"D:\Games\SNES")]
    [InlineData(@"E:\Backup")]
    public void IsProtectedSystemPath_UserPaths_ReturnsFalse(string path)
        => Assert.False(SafetyValidator.IsProtectedSystemPath(path));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void IsProtectedSystemPath_EmptyOrWhitespace_ReturnsTrue(string path)
        => Assert.True(SafetyValidator.IsProtectedSystemPath(path));

    // =========================================================================
    //  IsDriveRoot Tests
    // =========================================================================

    [Theory]
    [InlineData(@"C:\")]
    [InlineData(@"C:")]
    [InlineData(@"D:\")]
    [InlineData(@"Z:")]
    public void IsDriveRoot_DriveRoots_ReturnsTrue(string path)
        => Assert.True(SafetyValidator.IsDriveRoot(path));

    [Theory]
    [InlineData(@"C:\Users")]
    [InlineData(@"D:\Games")]
    [InlineData(@"\\server\share")]
    [InlineData("")]
    [InlineData("   ")]
    public void IsDriveRoot_NonRoots_ReturnsFalse(string path)
        => Assert.False(SafetyValidator.IsDriveRoot(path));

    [Fact]
    public void EnsureSafeOutputPath_ValidPath_ReturnsNormalizedPath()
    {
        var path = Path.Combine(Path.GetTempPath(), "romulus-safe-output", "report.html");

        var normalized = SafetyValidator.EnsureSafeOutputPath(path);

        Assert.Equal(Path.GetFullPath(path), normalized);
    }

    [Fact]
    public void EnsureSafeOutputPath_ProtectedSystemPath_Throws()
    {
        var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(windowsDir))
            return;

        var ex = Assert.Throws<InvalidOperationException>(() =>
            SafetyValidator.EnsureSafeOutputPath(Path.Combine(windowsDir, "report.html")));

        Assert.Contains("protected system path", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // =========================================================================
    //  TestTools Tests
    // =========================================================================

    [Fact]
    public void TestTools_AllMissing_AllReportedMissing()
    {
        var validator = new SafetyValidator(new NullToolRunner(), new StubFs());
        var result = validator.TestTools();
        Assert.True(result.MissingCount > 0);
        Assert.Contains(result.Results, r => r.Status == "missing");
    }

    [Fact]
    public void TestTools_ToolFound_ReportsHealthy()
    {
        var validator = new SafetyValidator(new StubToolRunner(), new StubFs());
        var result = validator.TestTools();
        Assert.True(result.HealthyCount > 0);
        Assert.Contains(result.Results, r => r.Status == "healthy");
    }

    [Fact]
    public void TestTools_ForwardsTimeoutSeconds_ToToolRunner()
    {
        var runner = new TimeoutCapturingToolRunner();
        var validator = new SafetyValidator(runner, new StubFs());

        _ = validator.TestTools(timeoutSeconds: 13);

        Assert.NotEmpty(runner.Timeouts);
        Assert.All(runner.Timeouts, timeout => Assert.Equal(TimeSpan.FromSeconds(13), timeout));
    }

    // Fakes
    private sealed class StubToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => $@"C:\tools\{toolName}.exe";
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(0, "v1.0.0", true);
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel, TimeSpan? timeout, CancellationToken cancellationToken)
            => InvokeProcess(filePath, arguments, errorLabel);
        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "7-Zip 21.07", true);
    }

    private sealed class NullToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => null;
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(1, "", false);
        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(1, "", false);
    }

    private sealed class TimeoutCapturingToolRunner : IToolRunner
    {
        public List<TimeSpan?> Timeouts { get; } = new();

        public string? FindTool(string toolName) => $@"C:\tools\{toolName}.exe";

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(0, "v1.0.0", true);

        public ToolResult InvokeProcess(
            string filePath,
            string[] arguments,
            string? errorLabel,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            Timeouts.Add(timeout);
            return new ToolResult(0, "v1.0.0", true);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "7-Zip 21.07", true);
    }

    private sealed class StubFs : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => [];
        public string? MoveItemSafely(string src, string dest) => dest;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }
}
