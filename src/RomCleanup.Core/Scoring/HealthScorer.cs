namespace RomCleanup.Core.Scoring;

/// <summary>
/// Channel-neutral health score formula for run quality (0..100).
/// </summary>
public static class HealthScorer
{
    public static int GetHealthScore(int totalFiles, int dupes, int junk, int verified, int errors = 0)
    {
        if (totalFiles <= 0)
            return 0;

        var dupePct = 100.0 * dupes / totalFiles;
        var junkPct = 100.0 * junk / totalFiles;
        var verifiedPct = 100.0 * verified / totalFiles;

        var baseScore = 100.0 - dupePct;
        var junkPenalty = Math.Min(30.0, junkPct * 0.3);
        var verifiedBonus = Math.Min(10.0, verifiedPct * 0.15);
        var errorPenalty = Math.Min(20.0, errors * 2.0);

        return (int)Math.Clamp(baseScore - junkPenalty + verifiedBonus - errorPenalty, 0, 100);
    }
}