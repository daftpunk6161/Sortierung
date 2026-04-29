using System;
using System.Collections.Generic;
using System.Linq;
using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 2 — T-W2-SCORING-REASON-TRACE pin tests.
/// Acceptance gates from plan.yaml:
///   * RunResult.WinnerReasons existiert und liefert genau einen Eintrag pro Winner.
///   * Trace-Felder sind deterministisch (gleiche Inputs -> gleiche Outputs, gleiche Reihenfolge).
///   * Trace verraet KEINE vollstaendigen Pfade (nur Dateiname) — Failure-Mode-Schutz.
///   * Tiebreaker-Reihenfolge ist als Single-Source-of-Truth dokumentiert.
/// </summary>
public sealed class Wave2ScoringReasonTraceTests
{
    private static RomCandidate Cand(
        string path,
        string console,
        string gameKey,
        string region,
        int regionScore,
        int formatScore,
        long versionScore = 0,
        int header = 0,
        int complete = 100,
        bool dat = false,
        long sizeTie = 0,
        FileCategory cat = FileCategory.Game,
        string ext = ".rom") => new()
    {
        MainPath = path,
        ConsoleKey = console,
        GameKey = gameKey,
        Region = region,
        RegionScore = regionScore,
        FormatScore = formatScore,
        VersionScore = versionScore,
        HeaderScore = header,
        CompletenessScore = complete,
        DatMatch = dat,
        SizeTieBreakScore = sizeTie,
        Category = cat,
        Extension = ext,
    };

    [Fact]
    public void RunResult_WinnerReasons_DefaultsToEmpty()
    {
        var rr = new RunResult();
        Assert.NotNull(rr.WinnerReasons);
        Assert.Empty(rr.WinnerReasons);
    }

    [Fact]
    public void BuildWinnerReasons_OneTracePerGroup()
    {
        var groups = new[]
        {
            new DedupeGroup
            {
                Winner = Cand("C:\\roms\\smb (USA).nes", "NES", "smb", "USA", 100, 80),
                Losers = new[] { Cand("C:\\roms\\smb (PAL).nes", "NES", "smb", "PAL", 50, 80) },
                GameKey = "smb",
            },
            new DedupeGroup
            {
                Winner = Cand("C:\\roms\\zelda (JPN).nes", "NES", "zelda", "JPN", 60, 80),
                Losers = Array.Empty<RomCandidate>(),
                GameKey = "zelda",
            },
        };

        var traces = DeduplicationEngine.BuildWinnerReasons(groups);

        Assert.Equal(2, traces.Count);
        Assert.Equal("smb", traces[0].GameKey);
        Assert.Equal("zelda", traces[1].GameKey);
        Assert.Equal(1, traces[0].LoserCount);
        Assert.Equal(0, traces[1].LoserCount);
    }

    [Fact]
    public void BuildWinnerReasons_IsDeterministic()
    {
        var groups = new[]
        {
            new DedupeGroup
            {
                Winner = Cand("C:\\roms\\a.nes", "NES", "a", "USA", 100, 80, 5, 2, 100, true, 1234),
                Losers = Array.Empty<RomCandidate>(),
                GameKey = "a",
            },
        };

        var t1 = DeduplicationEngine.BuildWinnerReasons(groups);
        var t2 = DeduplicationEngine.BuildWinnerReasons(groups);

        Assert.Equal(t1.Count, t2.Count);
        for (var i = 0; i < t1.Count; i++)
        {
            Assert.Equal(t1[i], t2[i]);
        }
    }

    [Fact]
    public void BuildWinnerReason_DoesNotLeakFullPath()
    {
        var group = new DedupeGroup
        {
            Winner = Cand("C:\\Users\\alice\\very\\deep\\private\\roms\\smb.nes", "NES", "smb", "USA", 100, 80),
            Losers = Array.Empty<RomCandidate>(),
            GameKey = "smb",
        };

        var trace = DeduplicationEngine.BuildWinnerReason(group);

        Assert.Equal("smb.nes", trace.WinnerFileName);
        // Single source of truth: trace MUST NOT contain the full path or any directory segment.
        Assert.DoesNotContain("Users", trace.WinnerFileName, StringComparison.Ordinal);
        Assert.DoesNotContain("alice", trace.WinnerFileName, StringComparison.Ordinal);
        Assert.DoesNotContain("\\", trace.WinnerFileName, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildWinnerReason_CapturesAllScoringAxes()
    {
        var group = new DedupeGroup
        {
            Winner = Cand("a.nes", "NES", "a", "USA", 100, 80, 7L, 3, 95, true, 1234L, FileCategory.Game, ".nes"),
            Losers = new[] { Cand("b.nes", "NES", "a", "PAL", 50, 80) },
            GameKey = "a",
        };

        var t = DeduplicationEngine.BuildWinnerReason(group);

        Assert.Equal("NES", t.ConsoleKey);
        Assert.Equal("a", t.GameKey);
        Assert.Equal("USA", t.WinnerRegion);
        Assert.Equal(100, t.RegionScore);
        Assert.Equal(80, t.FormatScore);
        Assert.Equal(7L, t.VersionScore);
        Assert.Equal(3, t.HeaderScore);
        Assert.Equal(95, t.CompletenessScore);
        Assert.True(t.DatMatch);
        Assert.Equal(1234L, t.SizeTieBreakScore);
        Assert.Equal("Game", t.WinnerCategory);
        Assert.Equal(".nes", t.WinnerExtension);
        Assert.Equal(1, t.LoserCount);
        Assert.False(string.IsNullOrWhiteSpace(t.TiebreakerSummary));
        Assert.Contains("Compl=95", t.TiebreakerSummary, StringComparison.Ordinal);
        Assert.Contains("Reg=100", t.TiebreakerSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void TiebreakerOrder_IsSingleSourceOfTruth()
    {
        var order = WinnerReasonTrace.TiebreakerOrder;
        // Reihenfolge muss exakt der DeduplicationEngine.SelectWinner-Reihenfolge entsprechen.
        var expected = new[]
        {
            "Category", "Completeness", "DatMatch", "Region",
            "Header", "Version", "Format", "SizeTieBreak", "Path(Ordinal)"
        };
        Assert.Equal(expected, order.ToArray());
    }

    [Fact]
    public void BuildWinnerReasons_EmptyInput_ReturnsEmpty()
    {
        var traces = DeduplicationEngine.BuildWinnerReasons(Array.Empty<DedupeGroup>());
        Assert.NotNull(traces);
        Assert.Empty(traces);
    }
}
