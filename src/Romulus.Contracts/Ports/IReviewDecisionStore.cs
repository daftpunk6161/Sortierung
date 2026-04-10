using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Storage-agnostic port for persisted review approvals.
/// The store persists user decisions; it must not recompute recognition or sorting logic.
/// </summary>
public interface IReviewDecisionStore
{
    /// <summary>
    /// Upserts one or more persisted approvals.
    /// Callers must pass normalized paths and UTC timestamps.
    /// </summary>
    ValueTask UpsertApprovalsAsync(
        IReadOnlyList<ReviewApprovalEntry> approvals,
        CancellationToken ct = default);

    /// <summary>
    /// Resolves persisted approvals by absolute normalized path.
    /// Implementations should preserve deterministic ordering for identical inputs.
    /// </summary>
    ValueTask<IReadOnlyList<ReviewApprovalEntry>> ListApprovalsAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default);
}
