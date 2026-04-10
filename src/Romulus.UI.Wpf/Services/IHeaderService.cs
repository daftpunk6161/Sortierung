using Romulus.Contracts.Models;

namespace Romulus.UI.Wpf.Services;

/// <summary>GUI-039: ROM header analysis, integrity monitoring, backup, patch, header repair.</summary>
public interface IHeaderService
{
    RomHeaderInfo? AnalyzeHeader(string filePath);
    void SaveTrendSnapshot(int totalFiles, long sizeBytes, int verified, int dupes, int junk);
    List<TrendSnapshot> LoadTrendHistory();
    string FormatTrendReport(List<TrendSnapshot> history);
    Task<Dictionary<string, IntegrityEntry>> CreateBaseline(IReadOnlyList<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default);
    Task<IntegrityCheckResult> CheckIntegrity(IProgress<string>? progress = null, CancellationToken ct = default);
    string CreateBackup(IReadOnlyList<string> filePaths, string backupRoot, string label);
    string? FindCommonRoot(IReadOnlyList<string> paths);
    int CleanupOldBackups(string backupRoot, int retentionDays, Func<int, bool>? confirmDelete = null);
    string? DetectPatchFormat(string patchPath);
    bool RepairNesHeader(string path);
    bool RemoveCopierHeader(string path);
}
