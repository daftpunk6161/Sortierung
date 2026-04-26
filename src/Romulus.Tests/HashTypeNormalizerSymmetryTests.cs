using Romulus.Contracts.Hashing;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-DAT-13 + F-DAT-14: hash-type normalization and lookup-order coverage must
/// be deterministic and symmetric across DatIndex and EnrichmentPipelinePhase,
/// and the lookup chain must include SHA256 so DAT entries indexed under that
/// hash are reachable.
/// </summary>
public sealed class HashTypeNormalizerSymmetryTests
{
    [Theory]
    [InlineData("sha1", "SHA1")]
    [InlineData(" SHA1 ", "SHA1")]
    [InlineData("Sha-1", "SHA1")] // unknown alias falls back to default — but not silently misclassified
    [InlineData("crc", "CRC32")]
    [InlineData("CRC32", "CRC32")]
    [InlineData("md5", "MD5")]
    [InlineData("sha256", "SHA256")]
    [InlineData(null, "SHA1")]
    [InlineData("", "SHA1")]
    public void Normalize_ProducesCanonicalToken(string? input, string expected)
    {
        Assert.Equal(expected, HashTypeNormalizer.Normalize(input));
    }

    [Theory]
    [InlineData("sha1", true)]
    [InlineData("crc", true)]
    [InlineData("CRC32", true)]
    [InlineData("md5", true)]
    [InlineData("SHA256", true)]
    [InlineData("MD4", false)]
    [InlineData("blake2", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void TryNormalize_ReportsRecognitionStatus(string? input, bool expectedKnown)
    {
        var known = HashTypeNormalizer.TryNormalize(input, out var canonical);
        Assert.Equal(expectedKnown, known);
        if (!expectedKnown)
        {
            // Unknown still produces the canonical default to preserve backwards-compat
            // for callers using Normalize, but TryNormalize must return false so callers
            // that need fail-closed behaviour can opt in.
            Assert.Equal("SHA1", canonical);
        }
    }

    [Fact]
    public void DatIndex_UsesCentralNormalizer_ForCrcAlias()
    {
        // F-DAT-14: DatIndex must canonicalise hash-type aliases identically to the
        // central normalizer so a DAT row with hashType="crc" lands in the same key
        // as a lookup using "CRC32".
        var index = new DatIndex();
        index.Add("NES", "deadbeef", "Game", romFileName: null, isBios: false, parentGameName: null, hashType: "crc");

        var direct = index.Lookup("NES", "CRC32", "deadbeef");
        Assert.Equal("Game", direct);
    }

    [Fact]
    public void GetLookupHashTypeOrder_IncludesSha256_ForReachability()
    {
        // F-DAT-13: modern DATs publish SHA256 hashes; without SHA256 in the
        // lookup chain, an index entry stored under SHA256 is unreachable.
        var order = EnrichmentPipelinePhase.GetLookupHashTypeOrder("SHA1");
        Assert.Contains("SHA256", order);
    }

    [Fact]
    public void GetLookupHashTypeOrder_PreferredHashStaysFirst()
    {
        var order = EnrichmentPipelinePhase.GetLookupHashTypeOrder("SHA256");
        Assert.Equal("SHA256", order[0]);
    }
}
