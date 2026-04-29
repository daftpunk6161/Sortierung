using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
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
            AtomicFileWriter.WriteAllText(rulesPath, normalizedJson, Encoding.UTF8);
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
                        if (_headerRepairService.RepairNesHeader(path))
                        {
                            TryAppendHeaderRepairAudit(path, "nes-header");
                            _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.NesFixed"], "INFO");
                        }
                        else
                            _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.RestoreFromBackupFailedFix"], "ERROR");
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
                        if (_headerRepairService.RemoveCopierHeader(path))
                        {
                            TryAppendHeaderRepairAudit(path, "snes-copier-header");
                            _vm.AddLog(_vm.Loc.Format("Cmd.HeaderRepair.SnesHeaderRemoved", fileInfo.Length, fileInfo.Length - 512), "INFO");
                        }
                        else
                            _vm.AddLog(_vm.Loc["Cmd.HeaderRepair.RestoreFromBackupFailedFix"], "ERROR");
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

    private void TryAppendHeaderRepairAudit(string path, string reason)
    {
        try
        {
            var rootPath = Path.GetDirectoryName(path) ?? Directory.GetCurrentDirectory();
            var auditDir = !string.IsNullOrWhiteSpace(_vm.AuditRoot)
                ? _vm.AuditRoot
                : _vm.Roots.Count > 0
                    ? ArtifactPathResolver.GetArtifactDirectory(_vm.Roots, AppIdentity.ArtifactDirectories.AuditLogs)
                    : Path.Combine(rootPath, AppIdentity.ArtifactDirectories.AuditLogs);

            Directory.CreateDirectory(auditDir);
            var auditPath = Path.Combine(auditDir, $"header-repair-{DateTime.UtcNow:yyyyMMdd-HHmmssfff}.csv");
            _auditStore.AppendAuditRows(
                auditPath,
                [new AuditAppendRow(rootPath, path, path, "HEADER_REPAIR", "GAME", "", reason)]);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogWarning("HEADER-REPAIR-AUDIT", $"Header-Repair-Audit konnte nicht geschrieben werden: {ex.Message}");
        }
    }

}

// === merged from FeatureCommandService.Infra.cs (Wave 1 T-W1-UI-REDUCTION) ===
// (namespace dedup — Wave 1 merge)

public sealed partial class FeatureCommandService
{
    // Wave-8 F-T09: named constant replaces the previous magic tab index
    // used by the CommandPalette "settings" shortcut. Kept private to this
    // partial because it is the only call site.
    private const int SettingsTabIndex = 3;

    // ═══ INFRASTRUKTUR & DEPLOYMENT ═════════════════════════════════════

    private async Task StorageTieringAsync()
    {
        var (success, snapshots, collectionIndex) = await TryLoadSnapshotsAsync(30);
        if (!success || collectionIndex is null)
            return;

        using (collectionIndex)
        {
            try
            {
                var insights = await RunHistoryInsightsService.BuildStorageInsightsAsync(collectionIndex, 30);
                var trends = await RunHistoryTrendService.LoadTrendHistoryAsync(collectionIndex, 30);

                var sb = new StringBuilder();
                sb.AppendLine(RunHistoryInsightsService.FormatStorageInsightReport(insights));
                sb.AppendLine();
                sb.AppendLine(RunHistoryTrendService.FormatTrendReport(
                    trends,
                    _vm.Loc["Cmd.StorageTiering.TrendsTitle"],
                    _vm.Loc["Cmd.StorageTiering.NoHistory"],
                    _vm.Loc["Cmd.StorageTiering.Current"],
                    _vm.Loc["Cmd.StorageTiering.DeltaFiles"],
                    _vm.Loc["Cmd.StorageTiering.DeltaDuplicates"],
                    _vm.Loc["Cmd.StorageTiering.History"],
                    _vm.Loc["Cmd.StorageTiering.Files"],
                    _vm.Loc["Cmd.StorageTiering.Quality"]));

                if (_vm.LastCandidates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(FeatureService.AnalyzeStorageTiers(_vm.LastCandidates));
                }

                _dialog.ShowText(_vm.Loc["Cmd.StorageTiering.Title"], sb.ToString());
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                LogWarning("GUI-HISTORY", _vm.Loc.Format("Cmd.StorageTiering.NoHistoryWarning", ex.Message));
                var sb = new StringBuilder();
                sb.AppendLine(_vm.Loc["Cmd.StorageTiering.Title"]);
                sb.AppendLine();
                sb.AppendLine(_vm.Loc["Cmd.StorageTiering.NoHistory"]);

                if (_vm.LastCandidates.Count > 0)
                {
                    sb.AppendLine();
                    sb.AppendLine(FeatureService.AnalyzeStorageTiers(_vm.LastCandidates));
                }

                _dialog.ShowText(_vm.Loc["Cmd.StorageTiering.Title"], sb.ToString());
            }
        }
    }

    private void NasOptimization()
    {
        if (_vm.Roots.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.NasOptimization.NoRoots"], "WARN"); return; }
        _dialog.ShowText(_vm.Loc["Cmd.NasOptimization.Title"], FeatureService.GetNasInfo(_vm.Roots.ToList()));
    }

    private void PortableMode()
    {
        var isPortable = FeatureService.IsPortableMode();
        var resolvedSettingsDir = AppStoragePathResolver.ResolveRoamingAppDirectory();
        var sb = new StringBuilder();
        sb.AppendLine(_vm.Loc["Cmd.PortableMode.Title"]);
        sb.AppendLine();
        var modeLabel = isPortable
            ? _vm.Loc["Cmd.PortableMode.ModePortable"]
            : _vm.Loc["Cmd.PortableMode.ModeStandard"];
        sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.ModeLine", modeLabel));
        sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.ProgramDirLine", AppContext.BaseDirectory));
        if (isPortable) sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.SettingsDirLine", resolvedSettingsDir));
        else
        {
            sb.AppendLine(_vm.Loc.Format("Cmd.PortableMode.SettingsDirLine", resolvedSettingsDir));
            sb.AppendLine();
            sb.AppendLine(_vm.Loc["Cmd.PortableMode.PortableHint"]);
        }
        _dialog.ShowText(_vm.Loc["Cmd.PortableMode.Title"], sb.ToString());
    }

    private void HardlinkMode()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.RunRequired"], "WARN"); return; }
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
        var ntfsStatus = isNtfs
            ? _vm.Loc["Cmd.HardlinkMode.NtfsAvailable"]
            : _vm.Loc["Cmd.HardlinkMode.NtfsUnavailable"];
        _dialog.ShowText(
            _vm.Loc["Cmd.HardlinkMode.Title"],
            _vm.Loc.Format("Cmd.HardlinkMode.Body", estimate, ntfsStatus));
    }

    // ═══ WINDOW-LEVEL COMMANDS (require IWindowHost) ════════════════════

    private void CommandPalette()
    {
        var input = _dialog.ShowInputBox(_vm.Loc["Cmd.CommandPalette.SearchPrompt"], _vm.Loc["Cmd.CommandPalette.Title"], string.Empty);
        if (string.IsNullOrWhiteSpace(input)) return;
        var results = FeatureService.SearchCommands(input, _vm.FeatureCommands);
        if (results.Count == 0)
        { _vm.AddLog(_vm.Loc.Format("Cmd.CommandPalette.NotFound", input), "WARN"); return; }

        _dialog.ShowText(_vm.Loc["Cmd.CommandPalette.Title"], FeatureService.BuildCommandPaletteReport(input, results));
        if (results[0].score == 0) ExecuteCommand(results[0].key);
    }

    internal void ExecuteCommand(string key)
    {
        // 1. Try FeatureCommands dictionary (all registered tool commands)
        if (_vm.FeatureCommands.TryGetValue(key, out var featureCmd))
        {
            featureCmd.Execute(null);
            _vm.RecordToolUsage(key);
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
            case "settings": _windowHost?.SelectTab(SettingsTabIndex); break;
            default: _vm.AddLog(_vm.Loc.Format("Cmd.CommandPalette.UnknownCommand", key), "WARN"); break;
        }
    }

    private void ApiServer()
    {
        var apiProject = FeatureService.FindApiProjectPath();
        if (apiProject is not null)
        {
            if (_dialog.Confirm(_vm.Loc["Cmd.ApiServer.StartPrompt"], _vm.Loc["Cmd.ApiServer.Title"]))
            {
                _windowHost?.StartApiProcess(apiProject);
                return;
            }
        }
        else
        {
            _dialog.ShowText(_vm.Loc["Cmd.ApiServer.Title"], _vm.Loc["Cmd.ApiServer.NotFoundMessage"]);
        }
    }

    private void Accessibility()
    {
        if (_windowHost is null) return;
        var isHC = FeatureService.IsHighContrastActive();
        var currentSize = _windowHost.FontSize;

        var input = _dialog.ShowInputBox(
            _vm.Loc.Format(
                "Cmd.Accessibility.Prompt",
                isHC ? _vm.Loc["Cmd.Accessibility.HighContrastActive"] : _vm.Loc["Cmd.Accessibility.HighContrastInactive"],
                currentSize),
            _vm.Loc["Cmd.Accessibility.Title"],
            currentSize.ToString("0"));
        if (string.IsNullOrWhiteSpace(input)) return;

        if (double.TryParse(input, System.Globalization.CultureInfo.InvariantCulture, out var newSize) && newSize >= 10 && newSize <= 24)
        {
            _windowHost.FontSize = newSize;
            _vm.AddLog(_vm.Loc.Format("Cmd.Accessibility.FontSizeChanged", newSize), "INFO");
        }
        else
        {
            _vm.AddLog(_vm.Loc.Format("Cmd.Accessibility.InvalidFontSize", input), "WARN");
        }
    }
}
