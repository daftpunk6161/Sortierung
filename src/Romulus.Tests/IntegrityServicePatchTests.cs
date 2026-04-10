using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for IntegrityService: IPS patch application, ResolvePatchFormat, EnsureOutputLength,
/// and ApplyPatch edge cases.
/// </summary>
public sealed class IntegrityServicePatchTests : IDisposable
{
    private readonly string _tempDir;

    public IntegrityServicePatchTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "IntegrityPatch_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ═══ EnsureOutputLength ══════════════════════════════════════════

    [Fact]
    public void EnsureOutputLength_ShorterThanTarget_PadsWithZeros()
    {
        var output = new List<byte> { 0x01, 0x02 };

        IntegrityService.EnsureOutputLength(output, 5);

        Assert.Equal(5, output.Count);
        Assert.Equal(0x01, output[0]);
        Assert.Equal(0x02, output[1]);
        Assert.Equal(0x00, output[2]);
        Assert.Equal(0x00, output[3]);
        Assert.Equal(0x00, output[4]);
    }

    [Fact]
    public void EnsureOutputLength_EqualOrGreater_NoChange()
    {
        var output = new List<byte> { 0x01, 0x02, 0x03 };

        IntegrityService.EnsureOutputLength(output, 3);
        Assert.Equal(3, output.Count);

        IntegrityService.EnsureOutputLength(output, 1);
        Assert.Equal(3, output.Count);
    }

    // ═══ ReadUInt16BigEndian / ReadUInt24BigEndian ════════════════════

    [Fact]
    public void ReadUInt16BigEndian_ParsesCorrectly()
    {
        using var ms = new MemoryStream([0x01, 0x80]);
        using var reader = new BinaryReader(ms);

        var value = IntegrityService.ReadUInt16BigEndian(reader);

        Assert.Equal(0x0180, value);
    }

    [Fact]
    public void ReadUInt24BigEndian_ParsesCorrectly()
    {
        using var ms = new MemoryStream([0x00, 0x10, 0x20]);
        using var reader = new BinaryReader(ms);

        var value = IntegrityService.ReadUInt24BigEndian(reader);

        Assert.Equal(0x001020, value);
    }

    // ═══ ResolvePatchFormat ═══════════════════════════════════════════

    [Fact]
    public void ResolvePatchFormat_IpsMagic_ReturnsIPS()
    {
        var patchPath = Path.Combine(_tempDir, "test.ips");
        File.WriteAllBytes(patchPath, "PATCH"u8.ToArray());

        Assert.Equal("IPS", IntegrityService.ResolvePatchFormat(patchPath));
    }

    [Fact]
    public void ResolvePatchFormat_BpsMagic_ReturnsBPS()
    {
        var patchPath = Path.Combine(_tempDir, "test.bps");
        File.WriteAllBytes(patchPath, "BPS1"u8.ToArray());

        Assert.Equal("BPS", IntegrityService.ResolvePatchFormat(patchPath));
    }

    [Fact]
    public void ResolvePatchFormat_NoMagic_FallsBackToExtension()
    {
        var patchPath = Path.Combine(_tempDir, "test.xdelta");
        File.WriteAllBytes(patchPath, [0x00, 0x00, 0x00, 0x00, 0x00]);

        Assert.Equal("XDELTA", IntegrityService.ResolvePatchFormat(patchPath));
    }

    [Fact]
    public void ResolvePatchFormat_UnknownExtension_Throws()
    {
        var patchPath = Path.Combine(_tempDir, "test.xyz");
        File.WriteAllBytes(patchPath, [0x00, 0x00, 0x00, 0x00, 0x00]);

        Assert.Throws<InvalidOperationException>(() => IntegrityService.ResolvePatchFormat(patchPath));
    }

    // ═══ ApplyPatch – IPS format (via public API) ════════════════════

    [Fact]
    public void ApplyPatch_IPS_SimpleOverwrite_AppliesCorrectly()
    {
        var source = Path.Combine(_tempDir, "source.rom");
        var patch = Path.Combine(_tempDir, "patch.ips");
        var output = Path.Combine(_tempDir, "output.rom");

        // Source: 16 bytes of 0x00
        File.WriteAllBytes(source, new byte[16]);

        // IPS patch: header "PATCH" + one record at offset 0x000004, size 3 (bytes 0xAA 0xBB 0xCC) + EOF
        var ipsPatch = new List<byte>();
        ipsPatch.AddRange("PATCH"u8.ToArray());
        // Record: offset 0x000004
        ipsPatch.AddRange(new byte[] { 0x00, 0x00, 0x04 });
        // Size: 3
        ipsPatch.AddRange(new byte[] { 0x00, 0x03 });
        // Data
        ipsPatch.AddRange(new byte[] { 0xAA, 0xBB, 0xCC });
        // EOF marker
        ipsPatch.AddRange("EOF"u8.ToArray());

        File.WriteAllBytes(patch, ipsPatch.ToArray());

        var result = IntegrityService.ApplyPatch(source, patch, output);

        Assert.Equal("IPS", result.Format);
        Assert.True(File.Exists(output));
        var outputBytes = File.ReadAllBytes(output);
        Assert.Equal(16, outputBytes.Length);
        Assert.Equal(0xAA, outputBytes[4]);
        Assert.Equal(0xBB, outputBytes[5]);
        Assert.Equal(0xCC, outputBytes[6]);
        Assert.Equal(0x00, outputBytes[3]); // unchanged
        Assert.Equal(0x00, outputBytes[7]); // unchanged
    }

    [Fact]
    public void ApplyPatch_IPS_RLERecord_AppliesCorrectly()
    {
        var source = Path.Combine(_tempDir, "source.rom");
        var patch = Path.Combine(_tempDir, "patch.ips");
        var output = Path.Combine(_tempDir, "output.rom");

        File.WriteAllBytes(source, new byte[32]);

        // IPS with RLE record: offset 0x000008, size=0 (RLE marker), RLE size=4, value=0xFF
        var ipsPatch = new List<byte>();
        ipsPatch.AddRange("PATCH"u8.ToArray());
        // Record: offset 0x000008
        ipsPatch.AddRange(new byte[] { 0x00, 0x00, 0x08 });
        // Size: 0 = RLE
        ipsPatch.AddRange(new byte[] { 0x00, 0x00 });
        // RLE size: 4
        ipsPatch.AddRange(new byte[] { 0x00, 0x04 });
        // RLE value
        ipsPatch.Add(0xFF);
        // EOF
        ipsPatch.AddRange("EOF"u8.ToArray());

        File.WriteAllBytes(patch, ipsPatch.ToArray());

        var result = IntegrityService.ApplyPatch(source, patch, output);

        var outputBytes = File.ReadAllBytes(output);
        Assert.Equal(32, outputBytes.Length);
        Assert.Equal(0xFF, outputBytes[8]);
        Assert.Equal(0xFF, outputBytes[9]);
        Assert.Equal(0xFF, outputBytes[10]);
        Assert.Equal(0xFF, outputBytes[11]);
        Assert.Equal(0x00, outputBytes[12]); // unchanged
    }

    [Fact]
    public void ApplyPatch_IPS_EofWithTruncation_TruncatesOutput()
    {
        var source = Path.Combine(_tempDir, "source.rom");
        var patch = Path.Combine(_tempDir, "patch.ips");
        var output = Path.Combine(_tempDir, "output.rom");

        // Source: 32 bytes
        File.WriteAllBytes(source, new byte[32]);

        // IPS with EOF marker + 3 bytes for target size (truncate to 16)
        var ipsPatch = new List<byte>();
        ipsPatch.AddRange("PATCH"u8.ToArray());
        // EOF marker
        ipsPatch.AddRange("EOF"u8.ToArray());
        // Target size: 16 (0x000010)
        ipsPatch.AddRange(new byte[] { 0x00, 0x00, 0x10 });

        File.WriteAllBytes(patch, ipsPatch.ToArray());

        var result = IntegrityService.ApplyPatch(source, patch, output);

        var outputBytes = File.ReadAllBytes(output);
        Assert.Equal(16, outputBytes.Length);
    }

    [Fact]
    public void ApplyPatch_IPS_ExtendsBeyondSource_PadsWithZeros()
    {
        var source = Path.Combine(_tempDir, "source.rom");
        var patch = Path.Combine(_tempDir, "patch.ips");
        var output = Path.Combine(_tempDir, "output.rom");

        // Source: 8 bytes
        File.WriteAllBytes(source, new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06, 0x07, 0x08 });

        // IPS patch: write at offset 10 (beyond source end)
        var ipsPatch = new List<byte>();
        ipsPatch.AddRange("PATCH"u8.ToArray());
        ipsPatch.AddRange(new byte[] { 0x00, 0x00, 0x0A }); // offset 10
        ipsPatch.AddRange(new byte[] { 0x00, 0x02 });        // size 2
        ipsPatch.AddRange(new byte[] { 0xDE, 0xAD });        // data
        ipsPatch.AddRange("EOF"u8.ToArray());

        File.WriteAllBytes(patch, ipsPatch.ToArray());

        var result = IntegrityService.ApplyPatch(source, patch, output);

        var outputBytes = File.ReadAllBytes(output);
        Assert.Equal(12, outputBytes.Length);
        Assert.Equal(0x01, outputBytes[0]); // original preserved
        Assert.Equal(0x00, outputBytes[8]); // padded
        Assert.Equal(0x00, outputBytes[9]); // padded
        Assert.Equal(0xDE, outputBytes[10]);
        Assert.Equal(0xAD, outputBytes[11]);
    }

    [Fact]
    public void ApplyPatch_IPS_InvalidHeader_Throws()
    {
        var source = Path.Combine(_tempDir, "source.rom");
        var patch = Path.Combine(_tempDir, "patch.ips");
        var output = Path.Combine(_tempDir, "output.rom");

        File.WriteAllBytes(source, new byte[16]);
        File.WriteAllBytes(patch, "NOTANIPSPATCH"u8.ToArray());

        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(source, patch, output));
    }

    // ═══ ApplyPatch – Argument validation ════════════════════════════

    [Fact]
    public void ApplyPatch_MissingSourceFile_Throws()
    {
        var patch = Path.Combine(_tempDir, "patch.ips");
        File.WriteAllBytes(patch, "PATCH"u8.ToArray());

        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(
                Path.Combine(_tempDir, "nonexistent.rom"),
                patch,
                Path.Combine(_tempDir, "output.rom")));
    }

    [Fact]
    public void ApplyPatch_MissingPatchFile_Throws()
    {
        var source = Path.Combine(_tempDir, "source.rom");
        File.WriteAllBytes(source, new byte[16]);

        Assert.Throws<InvalidOperationException>(() =>
            IntegrityService.ApplyPatch(
                source,
                Path.Combine(_tempDir, "nonexistent.ips"),
                Path.Combine(_tempDir, "output.rom")));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void ApplyPatch_EmptyArguments_ThrowsArgumentException(string bad)
    {
        Assert.Throws<ArgumentException>(() =>
            IntegrityService.ApplyPatch(bad, "patch.ips", "output.rom"));
    }

    [Fact]
    public void ApplyPatch_NullArgument_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            IntegrityService.ApplyPatch(null!, "patch.ips", "output.rom"));
    }

    // ═══ ApplyPatch result ═══════════════════════════════════════════

    [Fact]
    public void ApplyPatch_ReturnsCorrectResult()
    {
        var source = Path.Combine(_tempDir, "source.rom");
        var patch = Path.Combine(_tempDir, "patch.ips");
        var output = Path.Combine(_tempDir, "output.rom");

        File.WriteAllBytes(source, new byte[] { 0x01, 0x02, 0x03, 0x04 });

        // Minimal IPS: header + EOF (no records)
        var ipsPatch = new List<byte>();
        ipsPatch.AddRange("PATCH"u8.ToArray());
        ipsPatch.AddRange("EOF"u8.ToArray());
        File.WriteAllBytes(patch, ipsPatch.ToArray());

        var result = IntegrityService.ApplyPatch(source, patch, output);

        Assert.Equal("IPS", result.Format);
        Assert.Equal(source, result.SourcePath);
        Assert.Equal(patch, result.PatchPath);
        Assert.Equal(output, result.OutputPath);
        Assert.Equal(4, result.OutputSizeBytes);
        Assert.NotNull(result.OutputSha256);
        Assert.Null(result.ToolPath); // IPS uses built-in, no external tool
    }
}
