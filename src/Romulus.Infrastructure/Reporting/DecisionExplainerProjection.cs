using System;
using System.Collections.Generic;
using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Reporting;

/// <summary>
/// Wave 4 — T-W4-DECISION-EXPLAINER.
/// Pure projection from <see cref="RunResult"/> / <see cref="WinnerReasonTrace"/>
/// to the UI/CLI/API-shared <see cref="DecisionExplanation"/> model.
///
/// <para>
/// <strong>Single Source of Truth:</strong> the GUI Decision Drawer, the CLI
/// <c>romulus explain</c> subcommand and the API
/// <c>GET /runs/{id}/decisions[/{key}]</c> all call into this projection.
/// They MUST NOT compute the explanation themselves — the failure_mode
/// "GUI hat eigene Berechnung" is blocked by routing every consumer
/// through <see cref="Project(RunResult)"/> / <see cref="Project(WinnerReasonTrace)"/>.
/// </para>
///
/// <para>
/// Deterministic — same input produces the same explanation list in the
/// same order (matches <see cref="RunResult.WinnerReasons"/>).
/// </para>
/// </summary>
public static class DecisionExplainerProjection
{
    public static IReadOnlyList<DecisionExplanation> Project(RunResult result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var traces = result.WinnerReasons;
        var output = new List<DecisionExplanation>(traces.Count);
        foreach (var trace in traces)
            output.Add(Project(trace));
        return output;
    }

    public static DecisionExplanation Project(WinnerReasonTrace trace)
    {
        ArgumentNullException.ThrowIfNull(trace);

        var scores = new DecisionScoreContribution[]
        {
            new("Category", trace.WinnerCategory == nameof(FileCategory.Game) ? 1 : 0),
            new("Completeness", trace.CompletenessScore),
            new("DatMatch", trace.DatMatch ? 1 : 0),
            new("Region", trace.RegionScore),
            new("Header", trace.HeaderScore),
            new("Version", trace.VersionScore),
            new("Format", trace.FormatScore),
            new("SizeTieBreak", trace.SizeTieBreakScore),
        };

        return new DecisionExplanation(
            ConsoleKey: trace.ConsoleKey,
            GameKey: trace.GameKey,
            WinnerFileName: trace.WinnerFileName,
            WinnerExtension: trace.WinnerExtension,
            WinnerCategory: trace.WinnerCategory,
            WinnerRegion: trace.WinnerRegion,
            DatMatch: trace.DatMatch,
            MultiDatResolution: trace.MultiDatResolution,
            LoserCount: trace.LoserCount,
            Scores: scores,
            TiebreakerOrder: WinnerReasonTrace.TiebreakerOrder,
            Summary: trace.TiebreakerSummary);
    }

    /// <summary>
    /// Locates one explanation by its (consoleKey, gameKey) pair. Both
    /// comparisons are case-insensitive (Ordinal). Returns <c>null</c>
    /// when no match is found — callers MUST surface a 404-equivalent.
    /// </summary>
    public static DecisionExplanation? Find(
        IReadOnlyList<DecisionExplanation> explanations,
        string consoleKey,
        string gameKey)
    {
        ArgumentNullException.ThrowIfNull(explanations);
        if (string.IsNullOrWhiteSpace(consoleKey) || string.IsNullOrWhiteSpace(gameKey))
            return null;

        foreach (var ex in explanations)
        {
            if (string.Equals(ex.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)
                && string.Equals(ex.GameKey, gameKey, StringComparison.OrdinalIgnoreCase))
            {
                return ex;
            }
        }
        return null;
    }
}
