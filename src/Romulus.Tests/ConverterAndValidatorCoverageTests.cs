using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for DolphinToolConverter, SevenZipToolConverter, PsxtractToolConverter,
/// ConversionOutputValidator, and PbpEncryptionDetector.
/// Uses IToolRunner stubs and temp files for isolation.
/// </summary>
public sealed class ConverterAndValidatorCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public ConverterAndValidatorCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"ConverterTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    // ═══ ConversionOutputValidator ═══════════════════════════════════

    [Fact]
    public void OutputValidator_MissingFile_ReturnsFalse()
    {
        var ok = ConversionOutputValidator.TryValidateCreatedOutput(
            Path.Combine(_tempDir, "no-such-file.chd"), out var reason);
        Assert.False(ok);
        Assert.Equal("output-not-created", reason);
    }

    [Fact]
    public void OutputValidator_EmptyFile_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "empty.chd");
        File.WriteAllBytes(path, []);
        var ok = ConversionOutputValidator.TryValidateCreatedOutput(path, out var reason);
        Assert.False(ok);
        Assert.Equal("output-empty", reason);
    }

    [Fact]
    public void OutputValidator_NonEmptyFile_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "valid.chd");
        File.WriteAllBytes(path, [0x01, 0x02, 0x03]);
        var ok = ConversionOutputValidator.TryValidateCreatedOutput(path, out var reason);
        Assert.True(ok);
        Assert.Equal("", reason);
    }

    // ═══ DolphinToolConverter.Verify (pure file I/O) ════════════════

    [Fact]
    public void DolphinVerify_ValidRvzMagic_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "game.rvz");
        var data = new byte[1024];
        data[0] = (byte)'R';
        data[1] = (byte)'V';
        data[2] = (byte)'Z';
        data[3] = 0x01;
        File.WriteAllBytes(path, data);

        Assert.True(DolphinToolConverter.Verify(path));
    }

    [Fact]
    public void DolphinVerify_WrongMagic_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "fake.rvz");
        File.WriteAllBytes(path, [0x00, 0x00, 0x00, 0x00, 0x00]);

        Assert.False(DolphinToolConverter.Verify(path));
    }

    [Fact]
    public void DolphinVerify_FileTooShort_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "tiny.rvz");
        File.WriteAllBytes(path, [0x52, 0x56]); // "RV" only

        Assert.False(DolphinToolConverter.Verify(path));
    }

    [Fact]
    public void DolphinVerify_MissingFile_ReturnsFalse()
    {
        Assert.False(DolphinToolConverter.Verify(Path.Combine(_tempDir, "missing.rvz")));
    }

    // ═══ DolphinToolConverter.Convert ════════════════════════════════

    [Fact]
    public void DolphinConvert_UnsupportedExtension_ReturnsSkipped()
    {
        var tools = new StubToolRunner();
        var converter = new DolphinToolConverter(tools);

        var result = converter.Convert("game.txt", "game.rvz", "dolphintool.exe", ".txt");

        Assert.Equal(ConversionOutcome.Skipped, result.Outcome);
        Assert.Equal("dolphintool-unsupported-source", result.Reason);
    }

    [Fact]
    public void DolphinConvert_ToolFails_ReturnsError()
    {
        var tools = new StubToolRunner { NextProcess = new ToolResult(1, "error", false) };
        var converter = new DolphinToolConverter(tools);

        var result = converter.Convert("game.iso", Path.Combine(_tempDir, "game.rvz"), "dolphintool.exe", ".iso");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("dolphintool-failed", result.Reason);
        Assert.Equal(1, result.ExitCode);
    }

    [Fact]
    public void DolphinConvert_ToolSucceeds_OutputMissing_ReturnsError()
    {
        var tools = new StubToolRunner { NextProcess = new ToolResult(0, "ok", true) };
        var converter = new DolphinToolConverter(tools);

        var result = converter.Convert("game.iso", Path.Combine(_tempDir, "missing.rvz"), "dolphintool.exe", ".iso");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("output-not-created", result.Reason);
    }

    [Fact]
    public void DolphinConvert_ToolSucceeds_OutputPresent_ReturnsSuccess()
    {
        var targetPath = Path.Combine(_tempDir, "success.rvz");
        File.WriteAllBytes(targetPath, new byte[100]);
        var tools = new StubToolRunner { NextProcess = new ToolResult(0, "ok", true) };
        var converter = new DolphinToolConverter(tools);

        var result = converter.Convert("game.iso", targetPath, "dolphintool.exe", ".iso");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.Equal(targetPath, result.TargetPath);
    }

    [Theory]
    [InlineData(".iso")]
    [InlineData(".gcm")]
    [InlineData(".wbfs")]
    [InlineData(".rvz")]
    [InlineData(".gcz")]
    [InlineData(".wia")]
    public void DolphinConvert_AllowedExtension_InvokesTool(string ext)
    {
        var targetPath = Path.Combine(_tempDir, $"game_{ext.TrimStart('.')}.rvz");
        File.WriteAllBytes(targetPath, new byte[100]);
        var tools = new StubToolRunner { NextProcess = new ToolResult(0, "ok", true) };
        var converter = new DolphinToolConverter(tools);

        var result = converter.Convert("game" + ext, targetPath, "dolphintool.exe", ext);

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.True(tools.InvokeCount > 0);
    }

    // ═══ SevenZipToolConverter ═══════════════════════════════════════

    [Fact]
    public void SevenZipConvert_ToolFails_ReturnsError()
    {
        var tools = new StubToolRunner { NextProcess = new ToolResult(2, "error", false) };
        var converter = new SevenZipToolConverter(tools);

        var result = converter.Convert("game.rom", Path.Combine(_tempDir, "game.zip"), "7z.exe");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("7z-failed", result.Reason);
        Assert.Equal(2, result.ExitCode);
    }

    [Fact]
    public void SevenZipConvert_ToolSucceeds_OutputPresent_ReturnsSuccess()
    {
        var targetPath = Path.Combine(_tempDir, "game.zip");
        File.WriteAllBytes(targetPath, new byte[50]);
        var tools = new StubToolRunner { NextProcess = new ToolResult(0, "ok", true) };
        var converter = new SevenZipToolConverter(tools);

        var result = converter.Convert("game.rom", targetPath, "7z.exe");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void SevenZipConvert_ToolSucceeds_OutputMissing_ReturnsError()
    {
        var tools = new StubToolRunner { NextProcess = new ToolResult(0, "ok", true) };
        var converter = new SevenZipToolConverter(tools);

        var result = converter.Convert("game.rom", Path.Combine(_tempDir, "missing.zip"), "7z.exe");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("output-not-created", result.Reason);
    }

    [Fact]
    public void SevenZipVerify_ToolNotFound_ReturnsFalse()
    {
        var tools = new StubToolRunner { FindToolResult = null };
        var converter = new SevenZipToolConverter(tools);

        Assert.False(converter.Verify("game.zip"));
    }

    [Fact]
    public void SevenZipVerify_ToolSucceeds_ReturnsTrue()
    {
        var tools = new StubToolRunner
        {
            FindToolResult = "7z.exe",
            NextProcess = new ToolResult(0, "Everything is Ok", true)
        };
        var converter = new SevenZipToolConverter(tools);

        Assert.True(converter.Verify("game.zip"));
    }

    [Fact]
    public void SevenZipVerify_ToolReportsError_ReturnsFalse()
    {
        var tools = new StubToolRunner
        {
            FindToolResult = "7z.exe",
            NextProcess = new ToolResult(2, "CRC Failed", false)
        };
        var converter = new SevenZipToolConverter(tools);

        Assert.False(converter.Verify("game.zip"));
    }

    // ═══ PsxtractToolConverter ══════════════════════════════════════

    [Fact]
    public void PsxtractConvert_ToolFails_ReturnsError()
    {
        var tools = new StubToolRunner { NextProcess = new ToolResult(1, "error", false) };
        var converter = new PsxtractToolConverter(tools);

        var result = converter.Convert("game.pbp", Path.Combine(_tempDir, "game.chd"), "psxtract.exe", "extract");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Equal("psxtract-failed", result.Reason);
    }

    [Fact]
    public void PsxtractConvert_ToolSucceeds_OutputPresent_ReturnsSuccess()
    {
        var targetPath = Path.Combine(_tempDir, "ps1.chd");
        File.WriteAllBytes(targetPath, new byte[100]);
        var tools = new StubToolRunner { NextProcess = new ToolResult(0, "ok", true) };
        var converter = new PsxtractToolConverter(tools);

        var result = converter.Convert("game.pbp", targetPath, "psxtract.exe", "extract");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
    }

    [Fact]
    public void PsxtractConvert_ToolSucceeds_OutputMissing_ReturnsError()
    {
        var tools = new StubToolRunner { NextProcess = new ToolResult(0, "ok", true) };
        var converter = new PsxtractToolConverter(tools);

        var result = converter.Convert("game.pbp", Path.Combine(_tempDir, "missing.chd"), "psxtract.exe", "extract");

        Assert.Equal(ConversionOutcome.Error, result.Outcome);
    }

    // ═══ PbpEncryptionDetector ══════════════════════════════════════

    [Fact]
    public void PbpEncryption_MissingFile_ReturnsFalse()
    {
        Assert.False(PbpEncryptionDetector.IsEncrypted(Path.Combine(_tempDir, "missing.pbp")));
    }

    [Fact]
    public void PbpEncryption_FileTooShort_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "tiny.pbp");
        File.WriteAllBytes(path, new byte[10]);
        Assert.False(PbpEncryptionDetector.IsEncrypted(path));
    }

    [Fact]
    public void PbpEncryption_WrongMagic_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "notpbp.bin");
        var data = new byte[4096];
        data[0] = 0xFF; // not PBP magic
        File.WriteAllBytes(path, data);
        Assert.False(PbpEncryptionDetector.IsEncrypted(path));
    }

    [Fact]
    public void PbpEncryption_ValidPbp_NoPspMagic_ReturnsFalse()
    {
        var data = BuildPbpHeader(dataPspOffset: 40, encrypted: false, includePspMagic: false);
        var path = Path.Combine(_tempDir, "nopsp.pbp");
        File.WriteAllBytes(path, data);
        Assert.False(PbpEncryptionDetector.IsEncrypted(path));
    }

    [Fact]
    public void PbpEncryption_ValidPbp_Unencrypted_ReturnsFalse()
    {
        var data = BuildPbpHeader(dataPspOffset: 40, encrypted: false, includePspMagic: true);
        var path = Path.Combine(_tempDir, "unencrypted.pbp");
        File.WriteAllBytes(path, data);
        Assert.False(PbpEncryptionDetector.IsEncrypted(path));
    }

    [Fact]
    public void PbpEncryption_ValidPbp_Encrypted_ReturnsTrue()
    {
        var data = BuildPbpHeader(dataPspOffset: 256, encrypted: true, includePspMagic: true);
        var path = Path.Combine(_tempDir, "encrypted.pbp");
        File.WriteAllBytes(path, data);
        Assert.True(PbpEncryptionDetector.IsEncrypted(path));
    }

    [Fact]
    public void PbpEncryption_ZeroDataPspOffset_ReturnsFalse()
    {
        var data = BuildPbpHeader(dataPspOffset: 0, encrypted: false, includePspMagic: true);
        var path = Path.Combine(_tempDir, "zerooffset.pbp");
        File.WriteAllBytes(path, data);
        Assert.False(PbpEncryptionDetector.IsEncrypted(path));
    }

    [Fact]
    public void PbpEncryption_DataPspOffset_BeyondFileEnd_ReturnsFalse()
    {
        // Valid PBP header but DATA.PSP offset points beyond file end
        var data = new byte[100];
        data[0] = 0x00; data[1] = (byte)'P'; data[2] = (byte)'B'; data[3] = (byte)'P';
        // Offset at byte 32: point to offset 50000 (beyond file)
        BitConverter.GetBytes((uint)50000).CopyTo(data, 32);
        var path = Path.Combine(_tempDir, "beyond.pbp");
        File.WriteAllBytes(path, data);
        Assert.False(PbpEncryptionDetector.IsEncrypted(path));
    }

    // ═══ Constructor null guards ════════════════════════════════════

    [Fact]
    public void DolphinConverter_NullToolRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new DolphinToolConverter(null!));
    }

    [Fact]
    public void SevenZipConverter_NullToolRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new SevenZipToolConverter(null!));
    }

    [Fact]
    public void PsxtractConverter_NullToolRunner_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => new PsxtractToolConverter(null!));
    }

    // ═══ Helpers ════════════════════════════════════════════════════

    private static byte[] BuildPbpHeader(uint dataPspOffset, bool encrypted, bool includePspMagic)
    {
        // PBP needs: 40 bytes header + space for DATA.PSP section
        var totalSize = Math.Max(dataPspOffset + 0xD5, 512);
        var data = new byte[totalSize];

        // PBP magic: 0x00 'P' 'B' 'P'
        data[0] = 0x00;
        data[1] = (byte)'P';
        data[2] = (byte)'B';
        data[3] = (byte)'P';

        // Version (bytes 4-7)
        data[4] = 0x01;

        // Offset table: 8 entries × 4 bytes at bytes 8-39
        // 7th entry (index 6) at byte 32 = DATA.PSP offset
        BitConverter.GetBytes(dataPspOffset).CopyTo(data, 32);

        if (dataPspOffset > 0 && dataPspOffset + 0xD5 <= totalSize)
        {
            if (includePspMagic)
            {
                // ~PSP magic at DATA.PSP start
                data[dataPspOffset] = (byte)'~';
                data[dataPspOffset + 1] = (byte)'P';
                data[dataPspOffset + 2] = (byte)'S';
                data[dataPspOffset + 3] = (byte)'P';
            }

            // Encryption flag at DATA.PSP + 0xD4
            data[dataPspOffset + 0xD4] = encrypted ? (byte)0x01 : (byte)0x00;
        }

        return data;
    }

    /// <summary>Minimal IToolRunner stub for converter tests.</summary>
    private sealed class StubToolRunner : IToolRunner
    {
        public ToolResult NextProcess { get; set; } = new(0, "ok", true);
        public string? FindToolResult { get; set; } = "tool.exe";
        public int InvokeCount { get; private set; }

        public string? FindTool(string toolName) => FindToolResult;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            InvokeCount++;
            return NextProcess;
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
        {
            InvokeCount++;
            return NextProcess;
        }
    }
}
