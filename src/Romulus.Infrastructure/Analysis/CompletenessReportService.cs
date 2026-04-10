using System.Text;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Audit;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Infrastructure.Analysis;

/// <summary>
/// Completeness report per console: compares DAT index entries against
/// files found in the collection roots to show coverage percentage.
/// </summary>
public static class CompletenessReportService
{
    public static async ValueTask<CompletenessReport> BuildAsync(
        DatIndex datIndex,
        IReadOnlyList<string> roots,
        ICollectionIndex? collectionIndex = null,
        IReadOnlyCollection<string>? extensions = null,
        IReadOnlyList<RomCandidate>? candidates = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(datIndex);
        ArgumentNullException.ThrowIfNull(roots);

        if (candidates is not null)
            return BuildFromCandidates(datIndex, candidates);

        var normalizedExtensions = NormalizeExtensions(extensions);
        if (collectionIndex is not null)
        {
            var indexEntries = await collectionIndex.ListEntriesInScopeAsync(roots, normalizedExtensions, ct).ConfigureAwait(false);
            if (indexEntries.Count > 0)
                return BuildFromCollectionIndex(datIndex, indexEntries);
        }

        return BuildFromFileSystem(datIndex, roots, normalizedExtensions);
    }

    /// <summary>
    /// Build completeness report comparing DAT entries against files in roots.
    /// </summary>
    public static CompletenessReport Build(DatIndex datIndex, IReadOnlyList<string> roots)
    {
        return BuildFromFileSystem(datIndex, roots, allowedExtensions: null);
    }

    /// <summary>
    /// Format completeness report as human-readable text.
    /// </summary>
    public static string FormatReport(CompletenessReport report)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Collection Completeness Report");
        sb.AppendLine(new string('=', 60));
        sb.AppendLine();
        sb.AppendLine($"  Source: {report.Source}");
        sb.AppendLine($"  Source items: {report.SourceItemCount}");

        if (report.Entries.Count == 0)
        {
            sb.AppendLine("\n  No DAT data available. Enable DAT verification first.");
            return sb.ToString();
        }

        sb.AppendLine($"\n  {"Console",-20} {"In DAT",8} {"Have",8} {"Missing",8} {"Complete",10}");
        sb.AppendLine($"  {new string('-', 20)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 10)}");

        foreach (var entry in report.Entries.OrderByDescending(e => e.Percentage))
        {
            var bar = entry.Percentage >= 100 ? "[FULL]" : $"{entry.Percentage,5:F1}%";
            sb.AppendLine($"  {entry.ConsoleKey,-20} {entry.TotalInDat,8} {entry.Verified,8} {entry.MissingCount,8} {bar,10}");
        }

        var totalDat = report.Entries.Sum(e => e.TotalInDat);
        var totalHave = report.Entries.Sum(e => e.Verified);
        var totalMissing = report.Entries.Sum(e => e.MissingCount);
        var overallPct = totalDat > 0 ? Math.Round(100.0 * totalHave / totalDat, 1) : 0.0;

        sb.AppendLine($"  {new string('-', 20)} {new string('-', 8)} {new string('-', 8)} {new string('-', 8)} {new string('-', 10)}");
        sb.AppendLine($"  {"TOTAL",-20} {totalDat,8} {totalHave,8} {totalMissing,8} {overallPct,5:F1}%");

        return sb.ToString();
    }

    private static CompletenessReport BuildFromCandidates(DatIndex datIndex, IReadOnlyList<RomCandidate> candidates)
    {
        var ownedGamesByConsole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            if (!TryResolveOwnedDatGame(
                datIndex,
                candidate.ConsoleKey,
                Path.GetFileName(candidate.MainPath),
                candidate.Hash,
                candidate.HeaderlessHash,
                candidate.DatGameName,
                out var resolvedConsoleKey,
                out var resolvedGameName))
            {
                continue;
            }

            AddOwnedGame(ownedGamesByConsole, resolvedConsoleKey, resolvedGameName);
        }

        return BuildReport(datIndex, ownedGamesByConsole, CompletenessSources.RunCandidates, candidates.Count);
    }

    private static CompletenessReport BuildFromCollectionIndex(DatIndex datIndex, IReadOnlyList<CollectionIndexEntry> indexEntries)
    {
        var ownedGamesByConsole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in indexEntries)
        {
            if (!TryResolveOwnedDatGame(
                datIndex,
                entry.ConsoleKey,
                entry.FileName,
                entry.PrimaryHash,
                entry.HeaderlessHash,
                entry.DatGameName,
                out var resolvedConsoleKey,
                out var resolvedGameName))
            {
                continue;
            }

            AddOwnedGame(ownedGamesByConsole, resolvedConsoleKey, resolvedGameName);
        }

        return BuildReport(datIndex, ownedGamesByConsole, CompletenessSources.CollectionIndex, indexEntries.Count);
    }

    private static CompletenessReport BuildFromFileSystem(
        DatIndex datIndex,
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string>? allowedExtensions)
    {
        var filesByConsole = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        var scannedFiles = 0;

        foreach (var root in roots)
        {
            if (!Directory.Exists(root))
                continue;

            foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
            {
                if (!ShouldIncludeFile(file, allowedExtensions))
                    continue;

                scannedFiles++;
                var consoleKey = CollectionAnalysisService.DetectConsoleFromPath(file);
                if (!filesByConsole.TryGetValue(consoleKey, out var set))
                {
                    set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    filesByConsole[consoleKey] = set;
                }

                set.Add(Path.GetFileNameWithoutExtension(file));
            }
        }

        return BuildReport(datIndex, filesByConsole, CompletenessSources.FilesystemFallback, scannedFiles);
    }

    private static CompletenessReport BuildReport(
        DatIndex datIndex,
        IReadOnlyDictionary<string, HashSet<string>> ownedGamesByConsole,
        string source,
        int sourceItemCount)
    {
        var entries = new List<CompletenessEntry>();

        foreach (var consoleKey in datIndex.ConsoleKeys.OrderBy(k => k, StringComparer.OrdinalIgnoreCase))
        {
            var datEntries = datIndex.GetConsoleEntries(consoleKey);
            if (datEntries is null || datEntries.Count == 0)
                continue;

            var datGameNames = new HashSet<string>(
                datEntries.Values.Distinct(StringComparer.OrdinalIgnoreCase),
                StringComparer.OrdinalIgnoreCase);

            var ownedGames = ownedGamesByConsole.TryGetValue(consoleKey, out var owned)
                ? owned
                : EmptyOwnedGames;

            var verified = 0;
            var missing = new List<string>();

            foreach (var gameName in datGameNames.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
            {
                if (ownedGames.Contains(gameName))
                    verified++;
                else
                    missing.Add(gameName);
            }

            var totalInDat = datGameNames.Count;
            var percentage = totalInDat > 0
                ? Math.Round(100.0 * verified / totalInDat, 1)
                : 0.0;

            entries.Add(new CompletenessEntry(
                consoleKey, totalInDat, verified, missing.Count, percentage, missing));
        }

        return new CompletenessReport(entries, source, sourceItemCount);
    }

    private static IReadOnlyCollection<string> NormalizeExtensions(IReadOnlyCollection<string>? extensions)
    {
        var source = extensions is { Count: > 0 }
            ? extensions
            : RunOptions.DefaultExtensions;

        return source
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Select(static extension =>
            {
                var trimmed = extension.Trim();
                return trimmed.StartsWith('.')
                    ? trimmed.ToLowerInvariant()
                    : "." + trimmed.ToLowerInvariant();
            })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool ShouldIncludeFile(string filePath, IReadOnlyCollection<string>? allowedExtensions)
    {
        if (allowedExtensions is null || allowedExtensions.Count == 0)
            return true;

        var extension = Path.GetExtension(filePath);
        return allowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase);
    }

    private static void AddOwnedGame(
        IDictionary<string, HashSet<string>> ownedGamesByConsole,
        string consoleKey,
        string gameName)
    {
        if (!ownedGamesByConsole.TryGetValue(consoleKey, out var set))
        {
            set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            ownedGamesByConsole[consoleKey] = set;
        }

        set.Add(gameName);
    }

    private static bool TryResolveOwnedDatGame(
        DatIndex datIndex,
        string? consoleKey,
        string fileName,
        string? hash,
        string? headerlessHash,
        string? storedDatGameName,
        out string resolvedConsoleKey,
        out string resolvedGameName)
    {
        if (TryResolveByName(datIndex, consoleKey, storedDatGameName, out resolvedConsoleKey, out resolvedGameName))
            return true;

        var auditResult = DatAuditClassifier.ClassifyFull(hash, headerlessHash, fileName, consoleKey, datIndex);
        if (auditResult.Status is DatAuditStatus.Have or DatAuditStatus.HaveWrongName &&
            IsRealConsoleKey(auditResult.ResolvedConsoleKey) &&
            !string.IsNullOrWhiteSpace(auditResult.DatGameName))
        {
            resolvedConsoleKey = auditResult.ResolvedConsoleKey!;
            resolvedGameName = auditResult.DatGameName!;
            return true;
        }

        return TryResolveByName(
            datIndex,
            consoleKey,
            Path.GetFileNameWithoutExtension(fileName),
            out resolvedConsoleKey,
            out resolvedGameName);
    }

    private static bool TryResolveByName(
        DatIndex datIndex,
        string? consoleKey,
        string? gameName,
        out string resolvedConsoleKey,
        out string resolvedGameName)
    {
        resolvedConsoleKey = string.Empty;
        resolvedGameName = string.Empty;

        if (string.IsNullOrWhiteSpace(gameName))
            return false;

        if (IsRealConsoleKey(consoleKey))
        {
            var byConsole = datIndex.LookupByName(consoleKey!, gameName);
            if (byConsole is null)
                return false;

            resolvedConsoleKey = consoleKey!;
            resolvedGameName = byConsole.Value.GameName;
            return true;
        }

        var crossConsoleMatches = datIndex.LookupAllByName(gameName);
        if (crossConsoleMatches.Count != 1)
            return false;

        resolvedConsoleKey = crossConsoleMatches[0].ConsoleKey;
        resolvedGameName = crossConsoleMatches[0].Entry.GameName;
        return true;
    }

    private static bool IsRealConsoleKey(string? consoleKey)
        => !string.IsNullOrWhiteSpace(consoleKey)
           && !string.Equals(consoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)
           && !string.Equals(consoleKey, "AMBIGUOUS", StringComparison.OrdinalIgnoreCase);

    private static readonly HashSet<string> EmptyOwnedGames = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// Completeness report for the entire collection.
/// </summary>
public sealed record CompletenessReport(
    IReadOnlyList<CompletenessEntry> Entries,
    string Source = CompletenessSources.FilesystemFallback,
    int SourceItemCount = 0);

public static class CompletenessSources
{
    public const string RunCandidates = "run-candidates";
    public const string CollectionIndex = "collection-index";
    public const string FilesystemFallback = "filesystem-fallback";
}

/// <summary>
/// Completeness per console: how many DAT entries are present in the collection.
/// </summary>
public sealed record CompletenessEntry(
    string ConsoleKey,
    int TotalInDat,
    int Verified,
    int MissingCount,
    double Percentage,
    IReadOnlyList<string> MissingGames);
