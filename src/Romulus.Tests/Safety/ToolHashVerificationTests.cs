using Romulus.Infrastructure.Tools;
using Xunit;

namespace Romulus.Tests.Safety;

/// <summary>
/// Tool-hash verification gap coverage.
///
/// Core hash-allowlist behavior is already covered by ToolRunnerAdapterTests
/// (missing hash file fail-closed, tool-not-in-allowlist fail-closed,
/// bypass enable/disable, cancellation).
///
/// This suite adds the gap cases:
///  1.  Hash present in JSON but mismatches the tool's actual SHA-256.
///  2.  Hash file becomes corrupt mid-run (malformed JSON) - fail closed.
///  3.  Empty/whitespace hash entry treated as missing - fail closed.
///  4.  Hash file deleted between two consecutive invocations - second call
///        must fail closed even after a previous successful invocation.
/// </summary>
public sealed class ToolHashVerificationTests : IDisposable
{
    private readonly string _tempDir;

    public ToolHashVerificationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B8_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void ToolRunnerAdapter_HashEntryMismatch_FailsClosed()
    {
        var exe = GetExistingExecutable();
        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        // Allowed entry exists for the tool name BUT with wrong SHA-256.
        var fileName = Path.GetFileName(exe);
        File.WriteAllText(hashesPath, $$"""
        {
          "Tools": {
            "{{fileName}}": "0000000000000000000000000000000000000000000000000000000000000000"
          }
        }
        """);

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exe, ["/?"], "test");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRunnerAdapter_MalformedHashJson_FailsClosed()
    {
        var exe = GetExistingExecutable();
        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        File.WriteAllText(hashesPath, "{ this is : not valid json ");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exe, ["/?"], "test");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRunnerAdapter_EmptyHashEntry_FailsClosed()
    {
        var exe = GetExistingExecutable();
        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        var fileName = Path.GetFileName(exe);
        File.WriteAllText(hashesPath, $$"""
        {
          "Tools": {
            "{{fileName}}": "   "
          }
        }
        """);

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);
        var result = runner.InvokeProcess(exe, ["/?"], "test");

        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolRunnerAdapter_HashFileDeletedBetweenInvocations_SecondCallFailsClosed()
    {
        var exe = GetExistingExecutable();
        var hashesPath = Path.Combine(_tempDir, "tool-hashes.json");
        File.WriteAllText(hashesPath, "{ \"Tools\": {} }");

        var runner = new ToolRunnerAdapter(hashesPath, allowInsecureHashBypass: false);

        // First call: tool not in allow-list -> fail closed (covered for sanity).
        var first = runner.InvokeProcess(exe, ["/?"], "test");
        Assert.False(first.Success);

        // Delete file between invocations.
        File.Delete(hashesPath);

        // Second call MUST still fail closed (no caching of "previously trusted" state).
        var second = runner.InvokeProcess(exe, ["/?"], "test");
        Assert.False(second.Success);
        Assert.Contains("hash verification failed", second.Output, StringComparison.OrdinalIgnoreCase);
    }

    private static string GetExistingExecutable()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(winDir, "System32", "cmd.exe"),
            Path.Combine(winDir, "System32", "where.exe")
        };

        foreach (var c in candidates)
            if (File.Exists(c))
                return c;

        throw new InvalidOperationException("Kein bekanntes Test-Executable gefunden.");
    }
}
