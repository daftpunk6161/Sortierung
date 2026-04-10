using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

public class Crc32Tests
{
    [Fact]
    public void HashFile_ReturnsNonEmpty()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f }); // "Hello"
            var hash = Crc32.HashFile(path);
            Assert.NotNull(hash);
            Assert.Equal(8, hash.Length); // hex-8
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void HashStream_KnownValue()
    {
        // CRC32 of "Hello" = f7d18982
        using var ms = new MemoryStream(new byte[] { 0x48, 0x65, 0x6c, 0x6c, 0x6f });
        var hash = Crc32.HashStream(ms);
        Assert.Equal("f7d18982", hash);
    }

    [Fact]
    public void HashStream_EmptyStream()
    {
        using var ms = new MemoryStream(Array.Empty<byte>());
        var hash = Crc32.HashStream(ms);
        Assert.Equal("00000000", hash);
    }

    [Fact]
    public void HashFile_Deterministic()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5 });
            var h1 = Crc32.HashFile(path);
            var h2 = Crc32.HashFile(path);
            Assert.Equal(h1, h2);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
