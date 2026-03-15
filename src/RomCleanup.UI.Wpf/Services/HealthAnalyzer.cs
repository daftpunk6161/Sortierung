using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-034: Delegates to static FeatureService.Analysis methods.</summary>
public sealed class HealthAnalyzer : IHealthAnalyzer
{
    public int CalculateHealthScore(int totalFiles, int dupes, int junk, int verified)
        => FeatureService.CalculateHealthScore(totalFiles, dupes, junk, verified);

    public List<HeatmapEntry> GetDuplicateHeatmap(IReadOnlyList<DedupeResult> groups)
        => FeatureService.GetDuplicateHeatmap(groups);

    public List<DuplicateSourceEntry> GetDuplicateInspector(string? auditPath)
        => FeatureService.GetDuplicateInspector(auditPath);

    public List<RomCandidate> SearchRomCollection(IReadOnlyList<RomCandidate> candidates, string searchText)
        => FeatureService.SearchRomCollection(candidates, searchText);

    public string AnalyzeStorageTiers(IReadOnlyList<RomCandidate> candidates, int hotThresholdDays = 30)
        => FeatureService.AnalyzeStorageTiers(candidates, hotThresholdDays);

    public string GetHardlinkEstimate(IReadOnlyList<DedupeResult> groups)
        => FeatureService.GetHardlinkEstimate(groups);

    public string GetNasInfo(IReadOnlyList<string> roots)
        => FeatureService.GetNasInfo(roots);

    public string BuildCloneTree(IReadOnlyList<DedupeResult> groups)
        => FeatureService.BuildCloneTree(groups);

    public string BuildVirtualFolderPreview(IReadOnlyList<RomCandidate> candidates)
        => FeatureService.BuildVirtualFolderPreview(candidates);

    public string BuildSplitPanelPreview(IReadOnlyList<DedupeResult> groups)
        => FeatureService.BuildSplitPanelPreview(groups);

    public string ClassifyGenre(string gameName)
        => FeatureService.ClassifyGenre(gameName);

    public List<(string key, string name, string shortcut, int score)> SearchCommands(string query)
        => FeatureService.SearchCommands(query);

    public string ExportRetroArchPlaylist(IReadOnlyList<RomCandidate> winners, string playlistName)
        => FeatureService.ExportRetroArchPlaylist(winners, playlistName);

    public string DetectAutoProfile(IReadOnlyList<string> roots)
        => FeatureService.DetectAutoProfile(roots);

    public string BuildPlaytimeReport(string directory)
        => FeatureService.BuildPlaytimeReport(directory);

    public string BuildCollectionManagerReport(IReadOnlyList<RomCandidate> candidates)
        => FeatureService.BuildCollectionManagerReport(candidates);

    public string BuildCommandPaletteReport(string input, IReadOnlyList<(string key, string name, string shortcut, int score)> results)
        => FeatureService.BuildCommandPaletteReport(input, results);
}
