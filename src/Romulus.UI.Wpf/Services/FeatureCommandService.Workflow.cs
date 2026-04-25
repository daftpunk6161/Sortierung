using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.FileSystem;
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
        { _vm.AddLog(_vm.Loc["Cmd.RunRequired"], "WARN"); return; }
        var input = _dialog.ShowInputBox(
            _vm.Loc["Cmd.FilterBuilder.ExpressionPrompt"],
            _vm.Loc["Cmd.FilterBuilder.Title"], "region=US");
        if (string.IsNullOrWhiteSpace(input)) return;

        string field, op, value;
        if (input.Contains(">=")) { var p = input.Split(">=", 2); field = p[0].Trim().ToLowerInvariant(); op = ">="; value = p[1].Trim(); }
        else if (input.Contains("<=")) { var p = input.Split("<=", 2); field = p[0].Trim().ToLowerInvariant(); op = "<="; value = p[1].Trim(); }
        else if (input.Contains('>')) { var p = input.Split('>', 2); field = p[0].Trim().ToLowerInvariant(); op = ">"; value = p[1].Trim(); }
        else if (input.Contains('<')) { var p = input.Split('<', 2); field = p[0].Trim().ToLowerInvariant(); op = "<"; value = p[1].Trim(); }
        else if (input.Contains('=')) { var p = input.Split('=', 2); field = p[0].Trim().ToLowerInvariant(); op = "="; value = p[1].Trim(); }
        else { _vm.AddLog(_vm.Loc.Format("Cmd.FilterBuilder.InvalidExpression", input), "WARN"); return; }

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
        sb.AppendLine(_vm.Loc.Format("Cmd.FilterBuilder.Header", field, op, value));
        sb.AppendLine(new string('═', 50));
        sb.AppendLine(_vm.Loc.Format("Cmd.FilterBuilder.Total", _vm.LastCandidates.Count));
        sb.AppendLine(_vm.Loc.Format("Cmd.FilterBuilder.Filtered", filtered.Count));
        sb.AppendLine();
        foreach (var r in filtered.Take(50))
            sb.AppendLine($"  {Path.GetFileName(r.MainPath),-45} [{r.Region}] {r.Extension} {r.Category} {FeatureService.FormatSize(r.SizeBytes)}");
        if (filtered.Count > 50)
            sb.AppendLine(_vm.Loc.Format("Cmd.FilterBuilder.More", filtered.Count - 50));
        _dialog.ShowText(_vm.Loc["Cmd.FilterBuilder.Title"], sb.ToString());
    }

    private void SortTemplates()
    {
        var templates = FeatureService.GetSortTemplates();
        var sb = new StringBuilder();
        sb.AppendLine(_vm.Loc["Cmd.SortTemplates.Title"]);
        sb.AppendLine();
        foreach (var (name, pattern) in templates)
            sb.AppendLine($"  {name,-20} → {pattern}");
        sb.AppendLine();
        sb.AppendLine(_vm.Loc["Cmd.SortTemplates.Legend"]);
        _dialog.ShowText(_vm.Loc["Cmd.SortTemplates.Title"], sb.ToString());
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
            _dialog.ShowText(_vm.Loc["Cmd.PipelineEngine.Title"], sb.ToString());
        }
        else
        {
            _dialog.ShowText(_vm.Loc["Cmd.PipelineEngine.Title"], _vm.Loc["Cmd.PipelineEngine.EmptyDescription"]);
        }
    }

    private void RulePackSharing()
    {
        var doExport = _dialog.Confirm(_vm.Loc["Cmd.RulePackSharing.ModePrompt"], _vm.Loc["Cmd.RulePackSharing.Title"]);
        var dataDir = FeatureService.ResolveDataDirectory() ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var rulesPath = Path.Combine(dataDir, "rules.json");
        if (doExport)
        {
            if (!File.Exists(rulesPath))
            { _dialog.Info(_vm.Loc["Cmd.RulePackSharing.ExportNotFound"], _vm.Loc["Cmd.RulePackSharing.ExportTitle"]); return; }
            var savePath = _dialog.SaveFile(_vm.Loc["Cmd.RulePackSharing.ExportTitle"], _vm.Loc["Cmd.RulePackSharing.JsonFilter"], "rules-export.json");
            if (!TryResolveSafeOutputPath(savePath, "Regel-Export", out var safeSavePath)) return;
            try { AtomicFileWriter.CopyFile(rulesPath, safeSavePath, overwrite: true); _vm.AddLog(_vm.Loc.Format("Cmd.RulePackSharing.ExportDone", safeSavePath), "INFO"); }
            catch (Exception ex) { LogError("IO-EXPORT", _vm.Loc.Format("Cmd.RulePackSharing.ExportFailed", ex.Message)); }
        }
        else
        {
            var importPath = _dialog.BrowseFile(_vm.Loc["Cmd.RulePackSharing.ImportTitle"], _vm.Loc["Cmd.RulePackSharing.JsonFilter"]);
            if (importPath is null) return;
            try
            {
                var schemaPath = Path.Combine(dataDir, "schemas", "rules.schema.json");
                StartupDataSchemaValidator.ValidateFileAgainstSchema(importPath, schemaPath, "rules.json");
                Directory.CreateDirectory(dataDir);
                if (!TryResolveSafeOutputPath(rulesPath, "Regel-Import", out var safeRulesPath))
                    return;

                AtomicFileWriter.CopyFile(importPath, safeRulesPath, overwrite: true);
                _vm.AddLog(_vm.Loc.Format("Cmd.RulePackSharing.ImportDone", Path.GetFileName(importPath), safeRulesPath), "INFO");
            }
            catch (JsonException) { LogError("GUI-IMPORT", _vm.Loc["Cmd.RulePackSharing.ImportInvalidJson"], _vm.Loc["Cmd.RulePackSharing.ImportJsonHint"]); }
            catch (InvalidOperationException ex) { LogError("GUI-IMPORT", _vm.Loc.Format("Cmd.RulePackSharing.ImportFailed", ex.Message)); }
            catch (Exception ex) { LogError("GUI-IMPORT", _vm.Loc.Format("Cmd.RulePackSharing.ImportFailed", ex.Message)); }
        }
    }

    private void ArcadeMergeSplit()
    {
        var datPath = _dialog.BrowseFile(_vm.Loc["Cmd.ArcadeMergeSplit.BrowseDatTitle"], _vm.Loc["Cmd.ArcadeMergeSplit.DatFilter"]);
        if (datPath is null) return;
        _vm.AddLog(_vm.Loc.Format("Cmd.ArcadeMergeSplit.Analyzing", Path.GetFileName(datPath)), "INFO");
        try
        {
            var report = FeatureService.BuildArcadeMergeSplitReport(datPath);
            _dialog.ShowText(_vm.Loc["Cmd.ArcadeMergeSplit.Title"], report);
            _vm.AddLog(_vm.Loc["Cmd.ArcadeMergeSplit.Done"], "INFO");
        }
        catch (Exception ex)
        {
            LogError("GUI-ARCADE", _vm.Loc.Format("Cmd.ArcadeMergeSplit.Error", ex.Message));
            _dialog.Error(_vm.Loc.Format("Cmd.ArcadeMergeSplit.ParseErrorDialog", ex.Message), _vm.Loc["Cmd.ArcadeMergeSplit.Title"]);
        }
    }

}
