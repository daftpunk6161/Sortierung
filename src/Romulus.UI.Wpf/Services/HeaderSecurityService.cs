using System.IO;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;

namespace Romulus.UI.Wpf.Services;

/// <summary>GUI-039: Delegates header operations to Core/Infrastructure.</summary>
public sealed class HeaderSecurityService : IHeaderService
{
    private readonly IHeaderRepairService _headerRepairService;

    public HeaderSecurityService(IHeaderRepairService headerRepairService)
    {
        _headerRepairService = headerRepairService ?? throw new ArgumentNullException(nameof(headerRepairService));
    }

    public RomHeaderInfo? AnalyzeHeader(string filePath)
    {
        if (!System.IO.File.Exists(filePath)) return null;
        try
        {
            using var fs = System.IO.File.OpenRead(filePath);
            var header = new byte[Math.Min(65536, fs.Length)];
            _ = fs.Read(header, 0, header.Length);
            return HeaderAnalyzer.AnalyzeHeader(header, fs.Length);
        }
        catch (IOException)
        {
            return null;
        }
    }

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
        => _headerRepairService.RepairNesHeader(path);

    public bool RemoveCopierHeader(string path)
        => _headerRepairService.RemoveCopierHeader(path);
}
