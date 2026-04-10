using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Romulus.Contracts;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Export;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ EXPORT & INTEGRATION ═══════════════════════════════════════════

    private void HtmlReport()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var path = _dialog.SaveFile("HTML-Report speichern", "HTML (*.html)|*.html", "report.html");
        if (!TryResolveSafeOutputPath(path, "HTML-Report", out var safePath)) return;

        var mode = _vm.CurrentRunState == Romulus.UI.Wpf.Models.RunState.CompletedDryRun
            ? RunConstants.ModeDryRun
            : RunConstants.ModeMove;
        try
        {
            if (_vm.LastRunResult is not null)
            {
                RunReportWriter.WriteReport(safePath, _vm.LastRunResult, mode);
            }
            else
            {
                var (summary, entries) = FeatureService.BuildHtmlReportData(
                    _vm.LastCandidates.ToArray(),
                    _vm.LastDedupeGroups.ToArray(),
                    runResult: null,
                    dryRun: string.Equals(mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase));
                ReportGenerator.WriteHtmlToFile(safePath, Path.GetDirectoryName(safePath) ?? ".", summary, entries);
            }

            _vm.AddLog($"Report erstellt: {safePath} (Im Browser drucken → PDF)", "INFO");
            TryOpenWithShell(safePath, "Report");
        }
        catch (Exception ex) { LogError("GUI-REPORT", $"Report-Fehler: {ex.Message}"); }
    }

    private void LauncherIntegration()
    {
        var path = _dialog.SaveFile("RetroArch Playlist exportieren", "Playlist (*.lpl)|*.lpl", "Romulus.lpl");
        if (path is null) return;
        if (!TryLoadFrontendExportResult(
                FrontendExportTargets.RetroArch,
                path,
                Path.GetFileNameWithoutExtension(path),
                out var exportResult) ||
            exportResult is null)
        {
            return;
        }

        _vm.AddLog($"Playlist exportiert: {path} ({exportResult.GameCount} Eintraege)", "INFO");
    }

    private void DatImport()
    {
        var path = _dialog.BrowseFile("DAT-Datei importieren (ClrMamePro, RomVault, Logiqx)",
            "DAT (*.dat;*.xml)|*.dat;*.xml|Alle (*.*)|*.*");
        if (path is null) return;
        _vm.AddLog($"DAT-Import: {Path.GetFileName(path)}", "INFO");
        if (string.IsNullOrWhiteSpace(_vm.DatRoot))
        { _dialog.Error("DAT-Root ist nicht konfiguriert. Bitte zuerst den DAT-Root-Ordner setzen.", "DAT-Import"); return; }
        try
        {
            var safeName = Path.GetFileName(path);
            var targetPath = Path.GetFullPath(Path.Combine(_vm.DatRoot, safeName));
            if (!targetPath.StartsWith(Path.GetFullPath(_vm.DatRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            { _vm.AddLog("DAT-Import blockiert: Pfad außerhalb des DatRoot.", "ERROR"); return; }
            if (!TryResolveSafeOutputPath(targetPath, "DAT-Import", out var safeTargetPath))
                return;

            File.Copy(path, safeTargetPath, overwrite: true);
            _vm.AddLog($"DAT importiert nach: {safeTargetPath}", "INFO");
            _dialog.Info($"DAT erfolgreich importiert:\n\n  Quelle: {path}\n  Ziel: {safeTargetPath}", "DAT-Import");
        }
        catch (Exception ex) { LogError("DAT-IMPORT", $"DAT-Import fehlgeschlagen: {ex.Message}"); }
    }

    private void ExportCollection()
    {
        var choice = _dialog.ShowInputBox(
            "Export-Format waehlen:\n\n" +
            "  1 - CSV (Sammlung)\n" +
            "  2 - Excel-XML (Sammlung)\n" +
            "  3 - CSV (nur Duplikate)\n" +
            "  4 - RetroArch Playlist\n" +
            "  5 - LaunchBox XML\n" +
            "  6 - EmulationStation gamelist\n" +
            "  7 - Playnite Bibliothek\n" +
            "  8 - MiSTer Paket (games/)\n" +
            "  9 - Analogue Pocket Paket (Assets/)\n" +
            " 10 - OnionOS Paket (Roms/)\n" +
            " 11 - M3U Multi-Disc Playlist\n\n" +
            "Nummer eingeben:",
            "Sammlung exportieren");
        if (string.IsNullOrWhiteSpace(choice))
            return;

        switch (choice.Trim())
        {
            case "1":
                ExportFrontend(FrontendExportTargets.Csv, "CSV (*.csv)|*.csv", "sammlung.csv", "Romulus");
                break;
            case "2":
                ExportFrontend(FrontendExportTargets.Excel, "Excel XML (*.xml)|*.xml", "sammlung.xml", "Romulus");
                break;
            case "3":
                ExportDuplicateCsv();
                break;
            case "4":
                ExportFrontend(FrontendExportTargets.RetroArch, "Playlist (*.lpl)|*.lpl", "Romulus.lpl", "Romulus");
                break;
            case "5":
                ExportFrontend(FrontendExportTargets.LaunchBox, "LaunchBox XML (*.xml)|*.xml", "LaunchBox.xml", "Romulus");
                break;
            case "6":
                ExportFrontend(FrontendExportTargets.EmulationStation, "Ordner|*.*", "emulationstation", "Romulus");
                break;
            case "7":
                ExportFrontend(FrontendExportTargets.Playnite, "JSON (*.json)|*.json", "playnite-library.json", "Romulus");
                break;
            case "8":
                ExportFrontend(FrontendExportTargets.MiSTer, "Ordner|*.*", "mister", "Romulus");
                break;
            case "9":
                ExportFrontend(FrontendExportTargets.AnaloguePocket, "Ordner|*.*", "analogue-pocket", "Romulus");
                break;
            case "10":
                ExportFrontend(FrontendExportTargets.OnionOs, "Ordner|*.*", "onionos", "Romulus");
                break;
            case "11":
                ExportFrontend(FrontendExportTargets.M3u, "Playlist (*.m3u)|*.m3u", "Romulus.m3u", "Romulus");
                break;
            default:
                _vm.AddLog("Ungueltige Auswahl. Bitte 1 bis 11 eingeben.", "WARN");
                break;
        }
    }

    private void ExportFrontend(string frontend, string filter, string defaultFileName, string collectionName)
    {
        var path = _dialog.SaveFile("Export speichern", filter, defaultFileName);
        if (path is null)
            return;

        if (!TryLoadFrontendExportResult(frontend, path, collectionName, out var exportResult) || exportResult is null)
            return;

        _vm.AddLog($"Export erstellt: {path} ({exportResult.GameCount} Spiele, Quelle={exportResult.Source})", "INFO");
        _dialog.ShowText("Frontend-Export", FormatFrontendExportSummary(exportResult));
    }

    private void ExportDuplicateCsv()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        {
            _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN");
            return;
        }

        var path = _dialog.SaveFile(_vm.Loc["Cmd.DupeExportTitle"], _vm.Loc["Cmd.FilterCsv"], "duplikate.csv");
        if (!TryResolveSafeOutputPath(path, "Duplikat-CSV-Export", out var safePath))
            return;

        var losers = _vm.LastDedupeGroups.SelectMany(static group => group.Losers).ToList();
        var dupeCsv = FeatureService.ExportCollectionCsv(losers);
        File.WriteAllText(safePath, dupeCsv, Encoding.UTF8);
        _vm.AddLog(_vm.Loc.Format("Cmd.DupeExported", safePath, losers.Count), "INFO");
    }

}
