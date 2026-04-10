using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ WORKFLOW & AUTOMATISIERUNG ═════════════════════════════════════

    private void FilterBuilder()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var input = _dialog.ShowInputBox(
            "Filter-Ausdruck eingeben (Feld=Wert, Feld>Wert, Feld<Wert):\n\nBeispiele:\n  region=US\n  category=JUNK\n  sizemb>100\n  extension=.chd\n  datmatch=true",
            "Filter-Builder", "region=US");
        if (string.IsNullOrWhiteSpace(input)) return;

        string field, op, value;
        if (input.Contains(">=")) { var p = input.Split(">=", 2); field = p[0].Trim().ToLowerInvariant(); op = ">="; value = p[1].Trim(); }
        else if (input.Contains("<=")) { var p = input.Split("<=", 2); field = p[0].Trim().ToLowerInvariant(); op = "<="; value = p[1].Trim(); }
        else if (input.Contains('>')) { var p = input.Split('>', 2); field = p[0].Trim().ToLowerInvariant(); op = ">"; value = p[1].Trim(); }
        else if (input.Contains('<')) { var p = input.Split('<', 2); field = p[0].Trim().ToLowerInvariant(); op = "<"; value = p[1].Trim(); }
        else if (input.Contains('=')) { var p = input.Split('=', 2); field = p[0].Trim().ToLowerInvariant(); op = "="; value = p[1].Trim(); }
        else { _vm.AddLog($"Ungültiger Filter-Ausdruck: {input}", "WARN"); return; }

        var filtered = _vm.LastCandidates.Where(c =>
        {
            string fieldValue = field switch
            {
                "region" => c.Region, "category" => FeatureService.ToCategoryLabel(c.Category), "extension" or "ext" => c.Extension,
                "gamekey" or "game" => c.GameKey, "type" or "consolekey" or "console" => c.ConsoleKey,
                "datmatch" or "dat" => c.DatMatch.ToString(),
                "sizemb" => (c.SizeBytes / 1048576.0).ToString("F1"),
                "sizebytes" or "size" => c.SizeBytes.ToString(),
                "filename" or "name" => Path.GetFileName(c.MainPath), _ => ""
            };
            if (op == "=") return fieldValue.Contains(value, StringComparison.OrdinalIgnoreCase);
            if (double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var numVal) &&
                double.TryParse(fieldValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var fieldNum))
                return op switch { ">" => fieldNum > numVal, "<" => fieldNum < numVal, ">=" => fieldNum >= numVal, "<=" => fieldNum <= numVal, _ => false };
            return false;
        }).ToList();

        var sb = new StringBuilder();
        sb.AppendLine($"Filter-Builder: {field} {op} {value}");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  Gesamt: {_vm.LastCandidates.Count}");
        sb.AppendLine($"  Gefiltert: {filtered.Count}\n");
        foreach (var r in filtered.Take(50))
            sb.AppendLine($"  {Path.GetFileName(r.MainPath),-45} [{r.Region}] {r.Extension} {r.Category} {FeatureService.FormatSize(r.SizeBytes)}");
        if (filtered.Count > 50)
            sb.AppendLine($"\n  … und {filtered.Count - 50} weitere");
        _dialog.ShowText("Filter-Builder", sb.ToString());
    }

    private void SortTemplates()
    {
        var templates = FeatureService.GetSortTemplates();
        var sb = new StringBuilder();
        sb.AppendLine("Sortierungs-Vorlagen\n");
        foreach (var (name, pattern) in templates)
            sb.AppendLine($"  {name,-20} → {pattern}");
        sb.AppendLine("\n  Legende: {console} = Konsolenname, {filename} = Dateiname");
        _dialog.ShowText("Sort-Templates", sb.ToString());
    }

    private void PipelineEngine()
    {
        if (_vm.LastRunResult is not null)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Pipeline-Engine — Letzter Lauf");
            sb.AppendLine(new string('═', 50));
            sb.AppendLine($"\n  Status: {_vm.LastRunResult.Status}");
            sb.AppendLine($"  Dauer:  {_vm.LastRunResult.DurationMs / 1000.0:F1}s\n");
            sb.AppendLine($"  {"Phase",-20} {"Status",-15} {"Details"}");
            sb.AppendLine($"  {new string('-', 20)} {new string('-', 15)} {new string('-', 30)}");
            sb.AppendLine($"  {"Scan",-20} {"OK",-15} {_vm.LastRunResult.TotalFilesScanned} Dateien");
            sb.AppendLine($"  {"Dedupe",-20} {"OK",-15} {_vm.LastRunResult.GroupCount} Gruppen, {_vm.LastRunResult.WinnerCount} Winner");
            var junkCount = _vm.LastCandidates.Count(c => c.Category == FileCategory.Junk);
            sb.AppendLine($"  {"Junk-Erkennung",-20} {"OK",-15} {junkCount} Junk-Dateien");
            if (_vm.LastRunResult.ConsoleSortResult is { }) sb.AppendLine($"  {"Konsolen-Sort",-20} {"OK",-15} sortiert");
            else sb.AppendLine($"  {"Konsolen-Sort",-20} {"Übersprungen",-15}");
            if (_vm.LastRunResult.ConvertedCount > 0) sb.AppendLine($"  {"Konvertierung",-20} {"OK",-15} {_vm.LastRunResult.ConvertedCount} konvertiert");
            else sb.AppendLine($"  {"Konvertierung",-20} {"Übersprungen",-15}");
            if (_vm.LastRunResult.MoveResult is { } mv) sb.AppendLine($"  {"Move",-20} {(mv.FailCount > 0 ? "WARNUNG" : "OK"),-15} {mv.MoveCount} verschoben, {mv.FailCount} Fehler");
            else sb.AppendLine($"  {"Move",-20} {"DryRun",-15} keine Änderungen");
            _dialog.ShowText("Pipeline-Engine", sb.ToString());
        }
        else
        {
            _dialog.ShowText("Pipeline-Engine", "Pipeline-Engine\n\nBedingte Multi-Step-Pipelines:\n\n" +
                "  1. Scan → Dateien erfassen\n  2. Dedupe → Duplikate erkennen\n  3. Sort → Nach Konsole sortieren\n" +
                "  4. Convert → Formate konvertieren\n  5. Verify → Konvertierung prüfen\n\n" +
                "Jeder Schritt kann übersprungen werden.\nDryRun-aware: Kein Schreibzugriff im DryRun-Modus.\n\n" +
                "Starte einen Lauf, um Pipeline-Ergebnisse zu sehen.");
        }
    }

    private void RulePackSharing()
    {
        var doExport = _dialog.Confirm("Regel-Pakete\n\nJA = Exportieren (rules.json speichern)\nNEIN = Importieren (rules.json laden)", "Regel-Pakete");
        var dataDir = FeatureService.ResolveDataDirectory() ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var rulesPath = Path.Combine(dataDir, "rules.json");
        if (doExport)
        {
            if (!File.Exists(rulesPath))
            { _dialog.Info("Keine rules.json zum Exportieren gefunden.\n\nErstelle zuerst Regeln in data/rules.json.", "Export"); return; }
            var savePath = _dialog.SaveFile("Regeln exportieren", "JSON (*.json)|*.json", "rules-export.json");
            if (!TryResolveSafeOutputPath(savePath, "Regel-Export", out var safeSavePath)) return;
            try { File.Copy(rulesPath, safeSavePath, overwrite: true); _vm.AddLog($"Regeln exportiert: {safeSavePath}", "INFO"); }
            catch (Exception ex) { LogError("IO-EXPORT", $"Export fehlgeschlagen: {ex.Message}"); }
        }
        else
        {
            var importPath = _dialog.BrowseFile("Regel-Paket importieren", "JSON (*.json)|*.json");
            if (importPath is null) return;
            try
            {
                var json = File.ReadAllText(importPath);
                JsonDocument.Parse(json).Dispose();
                Directory.CreateDirectory(dataDir);
                if (!TryResolveSafeOutputPath(rulesPath, "Regel-Import", out var safeRulesPath))
                    return;

                File.Copy(importPath, safeRulesPath, overwrite: true);
                _vm.AddLog($"Regeln importiert: {Path.GetFileName(importPath)} nach {safeRulesPath}", "INFO");
            }
            catch (JsonException) { LogError("GUI-IMPORT", "Import fehlgeschlagen: Ungültiges JSON-Format.", "JSON-Datei prüfen"); }
            catch (Exception ex) { LogError("GUI-IMPORT", $"Import fehlgeschlagen: {ex.Message}"); }
        }
    }

    private void ArcadeMergeSplit()
    {
        var datPath = _dialog.BrowseFile("MAME/FBNEO DAT wählen", "DAT (*.dat;*.xml)|*.dat;*.xml");
        if (datPath is null) return;
        _vm.AddLog($"Arcade Merge/Split: Analysiere {Path.GetFileName(datPath)}…", "INFO");
        try
        {
            var report = FeatureService.BuildArcadeMergeSplitReport(datPath);
            _dialog.ShowText("Arcade Merge/Split", report);
            _vm.AddLog("Arcade-Analyse abgeschlossen.", "INFO");
        }
        catch (Exception ex)
        {
            LogError("GUI-ARCADE", $"Arcade Merge/Split Fehler: {ex.Message}");
            _dialog.Error($"Fehler beim Parsen der DAT:\n\n{ex.Message}", "Arcade Merge/Split");
        }
    }

}
