using System.Text;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for IntegrityService – IPS binary patch parsing (ReadUInt16BigEndian,
/// ReadUInt24BigEndian, EnsureOutputLength, ApplyIpsPatch), backup/cleanup, CheckIntegrity
/// deserialization paths.
/// Targets ~174 uncovered lines.
/// </summary>
public sealed class IntegrityServiceCoverageBoostTests : IDisposable
{
    private readonly string _root;

    public IntegrityServiceCoverageBoostTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "IntSvc_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    // ══════ ReadUInt16BigEndian ════════════════════════════════════

    [Fact]
    public void ReadUInt16BigEndian_ReadsCorrectValue()
    {
        using var ms = new MemoryStream([0x01, 0x02]);
        using var reader = new BinaryReader(ms);
        var result = IntegrityService.ReadUInt16BigEndian(reader);
        Assert.Equal(0x0102, result);
    }

    [Fact]
    public void ReadUInt16BigEndian_ZeroValue()
    {
        using var ms = new MemoryStream([0x00, 0x00]);
        using var reader = new BinaryReader(ms);
        var result = IntegrityService.ReadUInt16BigEndian(reader);
        Assert.Equal(0, result);
    }

    [Fact]
    public void ReadUInt16BigEndian_MaxValue()
    {
        using var ms = new MemoryStream([0xFF, 0xFF]);
        using var reader = new BinaryReader(ms);
        var result = IntegrityService.ReadUInt16BigEndian(reader);
        Assert.Equal(0xFFFF, result);
    }

    // ══════ ReadUInt24BigEndian ════════════════════════════════════

    [Fact]
    public void ReadUInt24BigEndian_ReadsCorrectValue()
    {
        using var ms = new MemoryStream([0x01, 0x02, 0x03]);
        using var reader = new BinaryReader(ms);
        var result = IntegrityService.ReadUInt24BigEndian(reader);
        Assert.Equal(0x010203, result);
    }

    [Fact]
    public void ReadUInt24BigEndian_ZeroValue()
    {
        using var ms = new MemoryStream([0x00, 0x00, 0x00]);
        using var reader = new BinaryReader(ms);
        var result = IntegrityService.ReadUInt24BigEndian(reader);
        Assert.Equal(0, result);
    }

    // ══════ EnsureOutputLength ════════════════════════════════════

    [Fact]
    public void EnsureOutputLength_ShorterThanTarget_PadsWithZeros()
    {
        var output = new List<byte> { 0x01, 0x02 };
        IntegrityService.EnsureOutputLength(output, 5);
        Assert.Equal(5, output.Count);
        Assert.Equal(0x01, output[0]);
        Assert.Equal(0x02, output[1]);
        Assert.Equal(0x00, output[2]);
    }

    [Fact]
    public void EnsureOutputLength_AlreadyLargeEnough_NoChange()
    {
        var output = new List<byte> { 0x01, 0x02, 0x03, 0x04, 0x05 };
        IntegrityService.EnsureOutputLength(output, 3);
        Assert.Equal(5, output.Count);
    }

    [Fact]
    public void EnsureOutputLength_ExactSize_NoChange()
    {
        var output = new List<byte> { 0x01, 0x02 };
        IntegrityService.EnsureOutputLength(output, 2);
        Assert.Equal(2, output.Count);
    }

    // ══════ IPS Patch Application ═════════════════════════════════

    [Fact]
    public void ApplyPatch_IpsFormat_SimpleRecord_PatchesCorrectly()
    {
        // Create a simple source ROM (8 bytes)
        var sourceContent = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07 };
        var sourcePath = CreateFile("source.rom", sourceContent);

        // Build a minimal IPS patch:
        // Header: PATCH
        // Record: offset=0x000002, size=3, data=[0xAA, 0xBB, 0xCC]
        // Footer: EOF
        var patch = new List<byte>();
        patch.AddRange("PATCH"u8.ToArray());
        // Offset 2 (3 bytes big-endian)
        patch.AddRange(new byte[] { 0x00, 0x00, 0x02 });
        // Size 3 (2 bytes big-endian)
        patch.AddRange(new byte[] { 0x00, 0x03 });
        // Data
        patch.AddRange(new byte[] { 0xAA, 0xBB, 0xCC });
        // EOF
        patch.AddRange("EOF"u8.ToArray());
        var patchPath = CreateFile("patch.ips", patch.ToArray());

        var outputPath = Path.Combine(_root, "output.rom");
        var result = IntegrityService.ApplyPatch(sourcePath, patchPath, outputPath);

        Assert.Equal("IPS", result.Format);
        Assert.True(File.Exists(outputPath));
        var output = File.ReadAllBytes(outputPath);
        Assert.Equal(8, output.Length);
        Assert.Equal(0x00, output[0]);
        Assert.Equal(0x01, output[1]);
        Assert.Equal(0xAA, output[2]); // patched
        Assert.Equal(0xBB, output[3]); // patched
        Assert.Equal(0xCC, output[4]); // patched
        Assert.Equal(0x05, output[5]); // original
    }

    [Fact]
    public void ApplyPatch_IpsFormat_RleRecord_FillsCorrectly()
    {
        var sourceContent = new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 };
        var sourcePath = CreateFile("rle-source.rom", sourceContent);

        // IPS RLE: offset=1, size=0 (RLE marker), rle_size=3, value=0xFF
        var patch = new List<byte>();
        patch.AddRange("PATCH"u8.ToArray());
        patch.AddRange(new byte[] { 0x00, 0x00, 0x01 }); // offset 1
        patch.AddRange(new byte[] { 0x00, 0x00 });         // size 0 = RLE
        patch.AddRange(new byte[] { 0x00, 0x03 });         // RLE count 3
        patch.Add(0xFF);                                     // fill value
        patch.AddRange("EOF"u8.ToArray());
        var patchPath = CreateFile("rle-patch.ips", patch.ToArray());

        var outputPath = Path.Combine(_root, "rle-output.rom");
        var result = IntegrityService.ApplyPatch(sourcePath, patchPath, outputPath);

        Assert.Equal("IPS", result.Format);
        var output = File.ReadAllBytes(outputPath);
        Assert.Equal(0x00, output[0]);
        Assert.Equal(0xFF, output[1]); // RLE filled
        Assert.Equal(0xFF, output[2]); // RLE filled
        Assert.Equal(0xFF, output[3]); // RLE filled
        Assert.Equal(0x04, output[4]); // original
    }

    [Fact]
    public void ApplyPatch_IpsFormat_TruncateRecord_TruncatesOutput()
    {
        // Source is 10 bytes, IPS EOF with truncation to 6 bytes
        var sourceContent = new byte[10];
        var sourcePath = CreateFile("trunc-source.rom", sourceContent);

        var patch = new List<byte>();
        patch.AddRange("PATCH"u8.ToArray());
        // EOF with truncation size = 6
        patch.AddRange("EOF"u8.ToArray());
        patch.AddRange(new byte[] { 0x00, 0x00, 0x06 }); // truncate to 6 bytes
        var patchPath = CreateFile("trunc-patch.ips", patch.ToArray());

        var outputPath = Path.Combine(_root, "trunc-output.rom");
        var result = IntegrityService.ApplyPatch(sourcePath, patchPath, outputPath);

        var output = File.ReadAllBytes(outputPath);
        Assert.Equal(6, output.Length);
    }

    [Fact]
    public void ApplyPatch_IpsFormat_InvalidHeader_Throws()
    {
        var sourcePath = CreateFile("inv-src.rom", new byte[] { 0x00 });
        var patchPath = CreateFile("bad-header.ips", new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 });

        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(sourcePath, patchPath, Path.Combine(_root, "out.rom")));
    }

    [Fact]
    public void ApplyPatch_IpsFormat_PatchExpandsOutput()
    {
        // Source is 4 bytes, patch writes at offset 6 → output must grow
        var sourceContent = new byte[] { 0x01, 0x02, 0x03, 0x04 };
        var sourcePath = CreateFile("expand-src.rom", sourceContent);

        var patch = new List<byte>();
        patch.AddRange("PATCH"u8.ToArray());
        patch.AddRange(new byte[] { 0x00, 0x00, 0x06 }); // offset 6 (beyond source)
        patch.AddRange(new byte[] { 0x00, 0x02 });         // size 2
        patch.AddRange(new byte[] { 0xDE, 0xAD });
        patch.AddRange("EOF"u8.ToArray());
        var patchPath = CreateFile("expand-patch.ips", patch.ToArray());

        var outputPath = Path.Combine(_root, "expand-output.rom");
        IntegrityService.ApplyPatch(sourcePath, patchPath, outputPath);

        var output = File.ReadAllBytes(outputPath);
        Assert.True(output.Length >= 8); // At least offset 6 + 2 bytes
        Assert.Equal(0xDE, output[6]);
        Assert.Equal(0xAD, output[7]);
    }

    // ══════ Backup & Cleanup ══════════════════════════════════════

    [Fact]
    public void CreateBackup_CopiesFilesToLabelledDir()
    {
        var f1 = CreateFile("sub\\a.rom", new byte[] { 0x01 });
        var f2 = CreateFile("sub\\b.rom", new byte[] { 0x02 });
        var backupRoot = Path.Combine(_root, "backups");

        var sessionDir = IntegrityService.CreateBackup([f1, f2], backupRoot, "test-label");

        Assert.True(Directory.Exists(sessionDir));
        Assert.Contains("test-label", sessionDir);
        var backed = Directory.GetFiles(sessionDir, "*", SearchOption.AllDirectories);
        Assert.Equal(2, backed.Length);
    }

    [Fact]
    public void CreateBackup_SkipsMissingFiles()
    {
        var f1 = CreateFile("exists.rom", new byte[] { 0x01 });
        var missing = Path.Combine(_root, "missing.rom");
        var backupRoot = Path.Combine(_root, "backups");

        var sessionDir = IntegrityService.CreateBackup([f1, missing], backupRoot, "skip-test");

        var backed = Directory.GetFiles(sessionDir, "*", SearchOption.AllDirectories);
        Assert.Single(backed); // Only the existing file was copied
    }

    [Fact]
    public void CleanupOldBackups_RemovesExpiredDirs()
    {
        var backupRoot = Path.Combine(_root, "cleanup-backups");
        var oldDir = Path.Combine(backupRoot, "old-backup");
        var newDir = Path.Combine(backupRoot, "new-backup");

        Directory.CreateDirectory(oldDir);
        Directory.CreateDirectory(newDir);

        // Make old dir really old
        Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-100));
        Directory.SetCreationTime(newDir, DateTime.Now);

        var removed = IntegrityService.CleanupOldBackups(backupRoot, retentionDays: 30);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(oldDir));
        Assert.True(Directory.Exists(newDir));
    }

    [Fact]
    public void CleanupOldBackups_NonExistentRoot_ReturnsZero()
    {
        var miss = Path.Combine(_root, "nonexistent");
        Assert.Equal(0, IntegrityService.CleanupOldBackups(miss, retentionDays: 1));
    }

    [Fact]
    public void CleanupOldBackups_UserDeclinesConfirmation_ReturnsZero()
    {
        var backupRoot = Path.Combine(_root, "confirm-test");
        var oldDir = Path.Combine(backupRoot, "expired");
        Directory.CreateDirectory(oldDir);
        Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-100));

        var removed = IntegrityService.CleanupOldBackups(backupRoot, retentionDays: 1,
            confirmDelete: count => false);

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(oldDir)); // Not deleted
    }

    [Fact]
    public void CleanupOldBackups_NoneExpired_ReturnsZero()
    {
        var backupRoot = Path.Combine(_root, "fresh-backups");
        Directory.CreateDirectory(Path.Combine(backupRoot, "fresh1"));
        Directory.CreateDirectory(Path.Combine(backupRoot, "fresh2"));

        var removed = IntegrityService.CleanupOldBackups(backupRoot, retentionDays: 365);
        Assert.Equal(0, removed);
    }

    // ══════ ResolvePatchFormat (extension fallback) ═══════════════

    [Theory]
    [InlineData(".xdelta", "XDELTA")]
    [InlineData(".xdelta3", "XDELTA")]
    [InlineData(".vcdiff", "XDELTA")]
    public void ResolvePatchFormat_ExtensionFallback_ReturnsCorrectFormat(string extension, string expected)
    {
        // Create file with no magic bytes (not IPS/BPS/UPS header)
        var path = CreateFile($"patch{extension}", new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });
        var format = IntegrityService.ResolvePatchFormat(path);
        Assert.Equal(expected, format);
    }

    [Fact]
    public void ResolvePatchFormat_UnknownExtension_Throws()
    {
        var path = CreateFile("patch.unknown", new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04, 0x05 });
        Assert.Throws<InvalidOperationException>(() => IntegrityService.ResolvePatchFormat(path));
    }

    // ══════ ApplyPatch validation ═════════════════════════════════

    [Fact]
    public void ApplyPatch_MissingSourceRom_Throws()
    {
        var patchPath = CreateFile("valid.ips", "PATCH"u8.ToArray());
        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(
                Path.Combine(_root, "noexist.rom"),
                patchPath,
                Path.Combine(_root, "out.rom")));
    }

    [Fact]
    public void ApplyPatch_MissingPatchFile_Throws()
    {
        var srcPath = CreateFile("src.rom", new byte[] { 0x01 });
        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(
                srcPath,
                Path.Combine(_root, "noexist.ips"),
                Path.Combine(_root, "out.rom")));
    }

    [Fact]
    public void ApplyPatch_NullOrWhitespaceArgs_Throws()
    {
        Assert.Throws<ArgumentException>(() => IntegrityService.ApplyPatch("", "p", "o"));
        Assert.Throws<ArgumentException>(() => IntegrityService.ApplyPatch("s", "", "o"));
        Assert.Throws<ArgumentException>(() => IntegrityService.ApplyPatch("s", "p", ""));
    }

    // ══════ FindCommonRoot edge cases ═════════════════════════════

    [Fact]
    public void FindCommonRoot_EmptyList_ReturnsNull()
    {
        Assert.Null(IntegrityService.FindCommonRoot([]));
    }

    [Fact]
    public void FindCommonRoot_SingleFile_ReturnsParent()
    {
        var path = Path.Combine(_root, "sub", "file.rom");
        var result = IntegrityService.FindCommonRoot([path]);
        Assert.Equal(Path.Combine(_root, "sub"), result);
    }

    [Fact]
    public void FindCommonRoot_DisjointRoots_ReturnsHigherParent()
    {
        var a = Path.Combine(_root, "a", "file1.rom");
        var b = Path.Combine(_root, "b", "file2.rom");
        var result = IntegrityService.FindCommonRoot([a, b]);
        Assert.Equal(_root, result);
    }

    // ══════ ComputeSha256 determinism ═════════════════════════════

    [Fact]
    public void ComputeSha256_Deterministic()
    {
        var path = CreateFile("det.bin", new byte[] { 0xDE, 0xAD, 0xBE, 0xEF });
        var h1 = IntegrityService.ComputeSha256(path);
        var h2 = IntegrityService.ComputeSha256(path);
        Assert.Equal(h1, h2);
        Assert.Equal(64, h1.Length); // SHA-256 hex = 64 chars
    }

    // ══════ Helpers ════════════════════════════════════════════════

    private string CreateFile(string relativePath, byte[] content)
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
        return path;
    }

    private string CreateFile(string relativePath, string content = "data")
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }
}
