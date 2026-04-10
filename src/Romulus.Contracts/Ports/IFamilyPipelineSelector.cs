using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Applies family-specific post-enrichment adjustments before candidate materialization.
/// </summary>
public interface IFamilyPipelineSelector
{
    FamilyPipelineDecision Apply(FamilyPipelineInput input);
}

/// <summary>
/// Input for family pipeline selection – all values come from enrichment.
/// </summary>
public sealed record FamilyPipelineInput(
    DecisionClass DecisionClass,
    SortDecision SortDecision,
    MatchEvidence MatchEvidence,
    int DetectionConfidence,
    bool DetectionConflict,
    ConflictType ConflictType,
    bool DatMatch,
    PlatformFamily DetectedFamily,
    PlatformFamily ResolvedFamily,
    MatchKind FinalMatchKind);

/// <summary>
/// Output from family pipeline selection – carried forward to candidate materialization.
/// </summary>
public readonly record struct FamilyPipelineDecision(
    DecisionClass DecisionClass,
    SortDecision SortDecision,
    MatchEvidence MatchEvidence,
    int DetectionConfidence,
    bool DetectionConflict,
    ConflictType ConflictType);
