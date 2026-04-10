using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Review;

/// <summary>
/// Shared service for persisting and reapplying review approvals without duplicating run logic in entry points.
/// </summary>
public sealed class PersistedReviewDecisionService : IDisposable
{
    private readonly IReviewDecisionStore _store;
    private readonly Action<string>? _onWarning;
    private bool _disposed;

    public PersistedReviewDecisionService(IReviewDecisionStore store, Action<string>? onWarning = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _onWarning = onWarning;
    }

    public async Task<IReadOnlyList<RomCandidate>> ApplyApprovalsAsync(
        IReadOnlyList<RomCandidate> candidates,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
            return candidates;

        try
        {
            var paths = candidates
                .Select(static candidate => candidate.MainPath)
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();

            if (paths.Length == 0)
                return candidates;

            var approvals = await _store.ListApprovalsAsync(paths, ct).ConfigureAwait(false);
            if (approvals.Count == 0)
                return candidates;

            var approvedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var staleCount = 0;
            foreach (var approval in approvals)
            {
                if (IsApprovalStale(approval))
                {
                    staleCount++;
                    continue;
                }

                approvedPaths.Add(approval.Path);
            }

            if (staleCount > 0)
                _onWarning?.Invoke($"[ReviewStore] Ignored {staleCount} stale approval(s) due to file changes.");

            if (approvedPaths.Count == 0)
                return candidates;

            var changed = false;
            var updated = new RomCandidate[candidates.Count];
            for (var i = 0; i < candidates.Count; i++)
            {
                ct.ThrowIfCancellationRequested();
                var candidate = candidates[i];
                if (candidate.SortDecision == SortDecision.Review && approvedPaths.Contains(candidate.MainPath))
                {
                    updated[i] = candidate with { SortDecision = SortDecision.Sort };
                    changed = true;
                }
                else
                {
                    updated[i] = candidate;
                }
            }

            return changed ? updated : candidates;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _onWarning?.Invoke($"[ReviewStore] Persisted approval reuse skipped: {ex.Message}");
            return candidates;
        }
    }

    public async Task<int> PersistApprovalsAsync(
        IReadOnlyList<RomCandidate> candidates,
        string source,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        if (candidates.Count == 0)
            return 0;

        try
        {
            var nowUtc = DateTime.UtcNow;
            var approvals = candidates
                .Where(static candidate => !string.IsNullOrWhiteSpace(candidate.MainPath))
                .GroupBy(static candidate => candidate.MainPath, StringComparer.OrdinalIgnoreCase)
                .Select(static group => group.First())
                .Select(candidate => new ReviewApprovalEntry
                {
                    Path = candidate.MainPath,
                    ConsoleKey = string.IsNullOrWhiteSpace(candidate.ConsoleKey) ? "UNKNOWN" : candidate.ConsoleKey,
                    SortDecision = candidate.SortDecision,
                    MatchLevel = candidate.MatchEvidence.Level,
                    MatchReasoning = candidate.MatchEvidence.Reasoning ?? string.Empty,
                    Source = source,
                    ApprovedUtc = nowUtc,
                    FileLastWriteUtcTicks = TryGetLastWriteUtcTicks(candidate.MainPath)
                })
                .ToArray();

            if (approvals.Length == 0)
                return 0;

            await _store.UpsertApprovalsAsync(approvals, ct).ConfigureAwait(false);
            return approvals.Length;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _onWarning?.Invoke($"[ReviewStore] Persisted approval write skipped: {ex.Message}");
            return 0;
        }
    }

    public async Task<IReadOnlySet<string>> GetApprovedPathSetAsync(
        IReadOnlyList<string> paths,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(paths);

        if (paths.Count == 0)
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var approvals = await _store.ListApprovalsAsync(paths, ct).ConfigureAwait(false);
            return approvals
                .Where(static approval => !IsApprovalStale(approval))
                .Select(static approval => approval.Path)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _onWarning?.Invoke($"[ReviewStore] Approval read skipped: {ex.Message}");
            return new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        if (_store is IDisposable disposableStore)
            disposableStore.Dispose();
    }

    private static bool IsApprovalStale(ReviewApprovalEntry approval)
    {
        if (!approval.FileLastWriteUtcTicks.HasValue)
            return false;

        try
        {
            if (!File.Exists(approval.Path))
                return true;

            var currentTicks = File.GetLastWriteTimeUtc(approval.Path).Ticks;
            return currentTicks != approval.FileLastWriteUtcTicks.Value;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return true;
        }
    }

    private static long? TryGetLastWriteUtcTicks(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;

            return File.GetLastWriteTimeUtc(path).Ticks;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
