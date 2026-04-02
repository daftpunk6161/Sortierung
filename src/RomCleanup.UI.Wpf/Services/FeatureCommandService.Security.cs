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
    // ═══ SICHERHEIT & INTEGRITÄT ════════════════════════════════════════

    private void PatchPipeline()
    {
        var sourcePath = _dialog.BrowseFile(
            "Quell-ROM fuer Patch waehlen",
            "ROM-Dateien (*.zip;*.7z;*.chd;*.iso;*.bin;*.cue;*.nes;*.sfc;*.gba;*.gb;*.gbc;*.n64;*.z64;*.v64)|*.zip;*.7z;*.chd;*.iso;*.bin;*.cue;*.nes;*.sfc;*.gba;*.gb;*.gbc;*.n64;*.z64;*.v64|Alle Dateien (*.*)|*.*");
        if (sourcePath is null)
            return;

        var patchPath = _dialog.BrowseFile(
            "Patch-Datei waehlen",
            "Patch-Dateien (*.ips;*.bps;*.ups;*.xdelta;*.xdelta3;*.vcdiff)|*.ips;*.bps;*.ups;*.xdelta;*.xdelta3;*.vcdiff|Alle Dateien (*.*)|*.*");
        if (patchPath is null)
            return;

        var patchFormat = ResolvePatchFormatForDialog(patchPath);
        if (patchFormat is null)
        {
            LogWarning("PATCH-FORMAT", "Patch-Format nicht erkannt. Unterstuetzt: IPS, BPS, UPS, xdelta.");
            return;
        }

        var sourceExtension = Path.GetExtension(sourcePath);
        var defaultName = Path.GetFileNameWithoutExtension(sourcePath) + ".patched" + sourceExtension;
        var outputPath = _dialog.SaveFile(
            "Patch-Ausgabe speichern",
            string.IsNullOrWhiteSpace(sourceExtension)
                ? "Alle Dateien (*.*)|*.*"
                : $"ROM (*{sourceExtension})|*{sourceExtension}|Alle Dateien (*.*)|*.*",
            defaultName);
        if (!TryResolveSafeOutputPath(outputPath, "Patch-Pipeline", out var safeOutputPath))
            return;

        try
        {
            var result = FeatureService.ApplyPatch(
                sourcePath,
                patchPath,
                safeOutputPath,
                toolRunner: new ToolRunnerAdapter());

            var toolLine = string.IsNullOrWhiteSpace(result.ToolPath)
                ? "Tool: intern"
                : $"Tool: {result.ToolPath}";
            _dialog.ShowText(
                "Patch-Pipeline",
                $"Format: {result.Format}\nQuelle: {result.SourcePath}\nPatch: {result.PatchPath}\nZiel: {result.OutputPath}\nGroesse: {FeatureService.FormatSize(result.OutputSizeBytes)}\nSHA256: {result.OutputSha256}\n{toolLine}");
            _vm.AddLog($"Patch angewendet ({result.Format}): {Path.GetFileName(result.PatchPath)} -> {result.OutputPath}", "INFO");
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("PATCH-APPLY", $"Patch konnte nicht angewendet werden: {ex.Message}");
            _dialog.Error($"Patch konnte nicht angewendet werden:\n\n{ex.Message}", "Patch-Pipeline");
        }
        catch (Exception ex)
        {
            LogError("PATCH-APPLY", $"Patch-Pipeline fehlgeschlagen: {ex.Message}");
        }
    }

    private async Task IntegrityMonitorAsync()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var createBaseline = _dialog.Confirm("Integritäts-Baseline erstellen oder prüfen?\n\nJA = Neue Baseline erstellen\nNEIN = Gegen Baseline prüfen", "Integritäts-Monitor");
        if (createBaseline)
        {
            _vm.AddLog("Erstelle Integritäts-Baseline…", "INFO");
            var paths = _vm.LastCandidates.Select(c => c.MainPath).ToList();
            var progress = new Progress<string>(msg => _vm.ProgressText = msg);
            try
            {
                var baseline = await FeatureService.CreateBaseline(paths, progress);
                _vm.AddLog($"Baseline erstellt: {baseline.Count} Dateien", "INFO");
            }
            catch (Exception ex) { LogError("SEC-BASELINE", $"Baseline-Fehler: {ex.Message}"); }
        }
        else
        {
            _vm.AddLog("Prüfe Integrität…", "INFO");
            var progress = new Progress<string>(msg => _vm.ProgressText = msg);
            try
            {
                var check = await FeatureService.CheckIntegrity(progress);
                _dialog.ShowText("Integritäts-Check", $"Ergebnis:\n\n" +
                    $"Intakt: {check.Intact.Count}\nGeändert: {check.Changed.Count}\nFehlend: {check.Missing.Count}\n" +
                    $"Bit-Rot-Risiko: {(check.BitRotRisk ? "⚠ JA" : "Nein")}");
            }
            catch (Exception ex) { LogError("SEC-INTEGRITY", $"Integritäts-Fehler: {ex.Message}"); }
        }
    }

    private void BackupManager()
    {
        var backupRoot = _dialog.BrowseFolder("Backup-Zielordner wählen");
        if (backupRoot is null) return;
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Keine Dateien für Backup.", "WARN"); return; }
        var winners = _vm.LastDedupeGroups.Select(g => g.Winner.MainPath).ToList();
        if (!_dialog.Confirm($"{winners.Count} Winner-Dateien sichern nach:\n{backupRoot}", "Backup bestätigen")) return;
        try
        {
            var sessionDir = FeatureService.CreateBackup(winners, backupRoot, "winners");
            _vm.AddLog($"Backup erstellt: {sessionDir} ({winners.Count} Dateien)", "INFO");
        }
        catch (Exception ex) { LogError("SEC-BACKUP", $"Backup-Fehler: {ex.Message}"); }
    }

    private void Quarantine()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var quarantined = _vm.LastCandidates.Where(c =>
            c.Category == FileCategory.Junk || (!c.DatMatch && c.Region == "UNKNOWN")).ToList();
        var sb = new StringBuilder();
        sb.AppendLine($"Quarantäne-Kandidaten: {quarantined.Count}\n");
        sb.AppendLine("Kriterien: Junk-Kategorie ODER (kein DAT-Match + Unbekannte Region)\n");
        foreach (var q in quarantined.Take(30))
            sb.AppendLine($"  {Path.GetFileName(q.MainPath),-50} [{FeatureService.ToCategoryLabel(q.Category)}] {q.Region}");
        if (quarantined.Count > 30)
            sb.AppendLine($"\n  … und {quarantined.Count - 30} weitere");
        _dialog.ShowText("Quarantäne", sb.ToString());
    }

    private void RuleEngine()
    {
        try { _dialog.ShowText("Regel-Engine", FeatureService.BuildRuleEngineReport()); }
        catch (Exception ex) { LogError("SEC-RULES", $"Fehler beim Laden der Regeln: {ex.Message}"); }
    }

    private void HeaderRepair()
    {
        var path = _dialog.BrowseFile("ROM für Header-Reparatur wählen",
            "ROMs (*.nes;*.sfc;*.smc)|*.nes;*.sfc;*.smc|Alle (*.*)|*.*");
        if (path is null) return;
        path = Path.GetFullPath(path);
        var header = FeatureService.AnalyzeHeader(path);
        if (header is null) { _vm.AddLog("Header nicht lesbar.", "ERROR"); return; }

        if (header.Platform == "NES")
        {
            try
            {
                var headerBuf = new byte[16];
                using (var hfs = File.OpenRead(path))
                { if (hfs.Read(headerBuf, 0, 16) < 16) { _vm.AddLog("NES-Header: Datei zu klein.", "ERROR"); return; } }
                bool hasDirtyBytes = (headerBuf[12] != 0 || headerBuf[13] != 0 || headerBuf[14] != 0 || headerBuf[15] != 0);
                if (hasDirtyBytes)
                {
                    var confirm = _dialog.Confirm(
                        $"NES-Header hat unsaubere Bytes (12-15).\n\nDatei: {Path.GetFileName(path)}\n" +
                        $"Byte 12-15: {headerBuf[12]:X2} {headerBuf[13]:X2} {headerBuf[14]:X2} {headerBuf[15]:X2}\n\n" +
                        "Bytes 12-15 auf 0x00 setzen?\n(Backup wird erstellt)", "Header-Reparatur");
                    if (confirm)
                    {
                        var backupPath = path + $".{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                        File.Copy(path, backupPath, overwrite: false);
                        _vm.AddLog($"Backup erstellt: {backupPath}", "INFO");
                        try
                        {
                            using var patchFs = File.OpenWrite(path);
                            patchFs.Seek(12, SeekOrigin.Begin);
                            patchFs.Write(new byte[4], 0, 4);
                            _vm.AddLog("NES-Header repariert: Bytes 12-15 genullt.", "INFO");
                        }
                        catch
                        {
                            File.Copy(backupPath, path, overwrite: true);
                            _vm.AddLog("Reparatur fehlgeschlagen — Backup wiederhergestellt.", "ERROR");
                            throw;
                        }
                    }
                }
                else
                    _dialog.Info($"NES-Header ist sauber. Keine Reparatur nötig.\n\n{header.Details}", "Header-Reparatur");
            }
            catch (Exception ex) { LogError("SEC-HEADER", $"Header-Reparatur fehlgeschlagen: {ex.Message}"); }
            return;
        }

        if (header.Platform == "SNES")
        {
            try
            {
                var fileInfo = new FileInfo(path);
                bool hasCopierHeader = fileInfo.Length % 1024 == 512;
                if (hasCopierHeader)
                {
                    var confirm = _dialog.Confirm(
                        $"SNES-ROM hat einen Copier-Header (512 Byte).\n\nDatei: {Path.GetFileName(path)}\n" +
                        $"Größe: {fileInfo.Length} Bytes ({fileInfo.Length % 1024} Byte Überschuss)\n\n" +
                        "Copier-Header (erste 512 Bytes) entfernen?\n(Backup wird erstellt)", "Header-Reparatur");
                    if (confirm)
                    {
                        var backupPath = path + $".{DateTime.UtcNow:yyyyMMddHHmmss}.bak";
                        File.Copy(path, backupPath, overwrite: false);
                        _vm.AddLog($"Backup erstellt: {backupPath}", "INFO");
                        try
                        {
                            var data = File.ReadAllBytes(path);
                            var trimmed = data[512..];
                            File.WriteAllBytes(path, trimmed);
                            _vm.AddLog($"SNES Copier-Header entfernt: {fileInfo.Length} → {trimmed.Length} Bytes.", "INFO");
                        }
                        catch
                        {
                            File.Copy(backupPath, path, overwrite: true);
                            _vm.AddLog("Reparatur fehlgeschlagen — Backup wiederhergestellt.", "ERROR");
                            throw;
                        }
                    }
                }
                else
                    _dialog.Info($"SNES-ROM hat keinen Copier-Header. Keine Reparatur nötig.\n\n{header.Details}", "Header-Reparatur");
            }
            catch (Exception ex) { LogError("SEC-HEADER", $"Header-Reparatur fehlgeschlagen: {ex.Message}"); }
            return;
        }

        _dialog.ShowText("Header-Reparatur", $"Datei: {Path.GetFileName(path)}\n\nPlattform: {header.Platform}\nFormat: {header.Format}\n{header.Details}\n\nAutomatische Reparatur ist nur für NES und SNES verfügbar.");
    }

    private static string? ResolvePatchFormatForDialog(string patchPath)
    {
        var format = FeatureService.DetectPatchFormat(patchPath);
        if (!string.IsNullOrWhiteSpace(format))
            return format;

        var extension = Path.GetExtension(patchPath).ToLowerInvariant();
        return extension is ".xdelta" or ".xdelta3" or ".vcdiff"
            ? "XDELTA"
            : null;
    }

}
