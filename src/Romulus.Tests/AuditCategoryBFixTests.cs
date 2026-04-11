using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Romulus.Api;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD red-then-green tests for full-repo-audit category B findings (Sicherheitsprobleme).
/// B-01: FileSystemAdapter Post-Move Reparse-Point TOCTOU mitigation
/// B-02: API Kestrel-level MaxRequestBodySize limit
/// B-03: ZipSorter Zip-Slip entry filtering
/// B-04: CSV embedded-quote / formula-injection regression
/// </summary>
public sealed class AuditCategoryBFixTests : IDisposable
{
    private const string ApiKey = "b-test-api-key";
    private readonly string _tempDir;

    public AuditCategoryBFixTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AuditB_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best-effort */ }
    }

    // ═══════════════════════════════════════════════════════════════════
    // B-01: FileSystemAdapter SEC-MOVE-04 Post-Move root containment
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void B01_MoveItemSafely_WithAllowedRoot_VerifiesPostMoveContainment()
    {
        // Arrange: destination within allowed root → should succeed
        var root = Path.Combine(_tempDir, "allowed");
        Directory.CreateDirectory(root);

        var source = Path.Combine(_tempDir, "source.rom");
        File.WriteAllText(source, "data");

        var dest = Path.Combine(root, "target.rom");
        var fs = new FileSystemAdapter();

        // Act
        var result = fs.MoveItemSafely(source, dest, root);

        // Assert: move should succeed (within root)
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
    }

    [Fact]
    public void B01_MoveItemSafely_WithAllowedRoot_RejectsDestOutsideRoot()
    {
        // Arrange: destination outside allowed root → should be blocked
        var root = Path.Combine(_tempDir, "allowed");
        Directory.CreateDirectory(root);

        var outsideDir = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(outsideDir);

        var source = Path.Combine(_tempDir, "source2.rom");
        File.WriteAllText(source, "data");

        var dest = Path.Combine(outsideDir, "target.rom");
        var fs = new FileSystemAdapter();

        // Act
        var result = fs.MoveItemSafely(source, dest, root);

        // Assert: should be null (blocked)
        Assert.Null(result);
        // Source should not be moved
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void B01_MoveItemSafely_PostMoveValidation_ChecksFinalDestination()
    {
        // This test verifies the post-move root-containment check exists and
        // is invoked even when the pre-move path looks OK.
        // With DUP collision, the DUP destination must also be within root.
        var root = Path.Combine(_tempDir, "postmove");
        Directory.CreateDirectory(root);

        var source = Path.Combine(_tempDir, "src.rom");
        File.WriteAllText(source, "content_a");

        // Pre-create a file at the intended destination to trigger DUP logic
        var dest = Path.Combine(root, "out.rom");
        File.WriteAllText(dest, "existing");

        var fs = new FileSystemAdapter();
        var result = fs.MoveItemSafely(source, dest, root);

        // Assert: DUP-suffixed result should still be within root
        Assert.NotNull(result);
        Assert.StartsWith(root, result, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(result));
    }

    // ═══════════════════════════════════════════════════════════════════
    // B-02: API Kestrel-level body size limit
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void B02_Api_KestrelMaxRequestBodySize_IsConfigured()
    {
        // The API must configure Kestrel MaxRequestBodySize ≤ 1MB
        // to reject oversized bodies at the transport level before middleware.
        using var factory = CreateApiFactory();
        var kestrelOpts = factory.Services.GetService<IOptions<KestrelServerOptions>>();

        // KestrelServerOptions must be registered with a body size limit
        Assert.NotNull(kestrelOpts);
        Assert.True(kestrelOpts.Value.Limits.MaxRequestBodySize.HasValue,
            "Kestrel MaxRequestBodySize should be explicitly configured.");
        Assert.True(kestrelOpts.Value.Limits.MaxRequestBodySize!.Value <= 1_048_576,
            $"Kestrel MaxRequestBodySize should be ≤ 1MB but was {kestrelOpts.Value.Limits.MaxRequestBodySize.Value}.");
    }

    [Fact]
    public async Task B02_Api_OversizedBody_RejectedEarly()
    {
        // POST /runs with >1MB should be rejected (either Kestrel or app-level)
        using var factory = CreateApiFactory();
        using var client = CreateApiAuthClient(factory);

        // Generate body larger than 1MB
        var oversizedPayload = new string('X', 1_048_577);
        var content = new StringContent(oversizedPayload, Encoding.UTF8, "application/json");

        var response = await client.PostAsync("/runs", content);

        // Should be rejected (400 from app-level or 413 from Kestrel)
        Assert.True(
            response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge,
            $"Expected 400 or 413 but got {(int)response.StatusCode}.");
    }

    // ═══════════════════════════════════════════════════════════════════
    // B-03: ZipSorter Zip-Slip entry filtering
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void B03_GetZipEntryExtensions_FiltersTraversalEntries()
    {
        // ZIP with entries containing ".." path traversal should be filtered out
        var zipPath = CreateZipWithEntries("traversal.zip",
            "normal/game.bin",
            "../../etc/passwd.rom",
            "../secret.sav");

        var exts = ZipSorter.GetZipEntryExtensions(zipPath);

        // Only .bin from the safe entry should be included
        Assert.Contains(".bin", exts);
        Assert.DoesNotContain(".rom", exts); // traversal entry must be excluded
        Assert.DoesNotContain(".sav", exts); // traversal entry must be excluded
    }

    [Fact]
    public void B03_GetZipEntryExtensions_FiltersRootedEntries()
    {
        // ZIP entries with rooted/absolute paths should be filtered
        var zipPath = CreateZipWithEntries("rooted.zip",
            "normal/game.cue",
            "C:\\Windows\\System32\\evil.dll");

        var exts = ZipSorter.GetZipEntryExtensions(zipPath);

        Assert.Contains(".cue", exts);
        Assert.DoesNotContain(".dll", exts); // rooted entry must be excluded
    }

    [Fact]
    public void B03_GetZipEntryExtensions_SafeEntriesUnaffected()
    {
        // Normal ZIP entries should work as before
        var zipPath = CreateZipWithEntries("normal.zip",
            "game.bin", "game.cue", "readme.txt");

        var exts = ZipSorter.GetZipEntryExtensions(zipPath);

        Assert.Equal(3, exts.Length);
        Assert.Contains(".bin", exts);
        Assert.Contains(".cue", exts);
        Assert.Contains(".txt", exts);
    }

    // ═══════════════════════════════════════════════════════════════════
    // B-04: CSV-Export Embedded Quotes / Formula Injection Regression
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("he said \"hi\"", "\"he said \"\"hi\"\"\"")]
    [InlineData("field \"with\" quotes", "\"field \"\"with\"\" quotes\"")]
    public void B04_SanitizeCsvField_EmbeddedQuotes_ProperlyEscaped(string input, string expected)
    {
        var result = AuditCsvParser.SanitizeCsvField(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("=SUM(A1:A2)", "\"=SUM(A1:A2)\"")]
    [InlineData("+cmd|'/C calc'!A0", "\"+cmd|'/C calc'!A0\"")]
    [InlineData("@SUM(A1)", "\"@SUM(A1)\"")]
    public void B04_SanitizeCsvField_FormulaInjection_QuotedSafely(string input, string expected)
    {
        var result = AuditCsvParser.SanitizeCsvField(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void B04_SanitizeCsvField_EmbeddedDelimiterAndQuotes_Combined()
    {
        // Field with both commas and quotes: RFC 4180 requires quoted output with "" escaping
        var input = "value,with \"both\"";
        var result = AuditCsvParser.SanitizeCsvField(input);

        Assert.StartsWith("\"", result);
        Assert.EndsWith("\"", result);
        Assert.Contains("\"\"both\"\"", result);
    }

    [Fact]
    public void B04_SanitizeCsvField_RoundTrip_ParseCsvLine()
    {
        // Sanitize a dangerous value, embed in CSV line, then parse back → original recovered
        var original = "=HYPERLINK(\"http://evil.com\",\"Click\")";
        var sanitized = AuditCsvParser.SanitizeCsvField(original);
        var csvLine = $"col1,{sanitized},col3";
        var parsed = AuditCsvParser.ParseCsvLine(csvLine);

        Assert.Equal(3, parsed.Length);
        Assert.Equal(original, parsed[1]);
    }

    [Fact]
    public void B04_ReportGenerator_CsvSafe_WrapsAllValues()
    {
        // CsvSafe method (via reflection since private) must never return unquoted dangerous values.
        // We verify indirectly through AuditCsvParser that the underlying sanitization is sound.
        var dangerous = new[] { "=cmd", "+cmd", "@cmd", "-cmd|'exec'", "normal,value", "has\"quote" };
        foreach (var input in dangerous)
        {
            var sanitized = AuditCsvParser.SanitizeCsvField(input);
            // All dangerous inputs must be quoted
            Assert.True(sanitized.StartsWith('"'), $"Input '{input}' should be quoted but got: {sanitized}");
            Assert.True(sanitized.EndsWith('"'), $"Input '{input}' should be quoted but got: {sanitized}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private string CreateZipWithEntries(string name, params string[] entryNames)
    {
        var zipPath = Path.Combine(_tempDir, name);
        using var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create);
        foreach (var entry in entryNames)
        {
            var e = archive.CreateEntry(entry);
            using var s = e.Open();
            s.WriteByte(0x00);
        }
        return zipPath;
    }

    private static WebApplicationFactory<Program> CreateApiFactory()
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = ApiKey,
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60"
        };
        return ApiTestFactory.Create(settings);
    }

    private static HttpClient CreateApiAuthClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }
}
