using Romulus.Contracts.Models;

namespace Romulus.Core.Classification;

/// <summary>
/// Resolves multiple detection hypotheses into a single deterministic result.
/// Uses weighted confidence scoring, evidence classification, and deterministic tie-breaking.
/// Produces a SortDecision that gates whether automatic sorting is safe.
/// </summary>
public static class HypothesisResolver
{
    /// <summary>Baseline soft-only cap for weak single-source signals.</summary>
    internal const int SoftOnlyCap = 75;

    /// <summary>Minimum confidence for Sort decision.</summary>
    internal const int SortThreshold = 85;

    /// <summary>Minimum confidence for Review decision (below Sort threshold).</summary>
    internal const int ReviewThreshold = 55;

    /// <summary>Agreement bonus when multiple sources point to the same console.</summary>
    internal const int MultiSourceAgreementBonus = 15;

    /// <summary>
    /// Resolve into the new DAT-first recognition projection while keeping
    /// ConsoleDetectionResult as compatibility output.
    /// </summary>
    public static RecognitionResult ResolveRecognition(IReadOnlyList<DetectionHypothesis> hypotheses)
    {
        var resolved = Resolve(hypotheses);
        var evidence = resolved.MatchEvidence ?? new MatchEvidence();

        var signals = resolved.Hypotheses
            .Select(h =>
            {
                var kind = MapDetectionSourceToMatchKind(h.Source);
                return new RecognitionSignal(
                    h.ConsoleKey,
                    kind.GetTier(),
                    kind,
                    h.Confidence,
                    h.Evidence);
            })
            .ToArray();

        return new RecognitionResult
        {
            ConsoleKey = resolved.ConsoleKey,
            Tier = evidence.Tier,
            PrimaryMatchKind = evidence.PrimaryMatchKind,
            Decision = resolved.DecisionClass,
            Confidence = resolved.Confidence,
            DatVerified = resolved.SortDecision == SortDecision.DatVerified,
            Signals = signals,
            Reasoning = evidence.Reasoning,
            HasConflict = resolved.HasConflict,
            ConflictType = resolved.ConflictType,
        };
    }

    /// <summary>
    /// Resolve a set of hypotheses into a single console detection result.
    /// Rules:
    /// 1. Group by ConsoleKey, sum confidence per key.
    /// 2. Highest total confidence wins.
    /// 3. On tie: alphabetical ConsoleKey (deterministic).
    /// 4. If multiple distinct ConsoleKeys have hypotheses, mark as conflict.
    /// 5. Soft-only detections are capped at 65 (never auto-sortable).
    /// 6. Single-source detections are capped per source type.
    /// 7. AMBIGUOUS is returned when two strong conflicting consoles are equally plausible.
    /// 8. SortDecision is derived from confidence, conflict, and evidence type.
    /// 9. Cross-family conflicts escalate to Blocked; intra-family conflicts without structural evidence → Review.
    /// </summary>
    public static ConsoleDetectionResult Resolve(
        IReadOnlyList<DetectionHypothesis> hypotheses,
        Func<string, PlatformFamily>? familyLookup = null)
    {
        if (hypotheses.Count == 0)
            return ConsoleDetectionResult.Unknown;

        // Group by ConsoleKey and sum confidence
        var groups = new Dictionary<string, (int TotalConfidence, int MaxSingleConfidence, List<DetectionHypothesis> Items, int MaxSourcePriority)>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var h in hypotheses)
        {
            if (!groups.TryGetValue(h.ConsoleKey, out var group))
            {
                group = (0, 0, new List<DetectionHypothesis>(), 0);
                groups[h.ConsoleKey] = group;
            }

            group.Items.Add(h);
            group.TotalConfidence += h.Confidence;
            if (h.Confidence > group.MaxSingleConfidence)
                group.MaxSingleConfidence = h.Confidence;

            var sourcePriority = GetWinnerPriority(h.Source);
            if (sourcePriority > group.MaxSourcePriority)
                group.MaxSourcePriority = sourcePriority;
            groups[h.ConsoleKey] = group;
        }

        // Sort by winner source reliability first, then by confidence for deterministic tie-breaking.
        var sorted = groups
            .OrderByDescending(g => g.Value.MaxSourcePriority)
            .ThenByDescending(g => g.Value.TotalConfidence)
            .ThenByDescending(g => g.Value.MaxSingleConfidence)
            .ThenBy(g => g.Key, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var winner = sorted[0];
        bool hasConflict = sorted.Count > 1;
        string? conflictDetail = null;

        if (hasConflict)
        {
            var runner = sorted[1];
            conflictDetail = $"Conflict: {winner.Key}({winner.Value.TotalConfidence}) vs {runner.Key}({runner.Value.TotalConfidence})";
        }

        // Check for AMBIGUOUS: two strong competing consoles both ≥ 60.
        // Only trigger when evidence quality is genuinely comparable; a clearly
        // stronger source class should degrade to Review, not erase the winner.
        if (hasConflict && sorted.Count >= 2)
        {
            var runnerMax = sorted[1].Value.MaxSingleConfidence;
            if (winner.Value.MaxSingleConfidence >= 60 && runnerMax >= 60)
            {
                var winnerHasHard = winner.Value.Items.Any(h => h.Source.IsHardEvidence());
                var runnerHasHard = sorted[1].Value.Items.Any(h => h.Source.IsHardEvidence());
                var winnerPriority = winner.Value.MaxSourcePriority;
                var runnerPriority = sorted[1].Value.MaxSourcePriority;

                // Only AMBIGUOUS when evidence quality is comparable:
                // both have hard evidence, or neither has hard evidence
                // and the strongest source tier is the same on both sides.
                if (winnerHasHard == runnerHasHard && winnerPriority == runnerPriority)
                {
                    var ratio = (double)sorted[1].Value.TotalConfidence / winner.Value.TotalConfidence;
                    if (ratio >= 0.7)
                    {
                        var ambiguousEvidence = new MatchEvidence
                        {
                            Level = MatchLevel.Ambiguous,
                            Reasoning = conflictDetail ?? "Ambiguous competing hypotheses.",
                            Sources = hypotheses.Select(h => h.Source.ToString()).Distinct(StringComparer.OrdinalIgnoreCase).ToArray(),
                            HasHardEvidence = false,
                            HasConflict = true,
                            DatVerified = false,
                            Tier = EvidenceTier.Tier4_Unknown,
                            PrimaryMatchKind = MatchKind.None,
                        };

                        return new ConsoleDetectionResult(
                            "AMBIGUOUS", 0, hypotheses, true, conflictDetail,
                            HasHardEvidence: false, IsSoftOnly: true,
                            SortDecision: SortDecision.Blocked,
                            DecisionClass: DecisionClass.Blocked,
                            MatchEvidence: ambiguousEvidence);
                    }
                }
            }
        }

        var winnerSources = winner.Value.Items
            .Select(i => i.Source)
            .Distinct()
            .ToList();

        var primarySource = winner.Value.Items
            .OrderByDescending(h => h.Confidence)
            .ThenByDescending(h => (int)h.Source)
            .ThenBy(h => h.ConsoleKey, StringComparer.OrdinalIgnoreCase)
            .Select(h => h.Source)
            .First();
        var primaryMatchKind = MapDetectionSourceToMatchKind(primarySource);
        var primaryTier = primaryMatchKind.GetTier();

        var hasHardEvidence = winnerSources.Any(s => s.IsHardEvidence());
        var runnerHasHardEvidence = hasConflict && sorted[1].Value.Items.Any(h => h.Source.IsHardEvidence());

        // Aggregate confidence: use the max single confidence from the winner.
        var aggregateConfidence = winner.Value.MaxSingleConfidence;
        if (winner.Value.Items.Count > 1)
        {
            aggregateConfidence = Math.Min(100, aggregateConfidence + MultiSourceAgreementBonus);
        }

        // Penalize if there's a strong competing hypothesis.
        // Keep deterministic behavior but avoid over-penalizing hard winner evidence
        // against weaker soft-context runner-up signals.
        if (hasConflict)
        {
            var runnerConfidence = sorted[1].Value.MaxSingleConfidence;
            var winnerConfidence = winner.Value.MaxSingleConfidence;
            var confidenceDelta = winnerConfidence - runnerConfidence;

            var effectivePenalty = 0;
            if (runnerConfidence >= 80)
            {
                effectivePenalty = 20;
            }
            else if (runnerConfidence >= 50)
            {
                effectivePenalty = 10;
            }

            if (hasHardEvidence && !runnerHasHardEvidence)
            {
                // Weak conflict against hard evidence: do not kill confidence aggressively.
                effectivePenalty = runnerConfidence >= 80
                    ? (confidenceDelta >= 10 ? 5 : 8)
                    : (confidenceDelta >= 10 ? 0 : 5);
            }

            aggregateConfidence = Math.Max(30, aggregateConfidence - effectivePenalty);
        }
        var isSoftOnly = !hasHardEvidence;

        // Soft-only cap: contextual-only detection remains bounded and explainable.
        // Source-specific caps remain respected, and multiple agreeing soft sources
        // may raise confidence into Review territory.
        if (isSoftOnly)
        {
            aggregateConfidence = Math.Min(aggregateConfidence, ComputeSoftOnlyCap(winnerSources));
        }

        // Single-source cap: one signal type alone is capped per its reliability
        if (winnerSources.Count == 1)
        {
            aggregateConfidence = Math.Min(aggregateConfidence, winnerSources[0].SingleSourceCap());
        }

        // Classify conflict type using platform family information
        var conflictType = ConflictType.None;
        if (hasConflict && familyLookup is not null && sorted.Count >= 2)
        {
            conflictType = ClassifyConflictType(sorted, familyLookup);
        }

        // Derive SortDecision with family-conflict awareness
        var decisionClass = DecisionResolver.Resolve(primaryTier, hasConflict, aggregateConfidence,
            datAvailable: false, conflictType: conflictType);
        var sortDecision = decisionClass.ToSortDecision();

        var matchEvidence = BuildMatchEvidence(
            aggregateConfidence,
            sortDecision,
            hasHardEvidence,
            hasConflict,
            winnerSources,
            primaryMatchKind,
            winner.Key,
            conflictDetail);

        return new ConsoleDetectionResult(
            winner.Key,
            aggregateConfidence,
            hypotheses,
            hasConflict,
            conflictDetail,
            hasHardEvidence,
            isSoftOnly,
            sortDecision,
            decisionClass,
            matchEvidence,
            conflictType);
    }

    /// <summary>
    /// Derives the sort gate decision from confidence, conflict, and evidence type.
    /// </summary>
    internal static SortDecision DetermineSortDecision(EvidenceTier tier, int confidence, bool conflict)
    {
        return DecisionResolver.Resolve(tier, conflict, confidence).ToSortDecision();
    }

    /// <summary>
    /// Backward-compatible matrix helper retained for existing tests.
    /// Prefer the tier-aware overload in production logic.
    /// </summary>
    internal static SortDecision DetermineSortDecision(int confidence, bool conflict, bool hardEvidence, int sourceCount, bool hasDatEvidence = false)
    {
        var tier = hasDatEvidence
            ? EvidenceTier.Tier0_ExactDat
            : hardEvidence
                ? EvidenceTier.Tier1_Structural
                : confidence >= ReviewThreshold || sourceCount > 1
                    ? EvidenceTier.Tier2_StrongHeuristic
                    : EvidenceTier.Tier3_WeakHeuristic;

        return DetermineSortDecision(tier, confidence, conflict);
    }

    private static int ComputeSoftOnlyCap(IReadOnlyList<DetectionSource> winnerSources)
    {
        if (winnerSources.Count == 0)
            return SoftOnlyCap;

        if (winnerSources.Count == 1)
            return winnerSources[0].SingleSourceCap();

        var strongestSourceCap = winnerSources.Max(s => s.SingleSourceCap());
        var multiSourceAgreementBonus = MultiSourceAgreementBonus;

        // Soft-only detections can become strong enough for review but should not
        // exceed hard-evidence sort confidence without corroboration.
        return Math.Min(85, strongestSourceCap + multiSourceAgreementBonus);
    }

    private static MatchEvidence BuildMatchEvidence(
        int confidence,
        SortDecision sortDecision,
        bool hasHardEvidence,
        bool hasConflict,
        IReadOnlyList<DetectionSource> winnerSources,
        MatchKind primaryMatchKind,
        string winnerKey,
        string? conflictDetail)
    {
        var level = sortDecision switch
        {
            SortDecision.DatVerified => MatchLevel.Exact,
            SortDecision.Sort => MatchLevel.Strong,
            SortDecision.Review when confidence >= 70 => MatchLevel.Probable,
            SortDecision.Review => MatchLevel.Weak,
            SortDecision.Unknown => MatchLevel.None,
            _ when hasConflict => MatchLevel.Ambiguous,
            _ when confidence > 0 => MatchLevel.Weak,
            _ => MatchLevel.None
        };

        var sourceLabels = winnerSources
            .Select(static s => s.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var reasoning = hasConflict && !string.IsNullOrWhiteSpace(conflictDetail)
            ? conflictDetail!
            : $"Console={winnerKey}, Confidence={confidence}, Sources={string.Join("+", sourceLabels)}";

        return new MatchEvidence
        {
            Level = level,
            Reasoning = reasoning,
            Sources = sourceLabels,
            HasHardEvidence = hasHardEvidence,
            HasConflict = hasConflict,
            DatVerified = sortDecision == SortDecision.DatVerified,
            Tier = primaryMatchKind.GetTier(),
            PrimaryMatchKind = primaryMatchKind,
        };
    }

    /// <summary>
    /// Maps a detection source to its corresponding MatchKind.
    /// </summary>
    internal static MatchKind MapDetectionSourceToMatchKind(DetectionSource source) => source switch
    {
        DetectionSource.DatHash => MatchKind.ExactDatHash,
        DetectionSource.UniqueExtension => MatchKind.UniqueExtensionMatch,
        DetectionSource.DiscHeader => MatchKind.DiscHeaderSignature,
        DetectionSource.CartridgeHeader => MatchKind.CartridgeHeaderMagic,
        DetectionSource.SerialNumber => MatchKind.SerialNumberMatch,
        DetectionSource.FolderName => MatchKind.FolderNameMatch,
        DetectionSource.ArchiveContent => MatchKind.ArchiveContentExtension,
        DetectionSource.FilenameKeyword => MatchKind.FilenameKeywordMatch,
        DetectionSource.AmbiguousExtension => MatchKind.AmbiguousExtensionSingle,
        _ => MatchKind.None,
    };

    private static int GetWinnerPriority(DetectionSource source) => source switch
    {
        DetectionSource.DatHash => 5,
        DetectionSource.DiscHeader => 4,
        DetectionSource.CartridgeHeader => 4,
        DetectionSource.UniqueExtension => 3,
        DetectionSource.SerialNumber => 2,
        DetectionSource.ArchiveContent => 2,
        DetectionSource.FolderName => 1,
        DetectionSource.FilenameKeyword => 1,
        DetectionSource.AmbiguousExtension => 0,
        _ => 0,
    };

    /// <summary>
    /// Classify whether a conflict is intra-family or cross-family.
    /// Cross-family = different platform families (e.g. PS1/RedumpDisc vs Vita/Hybrid).
    /// Intra-family = same family (e.g. PS1 vs PS2, both RedumpDisc).
    /// </summary>
    internal static ConflictType ClassifyConflictType(
        List<KeyValuePair<string, (int TotalConfidence, int MaxSingleConfidence, List<DetectionHypothesis> Items, int MaxSourcePriority)>> sortedGroups,
        Func<string, PlatformFamily> familyLookup)
    {
        if (sortedGroups.Count < 2)
            return ConflictType.None;

        var winnerFamily = familyLookup(sortedGroups[0].Key);

        // If the winner itself is Unknown family, we can't classify the conflict type.
        // Conservative: return None to avoid both over-blocking (CrossFamily) and
        // misleading intra-family labeling.
        if (winnerFamily == PlatformFamily.Unknown)
            return ConflictType.None;

        bool hasKnownCompetitor = false;

        // Check ALL competing groups, not just the runner-up.
        // This catches 3+ console scenarios (PS1 vs PS2 vs PSP).
        for (int i = 1; i < sortedGroups.Count; i++)
        {
            var competitorFamily = familyLookup(sortedGroups[i].Key);

            // Unknown family on competitor → skip (can't determine relationship)
            if (competitorFamily == PlatformFamily.Unknown)
                continue;

            hasKnownCompetitor = true;

            if (winnerFamily != competitorFamily)
                return ConflictType.CrossFamily;
        }

        // Only classify as IntraFamily if at least one known competitor was in the same family.
        // If all competitors were Unknown, we can't determine the conflict type.
        return hasKnownCompetitor ? ConflictType.IntraFamily : ConflictType.None;
    }
}
