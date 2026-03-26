using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-035: Delegates to static FeatureService.Collection methods.</summary>
public sealed class CollectionService : ICollectionService
{
    public IReadOnlyList<RomCandidate> ApplyFilter(IReadOnlyList<RomCandidate> candidates, string field, string op, string value)
        => FeatureService.ApplyFilter(candidates, field, op, value);

    public string? BuildMissingRomReport(IReadOnlyList<RomCandidate> candidates, IReadOnlyList<string> roots)
        => FeatureService.BuildMissingRomReport(candidates, roots);

    public string BuildCrossRootReport(IReadOnlyList<DedupeGroup> dedupeGroups, IReadOnlyList<string> roots)
        => FeatureService.BuildCrossRootReport(dedupeGroups, roots);

    public (string Report, int Matched, int Unmatched) BuildCoverReport(string coverDir, IReadOnlyList<RomCandidate> candidates)
        => FeatureService.BuildCoverReport(coverDir, candidates);

    public (string Field, string Op, string Value)? ParseFilterExpression(string input)
        => FeatureService.ParseFilterExpression(input);

    public string BuildFilterReport(IReadOnlyList<RomCandidate> candidates, string field, string op, string value)
        => FeatureService.BuildFilterReport(candidates, field, op, value);
}
