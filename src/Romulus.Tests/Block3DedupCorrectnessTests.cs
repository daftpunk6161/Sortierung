using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.Deduplication;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Block 3 (Dedup correctness) regression tests for findings F3, F4, F5, F11, F12.
/// Each test codifies a single concrete invariant; no no-crash assertions, no tautologies.
/// </summary>
public sealed class Block3DedupCorrectnessTests
{
    // ─────────────────────────────────────────────────────────────────────────
    // F3: CrossRootDeduplicator MUST normalize game key without extension.
    //     Two duplicates that differ only in container extension (.nes vs .zip)
    //     must produce a deterministic Keep selection — i.e., the GameKey used
    //     internally is independent of the extension. We assert this via a
    //     stable Keep result that does not flip when only extensions change.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void F3_CrossRootDeduplicator_GameKey_IsExtensionInsensitive()
    {
        var fileA = new CrossRootFile
        {
            Path = @"C:\root1\Super Mario Bros.nes",
            Root = @"C:\root1",
            Hash = "h-shared",
            Extension = ".nes",
            SizeBytes = 1024,
            Region = "US",
            FormatScore = 100,
        };
        var fileB = new CrossRootFile
        {
            Path = @"C:\root2\Super Mario Bros.zip",
            Root = @"C:\root2",
            Hash = "h-shared",
            Extension = ".zip",
            SizeBytes = 1024,
            Region = "US",
            FormatScore = 100,
        };

        var group = new CrossRootDuplicateGroup { Hash = "h-shared", Files = new List<CrossRootFile> { fileA, fileB } };

        var advice = CrossRootDeduplicator.GetMergeAdvice(group);

        // Invariant after F3 fix: both candidates share the same internal GameKey
        // (because extension is stripped before normalization), so SelectWinner
        // operates on a single coherent group. With identical scores the
        // alphabetical tiebreaker on path applies → root1 wins deterministically.
        Assert.NotNull(advice.Keep);
        Assert.Single(advice.Remove);
        Assert.Equal(fileA.Path, advice.Keep.Path);
        Assert.Equal(fileB.Path, advice.Remove[0].Path);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // F4: CrossRootDeduplicator MUST honour caller-supplied categoryRanks
    //     (single source of truth with the main pipeline). Today it ignores
    //     them and uses an internal fallback table.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void F4_CrossRootDeduplicator_RespectsCustomCategoryRanks()
    {
        var bios = new CrossRootFile
        {
            Path = @"C:\r1\bios.bin",
            Root = @"C:\r1",
            Hash = "h",
            Extension = ".bin",
            SizeBytes = 1024,
            Region = "US",
            Category = FileCategory.Bios,
            CompletenessScore = 10,
        };
        var game = new CrossRootFile
        {
            Path = @"C:\r2\bios.bin",
            Root = @"C:\r2",
            Hash = "h",
            Extension = ".bin",
            SizeBytes = 1024,
            Region = "US",
            Category = FileCategory.Game,
            CompletenessScore = 100,
        };

        // Inverted ranks: BIOS > Game. After F4 fix, CrossRoot must respect this.
        var customRanks = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [nameof(FileCategory.Bios)] = 99,
            [nameof(FileCategory.Game)] = 1,
            [nameof(FileCategory.NonGame)] = 1,
            [nameof(FileCategory.Junk)] = 1,
            [nameof(FileCategory.Unknown)] = 1,
        };

        var group = new CrossRootDuplicateGroup { Hash = "h", Files = new List<CrossRootFile> { bios, game } };
        var advice = CrossRootDeduplicator.GetMergeAdvice(group, preferRegions: null, categoryRanks: customRanks);

        Assert.Equal(bios.Path, advice.Keep.Path);
        Assert.Single(advice.Remove);
        Assert.Equal(game.Path, advice.Remove[0].Path);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // F5: CrossRootDeduplicator must NOT silently overwrite preset zero scores
    //     with recomputed defaults. When the caller signals scores are
    //     authoritative (recomputePresetScores: false), a preset zero stays zero.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void F5_CrossRootDeduplicator_PresetZeroScores_AreNotOverwritten()
    {
        // Two files, same hash, identical except RegionScore.
        // File A has preset RegionScore=0 (intentional, because preferRegions did not include US).
        // File B has preset RegionScore=50.
        // Today: CrossRoot recomputes A's RegionScore via its own preferRegions and may flip the winner.
        // After F5 fix with recomputePresetScores=false: B wins (because its preset score is higher).
        var fileA = new CrossRootFile
        {
            Path = @"C:\r1\game.iso",
            Root = @"C:\r1",
            Hash = "h",
            Extension = ".iso",
            SizeBytes = 1024,
            Region = "US",
            RegionScore = 0,
            FormatScore = 100,
            CompletenessScore = 10,
        };
        var fileB = new CrossRootFile
        {
            Path = @"C:\r2\game.iso",
            Root = @"C:\r2",
            Hash = "h",
            Extension = ".iso",
            SizeBytes = 1024,
            Region = "US",
            RegionScore = 50,
            FormatScore = 100,
            CompletenessScore = 10,
        };

        var group = new CrossRootDuplicateGroup { Hash = "h", Files = new List<CrossRootFile> { fileA, fileB } };

        var advice = CrossRootDeduplicator.GetMergeAdvice(
            group,
            preferRegions: null,
            categoryRanks: null,
            recomputePresetScores: false);

        Assert.Equal(fileB.Path, advice.Keep.Path);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // F11: Two BIOS files with identical normalized name and UNKNOWN region but
    //      DIFFERENT content (different hashes) MUST NOT collapse into one
    //      dedupe group. Today, CandidateFactory generates the same key
    //      "__BIOS__UNKNOWN__<gameKey>" for both → false collision.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void F11_DeduplicationEngine_TwoBiosUnknownRegion_DistinctHashes_NotGrouped()
    {
        var biosA = CandidateFactory.Create(
            normalizedPath: @"C:\bios\kernel.bin",
            extension: ".bin", sizeBytes: 1024,
            category: FileCategory.Bios, gameKey: "kernel",
            region: "UNKNOWN", regionScore: 0, formatScore: 0,
            versionScore: 0, headerScore: 0, completenessScore: 0,
            sizeTieBreakScore: 1024, datMatch: false, consoleKey: "PSX",
            hash: "hash-aaa");
        var biosB = CandidateFactory.Create(
            normalizedPath: @"C:\bios\kernel.bin",
            extension: ".bin", sizeBytes: 2048,
            category: FileCategory.Bios, gameKey: "kernel",
            region: "UNKNOWN", regionScore: 0, formatScore: 0,
            versionScore: 0, headerScore: 0, completenessScore: 0,
            sizeTieBreakScore: 2048, datMatch: false, consoleKey: "SAT",
            hash: "hash-bbb");

        // Different paths to avoid trivial collapse on MainPath
        biosA = biosA with { MainPath = @"C:\psx\kernel.bin" };
        biosB = biosB with { MainPath = @"C:\sat\kernel.bin" };

        var groups = DeduplicationEngine.Deduplicate(new[] { biosA, biosB });

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Empty(g.Losers));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // F12: Two UNKNOWN-console candidates with identical GameKey but DIFFERENT
    //      content hashes MUST NOT be grouped. Today BuildGroupKey collapses
    //      everything UNKNOWN-console with the same name → mass-group with
    //      false losers.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void F12_DeduplicationEngine_TwoUnknownConsole_SameGameKey_DistinctHashes_NotGrouped()
    {
        var a = new RomCandidate
        {
            MainPath = @"C:\dump\tetris.bin",
            GameKey = "tetris",
            ConsoleKey = "",          // UNKNOWN
            Category = FileCategory.Game,
            Hash = "hash-aaa",
            SizeBytes = 1024,
        };
        var b = new RomCandidate
        {
            MainPath = @"C:\other\tetris.bin",
            GameKey = "tetris",
            ConsoleKey = "",          // UNKNOWN
            Category = FileCategory.Game,
            Hash = "hash-bbb",
            SizeBytes = 1024,
        };

        var groups = DeduplicationEngine.Deduplicate(new[] { a, b });

        Assert.Equal(2, groups.Count);
        Assert.All(groups, g => Assert.Empty(g.Losers));
    }

    // ─────────────────────────────────────────────────────────────────────────
    // F12 sibling invariant: Two UNKNOWN-console candidates with identical
    // GameKey AND identical hash (same actual content found in two roots) MUST
    // still group together — the fix must not over-segregate.
    // ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void F12_DeduplicationEngine_TwoUnknownConsole_SameHash_StillGroup()
    {
        var a = new RomCandidate
        {
            MainPath = @"C:\rootA\tetris.bin",
            GameKey = "tetris",
            ConsoleKey = "",
            Category = FileCategory.Game,
            Hash = "hash-shared",
            SizeBytes = 1024,
            CompletenessScore = 50,
        };
        var b = new RomCandidate
        {
            MainPath = @"C:\rootB\tetris.bin",
            GameKey = "tetris",
            ConsoleKey = "",
            Category = FileCategory.Game,
            Hash = "hash-shared",
            SizeBytes = 1024,
            CompletenessScore = 10,
        };

        var groups = DeduplicationEngine.Deduplicate(new[] { a, b });

        Assert.Single(groups);
        Assert.Equal(a.MainPath, groups[0].Winner.MainPath);
        Assert.Single(groups[0].Losers);
    }
}
