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

    private void FtpSource()
    {
        var input = _dialog.ShowInputBox("FTP/SFTP-URL eingeben:\n\nFormat: ftp://host/pfad oder sftp://host/pfad",
            "FTP-Quelle", "ftp://");
        if (string.IsNullOrWhiteSpace(input) || input == "ftp://") return;
        var isValid = input.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                      input.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase);
        if (!isValid) { _vm.AddLog($"Ungültige FTP-URL: {input} (muss mit ftp:// oder sftp:// beginnen)", "ERROR"); return; }
        if (input.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase))
        {
            var useSftp = _dialog.Confirm(
                "⚠ FTP überträgt Daten unverschlüsselt.\nZugangsdaten und Dateien können abgefangen werden.\n\nEmpfehlung: Verwende SFTP (sftp://) stattdessen.\n\nTrotzdem mit unverschlüsseltem FTP fortfahren?",
                "Sicherheitshinweis");
            if (!useSftp) return;
        }
        try
        {
            var uri = new Uri(input);
            var sb = new StringBuilder();
            sb.AppendLine("FTP-Quelle konfiguriert\n");
            sb.AppendLine($"  Protokoll: {uri.Scheme.ToUpperInvariant()}");
            sb.AppendLine($"  Host:      {uri.Host}");
            sb.AppendLine($"  Port:      {(uri.Port > 0 ? uri.Port : (uri.Scheme == "sftp" ? 22 : 21))}");
            sb.AppendLine($"  Pfad:      {uri.AbsolutePath}");
            sb.AppendLine("\n  ℹ FTP-Download ist noch nicht implementiert.\n  Aktuell wird die URL nur registriert und angezeigt.\n  Geplantes Feature: Dateien vor Verarbeitung lokal cachen.");
            _dialog.ShowText("FTP-Quelle", sb.ToString());
            _vm.AddLog($"FTP-Quelle registriert: {uri.Host}{uri.AbsolutePath}", "INFO");
        }
        catch (Exception ex) { LogError("GUI-FTP", $"FTP-URL ungültig: {ex.Message}"); }
    }

    private void CloudSync()
    {
        var oneDrive = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "OneDrive");
        var dropbox = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Dropbox");
        var sb = new StringBuilder();
        sb.AppendLine("Cloud-Sync Status\n");
        sb.AppendLine($"  OneDrive: {(Directory.Exists(oneDrive) ? "✓ Gefunden" : "✗ Nicht gefunden")}");
        sb.AppendLine($"  Dropbox:  {(Directory.Exists(dropbox) ? "✓ Gefunden" : "✗ Nicht gefunden")}");
        sb.AppendLine("\n  ℹ Nur Statusanzeige – Cloud-Sync ist in Planung.\n  Geplant: Metadaten-Sync (Einstellungen, Profile).\n  Keine ROM-Dateien werden hochgeladen.");
        _dialog.ShowText("Cloud-Sync (Vorschau)", sb.ToString());
    }

    private void PluginMarketplace()
    {
        var pluginDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(pluginDir))
        {
            Directory.CreateDirectory(pluginDir);
            _vm.AddLog($"Plugin-Verzeichnis erstellt: {pluginDir}", "INFO");
        }
        var manifests = Directory.GetFiles(pluginDir, "*.json", SearchOption.AllDirectories);
        var dlls = Directory.GetFiles(pluginDir, "*.dll", SearchOption.AllDirectories);
        var sb = new StringBuilder();
        sb.AppendLine("Plugin-Manager (Coming Soon)\n");
        sb.AppendLine("  ℹ Das Plugin-System ist in Planung und noch nicht funktionsfähig.");
        sb.AppendLine($"  Plugin-Verzeichnis: {pluginDir}\n");
        sb.AppendLine($"  Manifeste:   {manifests.Length}");
        sb.AppendLine($"  DLLs:        {dlls.Length}\n");
        if (manifests.Length == 0 && dlls.Length == 0)
        {
            sb.AppendLine("  Keine Plugins installiert.\n");
            sb.AppendLine("  Plugin-Struktur:\n    plugins/\n      mein-plugin/\n        manifest.json\n        MeinPlugin.dll\n");
            sb.AppendLine("  Manifest-Format:\n    {\n      \"name\": \"Mein Plugin\",\n      \"version\": \"1.0.0\",\n      \"type\": \"console|format|report\"\n    }");
        }
        else
        {
            foreach (var manifest in manifests)
            {
                try
                {
                    var json = File.ReadAllText(manifest);
                    using var doc = JsonDocument.Parse(json);
                    var name = doc.RootElement.TryGetProperty("name", out var np) ? np.GetString() : Path.GetFileName(manifest);
                    var ver = doc.RootElement.TryGetProperty("version", out var vp) ? vp.GetString() : "?";
                    var type = doc.RootElement.TryGetProperty("type", out var tp) ? tp.GetString() : "?";
                    sb.AppendLine($"  [{type}] {name} v{ver}");
                    sb.AppendLine($"         {Path.GetDirectoryName(manifest)}");
                }
                catch (Exception ex) { LogWarning("GUI-PLUGIN", $"Manifest ungültig: {Path.GetFileName(manifest)} – {ex.Message}"); sb.AppendLine($"  [?] {Path.GetFileName(manifest)} (manifest ungültig)"); }
            }
            if (dlls.Length > 0) { sb.AppendLine($"\n  DLLs:"); foreach (var dll in dlls) sb.AppendLine($"    {Path.GetFileName(dll)}"); }
        }
        _dialog.ShowText("Plugin-Manager", sb.ToString());
        if (_dialog.Confirm($"Plugin-Verzeichnis im Explorer öffnen?\n\n{pluginDir}", "Plugins"))
            TryOpenWithShell(pluginDir, "Plugin-Verzeichnis", allowDirectory: true);
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
            sb.AppendLine($"  Settings-Ordner: {Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RomCleanupRegionDedupe")}");
            sb.AppendLine("\n  Tipp: Erstelle '.portable' im Programmverzeichnis für Portable-Modus.");
        }
        _dialog.ShowText("Portable-Modus", sb.ToString());
    }

    private void DockerContainer()
    {
        var sb = new StringBuilder();
        sb.AppendLine("Docker-Konfiguration\n");
        sb.AppendLine("═══ Dockerfile ═══");
        sb.AppendLine(FeatureService.GenerateDockerfile());
        sb.AppendLine("\n═══ docker-compose.yml ═══");
        sb.AppendLine(FeatureService.GenerateDockerCompose());
        _dialog.ShowText("Docker", sb.ToString());
        var savePath = _dialog.SaveFile("Docker-Dateien speichern", "Dockerfile|Dockerfile|YAML (*.yml)|*.yml", "Dockerfile");
        if (savePath is not null)
        {
            var ext = Path.GetExtension(savePath).ToLowerInvariant();
            var content = ext == ".yml" ? FeatureService.GenerateDockerCompose() : FeatureService.GenerateDockerfile();
            File.WriteAllText(savePath, content);
            _vm.AddLog($"Docker-Datei gespeichert: {savePath}", "INFO");
        }
    }

    private void WindowsContextMenu()
    {
        var regScript = FeatureService.GetContextMenuRegistryScript();
        var path = _dialog.SaveFile("Registry-Skript speichern", "Registry (*.reg)|*.reg", "romcleanup-context-menu.reg");
        if (path is null) return;
        File.WriteAllText(path, regScript);
        _vm.AddLog($"Kontextmenü-Registry exportiert: {path}", "INFO");
        _dialog.Info($"Registry-Skript gespeichert:\n{path}\n\nDoppelklicke die .reg-Datei, um das Kontextmenü zu installieren.\n\n⚠ Das Skript enthält den absoluten Pfad zur aktuellen EXE-Datei.\nBei Verschiebung der Anwendung muss das Skript neu generiert werden.\n\nEinträge:\n• ROM Cleanup – DryRun Scan\n• ROM Cleanup – Move Sort", "Kontextmenü");
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
            catch { }
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
            foreach (var (path, _) in locks)
            {
                try { File.Delete(path); removed++; } catch { }
            }
            _vm.AddLog($"Multi-Instanz: {removed} Lock(s) entfernt", "INFO");
        }
    }

    // ═══ WINDOW-LEVEL COMMANDS (require IWindowHost) ════════════════════

    private void CommandPalette()
    {
        var input = _dialog.ShowInputBox("Befehl suchen:", "Command-Palette", "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var results = FeatureService.SearchCommands(input);
        if (results.Count == 0)
        { _vm.AddLog($"Kein Befehl gefunden für: {input}", "WARN"); return; }

        _dialog.ShowText("Command-Palette", FeatureService.BuildCommandPaletteReport(input, results));
        if (results[0].score == 0) ExecuteCommand(results[0].key);
    }

    private void ExecuteCommand(string key)
    {
        switch (key)
        {
            case "dryrun": if (!_vm.IsBusy) { _vm.DryRun = true; _vm.RunCommand.Execute(null); } break;
            case "move": if (!_vm.IsBusy) { _vm.DryRun = false; _vm.RunCommand.Execute(null); } break;
            case "cancel": _vm.CancelCommand.Execute(null); break;
            case "rollback": _vm.RollbackCommand.Execute(null); break;
            case "theme": _vm.ThemeToggleCommand.Execute(null); break;
            case "clear-log": _vm.ClearLogCommand.Execute(null); break;
            case "settings": _windowHost?.SelectTab(3); break;
            default: _vm.AddLog($"Befehl: {key}", "INFO"); break;
        }
    }

    private void MobileWebUI()
    {
        var apiProject = FeatureService.FindApiProjectPath();
        if (apiProject is not null)
        {
            if (_dialog.Confirm("REST API starten und Browser öffnen?\n\nhttp://127.0.0.1:5000", "Mobile Web UI"))
            {
                _windowHost?.StartApiProcess(apiProject);
                return;
            }
        }
        else
        {
            _dialog.ShowText("Mobile Web UI", "Mobile Web UI\n\n  API-Projekt nicht gefunden.\n\n" +
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
