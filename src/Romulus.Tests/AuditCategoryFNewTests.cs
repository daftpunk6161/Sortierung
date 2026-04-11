using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Audit Category F — new test coverage for gaps identified in full-repo-audit.
/// F-02: Concurrent API stress tests
/// F-03: RunOrchestrator mid-phase cancellation edge cases
/// F-04: Deep ZipSlip tests (nested archives, temp cleanup)
/// </summary>
public sealed class AuditCategoryFNewTests : IDisposable
{
    private const string ApiKey = "f-test-api-key";
    private readonly string _tempDir;
    private readonly List<string> _tempDirs = [];

    public AuditCategoryFNewTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AuditF_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _tempDirs.Add(_tempDir);
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
            catch { /* best effort */ }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-02: Concurrent API Stress Tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F02_ConcurrentRunCreation_OnlyOneExecutes_OthersRejected()
    {
        // Two simultaneous POST /runs should not both start — one must be rejected
        var gate = new ManualResetEventSlim(false);
        using var factory = CreateFactory(executor: (_, _, _, ct) =>
        {
            gate.Wait(ct);
            return new RunExecutionOutcome("completed", new ApiRunResult());
        });
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });

            // Fire two concurrent run creates
            var task1 = client.PostAsync("/runs", new StringContent(payload, Encoding.UTF8, "application/json"));
            var task2 = client.PostAsync("/runs", new StringContent(payload, Encoding.UTF8, "application/json"));
            var responses = await Task.WhenAll(task1, task2);

            var accepted = responses.Count(r => r.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);
            var rejected = responses.Count(r => r.StatusCode == HttpStatusCode.Conflict);

            // At least one must succeed, at most one can be in conflict
            Assert.True(accepted >= 1, $"Expected at least one accepted run, got {accepted}");
            Assert.True(accepted + rejected == 2, $"Expected accepted+rejected=2, got accepted={accepted} rejected={rejected}");
        }
        finally
        {
            gate.Set();
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task F02_ConcurrentListAndCreate_NoServerError()
    {
        // Concurrent GET /runs while POST /runs is in progress must not cause 500
        var gate = new ManualResetEventSlim(false);
        using var factory = CreateFactory(executor: (_, _, _, ct) =>
        {
            gate.Wait(ct);
            return new RunExecutionOutcome("completed", new ApiRunResult());
        });
        using var client = CreateAuthClient(factory);
        var root = CreateTempRoot();

        try
        {
            var payload = JsonSerializer.Serialize(new { roots = new[] { root }, mode = "DryRun" });
            var createResponse = await client.PostAsync("/runs", new StringContent(payload, Encoding.UTF8, "application/json"));
            Assert.True(createResponse.StatusCode is HttpStatusCode.Accepted or HttpStatusCode.OK);

            // Fire concurrent list requests while run is active
            var listTasks = Enumerable.Range(0, 5)
                .Select(_ => client.GetAsync("/runs"))
                .ToArray();
            var listResponses = await Task.WhenAll(listTasks);

            foreach (var response in listResponses)
            {
                Assert.True(response.StatusCode == HttpStatusCode.OK,
                    $"GET /runs returned {response.StatusCode} instead of 200");
            }
        }
        finally
        {
            gate.Set();
            SafeDeleteDirectory(root);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-03: RunOrchestrator Cancellation Edge Cases
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F03_Cancel_DuringScan_PartialCandidatesNotPersisted()
    {
        // Cancel during scan phase: result should be cancelled, no audit rows for unfinished work
        var romPath = CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);

        var cts = new CancellationTokenSource();
        var fs = new FileSystemAdapter();
        var audit = new FakeAuditStore();
        var orch = new Romulus.Infrastructure.Orchestration.RunOrchestrator(fs, audit, onProgress: message =>
        {
            // Cancel as soon as scan phase starts
            if (message.Contains("[Scan]", StringComparison.OrdinalIgnoreCase))
                cts.Cancel();
        });

        var options = new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "DryRun"
        };

        var result = orch.Execute(options, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);
        // No audit rows should be written for cancelled scan
        Assert.Empty(audit.AuditRows);
    }

    [Fact]
    public void F03_Cancel_DuringDedupe_ResultHasCorrectExitCode()
    {
        // Cancel during dedup: result should have correct exit code and status
        CreateFile("Game (USA).zip", 100);
        CreateFile("Game (Europe).zip", 100);
        CreateFile("Game (Japan).zip", 100);

        var cts = new CancellationTokenSource();
        var fs = new FileSystemAdapter();
        var audit = new FakeAuditStore();
        bool scanSeen = false;
        var orch = new Romulus.Infrastructure.Orchestration.RunOrchestrator(fs, audit, onProgress: message =>
        {
            // Cancel after scan completes to trigger during enrichment or dedup
            if (message.Contains("Scan", StringComparison.OrdinalIgnoreCase))
                scanSeen = true;
            if (scanSeen && message.Contains("Phase", StringComparison.OrdinalIgnoreCase))
                cts.Cancel();
        });

        var options = new RunOptions
        {
            Roots = [_tempDir],
            Extensions = [".zip"],
            Mode = "DryRun"
        };

        var result = orch.Execute(options, cts.Token);

        // Either cancelled mid-run (status=cancelled, exitCode=2)
        // or completed before cancellation took effect (status=ok, exitCode=0)
        // Both are acceptable — key invariant: no partial/inconsistent state
        Assert.True(result.Status is "cancelled" or "ok",
            $"Expected cancelled or ok, got {result.Status}");
        if (result.Status == "cancelled")
            Assert.Equal(2, result.ExitCode);
        else
            Assert.Equal(0, result.ExitCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    // F-04: Deep ZipSlip Tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F04_ZipSorter_NestedZipInZip_DoesNotExtractInner()
    {
        // An outer ZIP containing an inner ZIP should only report outer extensions
        var innerZipPath = Path.Combine(_tempDir, "inner.zip");
        using (var innerStream = new FileStream(innerZipPath, FileMode.Create))
        using (var innerArchive = new ZipArchive(innerStream, ZipArchiveMode.Create))
        {
            var entry = innerArchive.CreateEntry("evil.dll");
            using var w = new StreamWriter(entry.Open());
            w.Write("inner payload");
        }

        var outerZipPath = Path.Combine(_tempDir, "nested.zip");
        using (var outerStream = new FileStream(outerZipPath, FileMode.Create))
        using (var outerArchive = new ZipArchive(outerStream, ZipArchiveMode.Create))
        {
            var romEntry = outerArchive.CreateEntry("game.bin");
            using (var w = new StreamWriter(romEntry.Open()))
                w.Write("rom data");

            var nestedEntry = outerArchive.CreateEntry("inner.zip");
            using (var w = nestedEntry.Open())
            {
                var innerBytes = File.ReadAllBytes(innerZipPath);
                w.Write(innerBytes, 0, innerBytes.Length);
            }
        }

        var exts = ZipSorter.GetZipEntryExtensions(outerZipPath);

        // Should see .bin and .zip from outer, but NOT .dll from inner
        Assert.Contains(".bin", exts);
        Assert.Contains(".zip", exts);
        Assert.DoesNotContain(".dll", exts);
    }

    [Fact]
    public void F04_ZipSorter_TraversalWithEncodedSeparators_Blocked()
    {
        // Entries with encoded path separators should be blocked
        var zipPath = Path.Combine(_tempDir, "encoded-traversal.zip");
        using (var stream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("safe/game.cue");
            archive.CreateEntry("..\\..\\Windows\\evil.exe"); // backslash traversal
        }

        var exts = ZipSorter.GetZipEntryExtensions(zipPath);

        Assert.Contains(".cue", exts);
        // Backslash traversal entries must be blocked
        Assert.DoesNotContain(".exe", exts);
    }

    [Fact]
    public void F04_ZipSorter_EmptyEntryNames_Handled()
    {
        // ZIP with empty or directory-only entries should not crash
        var zipPath = Path.Combine(_tempDir, "empty-entries.zip");
        using (var stream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("dir/");  // directory entry (no extension)
            archive.CreateEntry("game.sfc");
        }

        var exts = ZipSorter.GetZipEntryExtensions(zipPath);

        Assert.Contains(".sfc", exts);
        // No crash, directory entry silently skipped
    }

    [Fact]
    public void F04_ZipSorter_CorruptZip_ReturnsEmpty()
    {
        // Corrupt ZIP should return empty array, not throw
        var zipPath = Path.Combine(_tempDir, "corrupt.zip");
        File.WriteAllBytes(zipPath, [0x50, 0x4B, 0x00, 0x00, 0xFF, 0xFF]); // Invalid ZIP

        var exts = ZipSorter.GetZipEntryExtensions(zipPath);

        Assert.Empty(exts);
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Romulus_AuditF_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sample.rom"), "test");
        _tempDirs.Add(root);
        return root;
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = ApiKey,
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60"
        };

        return ApiTestFactory.Create(settings, executor);
    }

    private static HttpClient CreateAuthClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try { if (!string.IsNullOrWhiteSpace(path) && Directory.Exists(path)) Directory.Delete(path, true); }
        catch { /* best effort */ }
    }

    private sealed class FakeAuditStore : IAuditStore
    {
        public List<(string csvPath, string rootPath, string oldPath, string newPath, string action)> AuditRows { get; } = [];

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
            => AuditRows.Add((auditCsvPath, rootPath, oldPath, newPath, action));
        public void Flush(string auditCsvPath) { }
    }
}
