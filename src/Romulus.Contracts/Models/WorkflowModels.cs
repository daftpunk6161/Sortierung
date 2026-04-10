namespace Romulus.Contracts.Models;

public static class WorkflowScenarioIds
{
    public const string QuickClean = "quick-clean";
    public const string FullAudit = "full-audit";
    public const string DatVerification = "dat-verification";
    public const string FormatOptimization = "format-optimization";
    public const string NewCollectionSetup = "new-collection-setup";
}

/// <summary>
/// Guided workflow definition that maps directly to existing run semantics.
/// </summary>
public sealed record WorkflowScenarioDefinition(
    string Id,
    string Name,
    string Description,
    string RecommendedProfileId,
    string[] Steps,
    RunProfileSettings Settings);
