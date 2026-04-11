namespace Romulus.Core.Scoring;

/// <summary>
/// Channel-neutral health score formula for run quality (0..100).
/// </summary>
public static class HealthScorer
{
    public sealed record HealthScoreWeights(
        double BaseScoreMax,
        double JunkPenaltyCap,
        double JunkPenaltyPerPercent,
        double ExtremeJunkThresholdPercent,
        double ExtremeJunkPenaltyFloor,
        double VerifiedBonusCap,
        double VerifiedBonusPerPercent,
        double ErrorPenaltyCap,
        double ErrorPenaltyPerError);

    private static readonly object Sync = new();
    private static volatile HealthScoreWeights? _registeredWeights;
    private static Func<HealthScoreWeights>? _weightsFactory;

    private static readonly HealthScoreWeights FallbackWeights = new(
        BaseScoreMax: 100.0,
        JunkPenaltyCap: 30.0,
        JunkPenaltyPerPercent: 0.3,
        ExtremeJunkThresholdPercent: 90.0,
        ExtremeJunkPenaltyFloor: 70.0,
        VerifiedBonusCap: 10.0,
        VerifiedBonusPerPercent: 0.15,
        ErrorPenaltyCap: 20.0,
        ErrorPenaltyPerError: 2.0);

    /// <summary>
    /// Resets all registered state. For test isolation only – never call in production.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Sync)
        {
            _registeredWeights = null;
            _weightsFactory = null;
        }
    }

    public static void RegisterWeightFactory(Func<HealthScoreWeights> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Sync)
        {
            _weightsFactory = factory;
            _registeredWeights = null;
        }
    }

    public static void RegisterWeights(HealthScoreWeights weights)
    {
        ArgumentNullException.ThrowIfNull(weights);
        lock (Sync)
        {
            _registeredWeights = weights;
        }
    }

    private static HealthScoreWeights EnsureWeightsLoaded()
    {
        var cached = _registeredWeights;
        if (cached is not null)
            return cached;

        lock (Sync)
        {
            cached = _registeredWeights;
            if (cached is not null)
                return cached;

            if (_weightsFactory is not null)
            {
                var loaded = _weightsFactory();
                if (loaded is not null)
                    _registeredWeights = loaded;
            }

            _registeredWeights ??= FallbackWeights;
            return _registeredWeights;
        }
    }

    public static int GetHealthScore(int totalFiles, int dupes, int junk, int verified, int errors = 0)
    {
        if (totalFiles <= 0)
            return 0;

        var weights = EnsureWeightsLoaded();

        var dupePct = 100.0 * dupes / totalFiles;
        var junkPct = 100.0 * junk / totalFiles;
        var verifiedPct = 100.0 * verified / totalFiles;

        var baseScore = weights.BaseScoreMax - dupePct;
        var junkPenalty = Math.Min(weights.JunkPenaltyCap, junkPct * weights.JunkPenaltyPerPercent);
        if (junkPct >= weights.ExtremeJunkThresholdPercent)
            junkPenalty = Math.Max(junkPenalty, weights.ExtremeJunkPenaltyFloor);
        var verifiedBonus = Math.Min(weights.VerifiedBonusCap, verifiedPct * weights.VerifiedBonusPerPercent);
        var errorPenalty = Math.Min(weights.ErrorPenaltyCap, errors * weights.ErrorPenaltyPerError);

        return (int)Math.Clamp(baseScore - junkPenalty + verifiedBonus - errorPenalty, 0, (int)weights.BaseScoreMax);
    }
}