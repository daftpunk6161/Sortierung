using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-DAT-05: DatIndex.Add uses deterministic first-wins semantics for the
/// name index when the same gameName is added with multiple distinct hashes
/// (a normal situation when one DAT game contains several ROM tracks/files).
/// The hash index keeps every distinct hash, but the name index keeps only
/// the first observation. This regression test pins that contract so a future
/// "fix" cannot silently flip the semantics.
/// </summary>
public sealed class DatIndexInsertSymmetryTests
{
    [Fact]
    public void Add_SameGameName_WithDifferentHashes_NameIndexKeepsFirstWins()
    {
        var index = new DatIndex();
        const string console = "psx";
        const string game = "Crash Bandicoot (USA)";

        index.Add(console, hash: "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa", gameName: game,
            romFileName: "Crash Bandicoot (USA).cue");
        index.Add(console, hash: "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", gameName: game,
            romFileName: "Crash Bandicoot (USA).bin");

        // Hash index keeps both entries (TotalEntries reflects this).
        Assert.Equal(2, index.TotalEntries);

        // Name index keeps the FIRST observation (first-wins, deterministic).
        var nameHits = index.LookupAllByName(game);
        Assert.Single(nameHits);
        Assert.Equal(console, nameHits[0].ConsoleKey);
        Assert.Equal("Crash Bandicoot (USA).cue", nameHits[0].Entry.RomFileName);

        // Both hashes resolve back to the SAME gameName.
        var byHash1 = index.Lookup(console, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
        var byHash2 = index.Lookup(console, "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb");
        Assert.NotNull(byHash1);
        Assert.NotNull(byHash2);
        Assert.Equal(game, byHash1);
        Assert.Equal(game, byHash2);
    }

    [Fact]
    public void Add_DifferentGameNames_WithDifferentHashes_NameIndexHasBoth()
    {
        var index = new DatIndex();
        const string console = "snes";

        index.Add(console, hash: "1111111111111111111111111111111111111111", gameName: "Super Mario World (USA)");
        index.Add(console, hash: "2222222222222222222222222222222222222222", gameName: "Donkey Kong Country (USA)");

        Assert.NotNull(index.LookupByName(console, "Super Mario World (USA)"));
        Assert.NotNull(index.LookupByName(console, "Donkey Kong Country (USA)"));
        Assert.Equal(2, index.TotalEntries);
    }
}
