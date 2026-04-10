using Romulus.Infrastructure.Index;

namespace Romulus.Infrastructure.Review;

/// <summary>
/// Creates persisted review services against the shared collection database without duplicating open/recovery wiring in entry points.
/// </summary>
public static class ReviewDecisionServiceFactory
{
    public static PersistedReviewDecisionService? TryCreate(Action<string>? onWarning = null)
    {
        try
        {
            return new PersistedReviewDecisionService(
                new LiteDbReviewDecisionStore(CollectionIndexPaths.ResolveDefaultDatabasePath(), onWarning),
                onWarning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            onWarning?.Invoke($"[ReviewStore] Disabled for this run: {ex.Message}");
            return null;
        }
    }
}
