using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ DAT & VERIFIZIERUNG ════════════════════════════════════════════

    private async Task DatAutoUpdateAsync()
    {
        if (_datUpdateRunning)
        { _vm.AddLog("DAT-Update läuft bereits.", "WARN"); return; }
        _datUpdateRunning = true;
        try
        {
        if (string.IsNullOrWhiteSpace(_vm.DatRoot))
        { _vm.AddLog("DAT-Root nicht konfiguriert.", "WARN"); return; }
        if (!Directory.Exists(_vm.DatRoot))
        { _vm.AddLog($"DAT-Root existiert nicht: {_vm.DatRoot}", "ERROR"); return; }
        _vm.AddLog("DAT Auto-Update: Prüfe lokale DAT-Dateien…", "INFO");

        // ── Katalog laden (via DatSourceService) ───────────────────────
        var dataDir = FeatureService.ResolveDataDirectory() ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        var catalog = Infrastructure.Dat.DatSourceService.LoadCatalog(catalogPath);
        if (catalog.Count == 0)
        { _vm.AddLog("DAT-Katalog leer oder nicht gefunden.", "WARN"); return; }

        // ── Lokale DATs scannen (ein Durchlauf) ────────────────────────
        var localDats = Directory.GetFiles(_vm.DatRoot, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var localNames = new HashSet<string>(
            localDats.Select(d => Path.GetFileNameWithoutExtension(d)!.ToUpperInvariant()),
            StringComparer.OrdinalIgnoreCase);

        // ── Klassifizierung ────────────────────────────────────────────
        var staleThresholdDays = DatCatalogStateService.StaleThresholdDays;
        var staleDats = localDats.Where(d => (DateTime.Now - File.GetLastWriteTime(d)).TotalDays > staleThresholdDays).ToList();
        var freshCount = localDats.Count - staleDats.Count;

        var missing = catalog
            .Where(e => !localNames.Contains(e.Id.ToUpperInvariant())
                     && !localNames.Contains(e.ConsoleKey.ToUpperInvariant()))
            .ToList();

        bool CanAutoDownload(Infrastructure.Dat.DatCatalogEntry e) =>
            !string.IsNullOrWhiteSpace(e.Url);

        bool IsNoIntroPack(Infrastructure.Dat.DatCatalogEntry e) =>
            string.Equals(e.Format, "nointro-pack", StringComparison.OrdinalIgnoreCase);

        // Redump requires login → exclude from auto-download
        bool IsRedump(Infrastructure.Dat.DatCatalogEntry e) =>
            e.Group.Equals("Redump", StringComparison.OrdinalIgnoreCase);

        var autoMissing = missing.Where(e => CanAutoDownload(e) && !IsNoIntroPack(e) && !IsRedump(e)).ToList();
        var noIntroMissing = missing.Where(IsNoIntroPack).ToList();
        var otherManual = missing.Where(e => !CanAutoDownload(e) && !IsNoIntroPack(e)).ToList();

        // ── Download-Liste vorbereiten ─────────────────────────────────
        var toDownload = new List<(string Id, string Url, string FileName, string Format, string Group)>();

        foreach (var entry in autoMissing)
            toDownload.Add((entry.Id, entry.Url, entry.Id + ".dat", entry.Format, entry.Group));

        foreach (var staleFile in staleDats)
        {
            var stem = Path.GetFileNameWithoutExtension(staleFile)!;
            var entry = catalog.FirstOrDefault(e =>
                e.Id.Equals(stem, StringComparison.OrdinalIgnoreCase)
                || e.ConsoleKey.Equals(stem, StringComparison.OrdinalIgnoreCase));
            if (entry is not null && CanAutoDownload(entry) && !IsNoIntroPack(entry) && !IsRedump(entry))
                toDownload.Add((entry.Id, entry.Url, Path.GetFileName(staleFile), entry.Format, entry.Group));
        }

        _vm.AddLog($"DAT-Status: {localDats.Count} DATs, {staleDats.Count} veraltet, {missing.Count} fehlend", "INFO");

        // ── Dialog bauen ───────────────────────────────────────────────
        var sb = new StringBuilder();

        // Zeile 1: Kompakt-Status
        if (localDats.Count == 0)
            sb.AppendLine("Keine lokalen DATs gefunden.");
        else
        {
            sb.Append($"Aktuell: {freshCount}");
            if (staleDats.Count > 0)
                sb.Append($"  ·  Veraltet (>{staleThresholdDays}d): {staleDats.Count}");
            sb.AppendLine();
        }

        // Zeile 2: Fehlend nach Gruppe
        if (missing.Count > 0)
        {
            sb.AppendLine($"\n{missing.Count} DATs fehlen lokal:");
            var byGroup = missing.GroupBy(e => e.Group).OrderBy(g => g.Key);
            foreach (var g in byGroup)
            {
                var autoCount = g.Count(e => CanAutoDownload(e) && !IsNoIntroPack(e));
                var packCount = g.Count(IsNoIntroPack);
                var manualOnly = g.Key.Equals("Redump", StringComparison.OrdinalIgnoreCase);
                if (autoCount > 0 && manualOnly)
                    sb.AppendLine($"  {g.Key}: {autoCount} (erfordert Login auf redump.org → manuell herunterladen)");
                else if (autoCount > 0)
                    sb.AppendLine($"  {g.Key}: {autoCount} automatisch ladbar");
                if (packCount > 0) sb.AppendLine($"  {g.Key}: {packCount} via lokalem Pack-Import");
            }
        }

        // ── No-Intro Pack-Import anbieten ──────────────────────────────
        if (noIntroMissing.Count > 0)
        {
            sb.AppendLine($"\n{noIntroMissing.Count} No-Intro DATs können aus lokalem Ordner importiert werden.");
            sb.AppendLine("Lade DAT-Packs von datomatic.no-intro.org herunter und wähle den Ordner.");
        }

        // ── Redump-Hinweis ─────────────────────────────────────────────
        var redumpMissing = missing.Where(e => e.Group.Equals("Redump", StringComparison.OrdinalIgnoreCase)).ToList();
        if (redumpMissing.Count > 0)
        {
            sb.AppendLine($"\nHinweis: Redump-DATs erfordern manuellen Download:");
            sb.AppendLine("  1. Einloggen auf redump.org");
            sb.AppendLine("  2. ZIP-Dateien herunterladen und entpacken");
            sb.AppendLine($"  3. .dat-Dateien nach {_vm.DatRoot} kopieren");
        }

        // ── Download oder nur Info ─────────────────────────────────────
        bool hasWork = toDownload.Count > 0 || noIntroMissing.Count > 0 || redumpMissing.Count > 0;
        if (hasWork)
        {
            if (toDownload.Count > 0)
                sb.AppendLine($"\n{toDownload.Count} DATs jetzt herunterladen? (FBNEO, Non-Redump u.a.)");
            if (noIntroMissing.Count > 0)
                sb.AppendLine("No-Intro Packs aus lokalem Ordner importieren?");
            if (redumpMissing.Count > 0)
                sb.AppendLine($"Redump-DATs aus lokalem Ordner importieren? ({redumpMissing.Count} fehlend)");

            if (!_dialog.Confirm(sb.ToString(), "DAT Auto-Update"))
                return;

            // ── Auto-Downloads ─────────────────────────────────────────
            if (toDownload.Count > 0)
            {
                _vm.AddLog($"DAT-Download: {toDownload.Count} Dateien…", "INFO");
                using var datService = new Infrastructure.Dat.DatSourceService(_vm.DatRoot);
                int success = 0, failed = 0;
                var failedIds = new List<string>();

                // Gruppiert nach Quelle
                var byGroup = toDownload.GroupBy(t => t.Group).OrderBy(g => g.Key);
                foreach (var group in byGroup)
                {
                    _vm.AddLog($"── {group.Key} ({group.Count()} DATs) ──", "INFO");
                    foreach (var (id, url, fileName, format, _) in group)
                    {
                        _vm.AddLog($"  [{success + failed + 1}/{toDownload.Count}] {id}…", "DEBUG");
                        try
                        {
                            var result = await datService.DownloadDatByFormatAsync(url, fileName, format);
                            if (result is not null)
                            {
                                success++;
                                _vm.AddLog($"  ✓ {id}", "INFO");
                            }
                            else
                            {
                                failed++;
                                failedIds.Add(id);
                                _vm.AddLog($"  ✗ {id}: Download fehlgeschlagen", "WARN");
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.Contains("HTML"))
                        {
                            failed++;
                            failedIds.Add(id);
                            _vm.AddLog($"  ✗ {id}: Quelle erfordert manuellen Download (Login-Seite erhalten)", "WARN");
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            failedIds.Add(id);
                            LogWarning("DAT-DOWNLOAD", $"{id}: {ex.Message}");
                        }
                    }
                }

                _vm.AddLog($"DAT-Download: {success} erfolgreich, {failed} fehlgeschlagen", success > 0 ? "INFO" : "WARN");
                if (failedIds.Count > 0 && failedIds.Count <= 10)
                    _vm.AddLog($"  Fehlgeschlagen: {string.Join(", ", failedIds)}", "WARN");
            }

            // ── No-Intro Pack-Import ───────────────────────────────────
            if (noIntroMissing.Count > 0)
            {
                var packDir = _dialog.BrowseFolder("No-Intro DAT-Pack Ordner auswählen (enthält .dat/.xml Dateien)");
                if (!string.IsNullOrWhiteSpace(packDir) && Directory.Exists(packDir))
                {
                    using var datService = new Infrastructure.Dat.DatSourceService(_vm.DatRoot);
                    var imported = datService.ImportLocalDatPacks(packDir, catalog);
                    _vm.AddLog($"No-Intro Pack-Import: {imported} DATs importiert aus {packDir}", imported > 0 ? "INFO" : "WARN");
                }
            }

            // ── Redump lokaler Import ──────────────────────────────────
            if (redumpMissing.Count > 0)
            {
                var redumpDir = _dialog.BrowseFolder("Redump DAT-Ordner auswählen (enthält .dat/.xml Dateien von redump.org)");
                if (!string.IsNullOrWhiteSpace(redumpDir) && Directory.Exists(redumpDir))
                {
                    using var datService = new Infrastructure.Dat.DatSourceService(_vm.DatRoot);
                    var imported = datService.ImportLocalDatPacks(redumpDir, catalog);
                    _vm.AddLog($"Redump Import: {imported} DATs importiert aus {redumpDir}", imported > 0 ? "INFO" : "WARN");
                }
            }
        }
        else if (missing.Count > 0)
        {
            _dialog.Info(sb.ToString(), "DAT Auto-Update");
        }
        else
        {
            _dialog.Info($"Alle {catalog.Count} Katalog-DATs vorhanden.", "DAT Auto-Update");
        }
        }
        finally { _datUpdateRunning = false; }
    }

    private void DatDiffViewer()
    {
        var fileA = _dialog.BrowseFile("Alte DAT-Datei wählen", "DAT (*.dat;*.xml)|*.dat;*.xml");
        if (fileA is null) return;
        var fileB = _dialog.BrowseFile("Neue DAT-Datei wählen", "DAT (*.dat;*.xml)|*.dat;*.xml");
        if (fileB is null) return;
        _vm.AddLog($"DAT-Diff: {Path.GetFileName(fileA)} vs. {Path.GetFileName(fileB)}", "INFO");
        try
        {
            var diff = FeatureService.CompareDatFiles(fileA, fileB);
            var sb = new StringBuilder();
            sb.AppendLine("DAT-Diff-Viewer (Logiqx XML)");
            sb.AppendLine(new string('═', 50));
            sb.AppendLine($"\n  A: {Path.GetFileName(fileA)}");
            sb.AppendLine($"  B: {Path.GetFileName(fileB)}");
            sb.AppendLine($"\n  Gleich:       {diff.UnchangedCount}");
            sb.AppendLine($"  Geändert:     {diff.ModifiedCount}");
            sb.AppendLine($"  Hinzugefügt:  {diff.Added.Count}");
            sb.AppendLine($"  Entfernt:     {diff.Removed.Count}");
            if (diff.Added.Count > 0)
            {
                sb.AppendLine($"\n  --- Hinzugefügt (erste {Math.Min(30, diff.Added.Count)}) ---");
                foreach (var name in diff.Added.Take(30)) sb.AppendLine($"    + {name}");
                if (diff.Added.Count > 30) sb.AppendLine($"    … und {diff.Added.Count - 30} weitere");
            }
            if (diff.Removed.Count > 0)
            {
                sb.AppendLine($"\n  --- Entfernt (erste {Math.Min(30, diff.Removed.Count)}) ---");
                foreach (var name in diff.Removed.Take(30)) sb.AppendLine($"    - {name}");
                if (diff.Removed.Count > 30) sb.AppendLine($"    … und {diff.Removed.Count - 30} weitere");
            }
            _dialog.ShowText("DAT-Diff-Viewer", sb.ToString());
        }
        catch (Exception ex)
        {
            LogError("DAT-DIFF", $"DAT-Diff Fehler: {ex.Message}");
            _dialog.ShowText("DAT-Diff-Viewer", $"Fehler beim Parsen der DAT-Dateien:\n\n{ex.Message}\n\nStelle sicher, dass beide Dateien gültiges Logiqx-XML enthalten.");
        }
    }

    private void CustomDatEditor()
    {
        var gameName = _dialog.ShowInputBox("Spielname eingeben:", "Custom-DAT-Editor", "");
        if (string.IsNullOrWhiteSpace(gameName)) return;
        var romName = _dialog.ShowInputBox("ROM-Dateiname eingeben:", "Custom-DAT-Editor", $"{gameName}.zip");
        if (string.IsNullOrWhiteSpace(romName)) return;
        var crc32 = _dialog.ShowInputBox("CRC32-Hash eingeben (hex):", "Custom-DAT-Editor", "00000000");
        if (string.IsNullOrWhiteSpace(crc32)) return;
        if (!Regex.IsMatch(crc32, @"^[0-9A-Fa-f]{8}$", RegexOptions.None, TimeSpan.FromMilliseconds(200)))
        { _vm.AddLog($"Ungültiger CRC32-Hash: '{crc32}' — erwartet: 8 Hex-Zeichen.", "WARN"); return; }
        var sha1 = _dialog.ShowInputBox("SHA1-Hash eingeben (hex):", "Custom-DAT-Editor", "");
        if (string.IsNullOrWhiteSpace(sha1)) sha1 = "";
        if (sha1.Length > 0 && !Regex.IsMatch(sha1, @"^[0-9A-Fa-f]{40}$", RegexOptions.None, TimeSpan.FromMilliseconds(200)))
        { _vm.AddLog($"Ungültiger SHA1-Hash: '{sha1}' — erwartet: 40 Hex-Zeichen.", "WARN"); return; }

        var xmlEntry = $"  <game name=\"{System.Security.SecurityElement.Escape(gameName)}\">\n" +
                       $"    <description>{System.Security.SecurityElement.Escape(gameName)}</description>\n" +
                       $"    <rom name=\"{System.Security.SecurityElement.Escape(romName)}\" size=\"0\" crc=\"{crc32}\"" +
                       (sha1.Length > 0 ? $" sha1=\"{sha1}\"" : "") + " />\n  </game>";

        if (!string.IsNullOrWhiteSpace(_vm.DatRoot) && Directory.Exists(_vm.DatRoot))
        {
            try
            {
                var customDatPath = Path.Combine(_vm.DatRoot, "custom.dat");
                if (!TryResolveSafeOutputPath(customDatPath, "Custom-DAT", out var safeCustomDatPath))
                    return;

                if (File.Exists(safeCustomDatPath))
                {
                    var content = File.ReadAllText(safeCustomDatPath);
                    var closeTag = "</datafile>";
                    var idx = content.LastIndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
                    if (idx >= 0) content = content[..idx] + xmlEntry + "\n" + closeTag;
                    else content += "\n" + xmlEntry;
                    var tempPath = safeCustomDatPath + ".tmp";
                    if (!TryResolveSafeOutputPath(tempPath, "Custom-DAT", out var safeTempPath))
                        return;

                    File.WriteAllText(safeTempPath, content);
                    File.Move(safeTempPath, safeCustomDatPath, overwrite: true);
                }
                else
                {
                    var fullXml = "<?xml version=\"1.0\"?>\n" +
                                  "<!DOCTYPE datafile SYSTEM \"http://www.logiqx.com/Dats/datafile.dtd\">\n" +
                                  "<datafile>\n  <header>\n    <name>Custom DAT</name>\n" +
                                  "    <description>Benutzerdefinierte DAT-Einträge</description>\n  </header>\n" +
                                  xmlEntry + "\n</datafile>";
                    File.WriteAllText(safeCustomDatPath, fullXml);
                }
                _vm.AddLog($"Custom-DAT-Eintrag gespeichert: {safeCustomDatPath}", "INFO");
            }
            catch (Exception ex) { LogError("DAT-CUSTOM", $"Custom-DAT Fehler: {ex.Message}"); }
        }
        else
            _vm.AddLog("DatRoot nicht gesetzt – Eintrag wird nur angezeigt.", "WARN");
        _dialog.ShowText("Custom-DAT-Editor", $"Generierter Logiqx-XML-Eintrag:\n\n{xmlEntry}");
    }

    private void HashDatabaseExport()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Keine Daten für Hash-Export.", "WARN"); return; }
        var path = _dialog.SaveFile("Hash-Datenbank exportieren", "JSON (*.json)|*.json", "hash-database.json");
        if (!TryResolveSafeOutputPath(path, "Hash-Datenbank-Export", out var safePath)) return;
        var entries = _vm.LastCandidates.Select(c => new { c.MainPath, c.GameKey, c.Extension, c.Region, c.DatMatch, c.SizeBytes }).ToList();
        File.WriteAllText(safePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }));
        _vm.AddLog($"Hash-Datenbank exportiert: {safePath} ({entries.Count} Einträge)", "INFO");
    }

}
