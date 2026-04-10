using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ HEADER ANALYSIS ════════════════════════════════════════════════
    // Port of HeaderAnalysis.ps1

    public static RomHeaderInfo? AnalyzeHeader(string filePath)
    {
        if (!File.Exists(filePath)) return null;
        try
        {
            using var fs = File.OpenRead(filePath);
            var header = new byte[Math.Min(65536, fs.Length)];
            _ = fs.Read(header, 0, header.Length);
            var analyzed = HeaderAnalyzer.AnalyzeHeader(header, fs.Length);
            return analyzed is null
                ? null
                : new RomHeaderInfo(analyzed.Platform, analyzed.Format, analyzed.Details);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or SecurityException)
        {
            System.Diagnostics.Debug.WriteLine($"[FeatureService] AnalyzeHeader failed for '{filePath}': {ex.Message}");
            return null;
        }
    }


    // ═══ TREND ANALYSIS ═════════════════════════════════════════════════
    // Port of TrendAnalysis.ps1

    public static void SaveTrendSnapshot(int totalFiles, long sizeBytes, int verified, int dupes, int junk)
        => IntegrityService.SaveTrendSnapshot(totalFiles, sizeBytes, verified, dupes, junk);


    public static List<TrendSnapshot> LoadTrendHistory()
        => IntegrityService.LoadTrendHistory();


    public static string FormatTrendReport(List<TrendSnapshot> history)
        => RunHistoryTrendService.FormatTrendReport(
            history,
            title: "Trend-Analyse",
            emptyMessage: "Keine Trend-Daten vorhanden.",
            currentLabel: "Aktuell",
            deltaFilesLabel: "Δ Dateien",
            deltaDuplicatesLabel: "Δ Duplikate",
            historyLabel: "Verlauf (letzte 10):",
            filesLabel: "Dateien",
            qualityLabel: "Quality");


    // ═══ INTEGRITY MONITOR ══════════════════════════════════════════════
    // Port of IntegrityMonitor.ps1
    // Baseline stores paths relative to the common root for portability.

    public static async Task<Dictionary<string, IntegrityEntry>> CreateBaseline(
        IReadOnlyList<string> filePaths, IProgress<string>? progress = null, CancellationToken ct = default)
        => await IntegrityService.CreateBaseline(filePaths, progress, ct);


    public static async Task<IntegrityCheckResult> CheckIntegrity(IProgress<string>? progress = null, CancellationToken ct = default)
        => await IntegrityService.CheckIntegrity(progress, ct);


    // ═══ BACKUP MANAGER ═════════════════════════════════════════════════
    // Port of BackupManager.ps1

    public static string CreateBackup(IReadOnlyList<string> filePaths, string backupRoot, string label)
        => IntegrityService.CreateBackup(filePaths, backupRoot, label);


    internal static string? FindCommonRoot(IReadOnlyList<string> paths)
        => IntegrityService.FindCommonRoot(paths);


    public static int CleanupOldBackups(string backupRoot, int retentionDays, Func<int, bool>? confirmDelete = null)
        => IntegrityService.CleanupOldBackups(backupRoot, retentionDays, confirmDelete);


    // ═══ PATCH ENGINE ═══════════════════════════════════════════════════
    // Port of PatchEngine.ps1

    public static string? DetectPatchFormat(string patchPath)
        => IntegrityService.DetectPatchFormat(patchPath);

    public static PatchApplyResult ApplyPatch(
        string sourceRomPath,
        string patchPath,
        string outputPath,
        IToolRunner? toolRunner = null,
        string? flipsToolPath = null,
        string? xdeltaToolPath = null,
        CancellationToken ct = default)
        => IntegrityService.ApplyPatch(sourceRomPath, patchPath, outputPath, toolRunner, flipsToolPath, xdeltaToolPath, ct);

}
