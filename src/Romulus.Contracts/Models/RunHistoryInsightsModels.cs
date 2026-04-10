namespace Romulus.Contracts.Models;

public sealed record RunSnapshotMetricDelta(long Current, long Previous, long Delta);

public sealed record RunSnapshotComparison(
    string RunId,
    string CompareToRunId,
    DateTime CompletedUtc,
    DateTime CompareToCompletedUtc,
    string Status,
    string CompareToStatus,
    RunSnapshotMetricDelta TotalFiles,
    RunSnapshotMetricDelta CollectionSizeBytes,
    RunSnapshotMetricDelta Games,
    RunSnapshotMetricDelta Dupes,
    RunSnapshotMetricDelta Junk,
    RunSnapshotMetricDelta DatMatches,
    RunSnapshotMetricDelta ConvertedCount,
    RunSnapshotMetricDelta FailCount,
    RunSnapshotMetricDelta SavedBytes,
    RunSnapshotMetricDelta ConvertSavedBytes,
    RunSnapshotMetricDelta HealthScore);

public sealed record StorageInsightReport(
    int SampleCount,
    DateTime? LatestCompletedUtc,
    RunSnapshotMetricDelta TotalFiles,
    RunSnapshotMetricDelta CollectionSizeBytes,
    RunSnapshotMetricDelta HealthScore,
    long LatestSavedBytes,
    long LatestConvertSavedBytes,
    long CumulativeSavedBytes,
    long CumulativeConvertSavedBytes,
    double AverageRunGrowthBytes);
