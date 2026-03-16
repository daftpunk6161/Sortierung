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
    // ═══ ANALYSE & BERICHTE ═════════════════════════════════════════════

    private void ConversionEstimate()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten, um Konvertierungs-Schätzungen zu berechnen.", "WARN"); return; }
        var est = FeatureService.GetConversionEstimate(_vm.LastCandidates);
        var sb = new StringBuilder();
        sb.AppendLine("Konvertierungs-Schätzung");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"  Quellgröße:     {FeatureService.FormatSize(est.TotalSourceBytes)}");
        sb.AppendLine($"  Geschätzt:      {FeatureService.FormatSize(est.EstimatedTargetBytes)}");
        sb.AppendLine($"  Ersparnis:      {FeatureService.FormatSize(est.SavedBytes)} ({(1 - est.CompressionRatio) * 100:F1}%)");
        sb.AppendLine($"\nDetails ({est.Details.Count} konvertierbare Dateien):");
        foreach (var d in est.Details.Take(20))
            sb.AppendLine($"  {d.FileName}: {d.SourceFormat}→{d.TargetFormat} ({FeatureService.FormatSize(d.SourceBytes)}→{FeatureService.FormatSize(d.EstimatedBytes)})");
        if (est.Details.Count > 20)
            sb.AppendLine($"  … und {est.Details.Count - 20} weitere");
        _dialog.ShowText("Konvertierungs-Schätzung", sb.ToString());
    }

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

    private void DuplicateHeatmap()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog("Keine Deduplizierungs-Daten vorhanden.", "WARN"); return; }
        var heatmap = FeatureService.GetDuplicateHeatmap(_vm.LastDedupeGroups);
        var sb = new StringBuilder();
        sb.AppendLine("Duplikat-Heatmap (nach Konsole)\n");
        foreach (var h in heatmap)
        {
            var bar = new string('█', (int)(h.DuplicatePercent / 5));
            sb.AppendLine($"  {h.Console,-25} {h.Duplicates,4} Dupes ({h.DuplicatePercent:F1}%) {bar}");
        }
        _dialog.ShowText("Duplikat-Heatmap", sb.ToString());
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

        var roots = _vm.Roots.Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();
        string GetSubDir(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            foreach (var root in roots)
            {
                if (full.Length > root.Length && full.StartsWith(root, StringComparison.OrdinalIgnoreCase) && full[root.Length] is '\\' or '/')
                {
                    var relative = full[(root.Length + 1)..];
                    var sep = relative.IndexOfAny([Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar]);
                    return sep > 0 ? relative[..sep] : "(Root)";
                }
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

    private void CrossRootDupe()
    {
        if (_vm.Roots.Count < 2)
        { _vm.AddLog("Mindestens 2 Root-Ordner für Cross-Root-Duplikate erforderlich.", "WARN"); return; }
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog("Keine Deduplizierungs-Daten vorhanden. Erst einen DryRun starten.", "WARN"); return; }

        var roots = _vm.Roots.Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();
        string? GetRoot(string filePath)
        {
            var full = Path.GetFullPath(filePath);
            return roots.FirstOrDefault(r => full.Length > r.Length && full.StartsWith(r, StringComparison.OrdinalIgnoreCase) && full[r.Length] is '\\' or '/');
        }

        var crossRootGroups = new List<DedupeResult>();
        foreach (var g in _vm.LastDedupeGroups)
        {
            var allPaths = new[] { g.Winner }.Concat(g.Losers);
            var distinctRoots = allPaths.Select(c => GetRoot(c.MainPath)).Where(r => r is not null).Distinct().Count();
            if (distinctRoots > 1) crossRootGroups.Add(g);
        }

        var sb = new StringBuilder();
        sb.AppendLine("Cross-Root-Duplikate");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Roots: {_vm.Roots.Count}");
        sb.AppendLine($"  Dedupe-Gruppen gesamt: {_vm.LastDedupeGroups.Count}");
        sb.AppendLine($"  Cross-Root-Gruppen: {crossRootGroups.Count}\n");
        foreach (var g in crossRootGroups.Take(30))
        {
            sb.AppendLine($"  [{g.GameKey}]");
            sb.AppendLine($"    Winner: {g.Winner.MainPath}");
            foreach (var l in g.Losers) sb.AppendLine($"    Loser:  {l.MainPath}");
        }
        if (crossRootGroups.Count > 30) sb.AppendLine($"\n  … und {crossRootGroups.Count - 30} weitere Gruppen");
        if (crossRootGroups.Count == 0) sb.AppendLine("  Keine Cross-Root-Duplikate gefunden.");
        _dialog.ShowText("Cross-Root-Duplikate", sb.ToString());
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

    private void Completeness()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var verified = _vm.LastCandidates.Count(c => c.DatMatch);
        var total = _vm.LastCandidates.Count;
        var pct = total > 0 ? 100.0 * verified / total : 0;
        _dialog.ShowText("Vollständigkeit", $"Sammlungs-Vollständigkeit\n\n" +
            $"Verifizierte Dateien: {verified} / {total} ({pct:F1}%)\n\n" +
            $"Für eine DAT-basierte Vollständigkeitsanalyse\naktiviere DAT-Verifizierung und starte einen DryRun.");
    }

    private void DryRunCompare()
    {
        var fileA = _dialog.BrowseFile("Ersten DryRun-Report wählen", "CSV (*.csv)|*.csv|HTML (*.html)|*.html");
        var fileB = fileA is not null ? _dialog.BrowseFile("Zweiten DryRun-Report wählen", "CSV (*.csv)|*.csv|HTML (*.html)|*.html") : null;
        if (fileA is null || fileB is null) return;
        _vm.AddLog($"DryRun-Vergleich: {Path.GetFileName(fileA)} vs. {Path.GetFileName(fileB)}", "INFO");

        if (fileA.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) &&
            fileB.EndsWith(".csv", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                var setA = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var setB = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var line in File.ReadLines(fileA).Skip(1))
                { var mainPath = FeatureService.ExtractFirstCsvField(line); if (!string.IsNullOrWhiteSpace(mainPath)) setA.Add(mainPath); }
                foreach (var line in File.ReadLines(fileB).Skip(1))
                { var mainPath = FeatureService.ExtractFirstCsvField(line); if (!string.IsNullOrWhiteSpace(mainPath)) setB.Add(mainPath); }

                var added = setB.Except(setA).ToList();
                var removed = setA.Except(setB).ToList();
                var same = setA.Intersect(setB).Count();

                var sb = new StringBuilder();
                sb.AppendLine("DryRun-Vergleich (CSV)");
                sb.AppendLine(new string('═', 50));
                sb.AppendLine($"\n  A: {Path.GetFileName(fileA)} ({setA.Count} Einträge)");
                sb.AppendLine($"  B: {Path.GetFileName(fileB)} ({setB.Count} Einträge)");
                sb.AppendLine($"\n  Gleich:     {same}");
                sb.AppendLine($"  Hinzugefügt: {added.Count}");
                sb.AppendLine($"  Entfernt:    {removed.Count}");
                if (added.Count > 0)
                {
                    sb.AppendLine($"\n  --- Hinzugefügt (erste {Math.Min(30, added.Count)}) ---");
                    foreach (var entry in added.Take(30)) sb.AppendLine($"    + {Path.GetFileName(entry)}");
                    if (added.Count > 30) sb.AppendLine($"    … und {added.Count - 30} weitere");
                }
                if (removed.Count > 0)
                {
                    sb.AppendLine($"\n  --- Entfernt (erste {Math.Min(30, removed.Count)}) ---");
                    foreach (var entry in removed.Take(30)) sb.AppendLine($"    - {Path.GetFileName(entry)}");
                    if (removed.Count > 30) sb.AppendLine($"    … und {removed.Count - 30} weitere");
                }
                _dialog.ShowText("DryRun-Vergleich", sb.ToString());
            }
            catch (Exception ex) { LogError("GUI-DRYRUN", $"DryRun-Vergleich Fehler: {ex.Message}"); }
        }
        else
        {
            _dialog.ShowText("DryRun-Vergleich", $"Vergleich:\n  A: {fileA}\n  B: {fileB}\n\n" +
                "Detaillierter Vergleich erfordert CSV-Reports.\nExportiere Reports als CSV und vergleiche erneut.");
        }
    }

    private void TrendAnalysis()
    {
        if (_vm.LastCandidates.Count > 0)
        {
            var dupes = _vm.LastDedupeGroups.Sum(g => g.Losers.Count);
            var junk = _vm.LastCandidates.Count(c => c.Category == "JUNK");
            var verified = _vm.LastCandidates.Count(c => c.DatMatch);
            var totalSize = _vm.LastCandidates.Sum(c => c.SizeBytes);
            FeatureService.SaveTrendSnapshot(_vm.LastCandidates.Count, totalSize, verified, dupes, junk);
        }
        var history = FeatureService.LoadTrendHistory();
        var report = FeatureService.FormatTrendReport(history);
        _dialog.ShowText("Trend-Analyse", report);
    }

    private void EmulatorCompat()
    {
        _dialog.ShowText("Emulator-Kompatibilität", FeatureService.FormatEmulatorCompat());
    }

}
