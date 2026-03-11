using System.Globalization;
using System.Text;
using System.Text.Json;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Deduplication;
using RomCleanup.Core.GameKeys;
using RomCleanup.Core.Scoring;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Infrastructure.Analytics;

/// <summary>
/// Analytics and diagnostics engine.
/// Port of RunHelpers.Insights.ps1 — DuplicateInspector, HealthDashboard,
/// DatCoverageHeatmap, CrossCollectionHints, IncrementalDelta.
/// </summary>
public sealed class InsightsEngine
{
    private readonly IFileSystem _fs;
    private readonly Action<string>? _log;

    public InsightsEngine(IFileSystem fs, Action<string>? log = null)
    {
        _fs = fs;
        _log = log;
    }

    /// <summary>
    /// Build duplicate inspector rows with full scoring breakdown.
    /// Port of Get-DuplicateInspectorRows.
    /// </summary>
    public IReadOnlyList<DuplicateInspectorRow> GetDuplicateInspectorRows(
        IReadOnlyList<string> roots,
        IReadOnlyList<string> extensions,
        string[] preferRegions,
        bool aliasEditionKeying = false,
        int maxGroups = 250,
        IReadOnlyList<string>? excludedPaths = null)
    {
        var versionScorer = new VersionScorer();
        var allFiles = new List<string>();

        foreach (var root in roots)
        {
            var files = _fs.GetFilesSafe(root, extensions);
            if (excludedPaths is not null)
                files = files.Where(f => !excludedPaths.Any(ex =>
                    f.StartsWith(ex, StringComparison.OrdinalIgnoreCase))).ToList();
            allFiles.AddRange(files);
        }

        // Group by game key
        var groups = allFiles
            .Select(f => new
            {
                Path = f,
                FileName = Path.GetFileName(f),
                GameKey = GameKeyNormalizer.Normalize(Path.GetFileName(f))
            })
            .Where(x => !string.IsNullOrWhiteSpace(x.GameKey))
            .GroupBy(x => x.GameKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Count() > 1)
            .Take(maxGroups)
            .ToList();

        var rows = new List<DuplicateInspectorRow>();

        foreach (var group in groups)
        {
            // Build candidates for dedup scoring  
            var candidates = group.Select(item =>
            {
                var ext = Path.GetExtension(item.FileName).ToLowerInvariant();
                var region = Core.Regions.RegionDetector.GetRegionTag(item.FileName);
                var regionScore = FormatScorer.GetRegionScore(region, preferRegions);
                var formatScore = FormatScorer.GetFormatScore(ext);
                var verScore = (int)versionScorer.GetVersionScore(item.FileName);
                long sizeBytes = 0;
                try { sizeBytes = new FileInfo(item.Path).Length; } catch { }

                return new
                {
                    item.Path,
                    item.FileName,
                    item.GameKey,
                    Region = region,
                    RegionScore = regionScore,
                    FormatScore = formatScore,
                    VersionScore = verScore,
                    SizeBytes = sizeBytes,
                    Extension = ext,
                    TotalScore = regionScore + formatScore + verScore
                };
            }).OrderByDescending(c => c.TotalScore)
              .ThenByDescending(c => c.SizeBytes)
              .ToList();

            var winnerPath = candidates[0].Path;

            foreach (var c in candidates)
            {
                bool isWinner = c.Path == winnerPath;
                rows.Add(new DuplicateInspectorRow
                {
                    GameKey = c.GameKey,
                    Winner = isWinner,
                    WinnerSource = isWinner ? "AUTO" : "",
                    Region = c.Region,
                    Type = c.Extension,
                    SizeMB = Math.Round(c.SizeBytes / 1048576.0, 2),
                    RegionScore = c.RegionScore,
                    FormatScore = c.FormatScore,
                    VersionScore = c.VersionScore,
                    TotalScore = c.TotalScore,
                    ScoreBreakdown = $"R:{c.RegionScore} F:{c.FormatScore} V:{c.VersionScore}",
                    MainPath = c.Path
                });
            }
        }

        return rows;
    }

    /// <summary>
    /// Build collection health rows from a run result.
    /// Port of Get-CollectionHealthRows.
    /// </summary>
    public static IReadOnlyList<CollectionHealthRow> GetCollectionHealthRows(
        RunResult result,
        string filterText = "")
    {
        if (result.AllCandidates.Count == 0)
            return [];

        var byConsole = result.AllCandidates
            .GroupBy(c => c.Type ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase);

        var rows = new List<CollectionHealthRow>();

        foreach (var group in byConsole)
        {
            if (!string.IsNullOrWhiteSpace(filterText) &&
                !group.Key.Contains(filterText, StringComparison.OrdinalIgnoreCase))
                continue;

            var items = group.ToList();
            var dupes = result.DedupeGroups
                .Where(g => g.Winner.Type == group.Key)
                .Sum(g => g.Losers.Count);

            var formats = items
                .Select(i => i.Extension)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(e => e)
                .ToList();

            rows.Add(new CollectionHealthRow
            {
                Console = group.Key,
                Roms = items.Count,
                Duplicates = dupes,
                MissingDat = items.Count(i => !i.DatMatch),
                Formats = string.Join(", ", formats)
            });
        }

        return rows.OrderByDescending(r => r.Roms).ToList();
    }

    /// <summary>
    /// Compute incremental dry-run delta vs. a previous snapshot.
    /// Port of Get-IncrementalDryRunDelta.
    /// </summary>
    public IncrementalDelta GetIncrementalDelta(
        RunResult result,
        string? snapshotPath = null,
        int maxSamples = 12)
    {
        var currentKeys = new HashSet<string>(
            result.DedupeGroups.SelectMany(g => g.Losers.Select(l => l.MainPath)),
            StringComparer.OrdinalIgnoreCase);

        var previousKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        DateTime? previousTimestamp = null;

        // Load previous snapshot if exists
        if (snapshotPath is not null && File.Exists(snapshotPath))
        {
            try
            {
                var json = File.ReadAllText(snapshotPath, Encoding.UTF8);
                var snapshot = JsonSerializer.Deserialize<SnapshotData>(json);
                if (snapshot is not null)
                {
                    previousKeys = new HashSet<string>(snapshot.LoserPaths ?? [], StringComparer.OrdinalIgnoreCase);
                    previousTimestamp = snapshot.Timestamp;
                }
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Failed to load snapshot: {ex.Message}");
            }
        }

        var added = currentKeys.Except(previousKeys).ToList();
        var resolved = previousKeys.Except(currentKeys).ToList();

        // Save current snapshot
        if (snapshotPath is not null)
        {
            try
            {
                var dir = Path.GetDirectoryName(snapshotPath);
                if (dir is not null) _fs.EnsureDirectory(dir);

                var snapshot = new SnapshotData
                {
                    Timestamp = DateTime.UtcNow,
                    LoserPaths = currentKeys.ToList()
                };
                var json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(snapshotPath, json, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Failed to save snapshot: {ex.Message}");
            }
        }

        return new IncrementalDelta
        {
            SnapshotPath = snapshotPath,
            PreviousTimestampUtc = previousTimestamp,
            CurrentCount = currentKeys.Count,
            PreviousCount = previousKeys.Count,
            AddedCount = added.Count,
            ResolvedCount = resolved.Count,
            AddedSamples = added.Take(maxSamples).ToList(),
            ResolvedSamples = resolved.Take(maxSamples).ToList()
        };
    }

    /// <summary>
    /// Build DAT coverage heatmap rows.
    /// Port of Get-DatCoverageHeatmapRows.
    /// </summary>
    public static IReadOnlyList<DatCoverageRow> GetDatCoverageHeatmap(
        RunResult result,
        int top = 16)
    {
        if (result.AllCandidates.Count == 0)
            return [];

        var byConsole = result.AllCandidates
            .GroupBy(c => c.Type ?? "UNKNOWN", StringComparer.OrdinalIgnoreCase);

        var rows = new List<DatCoverageRow>();

        foreach (var group in byConsole)
        {
            var items = group.ToList();
            int matched = items.Count(i => i.DatMatch);
            int total = items.Count;
            double coverage = total > 0 ? (double)matched / total * 100 : 0;

            // Build heat bar (visual: █ for coverage, ░ for missing)
            int barLen = 10;
            int filled = (int)Math.Round(coverage / 100 * barLen);
            var heat = new string('█', filled) + new string('░', barLen - filled);

            rows.Add(new DatCoverageRow
            {
                Console = group.Key,
                Matched = matched,
                Expected = total,
                Missing = total - matched,
                Coverage = Math.Round(coverage, 1),
                Heat = heat
            });
        }

        return rows
            .OrderByDescending(r => r.Expected)
            .Take(top)
            .ToList();
    }

    /// <summary>
    /// Find cross-collection duplicate hints (same GameKey across multiple roots).
    /// Port of Get-CrossCollectionDedupHints.
    /// </summary>
    public IReadOnlyList<CrossCollectionHint> GetCrossCollectionHints(
        IReadOnlyList<string> roots,
        IReadOnlyList<string> extensions,
        int top = 40,
        IReadOnlyList<string>? excludedPaths = null)
    {
        var allEntries = new List<(string GameKey, string Path, string Root)>();

        foreach (var root in roots)
        {
            var files = _fs.GetFilesSafe(root, extensions);
            if (excludedPaths is not null)
                files = files.Where(f => !excludedPaths.Any(ex =>
                    f.StartsWith(ex, StringComparison.OrdinalIgnoreCase))).ToList();

            foreach (var file in files)
            {
                var gameKey = GameKeyNormalizer.Normalize(Path.GetFileName(file));
                if (!string.IsNullOrWhiteSpace(gameKey))
                    allEntries.Add((gameKey, file, root));
            }
        }

        // Find game keys present in multiple roots
        var crossRoot = allEntries
            .GroupBy(e => e.GameKey, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Root).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            .OrderByDescending(g => g.Count())
            .Take(top)
            .Select(g =>
            {
                var distinctRoots = g.Select(x => x.Root).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                return new CrossCollectionHint
                {
                    GameKey = g.Key,
                    RootCount = distinctRoots.Count,
                    CandidateCount = g.Count(),
                    WinnerPath = g.First().Path,
                    Roots = distinctRoots,
                    RootsSummary = string.Join(", ", distinctRoots.Select(r => Path.GetFileName(r)))
                };
            })
            .ToList();

        return crossRoot;
    }

    /// <summary>
    /// Export duplicate inspector rows to CSV with injection protection.
    /// Port of Export-DuplicateInspectorCsv.
    /// </summary>
    public static void ExportInspectorCsv(IReadOnlyList<DuplicateInspectorRow> rows, string path)
    {
        var sb = new StringBuilder();
        sb.AppendLine("GameKey,Winner,Region,Type,SizeMB,RegionScore,FormatScore,VersionScore,TotalScore,ScoreBreakdown,MainPath");

        foreach (var row in rows)
        {
            sb.Append(SanitizeCsv(row.GameKey)).Append(',');
            sb.Append(row.Winner ? "YES" : "").Append(',');
            sb.Append(SanitizeCsv(row.Region)).Append(',');
            sb.Append(SanitizeCsv(row.Type)).Append(',');
            sb.Append(row.SizeMB.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(row.RegionScore).Append(',');
            sb.Append(row.FormatScore).Append(',');
            sb.Append(row.VersionScore).Append(',');
            sb.Append(row.TotalScore).Append(',');
            sb.Append(SanitizeCsv(row.ScoreBreakdown)).Append(',');
            sb.AppendLine(SanitizeCsv(row.MainPath));
        }

        File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
    }

    private static string SanitizeCsv(string value)
    {
        if (string.IsNullOrEmpty(value)) return "";
        // CSV injection prevention
        if (value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        return value;
    }

    private sealed class SnapshotData
    {
        public DateTime Timestamp { get; set; }
        public List<string> LoserPaths { get; set; } = [];
    }
}

/// <summary>
/// A hint about duplicate ROMs across multiple collection roots.
/// </summary>
public sealed class CrossCollectionHint
{
    public string GameKey { get; init; } = "";
    public int RootCount { get; init; }
    public int CandidateCount { get; init; }
    public string WinnerPath { get; init; } = "";
    public IReadOnlyList<string> Roots { get; init; } = [];
    public string RootsSummary { get; init; } = "";
}
