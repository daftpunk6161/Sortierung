using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-039: Delegates to static FeatureService.Security methods.</summary>
public sealed class HeaderSecurityService : IHeaderService
{
    public RomHeaderInfo? AnalyzeHeader(string filePath)
        => FeatureService.AnalyzeHeader(filePath);

    public void SaveTrendSnapshot(int totalFiles, long sizeBytes, int verified, int dupes, int junk)
        => FeatureService.SaveTrendSnapshot(totalFiles, sizeBytes, verified, dupes, junk);

    public List<TrendSnapshot> LoadTrendHistory()
        => FeatureService.LoadTrendHistory();

    public string FormatTrendReport(List<TrendSnapshot> history)
        => FeatureService.FormatTrendReport(history);

    public Task<Dictionary<string, IntegrityEntry>> CreateBaseline(IReadOnlyList<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default)
        => FeatureService.CreateBaseline(filePaths, progress, ct);

    public Task<IntegrityCheckResult> CheckIntegrity(IProgress<string>? progress = null, CancellationToken ct = default)
        => FeatureService.CheckIntegrity(progress, ct);

    public string CreateBackup(IReadOnlyList<string> filePaths, string backupRoot, string label)
        => FeatureService.CreateBackup(filePaths, backupRoot, label);

    public string? FindCommonRoot(IReadOnlyList<string> paths)
        => FeatureService.FindCommonRoot(paths);

    public int CleanupOldBackups(string backupRoot, int retentionDays, Func<int, bool>? confirmDelete = null)
        => FeatureService.CleanupOldBackups(backupRoot, retentionDays, confirmDelete);

    public string? DetectPatchFormat(string patchPath)
        => FeatureService.DetectPatchFormat(patchPath);

    public bool RepairNesHeader(string path)
        => FeatureService.RepairNesHeader(path);

    public bool RemoveCopierHeader(string path)
        => FeatureService.RemoveCopierHeader(path);
}
