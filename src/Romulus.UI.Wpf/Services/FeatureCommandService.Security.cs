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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
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
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-BASELINE", $"Baseline-Fehler: {ex.Message}"); }
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
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-INTEGRITY", $"Integritäts-Fehler: {ex.Message}"); }
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
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-BACKUP", $"Backup-Fehler: {ex.Message}"); }
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
        var mode = _dialog.YesNoCancel(
            "Regel-Engine\n\nJA = Regel-Report anzeigen\nNEIN = Custom Junk Rules bearbeiten\nABBRECHEN = Nichts tun",
            "Regel-Engine");

        if (mode == ConfirmResult.Cancel)
            return;

        if (mode == ConfirmResult.Yes)
        {
            try { _dialog.ShowText("Regel-Engine", FeatureService.BuildRuleEngineReport()); }
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or JsonException) { LogError("SEC-RULES", $"Fehler beim Laden der Regeln: {ex.Message}"); }
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
                "Custom Junk Rules als JSON bearbeiten.\n\n"
                + $"Datei: {rulesPath}\n"
                + "Felder: name, region, extension, path\n"
                + "Operatoren: contains, equals, regex\n"
                + "Logic: AND oder OR\n"
                + "Aktion: SetCategoryJunk\n\n"
                + "Leer lassen oder abbrechen = keine Aenderung.",
                "Custom Junk Rules Editor",
                LoadCustomJunkRulesEditorContent(rulesPath));

            if (string.IsNullOrWhiteSpace(editorText))
                return;

            if (!TryNormalizeCustomJunkRules(editorText, out var normalizedDocument, out var preview, out var validationError))
            {
                _dialog.Error($"Regeln konnten nicht gespeichert werden:\n\n{validationError}", "Custom Junk Rules Editor");
                return;
            }

            _dialog.ShowText("Custom Junk Rules Vorschau", preview);
            if (!_dialog.Confirm("Regeln aus der Vorschau speichern?", "Custom Junk Rules Editor"))
                return;

            var normalizedJson = JsonSerializer.Serialize(normalizedDocument, CustomJunkRulesWriteOptions);
            File.WriteAllText(rulesPath, normalizedJson, Encoding.UTF8);
            _vm.AddLog($"Custom Junk Rules gespeichert: {rulesPath} ({normalizedDocument.Rules.Count} Regeln)", "INFO");
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or ArgumentException or JsonException)
        {
            LogError("SEC-CUSTOM-RULES", $"Custom Junk Rules Editor fehlgeschlagen: {ex.Message}");
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
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-HEADER", $"Header-Reparatur fehlgeschlagen: {ex.Message}"); }
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
            catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException or NotSupportedException) { LogError("SEC-HEADER", $"Header-Reparatur fehlgeschlagen: {ex.Message}"); }
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
