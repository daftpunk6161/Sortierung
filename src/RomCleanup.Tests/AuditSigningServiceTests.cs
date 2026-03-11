using System.Text;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Audit;
using Xunit;

namespace RomCleanup.Tests;

public sealed class AuditSigningServiceTests : IDisposable
{
    private readonly string _tempDir;

    public AuditSigningServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audit_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // =========================================================================
    //  SanitizeCsvField Tests
    // =========================================================================

    [Theory]
    [InlineData("normal", "normal")]
    [InlineData("", "")]
    [InlineData("=cmd", "'=cmd")]
    [InlineData("+cmd", "'+cmd")]
    [InlineData("-cmd", "'-cmd")]
    [InlineData("@cmd", "'@cmd")]
    public void SanitizeCsvField_PreventsInjection(string input, string expected)
        => Assert.Equal(expected, AuditSigningService.SanitizeCsvField(input));

    [Fact]
    public void SanitizeCsvField_QuotesComma()
    {
        var result = AuditSigningService.SanitizeCsvField("hello,world");
        Assert.StartsWith("\"", result);
        Assert.EndsWith("\"", result);
    }

    [Fact]
    public void SanitizeCsvField_EscapesDoubleQuotes()
    {
        var result = AuditSigningService.SanitizeCsvField("he said \"hi\"");
        Assert.Contains("\"\"", result);
    }

    // =========================================================================
    //  Sidecar Write/Verify Tests
    // =========================================================================

    [Fact]
    public void WriteAndVerify_RoundTrip()
    {
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(csvPath, "Header\nRow1\nRow2\n", Encoding.UTF8);

        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        var metaPath = service.WriteMetadataSidecar(csvPath, rowCount: 2);

        Assert.NotNull(metaPath);
        Assert.True(File.Exists(metaPath));
        Assert.EndsWith(".meta.json", metaPath);

        // Verify should pass
        Assert.True(service.VerifyMetadataSidecar(csvPath));
    }

    [Fact]
    public void VerifyMetadataSidecar_TamperedCsv_Throws()
    {
        var csvPath = Path.Combine(_tempDir, "audit_tamper.csv");
        File.WriteAllText(csvPath, "Header\nRow1\n", Encoding.UTF8);

        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        service.WriteMetadataSidecar(csvPath, rowCount: 1);

        // Tamper with the CSV
        File.WriteAllText(csvPath, "Header\nRow1\nINJECTED\n", Encoding.UTF8);

        Assert.Throws<InvalidDataException>(() => service.VerifyMetadataSidecar(csvPath));
    }

    [Fact]
    public void VerifyMetadataSidecar_MissingSidecar_Throws()
    {
        var csvPath = Path.Combine(_tempDir, "no_sidecar.csv");
        File.WriteAllText(csvPath, "Header\n", Encoding.UTF8);

        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        Assert.Throws<FileNotFoundException>(() => service.VerifyMetadataSidecar(csvPath));
    }

    [Fact]
    public void WriteMetadataSidecar_NonExistentCsv_ReturnsNull()
    {
        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        var result = service.WriteMetadataSidecar(@"C:\nonexistent.csv", 0);
        Assert.Null(result);
    }

    // =========================================================================
    //  ComputeFileSha256 Tests
    // =========================================================================

    [Fact]
    public void ComputeFileSha256_Deterministic()
    {
        var path = Path.Combine(_tempDir, "hash_test.bin");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });

        var hash1 = AuditSigningService.ComputeFileSha256(path);
        var hash2 = AuditSigningService.ComputeFileSha256(path);
        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA256 hex = 64 chars
    }

    // =========================================================================
    //  HMAC Tests
    // =========================================================================

    [Fact]
    public void ComputeHmacSha256_SameInput_SameOutput()
    {
        var h1 = AuditSigningService.ComputeHmacSha256("test payload");
        var h2 = AuditSigningService.ComputeHmacSha256("test payload");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void ComputeHmacSha256_DifferentInputs_DifferentOutput()
    {
        var h1 = AuditSigningService.ComputeHmacSha256("payload A");
        var h2 = AuditSigningService.ComputeHmacSha256("payload B");
        Assert.NotEqual(h1, h2);
    }

    // =========================================================================
    //  BuildSignaturePayload Tests
    // =========================================================================

    [Fact]
    public void BuildSignaturePayload_CanonicalFormat()
    {
        var payload = AuditSigningService.BuildSignaturePayload("audit.csv", "abc123", 5, "2024-01-01T00:00:00Z");
        Assert.Equal("v1|audit.csv|abc123|5|2024-01-01T00:00:00Z", payload);
    }

    // =========================================================================
    //  Rollback Tests
    // =========================================================================

    [Fact]
    public void Rollback_EmptyCsv_ReturnsDefault()
    {
        var csvPath = Path.Combine(_tempDir, "empty_audit.csv");
        File.WriteAllText(csvPath, "Header\n", Encoding.UTF8);

        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        var result = service.Rollback(csvPath, ["D:\\"], ["D:\\"], dryRun: true);
        Assert.Equal(0, result.TotalRows);
    }

    [Fact]
    public void Rollback_DryRun_PlansButDoesNotMove()
    {
        var srcDir = Path.Combine(_tempDir, "src");
        var destDir = Path.Combine(_tempDir, "dest");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);

        // Create a file at the "current location" (after move)
        var movedFile = Path.Combine(destDir, "game.zip");
        File.WriteAllText(movedFile, "data");
        var origPath = Path.Combine(srcDir, "game.zip");

        var csvPath = Path.Combine(_tempDir, "audit_rollback.csv");
        File.WriteAllText(csvPath,
            $"RootPath,OldPath,NewPath,Action\n" +
            $"{_tempDir},{origPath},{movedFile},MOVE\n",
            Encoding.UTF8);

        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        var result = service.Rollback(csvPath,
            allowedRestoreRoots: [srcDir],
            allowedCurrentRoots: [destDir],
            dryRun: true);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.EligibleRows);
        Assert.Equal(1, result.DryRunPlanned);
        // File should still be at destination (no actual move)
        Assert.True(File.Exists(movedFile));
    }

    [Fact]
    public void Rollback_NonExistentCsv_ReturnsEmpty()
    {
        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        var result = service.Rollback(@"C:\nonexistent.csv", ["D:\\"], ["D:\\"]);
        Assert.Equal(0, result.TotalRows);
    }

    // Minimal IFileSystem for audit tests
    private sealed class MinimalFs : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any")
            => File.Exists(literalPath) || Directory.Exists(literalPath);
        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => [];
        public bool MoveItemSafely(string src, string dest)
        {
            File.Move(src, dest);
            return true;
        }
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
    }
}
