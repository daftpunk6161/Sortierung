using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ INFRASTRUKTUR & DEPLOYMENT ═════════════════════════════════════

    private void StorageTiering()
    {
        if (!TryLoadSnapshots(30, out var snapshots, out var collectionIndex) || collectionIndex is null)
            return;

        using (collectionIndex)
        {
            try
            {
                var insights = RunHistoryInsightsService.BuildStorageInsightsAsync(collectionIndex, 30).GetAwaiter().GetResult();
                var trends = RunHistoryTrendService.LoadTrendHistoryAsync(collectionIndex, 30).GetAwaiter().GetResult();

                var sb = new StringBuilder();
                sb.AppendLine(RunHistoryInsightsService.FormatStorageInsightReport(insights));
                sb.AppendLine();
                sb.AppendLine(RunHistoryTrendService.FormatTrendReport(
                    trends,
                    "Run Trends",
                    "Keine Run-Historie verfuegbar.",
                    "Aktuell",
                    "Delta Dateien",
                    "Delta Duplikate",
                    "Historie",
                    "Dateien",
                    "Qualitaet"));

                if (_vm.LastCandidates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(FeatureService.AnalyzeStorageTiers(_vm.LastCandidates));
                }

                _dialog.ShowText("Storage-Tiering", sb.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                LogWarning("GUI-HISTORY", $"Run-Historie nicht verfuegbar: {ex.Message}");
                var sb = new StringBuilder();
                sb.AppendLine("Storage-Tiering");
                sb.AppendLine();
                sb.AppendLine("Run-Historie nicht verfuegbar.");

                if (_vm.LastCandidates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(FeatureService.AnalyzeStorageTiers(_vm.LastCandidates));
                }

                _dialog.ShowText("Storage-Tiering", sb.ToString());
            }
        }
    }

    private void NasOptimization()
    {
        if (_vm.Roots.Count == 0)
        { _vm.AddLog("Keine Roots konfiguriert.", "WARN"); return; }
        _dialog.ShowText("NAS-Optimierung", FeatureService.GetNasInfo(_vm.Roots.ToList()));
    }

    private void PortableMode()
    {
        var isPortable = FeatureService.IsPortableMode();
        var resolvedSettingsDir = AppStoragePathResolver.ResolveRoamingAppDirectory();
        var sb = new StringBuilder();
        sb.AppendLine("Portable-Modus\n");
        sb.AppendLine($"  Aktueller Modus: {(isPortable ? "PORTABEL" : "Standard (AppData)")}");
        sb.AppendLine($"  Programm-Verzeichnis: {AppContext.BaseDirectory}");
        if (isPortable) sb.AppendLine($"  Settings-Ordner: {resolvedSettingsDir}");
        else
        {
            sb.AppendLine($"  Settings-Ordner: {resolvedSettingsDir}");
            sb.AppendLine("\n  Tipp: Erstelle '.portable' im Programmverzeichnis für Portable-Modus.");
        }
        _dialog.ShowText("Portable-Modus", sb.ToString());
    }

    private void HardlinkMode()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var estimate = FeatureService.GetHardlinkEstimate(_vm.LastDedupeGroups);
        var firstRoot = _vm.LastDedupeGroups.FirstOrDefault()?.Winner.MainPath;
        var isNtfs = false;
        if (firstRoot is not null)
        {
            try
            {
                var driveRoot = Path.GetPathRoot(firstRoot);
                if (driveRoot is not null) isNtfs = new DriveInfo(driveRoot).DriveFormat.Equals("NTFS", StringComparison.OrdinalIgnoreCase);
            }
            catch (IOException) { /* drive info unavailable — show as not available */ }
            catch (UnauthorizedAccessException) { /* no access to drive info */ }
        }
        _dialog.ShowText("Hardlink-Modus", $"Hardlink-Modus\n\n{estimate}\n\nNTFS-Unterstützung: {(isNtfs ? "Verfügbar" : "Nicht verfügbar")}\n\nHardlinks teilen den Speicherplatz auf Dateisystemebene.\nBeide Pfade zeigen auf dieselben Daten – kein zusätzlicher Speicher.");
    }

    // ═══ WINDOW-LEVEL COMMANDS (require IWindowHost) ════════════════════

    private void CommandPalette()
    {
        var input = _dialog.ShowInputBox("Befehl suchen:", "Command-Palette", "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var results = FeatureService.SearchCommands(input, _vm.FeatureCommands);
        if (results.Count == 0)
        { _vm.AddLog($"Kein Befehl gefunden für: {input}", "WARN"); return; }

        _dialog.ShowText("Command-Palette", FeatureService.BuildCommandPaletteReport(input, results));
        if (results[0].score == 0) ExecuteCommand(results[0].key);
    }

    internal void ExecuteCommand(string key)
    {
        // 1. Try FeatureCommands dictionary (all registered tool commands)
        if (_vm.FeatureCommands.TryGetValue(key, out var featureCmd))
        {
            featureCmd.Execute(null);
            _vm.RecordToolUsage(key);
            return;
        }

        // 2. Fallback: core VM-level shortcuts not in FeatureCommands
        switch (key)
        {
            case "dryrun": if (!_vm.IsBusy) { _vm.DryRun = true; _vm.RunCommand.Execute(null); } break;
            case "move": if (!_vm.IsBusy) { _vm.DryRun = false; _vm.RunCommand.Execute(null); } break;
            case "cancel": _vm.CancelCommand.Execute(null); break;
            case "rollback": _vm.RollbackCommand.Execute(null); break;
            case "theme": _vm.ThemeToggleCommand.Execute(null); break;
            case "clear-log": _vm.ClearLogCommand.Execute(null); break;
            case "settings": _windowHost?.SelectTab(3); break;
            default: _vm.AddLog($"Unbekannter Befehl: {key}", "WARN"); break;
        }
    }

    private void ApiServer()
    {
        var apiProject = FeatureService.FindApiProjectPath();
        if (apiProject is not null)
        {
            if (_dialog.Confirm("REST API starten und Browser öffnen?\n\nhttp://127.0.0.1:5000", "API-Server"))
            {
                _windowHost?.StartApiProcess(apiProject);
                return;
            }
        }
        else
        {
            _dialog.ShowText("API-Server", "API-Server\n\n  API-Projekt nicht gefunden.\n\n" +
                "  Zum manuellen Start:\n    dotnet run --project src/Romulus.Api\n\n" +
                "  Dann im Browser öffnen:\n    http://127.0.0.1:5000");
        }
    }

    private void Accessibility()
    {
        if (_windowHost is null) return;
        var isHC = FeatureService.IsHighContrastActive();
        var currentSize = _windowHost.FontSize;

        var input = _dialog.ShowInputBox(
            $"Barrierefreiheit\n\n" +
            $"High-Contrast: {(isHC ? "AKTIV" : "Inaktiv")}\n" +
            $"Aktuelle Schriftgröße: {currentSize}\n\n" +
            "Neue Schriftgröße eingeben (10-24):",
            "Barrierefreiheit", currentSize.ToString("0"));
        if (string.IsNullOrWhiteSpace(input)) return;

        if (double.TryParse(input, System.Globalization.CultureInfo.InvariantCulture, out var newSize) && newSize >= 10 && newSize <= 24)
        {
            _windowHost.FontSize = newSize;
            _vm.AddLog($"Schriftgröße geändert: {newSize}", "INFO");
        }
        else
        {
            _vm.AddLog($"Ungültige Schriftgröße: {input} (erlaubt: 10-24)", "WARN");
        }
    }
}
