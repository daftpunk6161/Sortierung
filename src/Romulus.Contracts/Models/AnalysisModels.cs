namespace Romulus.Contracts.Models;

/// <summary>Heatmap entry showing duplicate concentration per console.</summary>
public sealed record HeatmapEntry(string Console, int Total, int Duplicates, double DuplicatePercent);

/// <summary>Directory with highest duplicate action count from audit.</summary>
public sealed record DuplicateSourceEntry(string Directory, int Count);

/// <summary>Junk classification result for a single file.</summary>
public sealed record JunkReportEntry(string Tag, string Reason, string Level);

/// <summary>Conversion estimate for a set of files.</summary>
public sealed record ConversionEstimateResult(
    long TotalSourceBytes, long EstimatedTargetBytes, long SavedBytes, double CompressionRatio,
    IReadOnlyList<ConversionDetail> Details,
    string Disclaimer = "Estimates based on static average compression rates. Actual results may vary.");

/// <summary>Per-file conversion estimate detail.</summary>
public sealed record ConversionDetail(
    string FileName, string SourceFormat, string TargetFormat, long SourceBytes, long EstimatedBytes);

/// <summary>Per-console conversion estimate detail.</summary>
public sealed record ConsoleConversionEstimate(
    string ConsoleKey,
    int FileCount,
    long SourceBytes,
    long EstimatedBytes,
    long SavedBytes,
    double CompressionRatio,
    IReadOnlyList<ConversionDetail> Details);

/// <summary>Conversion advisor output with console breakdown and actionable recommendations.</summary>
public sealed record ConversionAdvisorResult(
    long TotalSourceBytes,
    long EstimatedTargetBytes,
    long SavedBytes,
    double CompressionRatio,
    IReadOnlyList<ConsoleConversionEstimate> Consoles,
    IReadOnlyList<string> Recommendations);

/// <summary>Point-in-time collection trend snapshot for historical tracking.</summary>
public sealed record TrendSnapshot(
    DateTime Timestamp, int TotalFiles, long SizeBytes, int Verified, int Dupes, int Junk, int QualityScore);

/// <summary>
/// T-W7-HEALTH-SCORE: Per-console HealthScore breakdown for a single run.
/// Deterministic projection of the run truth (RomCandidate + DedupeGroup);
/// no parallel scoring source. Persisted as part of <c>CollectionRunSnapshot</c>
/// and consumed by GUI/CLI/API trend surfaces.
/// </summary>
public sealed record ConsoleHealthBreakdown
{
    public string ConsoleKey { get; init; } = "UNKNOWN";
    public int TotalFiles { get; init; }
    public int Games { get; init; }
    public int Dupes { get; init; }
    public int Junk { get; init; }
    public int DatMatches { get; init; }
    public int HealthScore { get; init; }
}

/// <summary>Integrity baseline entry for a single file.</summary>
public sealed record IntegrityEntry(string Hash, long Size, DateTime LastModified);

/// <summary>Integrity baseline wrapper with common root for relative paths.</summary>
public sealed record IntegrityBaseline(string Root, Dictionary<string, IntegrityEntry> Entries);

/// <summary>Result of integrity check against baseline.</summary>
public sealed record IntegrityCheckResult(
    IReadOnlyList<string> Changed, IReadOnlyList<string> Missing,
    IReadOnlyList<string> Intact, bool BitRotRisk, string? Message = null);

/// <summary>Settings diff entry for config comparison.</summary>
public sealed record ConfigDiffEntry(string Key, string SavedValue, string CurrentValue);

/// <summary>DAT file diff result comparing two Logiqx XML DAT files.</summary>
public sealed record DatDiffResult(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, int ModifiedCount, int UnchangedCount);

/// <summary>Per-console summary for generated FixDAT output.</summary>
public sealed record FixDatConsoleSummary(string ConsoleKey, int MissingGames, int MissingRoms);

/// <summary>Generated FixDAT document and summary counters.</summary>
public sealed record FixDatResult(
    string DatName,
    int ConsoleCount,
    int MissingGames,
    int MissingRoms,
    string XmlContent,
    IReadOnlyList<FixDatConsoleSummary> Consoles);
