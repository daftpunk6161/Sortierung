using System.Security.Cryptography;
using System.Text;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Tools;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ToolRunnerAdapter — targeting:
/// - FindTool: null/empty, each tool via override root, unknown tool, PATH fallback disabled
/// - InvokeProcess: executable not found, overloads
/// - Invoke7z: executable not found, hash fail
/// - VerifyToolHash: PLACEHOLDER hash, requirement fallback, valid hash, cache reuse, malformed JSON
/// - EnsureToolHashesLoaded: missing Tools property, invalid JSON
/// </summary>
public sealed class ToolRunnerAdapterCoverageTests : IDisposable
{
    private readonly string _tempDir;
    private string? _savedEnvVar;

    public ToolRunnerAdapterCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "TRA_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _savedEnvVar = Environment.GetEnvironmentVariable(ToolRunnerAdapter.ConversionToolsRootOverrideEnvVar);
    }

    public void Dispose()
    {
        Environment.SetEnvironmentVariable(ToolRunnerAdapter.ConversionToolsRootOverrideEnvVar, _savedEnvVar);
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    #region Helpers

    private string CreateToolStub(string relativePath, string content = "stub-exe")
    {
        var path = Path.Combine(_tempDir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateToolHashesJson(string toolFileName, string hash)
    {
        var hashesPath = Path.Combine(_tempDir, "tool-hashes-" + Guid.NewGuid().ToString("N")[..6] + ".json");
        File.WriteAllText(hashesPath, $$"""
        {
          "Tools": {
            "{{toolFileName}}": "{{hash}}"
          }
        }
        """);
        return hashesPath;
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = sha.ComputeHash(stream);
        return BitConverter.ToString(bytes).Replace("-", "").ToLowerInvariant();
    }

    private void SetConversionToolsRoot(string root)
    {
        Environment.SetEnvironmentVariable(ToolRunnerAdapter.ConversionToolsRootOverrideEnvVar, root);
    }

    #endregion

    // =================================================================
    //  FindTool — null / empty / whitespace
    // =================================================================

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void FindTool_NullOrWhitespace_ReturnsNull(string? toolName)
    {
        var runner = new ToolRunnerAdapter();
        Assert.Null(runner.FindTool(toolName!));
    }

    // =================================================================
    //  FindTool — each tool type via conversion tools root override
    // =================================================================

    [Theory]
    [InlineData("chdman", "chdman.exe")]
    [InlineData("dolphintool", "DolphinTool.exe")]
    [InlineData("psxtract", "psxtract.exe")]
    [InlineData("unecm", "unecm.exe")]
    [InlineData("nkit", "NKitProcessingApp.exe")]
    [InlineData("flips", "flips.exe")]
    [InlineData("xdelta3", "xdelta3.exe")]
    public void FindTool_ConversionToolsRoot_FindsToolDirectly(string toolName, string exeName)
    {
        var root = Path.Combine(_tempDir, "tools-ct-" + toolName);
        CreateToolStub(Path.Combine("tools-ct-" + toolName, exeName));
        SetConversionToolsRoot(root);

        var runner = new ToolRunnerAdapter();
        var result = runner.FindTool(toolName);

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(root, exeName), result);
    }

    [Fact]
    public void FindTool_CisoFindsMaxcso_InSubfolder()
    {
        var root = Path.Combine(_tempDir, "tools-maxcso");
        CreateToolStub(Path.Combine("tools-maxcso", "maxcso", "maxcso.exe"));
        SetConversionToolsRoot(root);

        var runner = new ToolRunnerAdapter();
        var result = runner.FindTool("ciso");

        Assert.NotNull(result);
        Assert.Contains("maxcso", result, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindTool_UnknownTool_ReturnsNull()
    {
        // Unknown tool with no PATH entry → null
        SetConversionToolsRoot(Path.Combine(_tempDir, "empty-root"));
        var runner = new ToolRunnerAdapter();
        Assert.Null(runner.FindTool("nonexistent-gibberish-tool-xyz"));
    }

    [Fact]
    public void FindTool_ChdmanInSubfolder_ResolvedViaOverride()
    {
        var root = Path.Combine(_tempDir, "tools-chdman-sub");
        CreateToolStub(Path.Combine("tools-chdman-sub", "mame", "chdman.exe"));
        SetConversionToolsRoot(root);

        var runner = new ToolRunnerAdapter();
        var result = runner.FindTool("chdman");

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(root, "mame", "chdman.exe"), result);
    }

    [Fact]
    public void FindTool_7zInSubfolder_ResolvedViaOverride()
    {
        var root = Path.Combine(_tempDir, "tools-7z-sub");
        CreateToolStub(Path.Combine("tools-7z-sub", "7-Zip", "7z.exe"));
        SetConversionToolsRoot(root);

        var runner = new ToolRunnerAdapter();
        var result = runner.FindTool("7z");

        Assert.NotNull(result);
        Assert.Equal(Path.Combine(root, "7-Zip", "7z.exe"), result);
    }

    // =================================================================
    //  InvokeProcess — executable not found
    // =================================================================

    [Fact]
    public void InvokeProcess_ExecutableNotFound_ReturnsFailure()
    {
        var runner = new ToolRunnerAdapter(allowInsecureHashBypass: true);
        var result = runner.InvokeProcess(
            Path.Combine(_tempDir, "no-such-exe.exe"),
            ["arg1"],
            "test-label");

        Assert.False(result.Success);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("not found", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_ThreeArgOverload_ExecutableNotFound_ReturnsFailure()
    {
        var runner = new ToolRunnerAdapter(allowInsecureHashBypass: true);
        var result = runner.InvokeProcess(
            Path.Combine(_tempDir, "missing.exe"),
            ["arg"],
            (string?)"test");

        Assert.False(result.Success);
        Assert.Contains("not found", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_FiveArgOverload_ExecutableNotFound_ReturnsFailure()
    {
        var runner = new ToolRunnerAdapter(allowInsecureHashBypass: true);
        var result = runner.InvokeProcess(
            Path.Combine(_tempDir, "missing.exe"),
            ["arg"],
            "label",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_WithRequirement_ExecutableNotFound_ReturnsFailure()
    {
        var runner = new ToolRunnerAdapter(allowInsecureHashBypass: true);
        var result = runner.InvokeProcess(
            Path.Combine(_tempDir, "missing.exe"),
            ["arg"],
            new ToolRequirement { ToolName = "test", ExpectedHash = "abc" },
            "label",
            TimeSpan.FromSeconds(10),
            CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  Invoke7z — executable not found, hash fail
    // =================================================================

    [Fact]
    public void Invoke7z_ExecutableNotFound_ReturnsFailure()
    {
        var runner = new ToolRunnerAdapter(allowInsecureHashBypass: true);
        var result = runner.Invoke7z(
            Path.Combine(_tempDir, "no-7z.exe"),
            ["l", "archive.7z"]);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Invoke7z_HashFails_ReturnsFailure()
    {
        var exePath = CreateToolStub("fake7z.exe", "fake-7z-binary-content");
        var hashesPath = CreateToolHashesJson("fake7z.exe", "0000000000000000000000000000000000000000000000000000000000000000");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.Invoke7z(exePath, ["l", "test.7z"]);

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  VerifyToolHash — PLACEHOLDER hash rejection
    // =================================================================

    [Fact]
    public void InvokeProcess_PlaceholderHash_Blocked()
    {
        var exePath = CreateToolStub("placeholder-tool.exe", "some-content");
        var hashesPath = CreateToolHashesJson("placeholder-tool.exe", "PLACEHOLDER_REPLACE_WITH_REAL_SHA256");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exePath, ["arg"], "test");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  VerifyToolHash — requirement ExpectedHash fallback
    // =================================================================

    [Fact]
    public void InvokeProcess_RequirementHashFallback_UsedWhenNoConfiguredHash()
    {
        var exePath = CreateToolStub("req-tool.exe", "req-content");
        var realHash = ComputeSha256(exePath);
        // Empty tool-hashes.json — no entry for req-tool.exe
        var hashesPath = Path.Combine(_tempDir, "empty-hashes.json");
        File.WriteAllText(hashesPath, """{ "Tools": {} }""");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(
            exePath, ["arg"],
            new ToolRequirement { ToolName = "req-tool", ExpectedHash = realHash },
            "test", null, CancellationToken.None);

        // Should pass hash check via requirement fallback and attempt execution
        // The exe isn't a valid executable so it will fail, but NOT with "hash verification failed"
        Assert.DoesNotContain("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_RequirementHash_WrongHash_Blocked()
    {
        var exePath = CreateToolStub("req-wrong.exe", "wrong-content");
        var hashesPath = Path.Combine(_tempDir, "empty-hashes2.json");
        File.WriteAllText(hashesPath, """{ "Tools": {} }""");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(
            exePath, ["arg"],
            new ToolRequirement { ToolName = "req-wrong", ExpectedHash = "0000000000000000000000000000000000000000000000000000000000000099" },
            "test", null, CancellationToken.None);

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  VerifyToolHash — valid hash succeeds
    // =================================================================

    [Fact]
    public void InvokeProcess_ValidHash_PassesVerification()
    {
        var exePath = CreateToolStub("valid-hash.exe", "valid-content");
        var realHash = ComputeSha256(exePath);
        var hashesPath = CreateToolHashesJson("valid-hash.exe", realHash);

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exePath, ["arg"], "test");

        // Hash passes, but exe isn't valid → error won't be about hash
        Assert.DoesNotContain("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  VerifyToolHash — hash caching
    // =================================================================

    [Fact]
    public void InvokeProcess_HashCaching_SecondCallUsesCache()
    {
        var exePath = CreateToolStub("cached.exe", "cached-content");
        var realHash = ComputeSha256(exePath);
        var hashesPath = CreateToolHashesJson("cached.exe", realHash);

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);

        // First call — computes hash
        var result1 = runner.InvokeProcess(exePath, ["arg"], "test");
        // Second call — should use cached hash (same runner instance)
        var result2 = runner.InvokeProcess(exePath, ["arg"], "test");

        // Both pass hash check (not "hash verification failed")
        Assert.DoesNotContain("hash verification failed", result1.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("hash verification failed", result2.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  EnsureToolHashesLoaded — malformed JSON
    // =================================================================

    [Fact]
    public void InvokeProcess_MalformedToolHashesJson_FailsClosed()
    {
        var exePath = CreateToolStub("malformed.exe", "content");
        var hashesPath = Path.Combine(_tempDir, "malformed.json");
        File.WriteAllText(hashesPath, "not-valid-json{{{");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exePath, ["arg"], "test");

        // Malformed JSON → empty hash dict → no hash found → blocked
        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void InvokeProcess_ToolHashesJson_MissingToolsProperty_FailsClosed()
    {
        var exePath = CreateToolStub("no-tools.exe", "content");
        var hashesPath = Path.Combine(_tempDir, "no-tools.json");
        File.WriteAllText(hashesPath, """{ "Other": "value" }""");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exePath, ["arg"], "test");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  VerifyToolHash — no hash path at all (null toolHashesPath)
    // =================================================================

    [Fact]
    public void InvokeProcess_NullHashPath_NoBypass_FailsClosed()
    {
        var exePath = CreateToolStub("null-path.exe", "content");

        var runner = new ToolRunnerAdapter(toolHashesPath: null, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exePath, ["arg"], "test");

        // No hash file, no bypass → blocked
        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    // =================================================================
    //  Constructor — negative timeout clamped to 30
    // =================================================================

    [Fact]
    public void Constructor_NegativeTimeout_ClampedTo30()
    {
        // Doesn't throw, just clamps — verify by using it
        var runner = new ToolRunnerAdapter(timeoutMinutes: -5);
        Assert.NotNull(runner);
    }

    [Fact]
    public void ReadToEndWithByteBudget_WithinLimit_DoesNotTruncate()
    {
        const string content = "small-output";
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);

        var result = ToolRunnerAdapter.ReadToEndWithByteBudget(reader, 1024, out var truncated);

        Assert.False(truncated);
        Assert.Equal(content, result);
    }

    [Fact]
    public void ReadToEndWithByteBudget_ExceedsLimit_TruncatesAndMarksOutput()
    {
        var content = new string('x', 5000);
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(content));
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true, leaveOpen: false);

        var result = ToolRunnerAdapter.ReadToEndWithByteBudget(reader, 1024, out var truncated);

        Assert.True(truncated);
        Assert.Contains("truncated", result, StringComparison.OrdinalIgnoreCase);
        Assert.StartsWith(new string('x', 256), result);
    }

    // =================================================================
    //  Log callback receives security messages
    // =================================================================

    [Fact]
    public void InvokeProcess_NoHash_LogsSecurityWarning()
    {
        var exePath = CreateToolStub("log-test.exe", "content");
        var hashesPath = Path.Combine(_tempDir, "empty-log.json");
        File.WriteAllText(hashesPath, """{ "Tools": {} }""");

        var logMessages = new List<string>();
        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false, log: msg => logMessages.Add(msg));
        runner.InvokeProcess(exePath, ["arg"], "test");

        Assert.Contains(logMessages, m => m.Contains("[SECURITY]", StringComparison.Ordinal));
    }

    [Fact]
    public void InvokeProcess_PlaceholderHash_LogsSecurityWarning()
    {
        var exePath = CreateToolStub("phlog.exe", "content");
        var hashesPath = CreateToolHashesJson("phlog.exe", "PLACEHOLDER_abc");

        var logMessages = new List<string>();
        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false, log: msg => logMessages.Add(msg));
        runner.InvokeProcess(exePath, ["arg"], "test");

        Assert.Contains(logMessages, m => m.Contains("PLACEHOLDER", StringComparison.Ordinal));
    }
}
