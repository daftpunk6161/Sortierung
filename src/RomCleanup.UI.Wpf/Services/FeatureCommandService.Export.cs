using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.UI.Wpf.ViewModels;
namespace RomCleanup.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ EXPORT & INTEGRATION ═══════════════════════════════════════════

    private void PdfReport()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var path = _dialog.SaveFile("PDF-Report speichern", "HTML (*.html)|*.html", "report.html");
        if (path is null) return;
        var summary = new ReportSummary
        {
            Mode = _vm.DryRun ? "DryRun" : "Move",
            TotalFiles = _vm.LastCandidates.Count,
            KeepCount = _vm.LastDedupeGroups.Count,
            MoveCount = _vm.LastDedupeGroups.Sum(g => g.Losers.Count),
            JunkCount = _vm.LastCandidates.Count(c => c.Category == "JUNK"),
            GroupCount = _vm.LastDedupeGroups.Count,
            Duration = TimeSpan.FromMilliseconds(_vm.LastRunResult?.DurationMs ?? 0)
        };
        var loserPaths = new HashSet<string>(
            _vm.LastDedupeGroups.SelectMany(g => g.Losers.Select(l => l.MainPath)),
            StringComparer.OrdinalIgnoreCase);
        var entries = _vm.LastCandidates.Select(c => new ReportEntry
        {
            GameKey = c.GameKey,
            Action = c.Category == "JUNK" ? "JUNK" : loserPaths.Contains(c.MainPath) ? "MOVE" : "KEEP",
            Category = c.Category, Region = c.Region, FilePath = c.MainPath,
            FileName = Path.GetFileName(c.MainPath), Extension = c.Extension,
            SizeBytes = c.SizeBytes, RegionScore = c.RegionScore, FormatScore = c.FormatScore,
            VersionScore = (int)c.VersionScore, DatMatch = c.DatMatch
        }).ToList();
        try
        {
            ReportGenerator.WriteHtmlToFile(path, Path.GetDirectoryName(path) ?? ".", summary, entries);
            _vm.AddLog($"Report erstellt: {path} (Im Browser drucken → PDF)", "INFO");
            Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
        }
        catch (Exception ex) { LogError("GUI-REPORT", $"Report-Fehler: {ex.Message}"); }
    }

    private void LauncherIntegration()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var path = _dialog.SaveFile("RetroArch Playlist exportieren", "Playlist (*.lpl)|*.lpl", "RomCleanup.lpl");
        if (path is null) return;
        var winners = _vm.LastDedupeGroups.Select(g => g.Winner).ToList();
        var json = FeatureService.ExportRetroArchPlaylist(winners, Path.GetFileNameWithoutExtension(path));
        File.WriteAllText(path, json);
        _vm.AddLog($"Playlist exportiert: {path} ({winners.Count} Einträge)", "INFO");
    }

    private void ToolImport()
    {
        var path = _dialog.BrowseFile("DAT-Datei importieren (ClrMamePro, RomVault, Logiqx)",
            "DAT (*.dat;*.xml)|*.dat;*.xml|Alle (*.*)|*.*");
        if (path is null) return;
        _vm.AddLog($"Tool-Import: {Path.GetFileName(path)}", "INFO");
        if (string.IsNullOrWhiteSpace(_vm.DatRoot))
        { _dialog.Error("DAT-Root ist nicht konfiguriert. Bitte zuerst den DAT-Root-Ordner setzen.", "Tool-Import"); return; }
        try
        {
            var safeName = Path.GetFileName(path);
            var targetPath = Path.GetFullPath(Path.Combine(_vm.DatRoot, safeName));
            if (!targetPath.StartsWith(Path.GetFullPath(_vm.DatRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { _vm.AddLog("DAT-Import blockiert: Pfad außerhalb des DatRoot.", "ERROR"); return; }
            File.Copy(path, targetPath, overwrite: true);
            _vm.AddLog($"DAT importiert nach: {targetPath}", "INFO");
            _dialog.Info($"DAT erfolgreich importiert:\n\n  Quelle: {path}\n  Ziel: {targetPath}", "Tool-Import");
        }
        catch (Exception ex) { LogError("DAT-IMPORT", $"DAT-Import fehlgeschlagen: {ex.Message}"); }
    }

}
