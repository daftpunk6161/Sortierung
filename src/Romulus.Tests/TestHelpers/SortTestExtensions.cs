using Romulus.Contracts.Models;
using Romulus.Infrastructure.Sorting;

namespace Romulus.Tests.TestHelpers;

/// <summary>
/// Test-only adapter for ConsoleSorter that derives a default
/// SortDecision="Sort" map from the provided enriched-console keys.
/// Tests that historically relied on the legacy fail-open (no decision dict
/// → implicit Sort) should call this helper. The production contract requires
/// callers to pass enrichedSortDecisions explicitly; this helper makes the
/// test intent ("treat every enriched file as Sort") explicit and traceable.
/// </summary>
internal static class SortTestExtensions
{
    public static ConsoleSortResult SortWithAutoSortDecisions(
        this ConsoleSorter sorter,
        IReadOnlyList<string> roots,
        IEnumerable<string>? extensions = null,
        bool dryRun = true,
        CancellationToken cancellationToken = default,
        IReadOnlyDictionary<string, string>? enrichedConsoleKeys = null,
        IReadOnlyDictionary<string, string>? enrichedSortReasons = null,
        IReadOnlyDictionary<string, string>? enrichedCategories = null,
        IReadOnlyList<string>? candidatePaths = null,
        string? conflictPolicy = null)
    {
        var decisions = enrichedConsoleKeys is null ? null : AsAllSortDecisions(enrichedConsoleKeys);
        return sorter.Sort(
            roots: roots,
            extensions: extensions,
            dryRun: dryRun,
            cancellationToken: cancellationToken,
            enrichedConsoleKeys: enrichedConsoleKeys,
            enrichedSortDecisions: decisions,
            enrichedSortReasons: enrichedSortReasons,
            enrichedCategories: enrichedCategories,
            candidatePaths: candidatePaths,
            conflictPolicy: conflictPolicy);
    }

    public static IReadOnlyDictionary<string, string> AsAllSortDecisions(
        this IReadOnlyDictionary<string, string> enrichedConsoleKeys)
    {
        var decisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in enrichedConsoleKeys)
        {
            decisions[kv.Key] = "Sort";
        }
        return decisions;
    }
}
