using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Safety;
using Xunit;

namespace RomCleanup.Tests;

public sealed class SafetyValidatorTests
{
    // =========================================================================
    //  Profile Tests
    // =========================================================================

    [Fact]
    public void GetProfiles_ReturnsThreeProfiles()
    {
        var profiles = SafetyValidator.GetProfiles();
        Assert.Equal(3, profiles.Count);
        Assert.Contains("Conservative", profiles.Keys);
        Assert.Contains("Balanced", profiles.Keys);
        Assert.Contains("Expert", profiles.Keys);
    }

    [Fact]
    public void GetProfile_Conservative_IsStrict()
    {
        var profile = SafetyValidator.GetProfile("Conservative");
        Assert.True(profile.Strict);
        Assert.Contains("UserProfile", profile.ProtectedPathsText);
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
        Assert.True(result.BlockerCount > 0);
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
        }
        finally
        {
            Directory.Delete(dir, true);
        }
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

    // Fakes
    private sealed class StubToolRunner : IToolRunner
    {
        public string? FindTool(string toolName) => $@"C:\tools\{toolName}.exe";
        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(0, "v1.0.0", true);
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

    private sealed class StubFs : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => [];
        public bool MoveItemSafely(string src, string dest) => true;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
    }
}
