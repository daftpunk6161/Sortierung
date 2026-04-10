using Romulus.Contracts;
using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Workflow;

public static class WorkflowScenarioCatalog
{
    private static readonly WorkflowScenarioDefinition[] Scenarios =
    [
        new(
            WorkflowScenarioIds.QuickClean,
            "Quick Clean",
            "Fast dry-run cleanup for obvious junk and duplicate hotspots.",
            "quick-scan",
            ["Select roots", "Review basic cleanup scope", "Run preview", "Inspect report"],
            new RunProfileSettings
            {
                Mode = RunConstants.ModeDryRun,
                RemoveJunk = true,
                SortConsole = true,
                EnableDat = false,
                ApproveReviews = false,
                ConvertOnly = false
            }),
        new(
            WorkflowScenarioIds.FullAudit,
            "Full Audit",
            "Strict preview with DAT verification, sorting review, and detailed audit output.",
            "default",
            ["Select roots", "Enable DAT verification", "Preview full collection", "Review audit findings"],
            new RunProfileSettings
            {
                Mode = RunConstants.ModeDryRun,
                RemoveJunk = true,
                SortConsole = true,
                EnableDat = true,
                EnableDatAudit = true,
                EnableDatRename = false,
                ApproveReviews = false
            }),
        new(
            WorkflowScenarioIds.DatVerification,
            "DAT Verification",
            "Verification-focused preview without cleanup side effects.",
            "retro-purist",
            ["Select roots", "Load DAT-backed verification", "Preview mismatches", "Review DAT findings"],
            new RunProfileSettings
            {
                Mode = RunConstants.ModeDryRun,
                RemoveJunk = false,
                SortConsole = false,
                EnableDat = true,
                EnableDatAudit = true,
                EnableDatRename = false
            }),
        new(
            WorkflowScenarioIds.FormatOptimization,
            "Format Optimization",
            "Conversion-oriented workflow for safe space-saving planning.",
            "space-saver",
            ["Select roots", "Review conversion policy", "Preview optimization impact", "Apply when verified"],
            new RunProfileSettings
            {
                Mode = RunConstants.ModeDryRun,
                RemoveJunk = false,
                SortConsole = false,
                EnableDat = true,
                EnableDatAudit = true,
                ConvertFormat = "auto",
                ConvertOnly = false
            }),
        new(
            WorkflowScenarioIds.NewCollectionSetup,
            "New Collection Setup",
            "First-pass guided setup for a new library with safe defaults.",
            "default",
            ["Select roots", "Choose defaults", "Preview organization plan", "Save reusable setup"],
            new RunProfileSettings
            {
                Mode = RunConstants.ModeDryRun,
                RemoveJunk = true,
                SortConsole = true,
                EnableDat = true,
                EnableDatAudit = true,
                EnableDatRename = false,
                KeepUnknownWhenOnlyGames = true
            })
    ];

    public static IReadOnlyList<WorkflowScenarioDefinition> List()
        => Scenarios;

    public static WorkflowScenarioDefinition? TryGet(string? id)
        => Scenarios.FirstOrDefault(scenario => string.Equals(scenario.Id, id, StringComparison.OrdinalIgnoreCase));
}
