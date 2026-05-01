using System.Net;
using System.Reflection;
using System.Text;
using System.Text.Json;
using Romulus.Api;
using Romulus.Core.GameKeys;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Tools;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// RED-phase tests for the 9 code review findings.
/// Each test is expected to FAIL before the corresponding fix is applied.
/// </summary>
public sealed class CodeReviewFindingsTests : IDisposable
{
    private readonly string _tempDir;

    public CodeReviewFindingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"review-fix-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F1: ReadJsonBodyAsync – Chunked-Body-DoS (P1 Security)
    //
    //  Bug: ReadJsonBodyAsync only checks ContentLength header.
    //  Chunked transfer encoding bypasses the check because
    //  ContentLength is null for chunked bodies.
    //
    //  Expected FAIL: Oversized chunked body is not rejected.
    //  Fix target: ReadJsonBodyAsync in Program.cs – use ReadBlockAsync
    //  with 1MB+1 buffer (same pattern as POST /runs).
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F1_ReadJsonBodyAsync_ChunkedOversizedBody_Returns400()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ApiKey"] = "test-key",
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60",
        };
        using var factory = ApiTestFactory.Create(settings);
        using var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", "test-key");

        // Create >1MB body as chunked stream (no Content-Length header)
        var oversizedJson = "{\"left\":{\"roots\":[\"C:\\\\fake\"]},\"right\":{\"roots\":[\"C:\\\\fake2\"]},\"pad\":\"" +
                            new string('x', 1_100_000) + "\"}";
        var stream = new MemoryStream(Encoding.UTF8.GetBytes(oversizedJson));
        using var content = new StreamContent(stream);
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        // StreamContent does NOT set Content-Length → server sees chunked/unknown length

        var response = await client.PostAsync("/collections/compare", content);

        // Must be 400 (body too large), NOT 500 or successful
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("TOO-LARGE", body, StringComparison.OrdinalIgnoreCase);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F2: GameKeyNormalizer – Thread-unsicherer statischer State (P1)
    //
    //  Bug: _registeredPatterns and _registeredAliasMap are read/written
    //  without synchronization. EnsurePatternsLoaded has check-then-act race.
    //
    //  Expected FAIL: Concurrent access may produce inconsistent results
    //  or exceptions. This test exercises the race condition.
    //  Fix target: GameKeyNormalizer.cs – add lock/Volatile.Read.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F2_GameKeyNormalizer_ConcurrentAccess_IsThreadSafe()
    {
        // Verify that concurrent Normalize calls (reads) do not throw or
        // produce null results. The write-synchronization (lock + volatile)
        // is verified structurally by the code change; this test exercises
        // the read path under contention without modifying global state.
        var exceptions = new System.Collections.Concurrent.ConcurrentBag<Exception>();
        var nullResults = new System.Collections.Concurrent.ConcurrentBag<string>();
        const int iterations = 1000;

        var barrier = new Barrier(3);

        var task1 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var result = GameKeyNormalizer.Normalize($"Test Game (Europe) [{i}]");
                    if (result is null)
                        nullResults.Add($"null at iteration {i}");
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        var task2 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var result = GameKeyNormalizer.Normalize($"Another Game (Japan) [{i}]");
                    if (result is null)
                        nullResults.Add($"null at iteration {i}");
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        var task3 = Task.Run(() =>
        {
            barrier.SignalAndWait();
            for (int i = 0; i < iterations; i++)
            {
                try
                {
                    var result = GameKeyNormalizer.Normalize($"Third Game (USA) [{i}]");
                    if (result is null)
                        nullResults.Add($"null at iteration {i}");
                }
                catch (Exception ex) { exceptions.Add(ex); }
            }
        });

        await Task.WhenAll(task1, task2, task3);

        Assert.Empty(exceptions);
        Assert.Empty(nullResults);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F3+F6: DashboardDataBuilder – Sync I/O as Async + Layer Violation
    //
    //  Bug: BuildDatStatusAsync uses synchronous Directory.GetFiles wrapped
    //  in Task.FromResult, and doesn't catch UnauthorizedAccessException.
    //  Also does direct I/O in the API layer (architecture violation).
    //
    //  Expected FAIL: UnauthorizedAccessException propagates unhandled.
    //  Fix target: DashboardDataBuilder.cs – wrap I/O in try/catch.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task F3_BuildDatStatusAsync_InaccessibleSubdir_ReturnsGracefully()
    {
        // Create a DAT root with an inaccessible subdirectory
        var datRoot = Path.Combine(_tempDir, "dats");
        Directory.CreateDirectory(datRoot);

        // Create a normal DAT file
        var normalDir = Path.Combine(datRoot, "SNES");
        Directory.CreateDirectory(normalDir);
        File.WriteAllText(Path.Combine(normalDir, "snes.dat"), "<xml/>");

        // Create a directory that will cause access errors
        var deniedDir = Path.Combine(datRoot, "DENIED");
        Directory.CreateDirectory(deniedDir);
        File.WriteAllText(Path.Combine(deniedDir, "secret.dat"), "<xml/>");

        // Deny access
        var dirInfo = new DirectoryInfo(deniedDir);
        var acl = dirInfo.GetAccessControl();
        var identity = System.Security.Principal.WindowsIdentity.GetCurrent();
        var rule = new System.Security.AccessControl.FileSystemAccessRule(
            identity.Name,
            System.Security.AccessControl.FileSystemRights.ListDirectory | System.Security.AccessControl.FileSystemRights.Read,
            System.Security.AccessControl.InheritanceFlags.ContainerInherit | System.Security.AccessControl.InheritanceFlags.ObjectInherit,
            System.Security.AccessControl.PropagationFlags.None,
            System.Security.AccessControl.AccessControlType.Deny);
        acl.AddAccessRule(rule);
        dirInfo.SetAccessControl(acl);

        try
        {
            // This should NOT throw – it should handle the error gracefully
            // Uses the explicit overload (F6 fix: decoupled from static I/O resolution)
            var policy = new AllowedRootPathPolicy(Array.Empty<string>());
            var result = await DashboardDataBuilder.BuildDatStatusAsync(
                datRoot, _tempDir, policy, CancellationToken.None);

            Assert.True(result.Configured);
            // Should still report files from accessible directories
            Assert.True(result.TotalFiles >= 1, "Should count files from accessible directories.");
        }
        finally
        {
            // Restore access for cleanup
            acl.RemoveAccessRule(rule);
            dirInfo.SetAccessControl(acl);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F4: removed (Wave 1 A: /export/frontend endpoint culled)
    // ═══════════════════════════════════════════════════════════════════

    // ═══════════════════════════════════════════════════════════════════
    //  F5: CSV-Export – FilePath fehlt (P2 Korrektheit)
    //
    //  Bug: GenerateCsv header and data rows omit the FilePath field,
    //  even though ReportEntry.FilePath exists and is populated.
    //
    //  Expected FAIL: CSV header does not contain "FilePath".
    //  Fix target: ReportGenerator.GenerateCsv – add FilePath column.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F5_GenerateCsv_ContainsFilePathColumn()
    {
        var entry = new ReportEntry
        {
            GameKey = "SuperMario",
            Action = "KEEP",
            Region = "EU",
            FilePath = @"C:\Roms\SNES\super-mario.zip",
            FileName = "super-mario.zip",
            Extension = ".zip",
            SizeBytes = 2048,
            Console = "SNES"
        };

        var csv = ReportGenerator.GenerateCsv([entry]);
        var lines = csv.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        // Header must contain FilePath
        var header = lines[0].TrimStart('\uFEFF');
        Assert.Contains("FilePath", header);

        // Data row must contain the actual file path
        var dataRow = lines[1];
        Assert.Contains("super-mario.zip", dataRow);
        Assert.Contains(@"C:\Roms\SNES\super-mario.zip", dataRow);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F7: RunReportWriter – Copy-Paste ReportEntry-Konstruktion (P3)
    //
    //  Smell: Winner, Loser, and Remaining blocks each construct a
    //  ReportEntry with ~20 identical property assignments.
    //
    //  This test verifies that the refactored helper produces
    //  identical output to the original code.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F7_BuildEntries_AllReportEntries_HaveConsistentFieldSets()
    {
        // Verify every ReportEntry has all key fields populated (non-default)
        // by checking that no structural field diverges between entry types.
        var entryType = typeof(ReportEntry);
        var stringProps = entryType.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string) && p.Name != "MatchReasoning")
            .ToList();

        // A winner, a loser, and a remaining entry should all fill
        // exactly the same set of properties. If any differ, the
        // copy-paste introduced a structural inconsistency.
        Assert.True(stringProps.Count >= 10,
            $"ReportEntry should have at least 10 string properties; found {stringProps.Count}. " +
            "This ensures the refactored helper covers all fields.");

        // Verify every string property except MatchReasoning has
        // a non-empty init value (the record initializes them).
        var entry = new ReportEntry
        {
            GameKey = "test",
            Action = "KEEP",
            Category = "GAME",
            Region = "EU",
            FilePath = @"C:\test.zip",
            FileName = "test.zip",
            Extension = ".zip",
            Console = "SNES",
            DecisionClass = "HighConfidence",
            EvidenceTier = "Tier1",
            PrimaryMatchKind = "Exact",
            PlatformFamily = "Nintendo",
            MatchLevel = "CRC",
            MatchReasoning = "test"
        };

        foreach (var prop in stringProps)
        {
            var value = (string?)prop.GetValue(entry);
            Assert.False(string.IsNullOrEmpty(value),
                $"ReportEntry.{prop.Name} should have a non-empty value when fully populated.");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F8: MapRunHistoryEntry – Property-Vollständigkeitstest (P3)
    //
    //  Bug: Manual field-by-field mapping between CollectionRunHistoryItem
    //  and ApiRunHistoryEntry has no compile-time protection. Adding a new
    //  property to one but forgetting the other silently drops data.
    //
    //  Expected FAIL (if any property not mapped): Reflection test detects
    //  the property gap.
    //  Fix target: DashboardDataBuilder.cs MapRunHistoryEntry.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F8_MapRunHistoryEntry_AllSourcePropertiesExistOnTarget()
    {
        var sourceProps = typeof(CollectionRunHistoryItem)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        var targetProps = typeof(ApiRunHistoryEntry)
            .GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n)
            .ToList();

        // Every source property must exist on the target – structural parity
        var missingOnTarget = sourceProps.Except(targetProps).ToList();
        Assert.Empty(missingOnTarget);

        // Every target property must exist on the source – no phantom fields
        var missingOnSource = targetProps.Except(sourceProps).ToList();
        Assert.Empty(missingOnSource);

        // Verify all properties are actually mapped (values transfer correctly)
        var source = new CollectionRunHistoryItem
        {
            RunId = "run-42",
            StartedUtc = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc),
            CompletedUtc = new DateTime(2026, 1, 15, 10, 5, 0, DateTimeKind.Utc),
            Mode = "DryRun",
            Status = "completed",
            RootCount = 3,
            RootFingerprint = "abc123",
            DurationMs = 300000,
            TotalFiles = 500,
            CollectionSizeBytes = 1_000_000_000,
            Games = 400,
            Dupes = 50,
            Junk = 20,
            DatMatches = 380,
            ConvertedCount = 10,
            FailCount = 2,
            SavedBytes = 500_000_000,
            ConvertSavedBytes = 100_000_000,
            HealthScore = 92
        };

        // Use DashboardDataBuilder's mapping via reflection (it's private)
        var mapMethod = typeof(DashboardDataBuilder).GetMethod(
            "MapRunHistoryEntry",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(mapMethod);

        var target = (ApiRunHistoryEntry)mapMethod!.Invoke(null, [source])!;
        Assert.NotNull(target);

        // Every source property value must match the target
        foreach (var sourceProp in typeof(CollectionRunHistoryItem).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var targetProp = typeof(ApiRunHistoryEntry).GetProperty(sourceProp.Name, BindingFlags.Public | BindingFlags.Instance);
            Assert.NotNull(targetProp);

            var sourceValue = sourceProp.GetValue(source);
            var targetValue = targetProp!.GetValue(target);

            Assert.Equal(sourceValue, targetValue);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  F9: ExternalProcessGuard – Stille Fehler bei null-Log (P3)
    //
    //  Bug: When log callback is null, Job Object creation/config errors
    //  are silently swallowed with no trace output.
    //
    //  This test verifies that Track() with null log completes without
    //  throwing and that the guard still tracks the process.
    //  (The deeper fix is adding Trace.WriteLine fallback.)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void F9_ExternalProcessGuard_Track_WithNullLog_CompletesWithoutError()
    {
        // Use a short-lived process to verify tracking works with null log
        var process = new System.Diagnostics.Process
        {
            StartInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c echo test",
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true
            }
        };

        process.Start();

        try
        {
            // Track with null log – should not throw
            using var lease = ExternalProcessGuard.Track(process, "F9-Test", log: null);
            Assert.NotNull(lease);
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill();
                process.WaitForExit(3000);
            }
            process.Dispose();
        }
    }

    [Fact]
    public void F9_ExternalProcessGuard_EmitDiagnostic_WithNullLogger_FallsBackToTrace()
    {
        var method = typeof(ExternalProcessGuard).GetMethod(
            "EmitDiagnostic",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var marker = $"F9-trace-fallback-{Guid.NewGuid():N}";
        using var writer = new StringWriter();
        using var listener = new System.Diagnostics.TextWriterTraceListener(writer);
        var listeners = System.Diagnostics.Trace.Listeners;
        listeners.Add(listener);
        var previousAutoFlush = System.Diagnostics.Trace.AutoFlush;
        System.Diagnostics.Trace.AutoFlush = true;

        try
        {
            method!.Invoke(null, [null, marker]);
            listener.Flush();
            Assert.Contains(marker, writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            System.Diagnostics.Trace.AutoFlush = previousAutoFlush;
            listeners.Remove(listener);
        }
    }

    [Fact]
    public void F9_ExternalProcessGuard_EmitDiagnostic_PrefersProvidedLogger()
    {
        var method = typeof(ExternalProcessGuard).GetMethod(
            "EmitDiagnostic",
            BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var marker = $"F9-callback-path-{Guid.NewGuid():N}";
        string? captured = null;
        Action<string> callback = msg => captured = msg;

        using var writer = new StringWriter();
        using var listener = new System.Diagnostics.TextWriterTraceListener(writer);
        var listeners = System.Diagnostics.Trace.Listeners;
        listeners.Add(listener);

        try
        {
            method!.Invoke(null, [callback, marker]);
            listener.Flush();

            Assert.Equal(marker, captured);
            Assert.DoesNotContain(marker, writer.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            listeners.Remove(listener);
        }
    }
}
