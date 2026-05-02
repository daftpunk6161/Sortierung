using System;
using System.Collections.Generic;

namespace Romulus.Contracts.Models;

/// <summary>
/// Deterministic, side-effect-free trace explaining a single winner-selection
/// decision from <see cref="Romulus.Core.Deduplication.DeduplicationEngine"/>.
///
/// <para>
/// One trace per Winner / DedupeGroup. The trace surfaces the scoring axes that
/// drove the decision (Region, Format, Version, Header, Completeness, DAT match,
/// SizeTieBreak) plus the canonical tiebreaker order. It is the contract that
/// later powers the GUI/CLI/API "Decision Explainer" (T-W4-DECISION-EXPLAINER).
/// </para>
///
/// <para>
/// <strong>Privacy / safety note:</strong> the trace intentionally does
/// <em>not</em> expose full file system paths — only the winner file name —
/// so reports and audit exports stay free of root-leaking information beyond
/// what is already stored in <see cref="RunResult.DedupeGroups"/>.
/// </para>
///
/// <para>
/// Wave 2 task: T-W2-SCORING-REASON-TRACE (covers TOP-8). Vorbereitung fuer
/// T-W4-DECISION-EXPLAINER.
/// </para>
/// </summary>
public sealed record WinnerReasonTrace(
    string ConsoleKey,
    string GameKey,
    string WinnerFileName,
    string WinnerExtension,
    string WinnerRegion,
    int RegionScore,
    int FormatScore,
    long VersionScore,
    int HeaderScore,
    int CompletenessScore,
    bool DatMatch,
    MultiDatResolution? MultiDatResolution,
    long SizeTieBreakScore,
    string WinnerCategory,
    int LoserCount,
    string TiebreakerSummary)
{
    /// <summary>
    /// Canonical tiebreaker order used by
    /// <see cref="Romulus.Core.Deduplication.DeduplicationEngine.SelectWinner(IReadOnlyList{RomCandidate})"/>.
    /// Kept here so GUI/CLI/API/Reports share one fachliche Wahrheit instead of
    /// re-listing the order in every consumer.
    /// </summary>
    public static IReadOnlyList<string> TiebreakerOrder { get; } = new[]
    {
        "Category",
        "Completeness",
        "DatMatch",
        "Region",
        "Header",
        "Version",
        "Format",
        "SizeTieBreak",
        "Path(Ordinal)"
    };
}
