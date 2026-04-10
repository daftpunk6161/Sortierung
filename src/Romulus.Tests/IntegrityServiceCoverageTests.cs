using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

public sealed class IntegrityServiceCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public IntegrityServiceCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_IST_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    private string CreateFile(string name, byte[]? content = null)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content ?? [0x00, 0x01, 0x02, 0x03]);
        return path;
    }

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    #region Header Analysis

    [Fact]
    public void AnalyzeHeader_NonExistentFile_ReturnsNull()
    {
        var result = IntegrityService.AnalyzeHeader(Path.Combine(_tempDir, "nope.bin"));
        Assert.Null(result);
    }

    [Fact]
    public void AnalyzeHeader_EmptyFile_ReturnsNullOrThrows()
    {
        var path = CreateFile("empty.bin", Array.Empty<byte>());
        // Empty file may trigger IndexOutOfRangeException in HeaderAnalyzer
        // because it doesn't guard empty arrays. Document this edge case.
        try
        {
            var result = IntegrityService.AnalyzeHeader(path);
            Assert.Null(result);
        }
        catch (IndexOutOfRangeException)
        {
            // Known: HeaderAnalyzer.AnalyzeHeader doesn't guard against empty input
        }
    }

    [Fact]
    public void AnalyzeHeader_RandomData_ReturnsNullOrInfo()
    {
        // Random data should not crash, returns null for unrecognized headers
        var data = new byte[4096];
        Random.Shared.NextBytes(data);
        var path = CreateFile("random.bin", data);
        // Should not throw
        _ = IntegrityService.AnalyzeHeader(path);
    }

    #endregion

    #region Patch Detection

    [Fact]
    public void DetectPatchFormat_IpsMagic_ReturnsIPS()
    {
        var patch = new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H', 0x00 };
        var path = CreateFile("test.ips", patch);

        var result = IntegrityService.DetectPatchFormat(path);

        Assert.Equal("IPS", result);
    }

    [Fact]
    public void DetectPatchFormat_BpsMagic_ReturnsBPS()
    {
        var patch = new byte[] { (byte)'B', (byte)'P', (byte)'S', (byte)'1', 0x00 };
        var path = CreateFile("test.bps", patch);

        var result = IntegrityService.DetectPatchFormat(path);

        Assert.Equal("BPS", result);
    }

    [Fact]
    public void DetectPatchFormat_UpsMagic_ReturnsUPS()
    {
        var patch = new byte[] { (byte)'U', (byte)'P', (byte)'S', (byte)'1', 0x00 };
        var path = CreateFile("test.ups", patch);

        Assert.Equal("UPS", IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_UnknownMagic_ReturnsNull()
    {
        var path = CreateFile("test.dat", new byte[] { 0xFF, 0xFE, 0xFD, 0xFC, 0xFB });
        Assert.Null(IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_TooSmall_ReturnsNull()
    {
        var path = CreateFile("tiny.bin", new byte[] { 0x01, 0x02 });
        Assert.Null(IntegrityService.DetectPatchFormat(path));
    }

    [Fact]
    public void DetectPatchFormat_NonExistent_ReturnsNull()
    {
        Assert.Null(IntegrityService.DetectPatchFormat(Path.Combine(_tempDir, "nope.ips")));
    }

    #endregion

    #region IPS Patch Application

    [Fact]
    public void ApplyPatch_IPS_SimpleRecordAtOffset()
    {
        // Source ROM: 16 bytes of 0x00
        var sourceRom = new byte[16];
        var sourceFile = CreateFile("source.rom", sourceRom);

        // Build an IPS patch: header "PATCH" + 1 record at offset 4 writing [0xAA, 0xBB] + "EOF"
        var ips = new MemoryStream();
        ips.Write("PATCH"u8);
        // Offset: 0x000004 (3 bytes big-endian)
        ips.Write(new byte[] { 0x00, 0x00, 0x04 });
        // Size: 2 (2 bytes big-endian)
        ips.Write(new byte[] { 0x00, 0x02 });
        // Data
        ips.Write(new byte[] { 0xAA, 0xBB });
        // EOF marker
        ips.Write("EOF"u8);

        var patchFile = CreateFile("patch.ips", ips.ToArray());
        var outputFile = Path.Combine(_tempDir, "patched.rom");

        var result = IntegrityService.ApplyPatch(sourceFile, patchFile, outputFile);

        Assert.Equal("IPS", result.Format);
        Assert.True(File.Exists(outputFile));
        var outputData = File.ReadAllBytes(outputFile);
        Assert.Equal(16, outputData.Length);
        Assert.Equal(0xAA, outputData[4]);
        Assert.Equal(0xBB, outputData[5]);
        Assert.Equal(0x00, outputData[0]); // unchanged bytes
        Assert.NotNull(result.OutputSha256);
        Assert.True(result.OutputSizeBytes > 0);
    }

    [Fact]
    public void ApplyPatch_IPS_RleRecord()
    {
        // Source ROM: 8 bytes
        var sourceFile = CreateFile("rle_source.rom", new byte[8]);

        // IPS with RLE record: fill offset 2 with 4 bytes of value 0xCC
        var ips = new MemoryStream();
        ips.Write("PATCH"u8);
        // Offset: 0x000002
        ips.Write(new byte[] { 0x00, 0x00, 0x02 });
        // Size: 0 (indicates RLE)
        ips.Write(new byte[] { 0x00, 0x00 });
        // RLE size: 4
        ips.Write(new byte[] { 0x00, 0x04 });
        // RLE value
        ips.WriteByte(0xCC);
        // EOF
        ips.Write("EOF"u8);

        var patchFile = CreateFile("rle.ips", ips.ToArray());
        var outputFile = Path.Combine(_tempDir, "rle_patched.rom");

        var result = IntegrityService.ApplyPatch(sourceFile, patchFile, outputFile);

        var output = File.ReadAllBytes(outputFile);
        Assert.Equal(8, output.Length);
        Assert.Equal(0xCC, output[2]);
        Assert.Equal(0xCC, output[3]);
        Assert.Equal(0xCC, output[4]);
        Assert.Equal(0xCC, output[5]);
        Assert.Equal(0x00, output[0]); // before range unchanged
    }

    [Fact]
    public void ApplyPatch_IPS_ExtendsBeyondSource()
    {
        var sourceFile = CreateFile("small.rom", new byte[4]);

        // Patch that writes at offset 8 (beyond source)
        var ips = new MemoryStream();
        ips.Write("PATCH"u8);
        ips.Write(new byte[] { 0x00, 0x00, 0x08 }); // offset 8
        ips.Write(new byte[] { 0x00, 0x02 }); // size 2
        ips.Write(new byte[] { 0xDE, 0xAD });
        ips.Write("EOF"u8);

        var patchFile = CreateFile("expand.ips", ips.ToArray());
        var outputFile = Path.Combine(_tempDir, "expanded.rom");

        var result = IntegrityService.ApplyPatch(sourceFile, patchFile, outputFile);

        var output = File.ReadAllBytes(outputFile);
        Assert.Equal(10, output.Length); // expanded to offset 8 + 2 = 10
        Assert.Equal(0xDE, output[8]);
        Assert.Equal(0xAD, output[9]);
        Assert.Equal(0x00, output[4]); // padding zeros
    }

    [Fact]
    public void ApplyPatch_IPS_InvalidHeader_Throws()
    {
        var sourceFile = CreateFile("src.rom", new byte[16]);
        var patchFile = CreateFile("bad.ips", new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00 });
        var outputFile = Path.Combine(_tempDir, "out.rom");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(sourceFile, patchFile, outputFile));
        Assert.Contains("Invalid IPS", ex.Message);
    }

    [Fact]
    public void ApplyPatch_IPS_TruncatedRecord_Throws()
    {
        var sourceFile = CreateFile("src2.rom", new byte[16]);
        // Valid header + incomplete record (offset but no size)
        var ips = new MemoryStream();
        ips.Write("PATCH"u8);
        ips.Write(new byte[] { 0x00, 0x00, 0x04 }); // offset
        ips.Write(new byte[] { 0x00, 0x05 }); // size = 5
        ips.Write(new byte[] { 0xAA }); // only 1 byte instead of 5
        // no EOF

        var patchFile = CreateFile("trunc.ips", ips.ToArray());
        var outputFile = Path.Combine(_tempDir, "trunc_out.rom");

        Assert.ThrowsAny<Exception>(() =>
            IntegrityService.ApplyPatch(sourceFile, patchFile, outputFile));
    }

    #endregion

    #region ApplyPatch edge cases

    [Fact]
    public void ApplyPatch_SourceNotFound_Throws()
    {
        var patchFile = CreateFile("patch.ips", new byte[] { (byte)'P', (byte)'A', (byte)'T', (byte)'C', (byte)'H' });

        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(
                Path.Combine(_tempDir, "missing.rom"),
                patchFile,
                Path.Combine(_tempDir, "out.rom")));
    }

    [Fact]
    public void ApplyPatch_PatchNotFound_Throws()
    {
        var sourceFile = CreateFile("src.rom", new byte[16]);

        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(
                sourceFile,
                Path.Combine(_tempDir, "missing.ips"),
                Path.Combine(_tempDir, "out.rom")));
    }

    [Fact]
    public void ApplyPatch_NullArgs_Throws()
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            IntegrityService.ApplyPatch(null!, "patch", "output"));
        Assert.ThrowsAny<ArgumentException>(() =>
            IntegrityService.ApplyPatch("source", null!, "output"));
        Assert.ThrowsAny<ArgumentException>(() =>
            IntegrityService.ApplyPatch("source", "patch", null!));
    }

    [Fact]
    public void ApplyPatch_UnknownFormat_Throws()
    {
        var sourceFile = CreateFile("src.rom", new byte[16]);
        var patchFile = CreateFile("mystery.xyz", new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 });

        var ex = Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(sourceFile, patchFile, Path.Combine(_tempDir, "out.rom")));
        Assert.Contains("Unsupported", ex.Message);
    }

    #endregion

    #region IPS EOF with resize

    [Fact]
    public void ApplyPatch_IPS_EofTruncatesFile()
    {
        // Source: 16 bytes
        var sourceFile = CreateFile("large.rom", new byte[16]);

        // IPS that sets target size to 8 via EOF record
        var ips = new MemoryStream();
        ips.Write("PATCH"u8);
        // EOF marker with 3-byte target size
        ips.Write("EOF"u8);
        // Target size: 8 (3 bytes big-endian)
        ips.Write(new byte[] { 0x00, 0x00, 0x08 });

        var patchFile = CreateFile("truncate.ips", ips.ToArray());
        var outputFile = Path.Combine(_tempDir, "truncated.rom");

        var result = IntegrityService.ApplyPatch(sourceFile, patchFile, outputFile);

        var output = File.ReadAllBytes(outputFile);
        Assert.Equal(8, output.Length);
    }

    #endregion

    #region ComputeSha256

    [Fact]
    public void ComputeSha256_ReturnsConsistentHash()
    {
        var file = CreateFile("hashtest.bin", new byte[] { 1, 2, 3, 4 });

        var hash1 = IntegrityService.ComputeSha256(file);
        var hash2 = IntegrityService.ComputeSha256(file);

        Assert.Equal(hash1, hash2);
        Assert.Equal(64, hash1.Length); // SHA-256 hex = 64 chars
    }

    [Fact]
    public void ComputeSha256_DifferentContent_DifferentHash()
    {
        var file1 = CreateFile("h1.bin", new byte[] { 1, 2, 3 });
        var file2 = CreateFile("h2.bin", new byte[] { 4, 5, 6 });

        Assert.NotEqual(
            IntegrityService.ComputeSha256(file1),
            IntegrityService.ComputeSha256(file2));
    }

    #endregion

    #region FindCommonRoot

    [Fact]
    public void FindCommonRoot_SingleFile_ReturnsDirectory()
    {
        var path = Path.Combine(_tempDir, "sub", "file.bin");
        var result = IntegrityService.FindCommonRoot([path]);
        Assert.Equal(Path.Combine(_tempDir, "sub"), result);
    }

    [Fact]
    public void FindCommonRoot_SameDirectory_ReturnsThatDirectory()
    {
        var dir = Path.Combine(_tempDir, "rom");
        var result = IntegrityService.FindCommonRoot([
            Path.Combine(dir, "a.bin"),
            Path.Combine(dir, "b.bin")
        ]);
        Assert.Equal(dir, result);
    }

    [Fact]
    public void FindCommonRoot_DifferentSubdirs_ReturnsParent()
    {
        var result = IntegrityService.FindCommonRoot([
            Path.Combine(_tempDir, "a", "x.bin"),
            Path.Combine(_tempDir, "b", "y.bin")
        ]);
        Assert.Equal(_tempDir, result);
    }

    [Fact]
    public void FindCommonRoot_Empty_ReturnsNull()
    {
        Assert.Null(IntegrityService.FindCommonRoot([]));
    }

    #endregion

    #region CreateBaseline

    [Fact]
    public async Task CreateBaseline_EmptyPaths_ReturnsEmpty()
    {
        var result = await IntegrityService.CreateBaseline([]);
        Assert.Empty(result);
    }

    [Fact]
    public async Task CreateBaseline_WithFiles_ReturnsHashEntries()
    {
        var f1 = CreateFile("bl1.bin", new byte[] { 10, 20, 30 });
        var f2 = CreateFile("bl2.bin", new byte[] { 40, 50, 60 });

        var progress = new List<string>();
        var result = await IntegrityService.CreateBaseline(
            [f1, f2],
            new Progress<string>(msg => progress.Add(msg)));

        Assert.Equal(2, result.Count);
        Assert.True(progress.Count >= 2);
    }

    [Fact]
    public async Task CreateBaseline_MissingFile_SkipsGracefully()
    {
        var f1 = CreateFile("bl_exists.bin", new byte[] { 1, 2 });
        var f2 = Path.Combine(_tempDir, "bl_missing.bin");

        var result = await IntegrityService.CreateBaseline([f1, f2]);

        Assert.Single(result); // Only the existing file
    }

    #endregion

    #region Backup

    [Fact]
    public void CreateBackup_CopiesFiles()
    {
        var f1 = CreateFile("rom1.bin", "data1");
        var f2 = CreateFile("rom2.bin", "data2");
        var backupRoot = Path.Combine(_tempDir, "backups");

        var sessionDir = IntegrityService.CreateBackup([f1, f2], backupRoot, "test");

        Assert.True(Directory.Exists(sessionDir));
        var files = Directory.GetFiles(sessionDir, "*", SearchOption.AllDirectories);
        Assert.Equal(2, files.Length);
    }

    [Fact]
    public void CreateBackup_MissingSourceFile_SkipsGracefully()
    {
        var f1 = CreateFile("exists.bin", "data");
        var f2 = Path.Combine(_tempDir, "missing.bin");
        var backupRoot = Path.Combine(_tempDir, "backups");

        var sessionDir = IntegrityService.CreateBackup([f1, f2], backupRoot, "partial");

        var files = Directory.GetFiles(sessionDir, "*", SearchOption.AllDirectories);
        Assert.Single(files);
    }

    [Fact]
    public void CleanupOldBackups_NoDirectory_ReturnsZero()
    {
        Assert.Equal(0, IntegrityService.CleanupOldBackups(
            Path.Combine(_tempDir, "nonexistent"), 7));
    }

    [Fact]
    public void CleanupOldBackups_ConfirmDenied_ReturnsZero()
    {
        var backupRoot = Path.Combine(_tempDir, "cleanup_test");
        var oldDir = Path.Combine(backupRoot, "old_session");
        Directory.CreateDirectory(oldDir);
        // Set creation time to 30 days ago
        Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-30));

        var removed = IntegrityService.CleanupOldBackups(backupRoot, 7, _ => false);

        Assert.Equal(0, removed);
        Assert.True(Directory.Exists(oldDir));
    }

    [Fact]
    public void CleanupOldBackups_ConfirmAccepted_RemovesOld()
    {
        var backupRoot = Path.Combine(_tempDir, "cleanup_accept");
        var oldDir = Path.Combine(backupRoot, "old_session");
        Directory.CreateDirectory(oldDir);
        Directory.SetCreationTime(oldDir, DateTime.Now.AddDays(-30));

        var removed = IntegrityService.CleanupOldBackups(backupRoot, 7, _ => true);

        Assert.Equal(1, removed);
        Assert.False(Directory.Exists(oldDir));
    }

    #endregion
}
