using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-DAT-06: <see cref="DatIndex.Lookup(string,string)"/> (untyped overload) MUST be
/// deterministic when the same raw hash literal is registered under multiple hash
/// types. The implementation orders by HashType ordinally then by full key, so
/// MD5 wins over SHA1 wins over SHA256 for an identical hash literal. This test
/// pins that contract to prevent silent reorderings (e.g. a future refactor that
/// returns the first ConcurrentDictionary entry — order would be undefined).
///
/// F-DAT-07 (also covered): <see cref="DatIndex.LookupByName"/> with explicit
/// consoleKey must keep returning the single console hit; the homonym fallback is
/// covered by <see cref="DatIndex.LookupAllByName"/>.
/// </summary>
public sealed class DatIndexLookupDeterminismTests
{
    private const string Console = "snes";
    private const string Hash = "deadbeefdeadbeefdeadbeefdeadbeefdeadbeef"; // 40 hex (legal for SHA1)

    [Fact]
    public void UntypedLookup_AcrossHashTypes_PrefersOrdinallyFirstHashType()
    {
        var index = new DatIndex();
        // Register the identical raw-hash literal under three hash types.
        // Distinct game names so we can verify which hash-type entry wins.
        index.Add(Console, Hash, "Game-MD5",    hashType: "MD5");
        index.Add(Console, Hash, "Game-SHA1",   hashType: "SHA1");
        index.Add(Console, Hash, "Game-SHA256", hashType: "SHA256");

        // Untyped lookup must pick a deterministic winner.
        var winner = index.Lookup(Console, Hash);
        Assert.NotNull(winner);

        // Stability check: repeating the call returns the same winner.
        Assert.Equal(winner, index.Lookup(Console, Hash));
        Assert.Equal(winner, index.Lookup(Console, Hash));

        // The deterministic winner is the ordinally-first hash-type entry (MD5 < SHA1 < SHA256).
        Assert.Equal("Game-MD5", winner);
    }

    [Fact]
    public void TypedLookup_DistinguishesHashTypes_WhenRawHashLiteralCollides()
    {
        var index = new DatIndex();
        index.Add(Console, Hash, "Game-MD5",    hashType: "MD5");
        index.Add(Console, Hash, "Game-SHA1",   hashType: "SHA1");
        index.Add(Console, Hash, "Game-SHA256", hashType: "SHA256");

        Assert.Equal("Game-MD5",    index.Lookup(Console, "MD5",    Hash));
        Assert.Equal("Game-SHA1",   index.Lookup(Console, "SHA1",   Hash));
        Assert.Equal("Game-SHA256", index.Lookup(Console, "SHA256", Hash));
    }

    [Fact]
    public void LookupByName_WithExplicitConsole_StaysSingleHit()
    {
        var index = new DatIndex();
        // Same gameName lives on two different consoles (real homonym scenario,
        // e.g. a multi-platform release with identical title).
        index.Add("snes", "1111111111111111111111111111111111111111", "Pac-Man");
        index.Add("nes",  "2222222222222222222222222222222222222222", "Pac-Man");

        // Single-console caller must still get exactly one entry.
        Assert.NotNull(index.LookupByName("snes", "Pac-Man"));
        Assert.NotNull(index.LookupByName("nes",  "Pac-Man"));

        // Homonym discovery is the explicit job of LookupAllByName(gameName).
        var all = index.LookupAllByName("Pac-Man");
        Assert.Equal(2, all.Count);
        // Result order must be deterministic (ordinal console-key sort: nes < snes).
        Assert.Equal(new[] { "nes", "snes" }, all.Select(t => t.ConsoleKey).ToArray());
    }
}
