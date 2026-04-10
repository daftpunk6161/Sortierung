namespace Romulus.Contracts.Models;

/// <summary>
/// Canonical side identifier for two-source collection comparison.
/// </summary>
public enum CollectionCompareSide
{
    Left,
    Right
}

/// <summary>
/// Deterministic compare outcome for a pair of collection entries.
/// </summary>
public enum CollectionDiffState
{
    OnlyInLeft,
    OnlyInRight,
    PresentInBothIdentical,
    PresentInBothDifferent,
    LeftPreferred,
    RightPreferred,
    ReviewRequired
}

/// <summary>
/// Planned merge action for a diff entry.
/// </summary>
public enum CollectionMergeDecision
{
    CopyToTarget,
    MoveToTarget,
    KeepExistingTarget,
    SkipAsDuplicate,
    ReviewRequired,
    Blocked
}

public static class CollectionMergeDecisionExtensions
{
    public static bool IsMutating(this CollectionMergeDecision decision)
        => decision is CollectionMergeDecision.CopyToTarget or CollectionMergeDecision.MoveToTarget;
}

/// <summary>
/// Deterministic apply outcome for one merge-plan entry.
/// </summary>
public enum CollectionMergeApplyOutcome
{
    Applied,
    KeptExistingTarget,
    SkippedAsDuplicate,
    ReviewRequired,
    Blocked,
    Failed
}

/// <summary>
/// Identifies a materialized collection scope without leaking adapter-specific state.
/// </summary>
public sealed record CollectionSourceScope
{
    public string SourceId { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public string RootFingerprint { get; init; } = string.Empty;
    public string EnrichmentFingerprint { get; init; } = string.Empty;
}

/// <summary>
/// Channel-neutral request contract for comparing two persisted collection scopes.
/// </summary>
public sealed record CollectionCompareRequest
{
    public int Version { get; init; } = 1;
    public CollectionSourceScope Left { get; init; } = new() { SourceId = "left", Label = "Left" };
    public CollectionSourceScope Right { get; init; } = new() { SourceId = "right", Label = "Right" };
    public int Offset { get; init; }
    public int Limit { get; init; } = 500;
}

/// <summary>
/// One deterministic diff row referencing the existing persisted collection truth.
/// </summary>
public sealed record CollectionDiffEntry
{
    public string DiffKey { get; init; } = string.Empty;
    public CollectionDiffState State { get; init; } = CollectionDiffState.ReviewRequired;
    public CollectionCompareSide? PreferredSide { get; init; }
    public bool ReviewRequired { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public CollectionIndexEntry? Left { get; init; }
    public CollectionIndexEntry? Right { get; init; }
}

/// <summary>
/// Summary projection for a compare result.
/// </summary>
public sealed record CollectionDiffSummary(
    int TotalEntries,
    int OnlyInLeft,
    int OnlyInRight,
    int PresentInBothIdentical,
    int PresentInBothDifferent,
    int LeftPreferred,
    int RightPreferred,
    int ReviewRequired)
{
    public static CollectionDiffSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    public static CollectionDiffSummary FromEntries(IEnumerable<CollectionDiffEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var totalEntries = 0;
        var onlyInLeft = 0;
        var onlyInRight = 0;
        var identical = 0;
        var different = 0;
        var leftPreferred = 0;
        var rightPreferred = 0;
        var reviewRequired = 0;

        foreach (var entry in entries)
        {
            totalEntries++;
            switch (entry.State)
            {
                case CollectionDiffState.OnlyInLeft:
                    onlyInLeft++;
                    break;
                case CollectionDiffState.OnlyInRight:
                    onlyInRight++;
                    break;
                case CollectionDiffState.PresentInBothIdentical:
                    identical++;
                    break;
                case CollectionDiffState.PresentInBothDifferent:
                    different++;
                    break;
                case CollectionDiffState.LeftPreferred:
                    leftPreferred++;
                    break;
                case CollectionDiffState.RightPreferred:
                    rightPreferred++;
                    break;
                case CollectionDiffState.ReviewRequired:
                    reviewRequired++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry.State), entry.State, "Unknown collection diff state.");
            }
        }

        return new CollectionDiffSummary(
            totalEntries,
            onlyInLeft,
            onlyInRight,
            identical,
            different,
            leftPreferred,
            rightPreferred,
            reviewRequired);
    }
}

/// <summary>
/// Full compare result contract shared by GUI, CLI, and API.
/// </summary>
public sealed record CollectionCompareResult
{
    public int Version { get; init; } = 1;
    public CollectionCompareRequest Request { get; init; } = new();
    public CollectionDiffSummary Summary { get; init; } = CollectionDiffSummary.Empty;
    public IReadOnlyList<CollectionDiffEntry> Entries { get; init; } = Array.Empty<CollectionDiffEntry>();
}

/// <summary>
/// Top-level request for building a merge plan from a compare scope.
/// </summary>
public sealed record CollectionMergeRequest
{
    public int Version { get; init; } = 1;
    public CollectionCompareRequest CompareRequest { get; init; } = new();
    public string TargetRoot { get; init; } = string.Empty;
    public bool AllowMoves { get; init; }
}

/// <summary>
/// Planned merge action for one diff entry.
/// </summary>
public sealed record CollectionMergePlanEntry
{
    public string PlanEntryId { get; init; } = string.Empty;
    public string DiffKey { get; init; } = string.Empty;
    public CollectionMergeDecision Decision { get; init; } = CollectionMergeDecision.Blocked;
    public CollectionCompareSide? SourceSide { get; init; }
    public bool ReviewRequired { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string? TargetRoot { get; init; }
    public string? TargetPath { get; init; }
    public CollectionIndexEntry? Source { get; init; }
    public CollectionIndexEntry? ExistingTarget { get; init; }
}

/// <summary>
/// Summary projection for a merge plan.
/// </summary>
public sealed record CollectionMergePlanSummary(
    int TotalEntries,
    int CopyToTarget,
    int MoveToTarget,
    int KeepExistingTarget,
    int SkipAsDuplicate,
    int ReviewRequired,
    int Blocked,
    int MutatingEntries)
{
    public static CollectionMergePlanSummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0);

    public static CollectionMergePlanSummary FromEntries(IEnumerable<CollectionMergePlanEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var totalEntries = 0;
        var copyToTarget = 0;
        var moveToTarget = 0;
        var keepExistingTarget = 0;
        var skipAsDuplicate = 0;
        var reviewRequired = 0;
        var blocked = 0;
        var mutatingEntries = 0;

        foreach (var entry in entries)
        {
            totalEntries++;
            switch (entry.Decision)
            {
                case CollectionMergeDecision.CopyToTarget:
                    copyToTarget++;
                    mutatingEntries++;
                    break;
                case CollectionMergeDecision.MoveToTarget:
                    moveToTarget++;
                    mutatingEntries++;
                    break;
                case CollectionMergeDecision.KeepExistingTarget:
                    keepExistingTarget++;
                    break;
                case CollectionMergeDecision.SkipAsDuplicate:
                    skipAsDuplicate++;
                    break;
                case CollectionMergeDecision.ReviewRequired:
                    reviewRequired++;
                    break;
                case CollectionMergeDecision.Blocked:
                    blocked++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry.Decision), entry.Decision, "Unknown collection merge decision.");
            }
        }

        return new CollectionMergePlanSummary(
            totalEntries,
            copyToTarget,
            moveToTarget,
            keepExistingTarget,
            skipAsDuplicate,
            reviewRequired,
            blocked,
            mutatingEntries);
    }
}

/// <summary>
/// Channel-neutral merge plan contract. Apply is intentionally a later concern than plan generation.
/// </summary>
public sealed record CollectionMergePlan
{
    public int Version { get; init; } = 1;
    public CollectionMergeRequest Request { get; init; } = new();
    public CollectionMergePlanSummary Summary { get; init; } = CollectionMergePlanSummary.Empty;
    public IReadOnlyList<CollectionMergePlanEntry> Entries { get; init; } = Array.Empty<CollectionMergePlanEntry>();
}

/// <summary>
/// Top-level apply contract. Execute rebuilds the authoritative plan from this request
/// so clients cannot drift from preview semantics.
/// </summary>
public sealed record CollectionMergeApplyRequest
{
    public int Version { get; init; } = 1;
    public CollectionMergeRequest MergeRequest { get; init; } = new();
    public string AuditPath { get; init; } = string.Empty;
}

/// <summary>
/// Execute outcome for one merge-plan entry.
/// </summary>
public sealed record CollectionMergeApplyEntryResult
{
    public string PlanEntryId { get; init; } = string.Empty;
    public string DiffKey { get; init; } = string.Empty;
    public CollectionMergeDecision Decision { get; init; } = CollectionMergeDecision.Blocked;
    public CollectionMergeApplyOutcome Outcome { get; init; } = CollectionMergeApplyOutcome.Blocked;
    public CollectionCompareSide? SourceSide { get; init; }
    public string ReasonCode { get; init; } = string.Empty;
    public string? SourcePath { get; init; }
    public string? TargetPath { get; init; }
}

/// <summary>
/// Summary projection for merge execution.
/// </summary>
public sealed record CollectionMergeApplySummary(
    int TotalEntries,
    int Applied,
    int Copied,
    int Moved,
    int KeptExistingTarget,
    int SkippedAsDuplicate,
    int ReviewRequired,
    int Blocked,
    int Failed)
{
    public static CollectionMergeApplySummary Empty { get; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0);

    public static CollectionMergeApplySummary FromEntries(IEnumerable<CollectionMergeApplyEntryResult> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);

        var totalEntries = 0;
        var applied = 0;
        var copied = 0;
        var moved = 0;
        var keptExistingTarget = 0;
        var skippedAsDuplicate = 0;
        var reviewRequired = 0;
        var blocked = 0;
        var failed = 0;

        foreach (var entry in entries)
        {
            totalEntries++;
            switch (entry.Outcome)
            {
                case CollectionMergeApplyOutcome.Applied:
                    applied++;
                    if (entry.Decision == CollectionMergeDecision.CopyToTarget)
                        copied++;
                    else if (entry.Decision == CollectionMergeDecision.MoveToTarget)
                        moved++;
                    break;
                case CollectionMergeApplyOutcome.KeptExistingTarget:
                    keptExistingTarget++;
                    break;
                case CollectionMergeApplyOutcome.SkippedAsDuplicate:
                    skippedAsDuplicate++;
                    break;
                case CollectionMergeApplyOutcome.ReviewRequired:
                    reviewRequired++;
                    break;
                case CollectionMergeApplyOutcome.Blocked:
                    blocked++;
                    break;
                case CollectionMergeApplyOutcome.Failed:
                    failed++;
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(entry.Outcome), entry.Outcome, "Unknown collection merge apply outcome.");
            }
        }

        return new CollectionMergeApplySummary(
            totalEntries,
            applied,
            copied,
            moved,
            keptExistingTarget,
            skippedAsDuplicate,
            reviewRequired,
            blocked,
            failed);
    }
}

/// <summary>
/// Full merge execution result shared by GUI, CLI and API.
/// </summary>
public sealed record CollectionMergeApplyResult
{
    public int Version { get; init; } = 1;
    public CollectionMergeApplyRequest Request { get; init; } = new();
    public CollectionMergePlan Plan { get; init; } = new();
    public CollectionMergeApplySummary Summary { get; init; } = CollectionMergeApplySummary.Empty;
    public IReadOnlyList<CollectionMergeApplyEntryResult> Entries { get; init; } = Array.Empty<CollectionMergeApplyEntryResult>();
    public string AuditPath { get; init; } = string.Empty;
    public bool RollbackAvailable { get; init; }
    public string? BlockedReason { get; init; }
}

/// <summary>
/// Rollback request for previously executed collection merge audits.
/// </summary>
public sealed record CollectionMergeRollbackRequest
{
    public int Version { get; init; } = 1;
    public string AuditPath { get; init; } = string.Empty;
    public bool DryRun { get; init; } = true;
}
