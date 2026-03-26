using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-035: ROM collection filtering, searching, cross-root analysis.</summary>
public interface ICollectionService
{
    IReadOnlyList<RomCandidate> ApplyFilter(IReadOnlyList<RomCandidate> candidates, string field, string op, string value);
    string? BuildMissingRomReport(IReadOnlyList<RomCandidate> candidates, IReadOnlyList<string> roots);
    string BuildCrossRootReport(IReadOnlyList<DedupeGroup> dedupeGroups, IReadOnlyList<string> roots);
    (string Report, int Matched, int Unmatched) BuildCoverReport(string coverDir, IReadOnlyList<RomCandidate> candidates);
    (string Field, string Op, string Value)? ParseFilterExpression(string input);
    string BuildFilterReport(IReadOnlyList<RomCandidate> candidates, string field, string op, string value);
}
