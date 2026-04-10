using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Applies family-specific post-enrichment adjustments before candidate materialization.
/// This keeps family conflict escalation and DAT-vs-detector consistency in one place.
/// </summary>
public sealed class FamilyPipelineSelector : IFamilyPipelineSelector
{
    public FamilyPipelineDecision Apply(FamilyPipelineInput input)
    {
        var decisionClass = input.DecisionClass;
        var sortDecision = input.SortDecision;
        var matchEvidence = input.MatchEvidence;
        var detectionConfidence = input.DetectionConfidence;
        var detectionConflict = input.DetectionConflict;
        var conflictType = input.ConflictType;

        // Cross-family mismatch between detector console and DAT-resolved console is unsafe.
        if (input.DatMatch
            && input.DetectedFamily != PlatformFamily.Unknown
            && input.ResolvedFamily != PlatformFamily.Unknown
            && input.DetectedFamily != input.ResolvedFamily)
        {
            detectionConflict = true;
            conflictType = ConflictType.CrossFamily;
            decisionClass = DecisionClass.Blocked;
            sortDecision = SortDecision.Blocked;
            detectionConfidence = Math.Min(detectionConfidence, 75);
            matchEvidence = matchEvidence with
            {
                Level = MatchLevel.Ambiguous,
                HasConflict = true,
                Reasoning = AppendReason(matchEvidence.Reasoning,
                    $"Cross-family mismatch: detector={input.DetectedFamily}, dat={input.ResolvedFamily}"),
            };
        }

        // Disc-family structural evidence should not be under-weighted.
        if (!input.DatMatch
            && input.ResolvedFamily == PlatformFamily.RedumpDisc
            && input.FinalMatchKind == MatchKind.DiscHeaderSignature)
        {
            detectionConfidence = Math.Max(detectionConfidence, 92);
        }

        // Arcade without DAT should stay conservative.
        if (!input.DatMatch
            && input.ResolvedFamily == PlatformFamily.Arcade
            && decisionClass == DecisionClass.Sort)
        {
            decisionClass = DecisionClass.Review;
            sortDecision = SortDecision.Review;
            matchEvidence = matchEvidence with
            {
                Reasoning = AppendReason(matchEvidence.Reasoning,
                    "Arcade family without DAT verification downgraded to review."),
            };
        }

        return new FamilyPipelineDecision(
            DecisionClass: decisionClass,
            SortDecision: sortDecision,
            MatchEvidence: matchEvidence,
            DetectionConfidence: detectionConfidence,
            DetectionConflict: detectionConflict,
            ConflictType: conflictType);
    }

    private static string AppendReason(string? baseReason, string suffix)
    {
        if (string.IsNullOrWhiteSpace(baseReason))
            return suffix;

        return baseReason.EndsWith('.')
            ? $"{baseReason} {suffix}"
            : $"{baseReason}; {suffix}";
    }
}
