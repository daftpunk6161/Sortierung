using System.Reflection;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Pin tests for T-W5-MULTI-DAT-PREP.
/// Introduces a canonical <see cref="DatMatch"/> projection and a
/// <see cref="DatRepositoryAdapter.LookupByHash(DatIndex, string)"/> overload that
/// returns <see cref="IReadOnlyList{DatMatch}"/> in deterministic console-key order
/// plus a <see cref="DatMatchSelector"/> single-match helper for legacy callers.
/// Default behavior of existing DatIndex / DatRepositoryAdapter members is unchanged.
/// </summary>
public sealed class MultiDatPrepTests
{
    private static DatIndex BuildIndex()
    {
        var index = new DatIndex();
        index.Add("NES", "hash-shared", "NES Game");
        index.Add("SNES", "hash-shared", "SNES Game");
        index.Add("MD", "hash-md-only", "Mega Drive Game");
        return index;
    }

    [Fact]
    public void DatMatch_HasCanonicalProperties_DriftGuard()
    {
        var type = typeof(DatMatch);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        var expected = new[]
        {
            nameof(DatMatch.ConsoleKey),
            nameof(DatMatch.GameName),
            nameof(DatMatch.HashType),
            nameof(DatMatch.IsBios),
            nameof(DatMatch.ParentGameName),
            nameof(DatMatch.RomFileName),
            // Canonical extension (Multi-DAT provenance, optional).
            nameof(DatMatch.SourceId),
        }.OrderBy(n => n, StringComparer.Ordinal).ToArray();

        Assert.Equal(expected, props);
    }

    [Fact]
    public void LookupByHash_NoMatches_ReturnsEmptyList()
    {
        var adapter = new DatRepositoryAdapter();
        var index = BuildIndex();

        var matches = adapter.LookupByHash(index, "does-not-exist");

        Assert.NotNull(matches);
        Assert.Empty(matches);
    }

    [Fact]
    public void LookupByHash_SingleConsole_ReturnsExactlyOneMatch()
    {
        var adapter = new DatRepositoryAdapter();
        var index = BuildIndex();

        var matches = adapter.LookupByHash(index, "hash-md-only");

        var single = Assert.Single(matches);
        Assert.Equal("MD", single.ConsoleKey);
        Assert.Equal("Mega Drive Game", single.GameName);
        Assert.Equal("SHA1", single.HashType);
        Assert.False(single.IsBios);
        Assert.Null(single.ParentGameName);
    }

    [Fact]
    public void LookupByHash_MultipleConsoles_OrdersDeterministicallyByConsoleKey()
    {
        var adapter = new DatRepositoryAdapter();
        var index = BuildIndex();

        var matches = adapter.LookupByHash(index, "hash-shared");

        Assert.Equal(2, matches.Count);
        Assert.Equal("NES", matches[0].ConsoleKey);
        Assert.Equal("SNES", matches[1].ConsoleKey);
        Assert.Equal("NES Game", matches[0].GameName);
        Assert.Equal("SNES Game", matches[1].GameName);
    }

    [Fact]
    public void LookupByHash_TypedHash_FiltersByHashType()
    {
        var adapter = new DatRepositoryAdapter();
        var index = new DatIndex();
        index.Add("NES", "shared-hex", "Sha1 Game", hashType: "SHA1");
        index.Add("NES", "shared-hex", "Md5 Game", hashType: "MD5");

        var sha1 = adapter.LookupByHash(index, "SHA1", "shared-hex");
        var md5 = adapter.LookupByHash(index, "MD5", "shared-hex");

        Assert.Single(sha1);
        Assert.Equal("Sha1 Game", sha1[0].GameName);
        Assert.Equal("SHA1", sha1[0].HashType);

        Assert.Single(md5);
        Assert.Equal("Md5 Game", md5[0].GameName);
        Assert.Equal("MD5", md5[0].HashType);
    }

    [Fact]
    public void LookupByHash_NullOrWhitespaceHash_ReturnsEmptyList()
    {
        var adapter = new DatRepositoryAdapter();
        var index = BuildIndex();

        Assert.Empty(adapter.LookupByHash(index, ""));
        Assert.Empty(adapter.LookupByHash(index, "   "));
        Assert.Empty(adapter.LookupByHash(index, null!));
    }

    [Fact]
    public void LookupByHash_NullIndex_ReturnsEmptyList()
    {
        var adapter = new DatRepositoryAdapter();

        var matches = adapter.LookupByHash(null!, "hash-shared");

        Assert.NotNull(matches);
        Assert.Empty(matches);
    }

    [Fact]
    public void DatMatch_PropagatesIsBiosAndParent()
    {
        var adapter = new DatRepositoryAdapter();
        var index = new DatIndex();
        index.Add(
            consoleKey: "NES",
            hash: "bios-hash",
            gameName: "Some BIOS",
            romFileName: "bios.bin",
            isBios: true,
            parentGameName: "Parent Game",
            hashType: "SHA1");

        var matches = adapter.LookupByHash(index, "bios-hash");

        var match = Assert.Single(matches);
        Assert.True(match.IsBios);
        Assert.Equal("Parent Game", match.ParentGameName);
        Assert.Equal("bios.bin", match.RomFileName);
    }

    // ---- DatMatchSelector single-match helper ----

    [Fact]
    public void Selector_SelectSingle_EmptyList_ReturnsNull()
    {
        Assert.Null(DatMatchSelector.SelectSingle(Array.Empty<DatMatch>()));
        Assert.Null(DatMatchSelector.SelectSingle(null!));
    }

    [Fact]
    public void Selector_SelectSingle_OneMatch_ReturnsThatMatch()
    {
        var only = new DatMatch("NES", "Game", null, false, null, "SHA1");

        var picked = DatMatchSelector.SelectSingle(new[] { only });

        Assert.Same(only, picked);
    }

    [Fact]
    public void Selector_SelectSingle_MultipleMatches_PicksDeterministicFirst()
    {
        var adapter = new DatRepositoryAdapter();
        var index = BuildIndex();
        var matches = adapter.LookupByHash(index, "hash-shared");

        var picked = DatMatchSelector.SelectSingle(matches);

        Assert.NotNull(picked);
        Assert.Equal("NES", picked!.ConsoleKey);
    }

    [Fact]
    public void Selector_SelectSingle_StableAcrossCalls()
    {
        var adapter = new DatRepositoryAdapter();
        var index = BuildIndex();

        var first = DatMatchSelector.SelectSingle(adapter.LookupByHash(index, "hash-shared"));
        var second = DatMatchSelector.SelectSingle(adapter.LookupByHash(index, "hash-shared"));
        var third = DatMatchSelector.SelectSingle(adapter.LookupByHash(index, "hash-shared"));

        Assert.NotNull(first);
        Assert.Equal(first!.ConsoleKey, second!.ConsoleKey);
        Assert.Equal(first.ConsoleKey, third!.ConsoleKey);
    }

    [Fact]
    public void LookupByHash_DefaultBehavior_DatIndexLookupAnyStillUnchanged()
    {
        // Regression guard: the new multi-match API must not change the existing
        // single-match LookupAny behavior on DatIndex.
        var index = BuildIndex();
        var any = index.LookupAny("hash-shared");

        Assert.NotNull(any);
        Assert.Equal("NES", any.Value.ConsoleKey);
        Assert.Equal("NES Game", any.Value.GameName);
    }
}
