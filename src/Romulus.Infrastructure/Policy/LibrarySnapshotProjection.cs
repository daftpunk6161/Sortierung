using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Policy;

public static class LibrarySnapshotProjection
{
    public static LibrarySnapshot FromCollectionIndex(
        IReadOnlyList<CollectionIndexEntry> entries,
        IReadOnlyList<string> roots,
        DateTime generatedUtc)
    {
        ArgumentNullException.ThrowIfNull(entries);
        ArgumentNullException.ThrowIfNull(roots);

        var projectedEntries = entries
            .Select(static entry => new LibrarySnapshotEntry
            {
                Path = entry.Path,
                Root = entry.Root,
                FileName = string.IsNullOrWhiteSpace(entry.FileName)
                    ? Path.GetFileName(entry.Path)
                    : entry.FileName,
                Extension = string.IsNullOrWhiteSpace(entry.Extension)
                    ? Path.GetExtension(entry.Path)
                    : entry.Extension,
                SizeBytes = entry.SizeBytes,
                ConsoleKey = entry.ConsoleKey,
                GameKey = entry.GameKey,
                Region = entry.Region,
                Category = entry.Category.ToString(),
                DatMatch = entry.DatMatch,
                DatGameName = entry.DatGameName,
                DecisionClass = entry.DecisionClass.ToString(),
                SortDecision = entry.SortDecision.ToString(),
                PrimaryHash = entry.PrimaryHash,
                PrimaryHashType = entry.PrimaryHashType
            })
            .OrderBy(static entry => entry.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new LibrarySnapshot
        {
            GeneratedUtc = generatedUtc,
            Roots = roots
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Select(static root => root.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(static root => root, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            Entries = projectedEntries,
            Summary = BuildSummary(projectedEntries)
        };
    }

    private static LibrarySnapshotSummary BuildSummary(IReadOnlyList<LibrarySnapshotEntry> entries)
    {
        return new LibrarySnapshotSummary
        {
            TotalEntries = entries.Count,
            TotalSizeBytes = entries.Sum(static entry => entry.SizeBytes),
            DatMatchedEntries = entries.Count(static entry => entry.DatMatch),
            UnknownConsoleEntries = entries.Count(static entry =>
                string.Equals(entry.ConsoleKey, "UNKNOWN", StringComparison.OrdinalIgnoreCase)),
            EntriesByConsole = entries
                .GroupBy(static entry => string.IsNullOrWhiteSpace(entry.ConsoleKey) ? "UNKNOWN" : entry.ConsoleKey, StringComparer.OrdinalIgnoreCase)
                .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal),
            EntriesByExtension = entries
                .GroupBy(static entry => NormalizeExtension(entry.Extension), StringComparer.OrdinalIgnoreCase)
                .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.Ordinal)
        };
    }

    private static string NormalizeExtension(string? extension)
    {
        var trimmed = (extension ?? "").Trim();
        if (trimmed.Length == 0)
            return "<none>";

        return (trimmed[0] == '.' ? trimmed : "." + trimmed).ToLowerInvariant();
    }
}
