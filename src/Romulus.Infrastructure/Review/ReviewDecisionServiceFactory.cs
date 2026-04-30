using Romulus.Infrastructure.Index;

namespace Romulus.Infrastructure.Review;

/// <summary>
/// Creates persisted review services against the shared collection database without duplicating open/recovery wiring in entry points.
/// </summary>
public static class ReviewDecisionServiceFactory
{
    public static PersistedReviewDecisionService? TryCreate(Action<string>? onWarning = null)
        => TryCreate(databasePath: null, onWarning);

    /// <summary>
    /// Variant that accepts an explicit database path, so CLI/test entry points
    /// can route the review store to the same overridden collection database
    /// they already use for <see cref="LiteDbCollectionIndex"/>. This avoids a
    /// second LiteDB process opening the user's real
    /// <c>%APPDATA%\Romulus\collection.db</c> in parallel and contending for
    /// the exclusive file lock (Pre-W7 test-isolation Fix #2).
    /// </summary>
    public static PersistedReviewDecisionService? TryCreate(string? databasePath, Action<string>? onWarning)
    {
        try
        {
            var resolvedPath = string.IsNullOrWhiteSpace(databasePath)
                ? CollectionIndexPaths.ResolveDefaultDatabasePath()
                : databasePath;
            return new PersistedReviewDecisionService(
                new LiteDbReviewDecisionStore(resolvedPath, onWarning),
                onWarning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            onWarning?.Invoke($"[ReviewStore] Disabled for this run: {ex.Message}");
            return null;
        }
    }
}
