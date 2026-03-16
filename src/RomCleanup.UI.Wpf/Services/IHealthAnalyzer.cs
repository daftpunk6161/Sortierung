using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-034: Health score, heatmaps, trends, duplicate analysis.</summary>
public interface IHealthAnalyzer
{
    int CalculateHealthScore(int totalFiles, int dupes, int junk, int verified);
    List<HeatmapEntry> GetDuplicateHeatmap(IReadOnlyList<DedupeResult> groups);
    List<DuplicateSourceEntry> GetDuplicateInspector(string? auditPath);
    List<RomCandidate> SearchRomCollection(IReadOnlyList<RomCandidate> candidates, string searchText);
    string AnalyzeStorageTiers(IReadOnlyList<RomCandidate> candidates, int hotThresholdDays = 30);
    string GetHardlinkEstimate(IReadOnlyList<DedupeResult> groups);
    string GetNasInfo(IReadOnlyList<string> roots);
    string BuildCloneTree(IReadOnlyList<DedupeResult> groups);
    string BuildVirtualFolderPreview(IReadOnlyList<RomCandidate> candidates);
    string BuildSplitPanelPreview(IReadOnlyList<DedupeResult> groups);
    string ClassifyGenre(string gameName);
    List<(string key, string name, string shortcut, int score)> SearchCommands(string query);
    string ExportRetroArchPlaylist(IReadOnlyList<RomCandidate> winners, string playlistName);
    string DetectAutoProfile(IReadOnlyList<string> roots);
    string BuildPlaytimeReport(string directory);
    string BuildCollectionManagerReport(IReadOnlyList<RomCandidate> candidates);
    string BuildCommandPaletteReport(string input, IReadOnlyList<(string key, string name, string shortcut, int score)> results);
}
