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
    // ═══ INFRASTRUKTUR & DEPLOYMENT ═════════════════════════════════════

    private void StorageTiering()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        _dialog.ShowText("Storage-Tiering", FeatureService.AnalyzeStorageTiers(_vm.LastCandidates));
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
        var sb = new StringBuilder();
        sb.AppendLine("Portable-Modus\n");
        sb.AppendLine($"  Aktueller Modus: {(isPortable ? "PORTABEL" : "Standard (AppData)")}");
        sb.AppendLine($"  Programm-Verzeichnis: {AppContext.BaseDirectory}");
        if (isPortable) sb.AppendLine($"  Settings-Ordner: {Path.Combine(AppContext.BaseDirectory, ".romcleanup")}");
        else
        {
            sb.AppendLine($"  Settings-Ordner: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), RomCleanup.Contracts.AppIdentity.AppFolderName)}");
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

    private void MultiInstanceSync()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Multi-Instanz-Synchronisation");
        sb.AppendLine(new string('═', 50));
        var locks = new List<(string path, string content)>();
        foreach (var root in _vm.Roots)
        {
            var lockFile = Path.Combine(root, ".romcleanup.lock");
            if (File.Exists(lockFile))
            {
                try { locks.Add((lockFile, File.ReadAllText(lockFile))); }
                catch { locks.Add((lockFile, "(nicht lesbar)")); }
            }
        }
        sb.AppendLine($"\n  Konfigurierte Roots: {_vm.Roots.Count}");
        sb.AppendLine($"  Aktive Locks:       {locks.Count}");
        if (locks.Count > 0)
        {
            sb.AppendLine("\n  Gefundene Lock-Dateien:");
            foreach (var (path, content) in locks) { sb.AppendLine($"    {path}"); sb.AppendLine($"      {content}"); }
        }
        else sb.AppendLine("\n  Keine aktiven Locks gefunden.");
        sb.AppendLine($"\n  Diese Instanz:\n    PID:      {Environment.ProcessId}\n    Hostname: {Environment.MachineName}\n    Status:   {(_vm.IsBusy ? "LÄUFT" : "Bereit")}");
        _dialog.ShowText("Multi-Instanz", sb.ToString());
        if (locks.Count > 0 && _dialog.Confirm($"{locks.Count} Lock-Datei(en) gefunden.\n\nAbgelaufene Locks entfernen?", "Multi-Instanz"))
        {
            var removed = 0;
            var failed = 0;
            foreach (var (path, _) in locks)
            {
                try { File.Delete(path); removed++; }
                catch (Exception ex) { failed++; _vm.AddLog($"Lock-Datei konnte nicht entfernt werden: {path} ({ex.Message})", "WARN"); }
            }
            _vm.AddLog($"Multi-Instanz: {removed} Lock(s) entfernt{(failed > 0 ? $", {failed} fehlgeschlagen" : "")}", "INFO");
        }
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
                "  Zum manuellen Start:\n    dotnet run --project src/RomCleanup.Api\n\n" +
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
