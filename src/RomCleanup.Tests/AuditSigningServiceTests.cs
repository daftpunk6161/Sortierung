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
    public void VerifyMetadataSidecar_TamperedSidecarHmac_ThrowsInvalidData()
    {
        var csvPath = Path.Combine(_tempDir, "audit_hmac_tamper.csv");
        File.WriteAllText(csvPath, "Header\nRow1\n", Encoding.UTF8);

        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        service.WriteMetadataSidecar(csvPath, rowCount: 1);

        // Tamper with the sidecar's HMAC (keep CSV and SHA256 intact)
        var metaPath = csvPath + ".meta.json";
        var json = File.ReadAllText(metaPath, Encoding.UTF8);
        // Replace the HMAC value with garbage
        var tampered = System.Text.RegularExpressions.Regex.Replace(
            json, @"""HmacSha256""\s*:\s*""[^""]+""",
            @"""HmacSha256"": ""0000000000000000000000000000000000000000000000000000000000000000""");
        File.WriteAllText(metaPath, tampered, Encoding.UTF8);

        var ex = Assert.Throws<InvalidDataException>(() => service.VerifyMetadataSidecar(csvPath));
        Assert.Contains("HMAC", ex.Message);
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
        var service = new AuditSigningService(new MinimalFs());
        var h1 = service.ComputeHmacSha256("test payload");
        var h2 = service.ComputeHmacSha256("test payload");
        Assert.Equal(h1, h2);
    }

    [Fact]
    public void ComputeHmacSha256_DifferentInputs_DifferentOutput()
    {
        var service = new AuditSigningService(new MinimalFs());
        var h1 = service.ComputeHmacSha256("payload A");
        var h2 = service.ComputeHmacSha256("payload B");
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

    [Fact]
    public void Rollback_PathTraversal_SkippedAsUnsafe()
    {
        // Build directories that exist
        var allowedRestore = Path.Combine(_tempDir, "restore");
        var allowedCurrent = Path.Combine(_tempDir, "current");
        Directory.CreateDirectory(allowedRestore);
        Directory.CreateDirectory(allowedCurrent);

        // Create a file in the "current" location
        var currentFile = Path.Combine(allowedCurrent, "game.zip");
        File.WriteAllText(currentFile, "data");

        // The OldPath uses path traversal to escape the allowed restore root
        var escapedPath = Path.Combine(allowedRestore, "..", "..", "etc", "game.zip");

        var csvPath = Path.Combine(_tempDir, "audit_traversal.csv");
        File.WriteAllText(csvPath,
            $"RootPath,OldPath,NewPath,Action\n" +
            $"{_tempDir},{escapedPath},{currentFile},MOVE\n",
            Encoding.UTF8);

        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);
        var result = service.Rollback(csvPath,
            allowedRestoreRoots: [allowedRestore],
            allowedCurrentRoots: [allowedCurrent],
            dryRun: true);

        // The row should be skipped because OldPath resolves outside the allowed restore root
        Assert.Equal(1, result.EligibleRows);
        Assert.Equal(1, result.SkippedUnsafe);
        Assert.Equal(0, result.DryRunPlanned);
    }

    [Fact]
    public void Rollback_NonDryRun_WritesForensicRollbackTrail()
    {
        var restoreDir = Path.Combine(_tempDir, "restore");
        var currentDir = Path.Combine(_tempDir, "current");
        Directory.CreateDirectory(restoreDir);
        Directory.CreateDirectory(currentDir);

        var oldPath = Path.Combine(restoreDir, "game.zip");
        var newPath = Path.Combine(currentDir, "game.zip");
        File.WriteAllText(newPath, "data");

        var csvPath = Path.Combine(_tempDir, "audit_forensic.csv");
        File.WriteAllText(csvPath,
            "RootPath,OldPath,NewPath,Action\n" +
            $"{_tempDir},{oldPath},{newPath},CONSOLE_SORT\n",
            Encoding.UTF8);

        var service = new AuditSigningService(new MinimalFs());
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        service.WriteMetadataSidecar(csvPath, 1);
        var result = service.Rollback(csvPath,
            allowedRestoreRoots: [restoreDir],
            allowedCurrentRoots: [currentDir],
            dryRun: false);

        Assert.Equal(1, result.RolledBack);
        Assert.NotNull(result.RollbackTrailPath);
        Assert.True(File.Exists(result.RollbackTrailPath!));

        var trail = File.ReadAllText(result.RollbackTrailPath!, Encoding.UTF8);
        Assert.Contains("RestoredPath,RestoredFrom,OriginalAction,Timestamp", trail);
        Assert.Contains(oldPath, trail);
        Assert.Contains(newPath, trail);
        Assert.Contains("CONSOLE_SORT", trail);
    }

    [Fact]
    public void Rollback_ConsoleSortAction_RestoresWinnerToOriginalLocation()
    {
        var root = Path.Combine(_tempDir, "root");
        var sortedDir = Path.Combine(root, "SNES");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(sortedDir);

        var originalPath = Path.Combine(root, "Winner (USA).sfc");
        var sortedPath = Path.Combine(sortedDir, "Winner (USA).sfc");
        File.WriteAllText(sortedPath, "winner-data");

        var csvPath = Path.Combine(_tempDir, "audit_console_sort.csv");
        File.WriteAllText(csvPath,
            "RootPath,OldPath,NewPath,Action\n" +
            $"{root},{originalPath},{sortedPath},CONSOLE_SORT\n",
            Encoding.UTF8);

        var service = new AuditSigningService(new MinimalFs());
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        service.WriteMetadataSidecar(csvPath, 1);
        var result = service.Rollback(csvPath,
            allowedRestoreRoots: [root],
            allowedCurrentRoots: [root],
            dryRun: false);

        Assert.Equal(1, result.RolledBack);
        Assert.True(File.Exists(originalPath));
        Assert.False(File.Exists(sortedPath));
    }

    [Fact]
    public void Rollback_DatRenameAction_RestoresOriginalFileName()
    {
        var root = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(root);

        var originalPath = Path.Combine(root, "Contra (World).nes");
        var renamedPath = Path.Combine(root, "contra-wrong.nes");
        File.WriteAllText(renamedPath, "rom-data");

        var csvPath = Path.Combine(_tempDir, "audit_datrename.csv");
        File.WriteAllText(csvPath,
            "RootPath,OldPath,NewPath,Action\n" +
            $"{root},{originalPath},{renamedPath},DAT_RENAME\n",
            Encoding.UTF8);

        var service = new AuditSigningService(new MinimalFs());
        service.WriteMetadataSidecar(csvPath, 1);

        var result = service.Rollback(csvPath,
            allowedRestoreRoots: [root],
            allowedCurrentRoots: [root],
            dryRun: false);

        Assert.Equal(1, result.EligibleRows);
        Assert.Equal(1, result.RolledBack);
        Assert.True(File.Exists(originalPath));
        Assert.False(File.Exists(renamedPath));
    }

    // Minimal IFileSystem for audit tests
    private sealed class MinimalFs : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any")
            => File.Exists(literalPath) || Directory.Exists(literalPath);
        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => [];
        public string? MoveItemSafely(string src, string dest)
        {
            File.Move(src, dest);
            return dest;
        }
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }
}
