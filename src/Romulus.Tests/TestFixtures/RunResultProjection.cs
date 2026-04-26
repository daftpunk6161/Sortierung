using Romulus.Contracts.Models;

namespace Romulus.Tests.TestFixtures;

/// <summary>
/// Block D4 - centralized projection helpers for RunResult parity assertions
/// across CLI / WPF / API entry points. Replaces ad-hoc <c>ProjectFromCandidates</c>
/// / <c>RoutingTuple</c> closures previously duplicated in entry-point parity tests,
/// ReportParityTests etc.
///
/// Each helper returns an <see cref="IReadOnlyList{T}"/> of stable, ordinal-sorted
/// pipe-separated strings that can be compared with <see cref="System.Collections.Generic.IEnumerable{T}"/>
/// equality (xUnit Assert.Equal). All keys are lowered to avoid case-sensitive
/// false positives from format casing differences across entry points.
/// </summary>
internal static class RunResultProjection
{
    /// <summary>
    /// Per-group decision/field projection used for entry-point parity tests.
    /// Includes: GameKey, ConsoleKey, PlatformFamily, DecisionClass, SortDecision,
    /// ClassificationReasonCode, DAT-flag.
    /// </summary>
    public static IReadOnlyList<string> DecisionFields(IEnumerable<RomCandidate> winners)
    {
        ArgumentNullException.ThrowIfNull(winners);
        return [.. winners
            .Select(w => string.Join("|",
                (w.GameKey ?? string.Empty).ToLowerInvariant(),
                w.ConsoleKey ?? string.Empty,
                w.PlatformFamily,
                w.DecisionClass,
                w.SortDecision,
                w.ClassificationReasonCode ?? string.Empty,
                w.DatMatch ? "DAT" : "NODAT"))
            .OrderBy(s => s, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Per-group routing projection used for Unknown/Review/Blocked parity.
    /// Includes: GameKey, ConsoleKey, PlatformFamily, DecisionClass, SortDecision, Category.
    /// </summary>
    public static IReadOnlyList<string> RoutingTuples(IEnumerable<(string GameKey, RomCandidate Winner)> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return [.. groups
            .Select(g => string.Join("|",
                (g.GameKey ?? string.Empty).ToLowerInvariant(),
                g.Winner.ConsoleKey ?? string.Empty,
                g.Winner.PlatformFamily,
                g.Winner.DecisionClass,
                g.Winner.SortDecision,
                g.Winner.Category.ToString()))
            .OrderBy(s => s, StringComparer.Ordinal)];
    }
}
