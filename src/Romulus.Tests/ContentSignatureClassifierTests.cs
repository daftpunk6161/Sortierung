using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public sealed class ContentSignatureClassifierTests
{
    [Fact]
    public void Pdf_DetectedByMagic()
    {
        byte[] header = [0x25, 0x50, 0x44, 0x46, 0x2D, 0x31, 0x2E, 0x34]; // %PDF-1.4
        Assert.Equal(ContentType.Pdf, ContentSignatureClassifier.Classify(header));
        Assert.True(ContentSignatureClassifier.IsNonRom(ContentType.Pdf));
    }

    [Fact]
    public void Png_DetectedByMagic()
    {
        byte[] header = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        Assert.Equal(ContentType.Png, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Jpeg_DetectedByMagic()
    {
        byte[] header = [0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x10, 0x4A, 0x46]; // JFIF
        Assert.Equal(ContentType.Jpeg, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Gif89a_DetectedByMagic()
    {
        byte[] header = [0x47, 0x49, 0x46, 0x38, 0x39, 0x61, 0x01, 0x00]; // GIF89a
        Assert.Equal(ContentType.Gif, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Gif87a_DetectedByMagic()
    {
        byte[] header = [0x47, 0x49, 0x46, 0x38, 0x37, 0x61, 0x01, 0x00]; // GIF87a
        Assert.Equal(ContentType.Gif, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Bmp_DetectedByMagic()
    {
        byte[] header = [0x42, 0x4D, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]; // BM
        Assert.Equal(ContentType.Bmp, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Mp3_Id3Tag_DetectedByMagic()
    {
        byte[] header = [0x49, 0x44, 0x33, 0x03, 0x00, 0x00, 0x00, 0x00]; // ID3
        Assert.Equal(ContentType.Mp3, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Mp3_SyncWord_DetectedByMagic()
    {
        byte[] header = [0xFF, 0xFB, 0x90, 0x00, 0x00, 0x00, 0x00, 0x00];
        Assert.Equal(ContentType.Mp3, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Flac_DetectedByMagic()
    {
        byte[] header = [0x66, 0x4C, 0x61, 0x43, 0x00, 0x00, 0x00, 0x22]; // fLaC
        Assert.Equal(ContentType.Flac, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Ogg_DetectedByMagic()
    {
        byte[] header = [0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0x00, 0x00]; // OggS
        Assert.Equal(ContentType.Ogg, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void WindowsExe_DetectedByMagic()
    {
        byte[] header = [0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00, 0x00, 0x00]; // MZ
        Assert.Equal(ContentType.WindowsExe, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Elf_DetectedByMagic()
    {
        byte[] header = [0x7F, 0x45, 0x4C, 0x46, 0x02, 0x01, 0x01, 0x00]; // ELF
        Assert.Equal(ContentType.Elf, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void MachO_BigEndian_DetectedByMagic()
    {
        byte[] header = [0xFE, 0xED, 0xFA, 0xCE, 0x00, 0x00, 0x00, 0x00];
        Assert.Equal(ContentType.MachO, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void MachO_LittleEndian_DetectedByMagic()
    {
        byte[] header = [0xCE, 0xFA, 0xED, 0xFE, 0x00, 0x00, 0x00, 0x00];
        Assert.Equal(ContentType.MachO, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Riff_DetectedByMagic()
    {
        byte[] header = [0x52, 0x49, 0x46, 0x46, 0x00, 0x00, 0x00, 0x00]; // RIFF
        Assert.Equal(ContentType.Riff, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Xml_DetectedByMagic()
    {
        byte[] header = [0x3C, 0x3F, 0x78, 0x6D, 0x6C, 0x20, 0x76, 0x65]; // <?xml ve...
        Assert.Equal(ContentType.Xml, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Sqlite_DetectedByMagic()
    {
        byte[] header = [0x53, 0x51, 0x4C, 0x69, 0x74, 0x65, 0x20, 0x66]; // SQLite f...
        Assert.Equal(ContentType.Sqlite, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Unknown_WhenTooShort()
    {
        byte[] header = [0x00];
        Assert.Equal(ContentType.Unknown, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Unknown_WhenNoMatch()
    {
        byte[] header = [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00];
        Assert.Equal(ContentType.Unknown, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void Unknown_IsNotNonRom()
    {
        Assert.False(ContentSignatureClassifier.IsNonRom(ContentType.Unknown));
    }

    [Theory]
    [InlineData(ContentType.Pdf)]
    [InlineData(ContentType.Png)]
    [InlineData(ContentType.Jpeg)]
    [InlineData(ContentType.Mp3)]
    [InlineData(ContentType.WindowsExe)]
    [InlineData(ContentType.Elf)]
    public void NonRom_Types_AreAllNonRom(ContentType type)
    {
        Assert.True(ContentSignatureClassifier.IsNonRom(type));
    }

    [Fact]
    public void NesHeader_NotDetectedAsNonRom()
    {
        // iNES header: NES\x1A
        byte[] header = [0x4E, 0x45, 0x53, 0x1A, 0x02, 0x01, 0x01, 0x00];
        Assert.Equal(ContentType.Unknown, ContentSignatureClassifier.Classify(header));
    }

    [Fact]
    public void GbaHeader_NotDetectedAsNonRom()
    {
        // GBA ROM header starts with ARM branch instruction, not a known non-ROM magic
        byte[] header = [0x2E, 0x00, 0x00, 0xEA, 0x24, 0xFF, 0xAE, 0x51];
        Assert.Equal(ContentType.Unknown, ContentSignatureClassifier.Classify(header));
    }
}
