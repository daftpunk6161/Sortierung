using System;
using System.IO;
using Romulus.Contracts.Models;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.Reporting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 4 — T-W4-DECISION-EXPLAINER pin tests.
/// Acceptance gates from plan.yaml:
///   * DecisionExplainerProjection liest WinnerReasons und produziert ein UI-Modell.
///   * Eine Wahrheit, drei Sichten — GUI/CLI/API geben identische Begruendung.
///   * Beta-Nutzer kann erklaeren, warum ein Spiel gewonnen hat.
///   * Failure-Mode "GUI hat eigene Berechnung" durch Projection-SoT verhindert.
/// </summary>
public sealed class Wave4DecisionExplainerTests
{
    private static RomCandidate Cand(
        string path, string console, string game, string region,
        int regionScore, int formatScore, long versionScore = 0,
        int header = 0, int complete = 100, bool dat = false,
        long sizeTie = 0, FileCategory cat = FileCategory.Game,
        string ext = ".rom") => new()
        {
            MainPath = path, ConsoleKey = console, GameKey = game, Region = region,
            RegionScore = regionScore, FormatScore = formatScore,
            VersionScore = versionScore, HeaderScore = header,
            CompletenessScore = complete, DatMatch = dat,
            SizeTieBreakScore = sizeTie, Category = cat, Extension = ext,
        };

    private static RunResult BuildRunResult()
    {
        var groups = new[]
        {
            new DedupeGroup
            {
                Winner = Cand("C:\\roms\\smb (USA).nes", "NES", "smb", "USA", 100, 80, 5, 2, 100, true, 1234),
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
        return new RunResult
        {
            DedupeGroups = groups,
            WinnerReasons = DeduplicationEngine.BuildWinnerReasons(groups),
        };
    }

    [Fact]
    public void Project_ProducesOneExplanationPerWinnerReason()
    {
        var rr = BuildRunResult();
        var explanations = DecisionExplainerProjection.Project(rr);
        Assert.Equal(rr.WinnerReasons.Count, explanations.Count);
        Assert.Equal("smb", explanations[0].GameKey);
        Assert.Equal("zelda", explanations[1].GameKey);
    }

    [Fact]
    public void Project_IsDeterministic_SameInputSameOutput()
    {
        var rr = BuildRunResult();
        var a = DecisionExplainerProjection.Project(rr);
        var b = DecisionExplainerProjection.Project(rr);
        Assert.Equal(a.Count, b.Count);
        for (var i = 0; i < a.Count; i++)
            Assert.Equal(a[i], b[i]);
    }

    [Fact]
    public void Project_NeverLeaksAbsolutePaths()
    {
        var rr = BuildRunResult();
        foreach (var ex in DecisionExplainerProjection.Project(rr))
        {
            Assert.DoesNotContain("\\", ex.WinnerFileName, StringComparison.Ordinal);
            Assert.DoesNotContain("/", ex.WinnerFileName, StringComparison.Ordinal);
            Assert.DoesNotContain(":\\", ex.Summary, StringComparison.Ordinal);
            Assert.DoesNotContain("Users", ex.WinnerFileName, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Find_LocatesByConsoleAndGameKey()
    {
        var rr = BuildRunResult();
        var explanations = DecisionExplainerProjection.Project(rr);
        var hit = DecisionExplainerProjection.Find(explanations, "NES", "smb");
        Assert.NotNull(hit);
        Assert.Equal("smb", hit!.GameKey);

        var miss = DecisionExplainerProjection.Find(explanations, "NES", "doesnotexist");
        Assert.Null(miss);
    }

    [Fact]
    public void Project_CapturesAllScoringAxes_WithCanonicalOrder()
    {
        var rr = BuildRunResult();
        var ex = DecisionExplainerProjection.Project(rr)[0];
        Assert.NotEmpty(ex.Scores);
        // SoT for tiebreaker order lives in WinnerReasonTrace.TiebreakerOrder.
        Assert.Equal(WinnerReasonTrace.TiebreakerOrder, ex.TiebreakerOrder);
    }

    [Fact]
    public void CliExplain_IsWiredAsSubcommand()
    {
        // Source-inspection pin: ensures the `explain` CLI subcommand is wired
        // to SubcommandExplainAsync and consumes DecisionExplainerProjection.
        // Functional behaviour is covered by the projection tests above; spinning
        // the entire RunOrchestrator just to print empty JSON would slow the suite
        // by minutes without adding fachliche Sicherheit (the projection IS the SoT).
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var parser = File.ReadAllText(Path.Combine(
            dir!.FullName, "src", "Romulus.CLI", "CliArgsParser.Subcommands.cs"));
        Assert.Contains("\"explain\"", parser, StringComparison.Ordinal);
        Assert.Contains("ParseExplainSubcommand", parser, StringComparison.Ordinal);

        var program = File.ReadAllText(Path.Combine(
            dir.FullName, "src", "Romulus.CLI", "Program.cs"));
        Assert.Contains("CliCommand.Explain", program, StringComparison.Ordinal);
        Assert.Contains("SubcommandExplainAsync", program, StringComparison.Ordinal);

        var handler = File.ReadAllText(Path.Combine(
            dir.FullName, "src", "Romulus.CLI", "Program.Subcommands.AnalysisAndDat.cs"));
        Assert.Contains("SubcommandExplainAsync", handler, StringComparison.Ordinal);
        Assert.Contains("DecisionExplainerProjection.Project", handler, StringComparison.Ordinal);
    }

    [Fact]
    public void Api_RegistersDecisionsEndpoint()
    {
        // Source-inspection pin: ensures /runs/{runId}/decisions endpoint
        // is wired up. Functional behaviour is covered by the projection tests
        // (single source of truth — the API merely projects RunResult).
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var src = File.ReadAllText(Path.Combine(
            dir!.FullName,
            "src", "Romulus.Api", "Program.DecisionEndpoints.cs"));
        Assert.Contains("/runs/{runId}/decisions", src, StringComparison.Ordinal);
        Assert.Contains("DecisionExplainerProjection.Project", src, StringComparison.Ordinal);
    }
}
