using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.ViewModels;
namespace Romulus.UI.Wpf.Services;

public sealed partial class FeatureCommandService
{
    // ═══ SICHERHEIT & INTEGRITÄT ════════════════════════════════════════

    private const string CustomJunkRulesFileName = "custom-junk-rules.json";
    private const string CustomJunkRuleAction = "SetCategoryJunk";
    private static readonly TimeSpan CustomJunkRegexTimeout = TimeSpan.FromMilliseconds(200);

    private static readonly HashSet<string> AllowedCustomJunkFields = new(StringComparer.OrdinalIgnoreCase)
    {
        "name", "region", "extension", "path"
    };

    private static readonly HashSet<string> AllowedCustomJunkOperators = new(StringComparer.OrdinalIgnoreCase)
    {
        "contains", "equals", "regex"
    };

    private static readonly HashSet<string> AllowedCustomJunkLogic = new(StringComparer.OrdinalIgnoreCase)
    {
        "AND", "OR"
    };

    private static readonly JsonSerializerOptions CustomJunkRulesReadOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip
    };

    private static readonly JsonSerializerOptions CustomJunkRulesWriteOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    private void PatchPipeline()
    {
        var sourcePath = _dialog.BrowseFile(
            _vm.Loc["Cmd.PatchPipeline.SourceFileTitle"],
            _vm.Loc["Cmd.PatchPipeline.SourceFileFilter"]);
        if (sourcePath is null)
            return;

        var patchPath = _dialog.BrowseFile(
            _vm.Loc["Cmd.PatchPipeline.PatchFileTitle"],
            _vm.Loc["Cmd.PatchPipeline.PatchFileFilter"]);
        if (patchPath is null)
            return;

        var patchFormat = ResolvePatchFormatForDialog(patchPath);
        if (patchFormat is null)
        {
            LogWarning("PATCH-FORMAT", _vm.Loc["Cmd.PatchPipeline.UnsupportedPatchFormat"]);
            return;
        }

        var sourceExtension = Path.GetExtension(sourcePath);
        var defaultName = Path.GetFileNameWithoutExtension(sourcePath) + ".patched" + sourceExtension;
        var outputPath = _dialog.SaveFile(
            _vm.Loc["Cmd.PatchPipeline.OutputFileTitle"],
            string.IsNullOrWhiteSpace(sourceExtension)
                ? _vm.Loc["Cmd.PatchPipeline.AllFilesFilter"]
                : _vm.Loc.Format("Cmd.PatchPipeline.OutputFileFilter", sourceExtension),
            defaultName);
        if (!TryResolveSafeOutputPath(outputPath, _vm.Loc["Cmd.PatchPipeline.Title"], out var safeOutputPath))
            return;

        try
        {
            var result = FeatureService.ApplyPatch(
                sourcePath,
                patchPath,
                safeOutputPath,
                toolRunner: new ToolRunnerAdapter());

            var toolLine = string.IsNullOrWhiteSpace(result.ToolPath)
                ? _vm.Loc["Cmd.PatchPipeline.ToolInternal"]
                : _vm.Loc.Format("Cmd.PatchPipeline.ToolPath", result.ToolPath);
            _dialog.ShowText(
                _vm.Loc["Cmd.PatchPipeline.Title"],
                _vm.Loc.Format(
                    "Cmd.PatchPipeline.Result",
                    result.Format,
                    result.SourcePath,
                    result.PatchPath,
                    result.OutputPath,
                    FeatureService.FormatSize(result.OutputSizeBytes),
                    result.OutputSha256,
                    toolLine));
            _vm.AddLog(_vm.Loc.Format("Cmd.PatchPipeline.Applied", result.Format, Path.GetFileName(result.PatchPath), result.OutputPath), "INFO");
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("PATCH-APPLY", _vm.Loc.Format("Cmd.PatchPipeline.ApplyFailed", ex.Message));
            _dialog.Error(_vm.Loc.Format("Cmd.PatchPipeline.ApplyFailedDialog", ex.Message), _vm.Loc["Cmd.PatchPipeline.Title"]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            LogError("PATCH-APPLY", _vm.Loc.Format("Cmd.PatchPipeline.ExecutionFailed", ex.Message));
        }
    }

    private async Task IntegrityMonitorAsync()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.RunRequired"], "WARN"); return; }
        var createBaseline = _dialog.Confirm(
            _vm.Loc["Cmd.IntegrityMonitor.Prompt"],
            _vm.Loc["Cmd.IntegrityMonitor.Title"]);
        if (createBaseline)
        {
            _vm.AddLog(_vm.Loc["Cmd.IntegrityMonitor.CreateBaselineStart"], "INFO");
            var paths = _vm.LastCandidates.Select(c => c.MainPath).ToList();
            var progress = new Progress<string>(msg => _vm.ProgressText = msg);
            try
            {
                var baseline = await FeatureService.CreateBaseline(paths, progress);
                _vm.AddLog(_vm.Loc.Format("Cmd.IntegrityMonitor.BaselineCreated", baseline.Count), "INFO");
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-BASELINE", _vm.Loc.Format("Cmd.IntegrityMonitor.BaselineError", ex.Message)); }
        }
        else
        {
            _vm.AddLog(_vm.Loc["Cmd.IntegrityMonitor.CheckStart"], "INFO");
            var progress = new Progress<string>(msg => _vm.ProgressText = msg);
            try
            {
                var check = await FeatureService.CheckIntegrity(progress);
                _dialog.ShowText(
                    _vm.Loc["Cmd.IntegrityMonitor.CheckTitle"],
                    _vm.Loc.Format(
                        "Cmd.IntegrityMonitor.CheckResult",
                        check.Intact.Count,
                        check.Changed.Count,
                        check.Missing.Count,
                        check.BitRotRisk
                            ? _vm.Loc["Cmd.IntegrityMonitor.BitRotYes"]
                            : _vm.Loc["Cmd.IntegrityMonitor.BitRotNo"]));
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-INTEGRITY", _vm.Loc.Format("Cmd.IntegrityMonitor.Error", ex.Message)); }
        }
    }

    private void BackupManager()
    {
        var backupRoot = _dialog.BrowseFolder(_vm.Loc["Cmd.BackupManager.TargetFolderTitle"]);
        if (backupRoot is null) return;
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.BackupManager.NoFiles"], "WARN"); return; }
        var winners = _vm.LastDedupeGroups.Select(g => g.Winner.MainPath).ToList();
        if (!_dialog.Confirm(_vm.Loc.Format("Cmd.BackupManager.Confirm", winners.Count, backupRoot), _vm.Loc["Cmd.BackupManager.ConfirmTitle"])) return;
        try
        {
            var sessionDir = FeatureService.CreateBackup(winners, backupRoot, "winners");
            _vm.AddLog(_vm.Loc.Format("Cmd.BackupManager.Created", sessionDir, winners.Count), "INFO");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-BACKUP", _vm.Loc.Format("Cmd.BackupManager.Error", ex.Message)); }
    }

    private void Quarantine()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.RunRequired"], "WARN"); return; }
        var quarantined = _vm.LastCandidates.Where(c =>
            c.Category == FileCategory.Junk || (!c.DatMatch && c.Region == "UNKNOWN")).ToList();
        var sb = new StringBuilder();
        sb.AppendLine(_vm.Loc.Format("Cmd.Quarantine.Candidates", quarantined.Count));
        sb.AppendLine();
        sb.AppendLine(_vm.Loc["Cmd.Quarantine.Criteria"]);
        sb.AppendLine();
        foreach (var q in quarantined.Take(30))
            sb.AppendLine($"  {Path.GetFileName(q.MainPath),-50} [{FeatureService.ToCategoryLabel(q.Category)}] {q.Region}");
        if (quarantined.Count > 30)
            sb.AppendLine(_vm.Loc.Format("Cmd.Quarantine.More", quarantined.Count - 30));
        _dialog.ShowText(_vm.Loc["Cmd.Quarantine.Title"], sb.ToString());
    }

    private void RuleEngine()
    {
        var mode = _dialog.YesNoCancel(
            _vm.Loc["Cmd.RuleEngine.ModePrompt"],
            _vm.Loc["Cmd.RuleEngine.Title"]);

        if (mode == ConfirmResult.Cancel)
            return;

        if (mode == ConfirmResult.Yes)
        {
            try { _dialog.ShowText(_vm.Loc["Cmd.RuleEngine.Title"], FeatureService.BuildRuleEngineReport()); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException) { LogError("SEC-RULES", _vm.Loc.Format("Cmd.RuleEngine.LoadError", ex.Message)); }
            return;
        }

        EditCustomJunkRules();
    }

    private void EditCustomJunkRules()
    {
        try
        {
            var rulesPath = ResolveCustomJunkRulesPath();
            var rulesDirectory = Path.GetDirectoryName(rulesPath);
            if (!string.IsNullOrWhiteSpace(rulesDirectory))
                Directory.CreateDirectory(rulesDirectory);

            var editorText = _dialog.ShowMultilineInputBox(
                _vm.Loc.Format("Cmd.CustomJunkRules.EditorPrompt", rulesPath),
                _vm.Loc["Cmd.CustomJunkRules.EditorTitle"],
                LoadCustomJunkRulesEditorContent(rulesPath));

            if (string.IsNullOrWhiteSpace(editorText))
                return;

            if (!TryNormalizeCustomJunkRules(editorText, out var normalizedDocument, out var preview, out var validationError))
            {
                _dialog.Error(_vm.Loc.Format("Cmd.CustomJunkRules.SaveValidationError", validationError), _vm.Loc["Cmd.CustomJunkRules.EditorTitle"]);
                return;
            }

            _dialog.ShowText(_vm.Loc["Cmd.CustomJunkRules.PreviewTitle"], preview);
            if (!_dialog.Confirm(_vm.Loc["Cmd.CustomJunkRules.ConfirmSave"], _vm.Loc["Cmd.CustomJunkRules.EditorTitle"]))
                return;

            var normalizedJson = JsonSerializer.Serialize(normalizedDocument, CustomJunkRulesWriteOptions);
            File.WriteAllText(rulesPath, normalizedJson, Encoding.UTF8);
            _vm.AddLog(_vm.Loc.Format("Cmd.CustomJunkRules.Saved", rulesPath, normalizedDocument.Rules.Count), "INFO");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            LogError("SEC-CUSTOM-RULES", _vm.Loc.Format("Cmd.CustomJunkRules.EditorError", ex.Message));
        }
    }

    private static string ResolveCustomJunkRulesPath()
        => AppStoragePathResolver.ResolveRoamingPath(CustomJunkRulesFileName);

    private static string LoadCustomJunkRulesEditorContent(string rulesPath)
    {
        if (!File.Exists(rulesPath))
            return BuildDefaultCustomJunkRulesJson();

        var existing = File.ReadAllText(rulesPath);
        if (string.IsNullOrWhiteSpace(existing))
            return BuildDefaultCustomJunkRulesJson();

        try
        {
            var parsed = JsonSerializer.Deserialize<CustomJunkRulesDocument>(existing, CustomJunkRulesReadOptions);
            return parsed is null
                ? BuildDefaultCustomJunkRulesJson()
                : JsonSerializer.Serialize(parsed, CustomJunkRulesWriteOptions);
        }
        catch (JsonException)
        {
            // Invalid JSON should stay visible to the user so they can repair it.
            return existing;
        }
    }

    internal static string BuildDefaultCustomJunkRulesJson()
    {
        var template = new CustomJunkRulesDocument
        {
            Enabled = true,
            Rules =
            [
                new CustomJunkRuleEntry
                {
                    Field = "name",
                    Operator = "contains",
                    Value = "(Beta)",
                    Logic = "AND",
                    Action = CustomJunkRuleAction,
                    Priority = 1000,
                    Enabled = true
                }
            ]
        };

        return JsonSerializer.Serialize(template, CustomJunkRulesWriteOptions);
    }

    internal static bool TryNormalizeCustomJunkRules(
        string json,
        out CustomJunkRulesDocument normalizedDocument,
        out string preview,
        out string validationError)
    {
        normalizedDocument = new CustomJunkRulesDocument();
        preview = string.Empty;
        validationError = string.Empty;

        CustomJunkRulesDocument? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<CustomJunkRulesDocument>(json, CustomJunkRulesReadOptions);
        }
        catch (JsonException ex)
        {
            validationError = $"Ungueltiges JSON: {ex.Message}";
            return false;
        }

        if (parsed is null)
        {
            validationError = "JSON konnte nicht gelesen werden.";
            return false;
        }

        if (parsed.Rules is null)
        {
            validationError = "Feld 'rules' fehlt.";
            return false;
        }

        var errors = new List<string>();
        var normalizedRules = new List<CustomJunkRuleEntry>(parsed.Rules.Count);

        for (var index = 0; index < parsed.Rules.Count; index++)
        {
            var sourceRule = parsed.Rules[index] ?? new CustomJunkRuleEntry();
            var field = (sourceRule.Field ?? string.Empty).Trim().ToLowerInvariant();
            var op = (sourceRule.Operator ?? string.Empty).Trim().ToLowerInvariant();
            var value = (sourceRule.Value ?? string.Empty).Trim();
            var logic = string.IsNullOrWhiteSpace(sourceRule.Logic)
                ? "AND"
                : sourceRule.Logic.Trim().ToUpperInvariant();
            var action = string.IsNullOrWhiteSpace(sourceRule.Action)
                ? CustomJunkRuleAction
                : sourceRule.Action.Trim();
            var priority = sourceRule.Priority > 0 ? sourceRule.Priority : 1000 + index;
            var ruleEngineOperator = op == "equals" ? "eq" : op;
            var ruleHasError = false;

            if (!AllowedCustomJunkFields.Contains(field))
            {
                errors.Add($"Regel {index + 1}: Feld '{sourceRule.Field}' ist nicht erlaubt.");
                ruleHasError = true;
            }

            if (!AllowedCustomJunkOperators.Contains(op))
            {
                errors.Add($"Regel {index + 1}: Operator '{sourceRule.Operator}' ist nicht erlaubt.");
                ruleHasError = true;
            }

            if (string.IsNullOrWhiteSpace(value))
            {
                errors.Add($"Regel {index + 1}: Value darf nicht leer sein.");
                ruleHasError = true;
            }

            if (string.Equals(op, "regex", StringComparison.OrdinalIgnoreCase)
                && !TryValidateCustomRegexPattern(value, out var regexError))
            {
                errors.Add($"Regel {index + 1}: Regex ungueltig ({regexError}).");
                ruleHasError = true;
            }

            if (!AllowedCustomJunkLogic.Contains(logic))
            {
                errors.Add($"Regel {index + 1}: Logic '{sourceRule.Logic}' ist ungueltig (nur AND/OR).");
                ruleHasError = true;
            }

            if (!string.Equals(action, CustomJunkRuleAction, StringComparison.OrdinalIgnoreCase))
            {
                errors.Add($"Regel {index + 1}: Action muss '{CustomJunkRuleAction}' sein.");
                ruleHasError = true;
            }

            if (ruleHasError)
                continue;

            var ruleEngineRule = new ClassificationRule
            {
                Name = $"custom-junk-{index + 1}",
                Priority = priority,
                Action = "junk",
                Enabled = sourceRule.Enabled,
                Conditions =
                [
                    new RuleCondition
                    {
                        Field = MapToRuleEngineField(field),
                        Op = ruleEngineOperator,
                        Value = value
                    }
                ]
            };

            var syntax = Romulus.Core.Rules.RuleEngine.ValidateSyntax(ruleEngineRule);
            if (!syntax.Valid)
            {
                var details = syntax.Errors.Count == 0
                    ? "Unbekannter Syntaxfehler"
                    : string.Join("; ", syntax.Errors);
                errors.Add($"Regel {index + 1}: {details}");
                continue;
            }

            normalizedRules.Add(new CustomJunkRuleEntry
            {
                Field = field,
                Operator = op,
                Value = value,
                Logic = logic,
                Action = CustomJunkRuleAction,
                Priority = priority,
                Enabled = sourceRule.Enabled
            });
        }

        if (errors.Count > 0)
        {
            validationError = string.Join("\n", errors);
            return false;
        }

        normalizedDocument = new CustomJunkRulesDocument
        {
            Enabled = parsed.Enabled,
            Rules = normalizedRules
        };
        preview = BuildCustomJunkRulesPreview(normalizedDocument);
        return true;
    }

    internal static bool TryValidateCustomRegexPattern(string pattern, out string error)
    {
        error = string.Empty;

        try
        {
            _ = new Regex(pattern, RegexOptions.CultureInvariant, CustomJunkRegexTimeout);
            return true;
        }
        catch (ArgumentException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    internal static string BuildCustomJunkRulesPreview(CustomJunkRulesDocument document)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Custom Junk Rules Vorschau");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"Aktiviert: {(document.Enabled ? "Ja" : "Nein")}");
        sb.AppendLine($"Regeln: {document.Rules.Count}");
        sb.AppendLine();

        foreach (var indexedRule in document.Rules.Select((rule, idx) => (rule, idx)))
        {
            sb.AppendLine($"[{indexedRule.idx + 1}] {(indexedRule.rule.Enabled ? "aktiv" : "inaktiv")}");
            sb.AppendLine($"  {indexedRule.rule.Field} {indexedRule.rule.Operator} \"{indexedRule.rule.Value}\"");
            sb.AppendLine($"  Logic={indexedRule.rule.Logic}, Priority={indexedRule.rule.Priority}, Action={indexedRule.rule.Action}");
        }

        if (document.Rules.Count == 0)
            sb.AppendLine("(Keine Regeln definiert)");

        return sb.ToString();
    }

    internal static string MapToRuleEngineField(string field)
        => field switch
        {
            "name" => "Name",
            "region" => "Region",
            "extension" => "Extension",
            "path" => "Path",
            _ => field
        };

    internal sealed class CustomJunkRulesDocument
    {
        public bool Enabled { get; set; } = true;
        public List<CustomJunkRuleEntry> Rules { get; set; } = [];
    }

    internal sealed class CustomJunkRuleEntry
    {
        public string Field { get; set; } = string.Empty;
        public string Operator { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Logic { get; set; } = "AND";
        public string Action { get; set; } = CustomJunkRuleAction;
        public int Priority { get; set; } = 1000;
        public bool Enabled { get; set; } = true;
    }

    private void HeaderRepair()
    {
        var path = _dialog.BrowseFile(_vm.Loc["Cmd.HeaderRepair.BrowseFileTitle"],
            _vm.Loc["Cmd.HeaderRepair.BrowseFileFilter"]);
        if (path is null) return;
        path = Path.GetFullPath(path);
        var header = FeatureService.AnalyzeHeader(path);
        if (header is null) { _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.HeaderUnreadable"], "ERROR"); return; }

        if (header.Platform == "NES")
        {
            try
            {
                var headerBuf = new byte[16];
                using (var hfs = File.OpenRead(path))
                { if (hfs.Read(headerBuf, 0, 16) < 16) { _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.NesFileTooSmall"], "ERROR"); return; } }
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
                        _vm.AddLog(_vm.Loc.Format("Cmd.HeaderRepair.BackupCreated", backupPath), "INFO");
                        try
                        {
                            using var patchFs = File.OpenWrite(path);
                            patchFs.Seek(12, SeekOrigin.Begin);
                            patchFs.Write(new byte[4], 0, 4);
                            _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.NesFixed"], "INFO");
                        }
                        catch
                        {
                            File.Copy(backupPath, path, overwrite: true);
                            _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.RestoreFromBackupFailedFix"], "ERROR");
                            throw;
                        }
                    }
                }
                else
                    _dialog.Info(_vm.Loc.Format("Cmd.HeaderRepair.NesNoFixNeeded", header.Details), _vm.Loc["Cmd.HeaderRepair.Title"]);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-HEADER", _vm.Loc.Format("Cmd.HeaderRepair.Error", ex.Message)); }
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
                        _vm.AddLog(_vm.Loc.Format("Cmd.HeaderRepair.BackupCreated", backupPath), "INFO");
                        try
                        {
                            var data = File.ReadAllBytes(path);
                            var trimmed = data[512..];
                            File.WriteAllBytes(path, trimmed);
                            _vm.AddLog(_vm.Loc.Format("Cmd.HeaderRepair.SnesHeaderRemoved", fileInfo.Length, trimmed.Length), "INFO");
                        }
                        catch
                        {
                            File.Copy(backupPath, path, overwrite: true);
                            _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.RestoreFromBackupFailedFix"], "ERROR");
                            throw;
                        }
                    }
                }
                else
                    _dialog.Info(_vm.Loc.Format("Cmd.HeaderRepair.SnesNoFixNeeded", header.Details), _vm.Loc["Cmd.HeaderRepair.Title"]);
            }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-HEADER", _vm.Loc.Format("Cmd.HeaderRepair.Error", ex.Message)); }
            return;
        }

        _dialog.ShowText(_vm.Loc["Cmd.HeaderRepair.Title"], _vm.Loc.Format("Cmd.HeaderRepair.GenericInfo", Path.GetFileName(path), header.Platform, header.Format, header.Details));
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
