namespace RomCleanup.Tests.Benchmark.Infrastructure;

/// <summary>
/// Evaluates repair-gate readiness per EVALUATION_STRATEGY §9.2 / ADR-015.
/// Repair is safe when: DAT-Exact ∧ Confidence ≥ 95 ∧ ¬HasConflict ∧ Hard Evidence.
/// </summary>
internal static class RepairGateEvaluator
{
    /// <summary>
    /// Evaluates each benchmark result against the repair-gate criteria.
    /// </summary>
    public static RepairGateReport Evaluate(IReadOnlyList<BenchmarkSampleResult> results)
    {
        if (results.Count == 0)
            return new RepairGateReport([], 0, 0, 0, 0);

        var entries = new List<RepairGateEntry>(results.Count);
        int safeCount = 0, riskyCount = 0, blockedCount = 0;

        foreach (var r in results)
        {
            var status = ClassifyRepairStatus(r);
            entries.Add(new RepairGateEntry(r.Id, status, r.ActualConfidence, r.ActualHasConflict,
                r.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable));

            switch (status)
            {
                case RepairStatus.Safe: safeCount++; break;
                case RepairStatus.Risky: riskyCount++; break;
                case RepairStatus.Blocked: blockedCount++; break;
            }
        }

        double safeRate = 100.0 * safeCount / results.Count;
        return new RepairGateReport(entries, safeRate, safeCount, riskyCount, blockedCount);
    }

    /// <summary>
    /// Classifies a single result into Safe/Risky/Blocked per the repair-gate criteria.
    /// </summary>
    private static RepairStatus ClassifyRepairStatus(BenchmarkSampleResult result)
    {
        bool isCorrect = result.Verdict is BenchmarkVerdict.Correct or BenchmarkVerdict.Acceptable;
        bool highConfidence = result.ActualConfidence >= 95;
        bool noConflict = !result.ActualHasConflict;

        // All three criteria must be met for Safe
        if (isCorrect && highConfidence && noConflict)
            return RepairStatus.Safe;

        // Partial criteria → Risky
        if (isCorrect && (highConfidence || noConflict))
            return RepairStatus.Risky;

        return RepairStatus.Blocked;
    }
}

internal enum RepairStatus
{
    Safe,
    Risky,
    Blocked
}

internal sealed record RepairGateEntry(
    string Id,
    RepairStatus Status,
    int Confidence,
    bool HasConflict,
    bool DetectionCorrect);

internal sealed record RepairGateReport(
    IReadOnlyList<RepairGateEntry> Entries,
    double SafeRatePercent,
    int SafeCount,
    int RiskyCount,
    int BlockedCount)
{
    public bool IsRepairFeatureReady => SafeRatePercent >= 70.0;
}
