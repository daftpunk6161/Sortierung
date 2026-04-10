using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for SafetyValidator — targeting:
/// - NormalizePath SEC checks (ADS, extended paths, trailing dots/spaces)
/// - ValidateSandbox protected paths, convert tool checks, audit root failure
/// - TestTools probe warning + exception paths
/// - EnsureSafeOutputPath UNC blocking
/// </summary>
public sealed class SafetyValidatorCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public SafetyValidatorCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "SafeValCov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    #region Test Fakes

    private sealed class MinimalFs : IFileSystem
    {
        public bool EnsureDirectoryThrows { get; set; }

        public bool TestPath(string literalPath, string pathType = "Any") =>
            File.Exists(literalPath) || Directory.Exists(literalPath);
        public string EnsureDirectory(string path)
        {
            if (EnsureDirectoryThrows) throw new IOException("Permission denied");
            Directory.CreateDirectory(path);
            return path;
        }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? ext = null) => [];
        public string? MoveItemSafely(string src, string dest) => dest;
        public string? ResolveChildPathWithinRoot(string root, string rel) =>
            Path.Combine(root, rel);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string src, string dest, bool overwrite = false) { }
    }

    private sealed class ConfigurableToolRunner : IToolRunner
    {
        public Func<string, string?>? FindToolFunc { get; set; }
        public Func<string[], ToolResult>? InvokeFunc { get; set; }

        public string? FindTool(string name) =>
            FindToolFunc?.Invoke(name);

        public ToolResult InvokeProcess(string filePath, string[] args, string? errorLabel = null) =>
            InvokeFunc?.Invoke(args) ?? new ToolResult(1, "", false);

        public ToolResult InvokeProcess(string filePath, string[] args,
            ToolRequirement? req, string? errorLabel = null) =>
            InvokeProcess(filePath, args, errorLabel);

        public ToolResult InvokeProcess(string filePath, string[] args,
            string? errorLabel, TimeSpan? timeout, CancellationToken ct) =>
            InvokeFunc?.Invoke(args) ?? new ToolResult(1, "", false);

        public ToolResult InvokeProcess(string filePath, string[] args,
            ToolRequirement? req, string? errorLabel, TimeSpan? timeout, CancellationToken ct) =>
            InvokeProcess(filePath, args, errorLabel, timeout, ct);

        public ToolResult Invoke7z(string path, string[] args) =>
            InvokeProcess(path, args);
    }

    #endregion

    // =================================================================
    //  NormalizePath — SEC path checks
    // =================================================================

    [Theory]
    [InlineData(@"\\?\C:\secret")]
    [InlineData(@"\\.\COM1")]
    public void NormalizePath_ExtendedOrDevicePath_ReturnsNull(string path)
    {
        Assert.Null(SafetyValidator.NormalizePath(path));
    }

    [Fact]
    public void NormalizePath_ADS_ReturnsNull()
    {
        // Alternate Data Stream after drive letter portion
        Assert.Null(SafetyValidator.NormalizePath(@"C:\temp\file.txt:hidden"));
    }

    [Theory]
    [InlineData(@"C:\folder.\file.txt")]
    [InlineData(@"C:\path \nested")]
    public void NormalizePath_TrailingDotOrSpace_InSegment_ReturnsNull(string path)
    {
        Assert.Null(SafetyValidator.NormalizePath(path));
    }

    [Fact]
    public void NormalizePath_ValidAbsolutePath_ReturnsFullPath()
    {
        var result = SafetyValidator.NormalizePath(_tempDir);
        Assert.NotNull(result);
        Assert.Equal(Path.GetFullPath(_tempDir), result);
    }

    // =================================================================
    //  ValidateSandbox gaps
    // =================================================================

    [Fact]
    public void ValidateSandbox_RootDoesNotExist_Blocked()
    {
        var tools = new ConfigurableToolRunner();
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.ValidateSandbox(
            [Path.Combine(_tempDir, "nonexistent_root")]);

        Assert.Equal("blocked", result.Status);
        Assert.True(result.BlockerCount > 0);
        Assert.Contains(result.Blockers, b => b.Contains("does not exist"));
    }

    [Fact]
    public void ValidateSandbox_RootInsideProtectedPath_Blocked()
    {
        var tools = new ConfigurableToolRunner();
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        // Use a custom protected path pointing to our temp dir
        var result = sv.ValidateSandbox(
            [_tempDir],
            protectedPathsText: _tempDir);

        Assert.Equal("blocked", result.Status);
        Assert.Contains(result.Blockers, b => b.Contains("protected path"));
    }

    [Fact]
    public void ValidateSandbox_ConvertEnabled_ChdmanNotFound_Warning()
    {
        var tools = new ConfigurableToolRunner
        {
            FindToolFunc = _ => null
        };
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.ValidateSandbox(
            [_tempDir],
            convertEnabled: true);

        Assert.Contains(result.Warnings, w => w.Contains("chdman"));
    }

    [Fact]
    public void ValidateSandbox_ConvertEnabled_ChdmanOverride_NoWarning()
    {
        var tools = new ConfigurableToolRunner
        {
            FindToolFunc = _ => null // normal lookup fails
        };
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.ValidateSandbox(
            [_tempDir],
            convertEnabled: true,
            toolOverrides: new Dictionary<string, string> { ["chdman"] = "custom_chdman.exe" });

        Assert.DoesNotContain(result.Warnings, w => w.Contains("chdman"));
    }

    [Fact]
    public void ValidateSandbox_AuditRootCreationFails_Blocked()
    {
        var tools = new ConfigurableToolRunner();
        var fs = new MinimalFs { EnsureDirectoryThrows = true };
        var sv = new SafetyValidator(tools, fs);
        var nonExistentAudit = Path.Combine(_tempDir, "audit_fail_" + Guid.NewGuid().ToString("N")[..4]);

        var result = sv.ValidateSandbox(
            [_tempDir],
            auditRoot: nonExistentAudit);

        Assert.Contains(result.Blockers, b => b.Contains("audit"));
    }

    [Fact]
    public void ValidateSandbox_DatRootDoesNotExist_Warning()
    {
        var tools = new ConfigurableToolRunner();
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.ValidateSandbox(
            [_tempDir],
            useDat: true,
            datRoot: Path.Combine(_tempDir, "missing_dats"));

        Assert.Contains(result.Warnings, w => w.Contains("DAT root does not exist"));
    }

    [Fact]
    public void ValidateSandbox_MultipleRoots_MixedPathChecks()
    {
        var validRoot = _tempDir;
        var invalidRoot = ":::bad:::path:::";
        var tools = new ConfigurableToolRunner();
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.ValidateSandbox([validRoot, invalidRoot]);

        Assert.True(result.PathChecks.Count >= 2);
        Assert.Contains(result.PathChecks, pc => pc.Status == "ok");
        Assert.Contains(result.PathChecks, pc => pc.Status == "blocked");
    }

    // =================================================================
    //  TestTools gaps
    // =================================================================

    [Fact]
    public void TestTools_ProbeNonZeroExit_ReportsWarning()
    {
        var tools = new ConfigurableToolRunner
        {
            FindToolFunc = name => $"fake_{name}.exe",
            InvokeFunc = _ => new ToolResult(42, "failed", false)
        };
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.TestTools();

        Assert.True(result.WarningCount > 0);
        Assert.Contains(result.Results, r => r.Status == "warning");
    }

    [Fact]
    public void TestTools_ProbeThrows_ReportsError()
    {
        var tools = new ConfigurableToolRunner
        {
            FindToolFunc = name => $"fake_{name}.exe",
            InvokeFunc = _ => throw new InvalidOperationException("crash!")
        };
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.TestTools();

        Assert.True(result.WarningCount > 0);
        Assert.Contains(result.Results, r => r.Status == "error" && r.Error == "crash!");
    }

    [Fact]
    public void TestTools_WithOverride_UsesOverridePath()
    {
        var tools = new ConfigurableToolRunner
        {
            FindToolFunc = _ => null, // normal lookup fails
            InvokeFunc = _ => new ToolResult(0, "v1.0", true)
        };
        var fs = new MinimalFs();
        var sv = new SafetyValidator(tools, fs);

        var result = sv.TestTools(
            toolOverrides: new Dictionary<string, string> { ["chdman"] = @"C:\tools\chdman.exe" });

        Assert.Contains(result.Results, r =>
            r.Tool == "chdman" && r.Status == "healthy" && r.Path == @"C:\tools\chdman.exe");
    }

    // =================================================================
    //  EnsureSafeOutputPath gaps
    // =================================================================

    [Fact]
    public void EnsureSafeOutputPath_UncBlocked_WhenDisallowed()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SafetyValidator.EnsureSafeOutputPath(@"\\server\share\output", allowUnc: false));
    }

    [Fact]
    public void EnsureSafeOutputPath_UNC_AllowedByDefault()
    {
        // UNC path with default allowUnc=true should not throw for UNC validation
        // (may still throw for other reasons like protected path)
        // We just verify it doesn't throw the UNC-specific error
        try
        {
            SafetyValidator.EnsureSafeOutputPath(@"\\server\share\roms");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("UNC"))
        {
            Assert.Fail("UNC should be allowed by default");
        }
        catch (InvalidOperationException)
        {
            // Other exceptions (e.g., reparse point check) are acceptable
        }
    }

    [Fact]
    public void EnsureSafeOutputPath_EmptyPath_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SafetyValidator.EnsureSafeOutputPath(""));
    }

    [Fact]
    public void EnsureSafeOutputPath_DriveRoot_Throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            SafetyValidator.EnsureSafeOutputPath(@"C:\"));
    }

    [Fact]
    public void EnsureSafeOutputPath_ValidTempPath_ReturnsNormalized()
    {
        var path = Path.Combine(_tempDir, "safe_output");
        var result = SafetyValidator.EnsureSafeOutputPath(path);
        Assert.Equal(Path.GetFullPath(path), result);
    }

    // =================================================================
    //  GetProfile + IsDriveRoot edge cases
    // =================================================================

    [Fact]
    public void GetProfile_Expert_LessRestrictive()
    {
        var profile = SafetyValidator.GetProfile("Expert");
        Assert.Equal("Expert", profile.Name);
        Assert.False(profile.Strict);
    }

    [Theory]
    [InlineData("c:", true)]
    [InlineData("Z:", true)]
    [InlineData(@"C:\Users", false)]
    [InlineData("", false)]
    public void IsDriveRoot_VariousInputs(string path, bool expected)
    {
        Assert.Equal(expected, SafetyValidator.IsDriveRoot(path));
    }
}
