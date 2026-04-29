using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;


namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ ANALYSE & BERICHTE ═════════════════════════════════════════════

    private void JunkReport()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var report = FeatureService.BuildJunkReport(_vm.LastCandidates, _vm.AggressiveJunk);
        _dialog.ShowText("Junk-Bericht", report);
    }

    private void RomFilter()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Keine ROM-Daten geladen.", "WARN"); return; }
        var input = _dialog.ShowInputBox("Suchbegriff eingeben (Name, Region, Konsole, Format):", "ROM-Filter", "");
        if (string.IsNullOrWhiteSpace(input)) return;
        var results = FeatureService.SearchRomCollection(_vm.LastCandidates, input);
        var sb = new StringBuilder();
        sb.AppendLine($"ROM-Filter: \"{input}\" → {results.Count} Treffer\n");
        foreach (var r in results.Take(50))
            sb.AppendLine($"  {Path.GetFileName(r.MainPath),-40} [{r.Region}] {r.Extension} {r.Category}");
        if (results.Count > 50)
            sb.AppendLine($"\n  … und {results.Count - 50} weitere");
        _dialog.ShowText("ROM-Filter", sb.ToString());
    }

    private void MissingRom()
    {
        if (!_vm.UseDat || string.IsNullOrWhiteSpace(_vm.DatRoot))
        { _vm.AddLog("DAT muss aktiviert und konfiguriert sein.", "WARN"); return; }
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen DryRun mit aktiviertem DAT starten.", "WARN"); return; }

        var unverified = _vm.LastCandidates.Where(c => !c.DatMatch).ToList();
        if (unverified.Count == 0)
        { _dialog.Info("Alle ROMs haben einen DAT-Match. Keine fehlenden ROMs erkannt.", "Fehlende ROMs"); return; }

        var roots = _vm.Roots.Select(ArtifactPathResolver.NormalizeRoot).ToList();
        string GetSubDir(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            var root = ArtifactPathResolver.FindContainingRoot(filePath, roots);
            if (root is not null)
            {
                var relative = full[(root.Length + 1)..];
                var sep = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
                return sep > 0 ? relative[..sep] : "(Root)";
            }
            return Path.GetDirectoryName(filePath) ?? "(Unbekannt)";
        }
        var byDir = unverified.GroupBy(c => GetSubDir(c.MainPath)).OrderByDescending(g => g.Count()).ToList();

        var sb = new StringBuilder();
        sb.AppendLine("Fehlende ROMs (ohne DAT-Match)");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Gesamt ohne DAT-Match: {unverified.Count} / {_vm.LastCandidates.Count}");
        sb.AppendLine($"\n  Nach Verzeichnis:\n");
        foreach (var g in byDir)
            sb.AppendLine($"    {g.Count(),5}  {g.Key}");
        _dialog.ShowText("Fehlende ROMs", sb.ToString());
    }

    private void HeaderAnalysis()
    {
        var path = _dialog.BrowseFile("ROM für Header-Analyse wählen",
            "ROM-Dateien (*.nes;*.sfc;*.gba;*.z64;*.v64;*.n64;*.smc)|*.nes;*.sfc;*.gba;*.z64;*.v64;*.n64;*.smc|Alle (*.*)|*.*");
        if (path is null) return;
        var header = FeatureService.AnalyzeHeader(path);
        if (header is null)
        { _vm.AddLog($"Header konnte nicht gelesen werden: {path}", "ERROR"); return; }
        _dialog.ShowText("Header-Analyse", $"Datei: {Path.GetFileName(path)}\n\n" +
            $"Plattform: {header.Platform}\nFormat: {header.Format}\nDetails: {header.Details}");
    }

    private async Task CompletenessAsync()
    {
        var (success, materialized, environment) = await TryCreateCurrentRunEnvironmentAsync();
        if (!success || materialized is null || environment is null)
            return;

        using (environment)
        {
            if (environment.DatIndex is null || environment.DatIndex.TotalEntries == 0)
            {
                _vm.AddLog("Keine DAT-Daten verfuegbar. DAT-Verifizierung aktivieren und DatRoot konfigurieren.", "WARN");
                return;
            }

            var report = await CompletenessReportService.BuildAsync(
                environment.DatIndex,
                materialized.Options.Roots.ToArray(),
                environment.CollectionIndex,
                materialized.Options.Extensions.ToArray(),
                _vm.LastCandidates.Count > 0 ? _vm.LastCandidates.ToArray() : null,
                fileSystem: environment.FileSystem);

            _dialog.ShowText("Vollstaendigkeit", CompletenessReportService.FormatReport(report));

            if (!_dialog.Confirm("FixDAT fuer fehlende Spiele erzeugen?", "Vollstaendigkeit"))
                return;

            try
            {
                var generatedUtc = DateTime.UtcNow;
                var datName = $"Romulus-FixDAT-{generatedUtc:yyyyMMdd-HHmmss}";
                var fixDat = DatAnalysisService.BuildFixDatFromCompleteness(environment.DatIndex, report, datName, generatedUtc);

                if (fixDat.MissingGames == 0)
                {
                    _dialog.Info("Keine fehlenden DAT-Eintraege erkannt. Es wurde keine FixDAT-Datei erzeugt.", "FixDAT");
                    return;
                }

                var savePath = _dialog.SaveFile("FixDAT speichern", "DAT (*.dat)|*.dat", $"{datName}.dat");
                if (!TryResolveSafeOutputPath(savePath, "FixDAT-Export", out var safePath))
                    return;

                AtomicFileWriter.WriteAllText(safePath, fixDat.XmlContent, Encoding.UTF8);
                _vm.AddLog($"FixDAT exportiert: {safePath} (Spiele={fixDat.MissingGames}, ROMs={fixDat.MissingRoms})", "INFO");
            }
            catch (OperationCanceledException)
            {
                LogWarning("ANALYSIS-FIXDAT", "FixDAT-Export abgebrochen.");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                LogError("ANALYSIS-FIXDAT", $"FixDAT-Export fehlgeschlagen: {ex.Message}");
                _dialog.Error($"FixDAT-Export fehlgeschlagen:\n\n{ex.Message}", "FixDAT");
            }
        }
    }

    private async Task DryRunCompareAsync()
    {
        var (success, snapshots, collectionIndex) = await TryLoadSnapshotsAsync(10);
        if (!success || collectionIndex is null)
            return;

        using (collectionIndex)
        {
            if (snapshots.Count < 2)
            {
                _vm.AddLog("Mindestens zwei persistierte Runs werden fuer den Vergleich benoetigt.", "WARN");
                return;
            }

            var prompt = BuildRunSnapshotChoicePrompt(snapshots);
            var defaultValue = $"{snapshots[0].RunId} {snapshots[1].RunId}";
            var input = _dialog.ShowInputBox(prompt, "Run-Vergleich", defaultValue);
            var pair = ResolveComparisonPair(input, snapshots);
            var comparison = await RunHistoryInsightsService.CompareAsync(collectionIndex, pair[0], pair[1]);
            if (comparison is null)
            {
                _vm.AddLog("Die ausgewaehlten Runs konnten nicht verglichen werden.", "WARN");
                return;
            }

            var insights = await RunHistoryInsightsService.BuildStorageInsightsAsync(collectionIndex, 30);
            var report = new StringBuilder();
            report.AppendLine("Time-Travel Vergleich");
            report.AppendLine(new string('=', 50));
            report.AppendLine();
            report.AppendLine("Snapshot-Timeline (neueste zuerst):");
            foreach (var snapshot in snapshots.Take(10))
            {
                report.AppendLine($"  {snapshot.RunId,-24} {snapshot.CompletedUtc:yyyy-MM-dd HH:mm}  {snapshot.Status,-22} Files={snapshot.TotalFiles,6} Health={snapshot.HealthScore,3}%");
            }

            report.AppendLine();
            report.AppendLine(RunHistoryInsightsService.FormatComparisonReport(comparison));
            report.AppendLine();
            report.AppendLine(RunHistoryInsightsService.FormatStorageInsightReport(insights));

            _dialog.ShowText("Run-Vergleich", report.ToString());
        }
    }

}

// === merged from FeatureCommandService.Dat.cs (Wave 1 T-W1-UI-REDUCTION) ===
// (namespace dedup — Wave 1 merge)

public sealed partial class FeatureCommandService
{
    // ═══ DAT & VERIFIZIERUNG ════════════════════════════════════════════

    private async Task DatAutoUpdateAsync()
    {
        if (_datUpdateRunning)
        { _vm.AddLog(_vm.Loc["Cmd.DatAutoUpdate.AlreadyRunning"], "WARN"); return; }
        _datUpdateRunning = true;
        try
        {
        if (string.IsNullOrWhiteSpace(_vm.DatRoot))
        { _vm.AddLog(_vm.Loc["Cmd.DatAutoUpdate.DatRootNotConfigured"], "WARN"); return; }
        if (!Directory.Exists(_vm.DatRoot))
        { _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.DatRootMissing", _vm.DatRoot), "ERROR"); return; }
        _vm.AddLog(_vm.Loc["Cmd.DatAutoUpdate.CheckingLocalDats"], "INFO");

        // ── Katalog laden (via DatSourceService) ───────────────────────
        var dataDir = FeatureService.ResolveDataDirectory() ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        var catalog = Infrastructure.Dat.DatSourceService.LoadCatalog(catalogPath);
        if (catalog.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.DatAutoUpdate.EmptyCatalog"], "WARN"); return; }

        // ── Lokale DATs scannen (ein Durchlauf) ────────────────────────
        var datFileSystem = new FileSystemAdapter();
        var localDats = datFileSystem.GetFilesSafe(_vm.DatRoot, [".dat", ".xml"]).ToList();
        foreach (var warning in datFileSystem.ConsumeScanWarnings())
            _vm.AddLog(warning, "WARN");
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
            string.Equals(e.Format, RunConstants.FormatNoIntroPack, StringComparison.OrdinalIgnoreCase);

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

        _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.Status", localDats.Count, staleDats.Count, missing.Count), "INFO");

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
                _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.DownloadStart", toDownload.Count), "INFO");
                using var datHttpClient = DatSourceService.CreateConfiguredHttpClient();
                var strictSidecarValidation = FeatureService.ResolveStrictDatSidecarValidation();
                using var datService = new DatSourceService(_vm.DatRoot, datHttpClient, strictSidecarValidation: strictSidecarValidation);
                int success = 0, failed = 0;
                var failedIds = new List<string>();

                // Gruppiert nach Quelle
                var byGroup = toDownload.GroupBy(t => t.Group).OrderBy(g => g.Key);
                foreach (var group in byGroup)
                {
                    _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.GroupHeader", group.Key, group.Count()), "INFO");
                    foreach (var (id, url, fileName, format, _) in group)
                    {
                        _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.ItemProgress", success + failed + 1, toDownload.Count, id), "DEBUG");
                        try
                        {
                            var result = await datService.DownloadDatByFormatAsync(url, fileName, format);
                            if (result is not null)
                            {
                                success++;
                                _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.ItemSuccess", id), "INFO");
                            }
                            else
                            {
                                failed++;
                                failedIds.Add(id);
                                _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.ItemFailed", id), "WARN");
                            }
                        }
                        catch (InvalidOperationException ex) when (ex.Message.Contains("HTML"))
                        {
                            failed++;
                            failedIds.Add(id);
                            _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.ItemManualRequired", id), "WARN");
                        }
                        catch (Exception ex)
                        {
                            failed++;
                            failedIds.Add(id);
                            LogWarning("DAT-DOWNLOAD", $"{id}: {ex.Message}");
                        }
                    }
                }

                _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.DownloadResult", success, failed), success > 0 ? "INFO" : "WARN");
                if (failedIds.Count > 0 && failedIds.Count <= 10)
                    _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.FailedList", string.Join(", ", failedIds)), "WARN");
            }

            // ── No-Intro Pack-Import ───────────────────────────────────
            if (noIntroMissing.Count > 0)
            {
                var packDir = _dialog.BrowseFolder(_vm.Loc["Cmd.DatAutoUpdate.NoIntroBrowseTitle"]);
                if (!string.IsNullOrWhiteSpace(packDir) && Directory.Exists(packDir))
                {
                    using var datHttpClient = DatSourceService.CreateConfiguredHttpClient();
                    var strictSidecarValidation = FeatureService.ResolveStrictDatSidecarValidation();
                    using var datService = new DatSourceService(_vm.DatRoot, datHttpClient, strictSidecarValidation: strictSidecarValidation);
                    var imported = datService.ImportLocalDatPacks(packDir, catalog);
                    _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.NoIntroImportResult", imported, packDir), imported > 0 ? "INFO" : "WARN");
                }
            }

            // ── Redump lokaler Import ──────────────────────────────────
            if (redumpMissing.Count > 0)
            {
                var redumpDir = _dialog.BrowseFolder(_vm.Loc["Cmd.DatAutoUpdate.RedumpBrowseTitle"]);
                if (!string.IsNullOrWhiteSpace(redumpDir) && Directory.Exists(redumpDir))
                {
                    using var datHttpClient = DatSourceService.CreateConfiguredHttpClient();
                    var strictSidecarValidation = FeatureService.ResolveStrictDatSidecarValidation();
                    using var datService = new DatSourceService(_vm.DatRoot, datHttpClient, strictSidecarValidation: strictSidecarValidation);
                    var imported = datService.ImportLocalDatPacks(redumpDir, catalog);
                    _vm.AddLog(_vm.Loc.Format("Cmd.DatAutoUpdate.RedumpImportResult", imported, redumpDir), imported > 0 ? "INFO" : "WARN");
                }
            }
        }
        else if (missing.Count > 0)
        {
            _dialog.Info(sb.ToString(), "DAT Auto-Update");
        }
        else
        {
            _dialog.Info(_vm.Loc.Format("Cmd.DatAutoUpdate.AllPresent", catalog.Count), _vm.Loc["Cmd.DatAutoUpdate.Title"]);
        }
        }
        finally { _datUpdateRunning = false; }
    }

    private void DatDiffViewer()
    {
        var fileA = _dialog.BrowseFile(_vm.Loc["Cmd.DatDiffViewer.OldDatTitle"], _vm.Loc["Cmd.DatDiffViewer.Filter"]);
        if (fileA is null) return;
        var fileB = _dialog.BrowseFile(_vm.Loc["Cmd.DatDiffViewer.NewDatTitle"], _vm.Loc["Cmd.DatDiffViewer.Filter"]);
        if (fileB is null) return;
        _vm.AddLog(_vm.Loc.Format("Cmd.DatDiffViewer.StartLog", Path.GetFileName(fileA), Path.GetFileName(fileB)), "INFO");
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
            _dialog.ShowText(_vm.Loc["Cmd.DatDiffViewer.Title"], sb.ToString());
        }
        catch (Exception ex)
        {
            LogError("DAT-DIFF", _vm.Loc.Format("Cmd.DatDiffViewer.Error", ex.Message));
            _dialog.ShowText(_vm.Loc["Cmd.DatDiffViewer.Title"], _vm.Loc.Format("Cmd.DatDiffViewer.ParseError", ex.Message));
        }
    }

    private void CustomDatEditor()
    {
        var gameName = _dialog.ShowInputBox(_vm.Loc["Cmd.CustomDatEditor.GameNamePrompt"], _vm.Loc["Cmd.CustomDatEditor.Title"], string.Empty);
        if (string.IsNullOrWhiteSpace(gameName)) return;
        var romName = _dialog.ShowInputBox(_vm.Loc["Cmd.CustomDatEditor.RomNamePrompt"], _vm.Loc["Cmd.CustomDatEditor.Title"], $"{gameName}.zip");
        if (string.IsNullOrWhiteSpace(romName)) return;
        var crc32 = _dialog.ShowInputBox(_vm.Loc["Cmd.CustomDatEditor.CrcPrompt"], _vm.Loc["Cmd.CustomDatEditor.Title"], "00000000");
        if (string.IsNullOrWhiteSpace(crc32)) return;
        if (!Regex.IsMatch(crc32, @"^[0-9A-Fa-f]{8}$", RegexOptions.None, TimeSpan.FromMilliseconds(200)))
        { _vm.AddLog(_vm.Loc.Format("Cmd.CustomDatEditor.InvalidCrc", crc32), "WARN"); return; }
        var sha1 = _dialog.ShowInputBox(_vm.Loc["Cmd.CustomDatEditor.Sha1Prompt"], _vm.Loc["Cmd.CustomDatEditor.Title"], string.Empty);
        if (string.IsNullOrWhiteSpace(sha1)) sha1 = "";
        if (sha1.Length > 0 && !Regex.IsMatch(sha1, @"^[0-9A-Fa-f]{40}$", RegexOptions.None, TimeSpan.FromMilliseconds(200)))
        { _vm.AddLog(_vm.Loc.Format("Cmd.CustomDatEditor.InvalidSha1", sha1), "WARN"); return; }

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

                FeatureService.AppendCustomDatEntry(Path.GetDirectoryName(safeCustomDatPath)!, xmlEntry, _vm.Loc["Cmd.CustomDat.Description"]);
                _vm.AddLog(_vm.Loc.Format("Cmd.CustomDatEditor.EntrySaved", safeCustomDatPath), "INFO");
            }
            catch (Exception ex) { LogError("DAT-CUSTOM", _vm.Loc.Format("Cmd.CustomDatEditor.Error", ex.Message)); }
        }
        else
            _vm.AddLog(_vm.Loc["Cmd.CustomDatEditor.DatRootMissing"], "WARN");
        _dialog.ShowText(_vm.Loc["Cmd.CustomDatEditor.Title"], _vm.Loc.Format("Cmd.CustomDatEditor.GeneratedXml", xmlEntry));
    }

    private void HashDatabaseExport()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.HashDatabaseExport.NoData"], "WARN"); return; }
        var path = _dialog.SaveFile(_vm.Loc["Cmd.HashDatabaseExport.Title"], _vm.Loc["Cmd.HashDatabaseExport.Filter"], "hash-database.json");
        if (!TryResolveSafeOutputPath(path, "Hash-Datenbank-Export", out var safePath)) return;
        var entries = _vm.LastCandidates.Select(c => new { c.MainPath, c.GameKey, c.Extension, c.Region, c.DatMatch, c.SizeBytes }).ToList();
        AtomicFileWriter.WriteAllText(safePath, JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true }), Encoding.UTF8);
        _vm.AddLog(_vm.Loc.Format("Cmd.HashDatabaseExport.Done", safePath, entries.Count), "INFO");
    }

}

// === merged from FeatureCommandService.Collection.cs (Wave 1 T-W1-UI-REDUCTION) ===
// (namespace dedup — Wave 1 merge)

public sealed partial class FeatureCommandService
{
    // ═══ SAMMLUNGSVERWALTUNG ════════════════════════════════════════════

    private void CollectionManager()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var byConsole = _vm.LastCandidates.GroupBy(FeatureService.ResolveConsoleLabel)
            .OrderByDescending(g => g.Count()).ToList();
        var sb = new StringBuilder();
        sb.AppendLine("Smart Collection Manager\n");
        sb.AppendLine($"Gesamt: {_vm.LastCandidates.Count} ROMs\n");
        foreach (var g in byConsole)
            sb.AppendLine($"  {g.Key,-20} {g.Count(),5} ROMs");
        _dialog.ShowText("Smart Collection", sb.ToString());
    }

    private void CloneListViewer()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog("Keine Gruppen vorhanden.", "WARN"); return; }
        _dialog.ShowText("Clone-Liste", FeatureService.BuildCloneTree(_vm.LastDedupeGroups));
    }

    private void VirtualFolderPreview()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        _dialog.ShowText("Virtuelle Ordner", FeatureService.BuildVirtualFolderPreview(_vm.LastCandidates));
    }

    private async Task CollectionMergeAsync()
    {
        var leftRoot = _dialog.BrowseFolder("Linke Sammlung waehlen");
        if (string.IsNullOrWhiteSpace(leftRoot))
            return;

        var rightRoot = _dialog.BrowseFolder("Rechte Sammlung waehlen");
        if (string.IsNullOrWhiteSpace(rightRoot))
            return;

        var targetRoot = _dialog.BrowseFolder("Ziel-Sammlung waehlen");
        if (string.IsNullOrWhiteSpace(targetRoot))
            return;

        var moveDecision = _dialog.YesNoCancel(
            "Soll Romulus die gewaehlten Quellen nach erfolgreicher Verifikation in das Ziel verschieben? 'Nein' erzeugt einen Copy-Merge.",
            "Merge-Modus");
        if (moveDecision == ConfirmResult.Cancel)
            return;

        var mergeRequest = new CollectionMergeRequest
        {
            CompareRequest = BuildCollectionCompareRequest(leftRoot, rightRoot),
            TargetRoot = targetRoot,
            AllowMoves = moveDecision == ConfirmResult.Yes
        };

        using var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath(), msg => _vm.AddLog(msg, "INFO"));
        var build = await CollectionMergeService.BuildPlanAsync(collectionIndex, _fileSystem, mergeRequest);
        if (!build.CanUse || build.Plan is null)
        {
            _vm.AddLog($"[CollectionMerge] Nicht verfuegbar: {build.Reason}", "WARN");
            _dialog.Info(build.Reason ?? "Collection Merge nicht verfuegbar.", "Collection Merge");
            return;
        }

        var planText = FormatCollectionMergePlan(build.Plan);
        _dialog.ShowText("Collection Merge Plan", planText);

        if (!_dialog.DangerConfirm(
                "Collection Merge anwenden",
                "Der Merge-Plan wird jetzt mit Audit-Trail ausgefuehrt. Ohne Bestaetigung bleibt es beim Preview.",
                "MERGE",
                "Merge anwenden"))
        {
            return;
        }

        var applyResult = await CollectionMergeService.ApplyAsync(
            collectionIndex,
            _fileSystem,
            _auditStore,
            new CollectionMergeApplyRequest
            {
                MergeRequest = mergeRequest,
                AuditPath = CollectionMergeService.CreateDefaultAuditPath(targetRoot)
            });

        if (!string.IsNullOrWhiteSpace(applyResult.BlockedReason))
        {
            _vm.AddLog($"[CollectionMerge] Apply blockiert: {applyResult.BlockedReason}", "WARN");
            _dialog.Info(applyResult.BlockedReason, "Collection Merge");
            return;
        }

        _dialog.ShowText("Collection Merge Ergebnis", FormatCollectionMergeApply(applyResult));
    }

    private CollectionCompareRequest BuildCollectionCompareRequest(string leftRoot, string rightRoot)
    {
        var extensions = _vm.GetSelectedExtensions();
        if (extensions.Length == 0)
            extensions = RunOptions.DefaultExtensions;

        return new CollectionCompareRequest
        {
            Left = new CollectionSourceScope
            {
                SourceId = "left",
                Label = Path.GetFileName(leftRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Roots = [leftRoot],
                Extensions = extensions
            },
            Right = new CollectionSourceScope
            {
                SourceId = "right",
                Label = Path.GetFileName(rightRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)),
                Roots = [rightRoot],
                Extensions = extensions
            },
            Limit = 500
        };
    }

    internal static string FormatCollectionMergePlan(CollectionMergePlan plan)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Collection Merge Plan");
        sb.AppendLine();
        sb.AppendLine($"Target: {plan.Request.TargetRoot}");
        sb.AppendLine($"AllowMoves: {plan.Request.AllowMoves}");
        sb.AppendLine();
        sb.AppendLine($"Total: {plan.Summary.TotalEntries}");
        sb.AppendLine($"Copy: {plan.Summary.CopyToTarget}");
        sb.AppendLine($"Move: {plan.Summary.MoveToTarget}");
        sb.AppendLine($"Keep existing: {plan.Summary.KeepExistingTarget}");
        sb.AppendLine($"Skip duplicate: {plan.Summary.SkipAsDuplicate}");
        sb.AppendLine($"Review: {plan.Summary.ReviewRequired}");
        sb.AppendLine($"Blocked: {plan.Summary.Blocked}");
        sb.AppendLine();
        foreach (var entry in plan.Entries.Take(25))
            sb.AppendLine($"[{entry.Decision}] {entry.DiffKey} -> {entry.TargetPath ?? "-"} ({entry.ReasonCode})");

        if (plan.Entries.Count > 25)
            sb.AppendLine($"... und {plan.Entries.Count - 25} weitere Eintraege");

        return sb.ToString();
    }

    internal static string FormatCollectionMergeApply(CollectionMergeApplyResult result)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Collection Merge Ergebnis");
        sb.AppendLine();
        sb.AppendLine($"Applied: {result.Summary.Applied}");
        sb.AppendLine($"Copied: {result.Summary.Copied}");
        sb.AppendLine($"Moved: {result.Summary.Moved}");
        sb.AppendLine($"Keep existing: {result.Summary.KeptExistingTarget}");
        sb.AppendLine($"Skip duplicate: {result.Summary.SkippedAsDuplicate}");
        sb.AppendLine($"Review: {result.Summary.ReviewRequired}");
        sb.AppendLine($"Blocked: {result.Summary.Blocked}");
        sb.AppendLine($"Failed: {result.Summary.Failed}");
        if (!string.IsNullOrWhiteSpace(result.AuditPath))
            sb.AppendLine($"Audit: {result.AuditPath}");
        sb.AppendLine();
        foreach (var entry in result.Entries.Take(25))
            sb.AppendLine($"[{entry.Outcome}] {entry.DiffKey} -> {entry.TargetPath ?? "-"} ({entry.ReasonCode})");

        if (result.Entries.Count > 25)
            sb.AppendLine($"... und {result.Entries.Count - 25} weitere Eintraege");

        return sb.ToString();
    }

}

// === merged from FeatureCommandService.Export.cs (Wave 1 T-W1-UI-REDUCTION) ===
// (namespace dedup — Wave 1 merge)

public sealed partial class FeatureCommandService
{
    // ═══ EXPORT & INTEGRATION ═══════════════════════════════════════════

    private void HtmlReport()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var path = _dialog.SaveFile("HTML-Report speichern", "HTML (*.html)|*.html", "report.html");
        if (!TryResolveSafeOutputPath(path, "HTML-Report", out var safePath)) return;

        try
        {
            // Wave-2 F-07: report writing centralised in IResultExportService.
            var result = _resultExport.WriteHtmlReport(safePath, _vm);
            if (!result.Success) return;

            _vm.AddLog($"Report erstellt: {result.Path} (Im Browser drucken → PDF) [{result.ChannelUsed}]", "INFO");
            TryOpenWithShell(result.Path, "Report");
        }
        catch (Exception ex) { LogError("GUI-REPORT", $"Report-Fehler: {ex.Message}"); }
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

            AtomicFileWriter.CopyFile(path, safeTargetPath, overwrite: true);
            _vm.AddLog($"DAT importiert nach: {safeTargetPath}", "INFO");
            _dialog.Info($"DAT erfolgreich importiert:\n\n  Quelle: {path}\n  Ziel: {safeTargetPath}", "DAT-Import");
        }
        catch (Exception ex) { LogError("DAT-IMPORT", $"DAT-Import fehlgeschlagen: {ex.Message}"); }
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
        AtomicFileWriter.WriteAllText(safePath, dupeCsv, Encoding.UTF8);
        _vm.AddLog(_vm.Loc.Format("Cmd.DupeExported", safePath, losers.Count), "INFO");
    }
}
