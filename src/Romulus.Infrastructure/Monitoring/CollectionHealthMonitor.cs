using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Scoring;
using Romulus.Infrastructure.Analysis;

namespace Romulus.Infrastructure.Monitoring;

/// <summary>
/// Composes HealthScorer + IntegrityService + CollectionIndex into a unified health report.
/// </summary>
public sealed class CollectionHealthMonitor
{
    private readonly ICollectionIndex? _collectionIndex;
    private readonly IFileSystem? _fileSystem;

    public CollectionHealthMonitor(ICollectionIndex? collectionIndex = null, IFileSystem? fileSystem = null)
    {
        _collectionIndex = collectionIndex;
        _fileSystem = fileSystem;
    }

    public async Task<CollectionHealthReport> GenerateReportAsync(
        IReadOnlyList<string>? roots = null,
        IReadOnlyCollection<string>? extensions = null,
        string? consoleFilter = null,
        CancellationToken ct = default)
    {
        var entries = await LoadEntriesAsync(roots, extensions, consoleFilter, ct).ConfigureAwait(false);

        var totalFiles = entries.Count;
        var games = entries.Count(e => e.Category == FileCategory.Game);
        var junk = entries.Count(e => e.Category == FileCategory.Junk);
        var datVerified = entries.Count(e => e.DatMatch);
        // Duplicates require dedup groups; index-only health omits duplicate penalty.
        var duplicates = 0;
        var errors = 0;

        var score = HealthScorer.GetHealthScore(totalFiles, duplicates, junk, datVerified, errors);
        var grade = ScoreToGrade(score);

        var integrity = await CheckIntegrityAsync(ct).ConfigureAwait(false);

        var breakdown = new CollectionHealthBreakdown
        {
            TotalFiles = totalFiles,
            Games = games,
            Duplicates = duplicates,
            Junk = junk,
            DatVerified = datVerified,
            Errors = errors,
            DuplicatePercent = totalFiles > 0 ? 100.0 * duplicates / totalFiles : 0,
            JunkPercent = totalFiles > 0 ? 100.0 * junk / totalFiles : 0,
            VerifiedPercent = totalFiles > 0 ? 100.0 * datVerified / totalFiles : 0
        };

        return new CollectionHealthReport
        {
            HealthScore = score,
            Grade = grade,
            Breakdown = breakdown,
            Integrity = integrity,
            GeneratedUtc = DateTime.UtcNow,
            ConsoleFilter = consoleFilter
        };
    }

    public static CollectionHealthReport GenerateFromCandidates(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyList<DedupeGroup>? dedupeGroups = null,
        string? consoleFilter = null)
    {
        ArgumentNullException.ThrowIfNull(candidates);

        var filtered = string.IsNullOrWhiteSpace(consoleFilter)
            ? candidates
            : candidates.Where(c => string.Equals(c.ConsoleKey, consoleFilter, StringComparison.OrdinalIgnoreCase)).ToList();

        var totalFiles = filtered.Count;
        var games = filtered.Count(c => c.Category == FileCategory.Game);
        var junk = filtered.Count(c => c.Category == FileCategory.Junk);
        var datVerified = filtered.Count(c => c.DatMatch);
        var duplicates = dedupeGroups?.Sum(g =>
            string.IsNullOrWhiteSpace(consoleFilter)
                ? g.Losers.Count
                : g.Losers.Count(l => string.Equals(l.ConsoleKey, consoleFilter, StringComparison.OrdinalIgnoreCase)))
            ?? 0;

        var score = HealthScorer.GetHealthScore(totalFiles, duplicates, junk, datVerified);
        var grade = ScoreToGrade(score);

        return new CollectionHealthReport
        {
            HealthScore = score,
            Grade = grade,
            Breakdown = new CollectionHealthBreakdown
            {
                TotalFiles = totalFiles,
                Games = games,
                Duplicates = duplicates,
                Junk = junk,
                DatVerified = datVerified,
                Errors = 0,
                DuplicatePercent = totalFiles > 0 ? 100.0 * duplicates / totalFiles : 0,
                JunkPercent = totalFiles > 0 ? 100.0 * junk / totalFiles : 0,
                VerifiedPercent = totalFiles > 0 ? 100.0 * datVerified / totalFiles : 0
            },
            Integrity = new CollectionHealthIntegrity
            {
                HasBaseline = false,
                IntactCount = 0,
                ChangedCount = 0,
                MissingCount = 0,
                BitRotRisk = false
            },
            GeneratedUtc = DateTime.UtcNow,
            ConsoleFilter = consoleFilter
        };
    }

    internal static string ScoreToGrade(int score) => score switch
    {
        >= 90 => "A",
        >= 80 => "B",
        >= 70 => "C",
        >= 50 => "D",
        _ => "F"
    };

    private async Task<IReadOnlyList<CollectionIndexEntry>> LoadEntriesAsync(
        IReadOnlyList<string>? roots,
        IReadOnlyCollection<string>? extensions,
        string? consoleFilter,
        CancellationToken ct)
    {
        if (_collectionIndex is null)
            return [];

        if (!string.IsNullOrWhiteSpace(consoleFilter))
            return await _collectionIndex.ListByConsoleAsync(consoleFilter, ct).ConfigureAwait(false);

        if (roots is { Count: > 0 })
        {
            extensions ??= RunOptions.DefaultExtensions;
            return await _collectionIndex.ListEntriesInScopeAsync(roots, extensions, ct).ConfigureAwait(false);
        }

        return [];
    }

    private static async Task<CollectionHealthIntegrity> CheckIntegrityAsync(CancellationToken ct)
    {
        try
        {
            var result = await IntegrityService.CheckIntegrity(ct: ct).ConfigureAwait(false);
            return new CollectionHealthIntegrity
            {
                HasBaseline = true,
                IntactCount = result.Intact.Count,
                ChangedCount = result.Changed.Count,
                MissingCount = result.Missing.Count,
                BitRotRisk = result.BitRotRisk,
                LastCheckedUtc = DateTime.UtcNow
            };
        }
        catch
        {
            return new CollectionHealthIntegrity
            {
                HasBaseline = false,
                IntactCount = 0,
                ChangedCount = 0,
                MissingCount = 0,
                BitRotRisk = false
            };
        }
    }
}
