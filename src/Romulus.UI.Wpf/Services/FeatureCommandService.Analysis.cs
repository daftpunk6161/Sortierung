using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
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
        if (!TryCreateCurrentRunEnvironment(out var materialized, out var environment) || materialized is null || environment is null)
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
                _vm.LastCandidates.Count > 0 ? _vm.LastCandidates.ToArray() : null);

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

                await File.WriteAllTextAsync(safePath, fixDat.XmlContent, Encoding.UTF8);
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

    private void DryRunCompare()
    {
        if (!TryLoadSnapshots(10, out var snapshots, out var collectionIndex) || collectionIndex is null)
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
            var comparison = RunHistoryInsightsService.CompareAsync(collectionIndex, pair[0], pair[1]).GetAwaiter().GetResult();
            if (comparison is null)
            {
                _vm.AddLog("Die ausgewaehlten Runs konnten nicht verglichen werden.", "WARN");
                return;
            }

            var insights = RunHistoryInsightsService.BuildStorageInsightsAsync(collectionIndex, 30).GetAwaiter().GetResult();
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
