using System.Collections.Generic;
using System.Linq;

namespace Romulus.Contracts.Models;

/// <summary>
/// Wave 4 — T-W4-DECISION-EXPLAINER. UI-friendly projection of a single
/// <see cref="WinnerReasonTrace"/>. Single source of truth between
/// GUI (Decision Drawer), CLI (<c>romulus explain</c>) and API
/// (<c>GET /runs/{id}/decisions[/{key}]</c>): all three render the same
/// projection so the Begruendung kann nicht divergieren.
///
/// <para>
/// <strong>Privacy contract:</strong> contains only the winner file name,
/// not the full path. Inherits the same invariant from
/// <see cref="WinnerReasonTrace"/>.
/// </para>
///
/// <para>
/// Equality is structural and uses <see cref="Enumerable.SequenceEqual{TSource}(IEnumerable{TSource}, IEnumerable{TSource})"/>
/// for <see cref="Scores"/> and <see cref="TiebreakerOrder"/> so that
/// determinism asserts (same input -&gt; same output) hold across two
/// independent <c>Project()</c> calls.
/// </para>
/// </summary>
public sealed record DecisionExplanation(
    string ConsoleKey,
    string GameKey,
    string WinnerFileName,
    string WinnerExtension,
    string WinnerCategory,
    string WinnerRegion,
    bool DatMatch,
    int LoserCount,
    IReadOnlyList<DecisionScoreContribution> Scores,
    IReadOnlyList<string> TiebreakerOrder,
    string Summary)
{
    public bool Equals(DecisionExplanation? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return ConsoleKey == other.ConsoleKey
            && GameKey == other.GameKey
            && WinnerFileName == other.WinnerFileName
            && WinnerExtension == other.WinnerExtension
            && WinnerCategory == other.WinnerCategory
            && WinnerRegion == other.WinnerRegion
            && DatMatch == other.DatMatch
            && LoserCount == other.LoserCount
            && Summary == other.Summary
            && Scores.SequenceEqual(other.Scores)
            && TiebreakerOrder.SequenceEqual(other.TiebreakerOrder, System.StringComparer.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new System.HashCode();
        hash.Add(ConsoleKey);
        hash.Add(GameKey);
        hash.Add(WinnerFileName);
        hash.Add(WinnerExtension);
        hash.Add(WinnerCategory);
        hash.Add(WinnerRegion);
        hash.Add(DatMatch);
        hash.Add(LoserCount);
        hash.Add(Summary);
        foreach (var s in Scores) hash.Add(s);
        foreach (var t in TiebreakerOrder) hash.Add(t);
        return hash.ToHashCode();
    }
}

/// <summary>
/// One scoring axis contribution. <see cref="Axis"/> matches an entry from
/// <see cref="WinnerReasonTrace.TiebreakerOrder"/>; <see cref="Value"/> is
/// the raw numeric contribution (long for size-tiebreak headroom).
/// </summary>
public sealed record DecisionScoreContribution(string Axis, long Value);
