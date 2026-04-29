using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
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

// === merged from FeatureCommandService.Productization.cs (Wave 1 T-W1-UI-REDUCTION) ===

// (namespace dedup — Wave 1 merge)

public sealed partial class FeatureCommandService
{
    private static readonly JsonSerializerOptions ProfileJsonOptions = new()
    {
        WriteIndented = true
    };

    private async Task<(bool Success, MaterializedRunConfiguration? Materialized)> TryCreateCurrentMaterializedRunConfigurationAsync()
    {
        try
        {
            var dataDir = FeatureService.ResolveDataDirectory()
                          ?? RunEnvironmentBuilder.ResolveDataDir();
            var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
            var materialized = await _vm.RunConfigurationMaterializer.MaterializeAsync(
                _vm.BuildCurrentRunConfigurationDraft(),
                _vm.BuildCurrentRunConfigurationExplicitness(),
                settings);
            return (true, materialized);
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-CONFIG", $"Run-Konfiguration ungueltig: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<(bool Success, MaterializedRunConfiguration? Materialized)> TryCreateSelectedMaterializedRunConfigurationAsync()
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedWorkflowScenarioId) &&
            string.IsNullOrWhiteSpace(_vm.SelectedRunProfileId))
        {
            LogWarning("GUI-PROFILE", "Kein Workflow oder Profil ausgewaehlt.");
            return (false, null);
        }

        try
        {
            var dataDir = FeatureService.ResolveDataDirectory()
                          ?? RunEnvironmentBuilder.ResolveDataDir();
            var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
            var baselineDraft = _vm.BuildCurrentRunConfigurationDraft(includeSelections: false);
            var selectionDraft = new RunConfigurationDraft
            {
                Roots = baselineDraft.Roots,
                WorkflowScenarioId = _vm.SelectedWorkflowScenarioId,
                ProfileId = _vm.SelectedRunProfileId
            };

            var materialized = await _vm.RunConfigurationMaterializer.MaterializeAsync(
                selectionDraft,
                new RunConfigurationExplicitness(),
                settings,
                baselineDraft: baselineDraft);
            return (true, materialized);
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-PROFILE", $"Auswahl konnte nicht materialisiert werden: {ex.Message}");
            return (false, null);
        }
    }

    private async Task<(bool Success, MaterializedRunConfiguration? Materialized, IRunEnvironment? Environment)> TryCreateCurrentRunEnvironmentAsync()
    {
        var (success, materialized) = await TryCreateCurrentMaterializedRunConfigurationAsync();
        if (!success || materialized is null)
            return (false, null, null);

        try
        {
            var environment = new RunEnvironmentFactory().Create(materialized.Options);
            return (true, materialized, environment);
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-ENV", $"Run-Umgebung konnte nicht erstellt werden: {ex.Message}");
            return (false, null, null);
        }
    }

    private bool TryCopyToClipboard(string text, string successMessage)
    {
        try
        {
            System.Windows.Clipboard.SetText(text);
            _vm.AddLog(successMessage, "INFO");
            return true;
        }
        catch (Exception ex)
        {
            LogWarning("GUI-CLIPBOARD", $"Zwischenablage nicht verfuegbar: {ex.Message}");
            return false;
        }
    }

    private async Task<RunProfileDocument?> TryGetSelectedProfileDocumentAsync()
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedRunProfileId))
            return null;

        try
        {
            return await _vm.RunProfileService.TryGetAsync(_vm.SelectedRunProfileId);
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-PROFILE", $"Profil konnte nicht geladen werden: {ex.Message}");
            return null;
        }
    }

    private bool TryPromptProfileDocument(out RunProfileDocument? document)
    {
        document = null;

        var defaultName = !string.IsNullOrWhiteSpace(_vm.SelectedRunProfileName) && _vm.HasSelectedRunProfile
            ? _vm.SelectedRunProfileName
            : _vm.ProfileName;
        var inputName = _dialog.ShowInputBox(
            "Profilname eingeben:",
            "Profil speichern",
            string.IsNullOrWhiteSpace(defaultName) ? "Custom Profile" : defaultName);
        if (string.IsNullOrWhiteSpace(inputName))
            return false;

        var defaultDescription = _vm.HasSelectedRunProfile ? _vm.SelectedRunProfileDescription : string.Empty;
        var inputDescription = _dialog.ShowInputBox(
            "Optionale Beschreibung eingeben:",
            "Profil speichern",
            defaultDescription);

        var profileName = inputName.Trim();
        var profileId = NormalizeProfileId(profileName);
        document = _vm.BuildCurrentRunProfileDocument(profileId, profileName, inputDescription);
        return true;
    }

    internal static string NormalizeProfileId(string name)
    {
        var builder = new StringBuilder(name.Length);
        foreach (var ch in name.Trim())
        {
            if (char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-')
            {
                builder.Append(ch);
            }
            else if (char.IsWhiteSpace(ch))
            {
                builder.Append('-');
            }
        }

        var normalized = builder.ToString().Trim('-', '.', '_');
        if (string.IsNullOrWhiteSpace(normalized))
            normalized = "custom-profile";

        return normalized.Length <= 64 ? normalized : normalized[..64];
    }

    private async Task<(bool Success, IReadOnlyList<CollectionRunSnapshot> Snapshots, LiteDbCollectionIndex? CollectionIndex)> TryLoadSnapshotsAsync(int limit)
    {
        try
        {
            var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath());
            var snapshots = await collectionIndex.ListRunSnapshotsAsync(limit);
            return (true, snapshots, collectionIndex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            LogWarning("GUI-HISTORY", $"Run-Historie nicht verfuegbar: {ex.Message}");
            return (false, Array.Empty<CollectionRunSnapshot>(), null);
        }
    }

    internal static string BuildRunSnapshotChoicePrompt(IReadOnlyList<CollectionRunSnapshot> snapshots)
    {
        var lines = new List<string>
        {
            "Run-IDs fuer Vergleich eingeben (\"aktuell alt\").",
            "Leer lassen, um die zwei neuesten Runs zu vergleichen.",
            string.Empty,
            "Neueste Snapshots:"
        };

        foreach (var snapshot in snapshots.Take(5))
            lines.Add($"  {snapshot.RunId}  [{snapshot.CompletedUtc:yyyy-MM-dd HH:mm}] {snapshot.Mode} {snapshot.Status}");

        return string.Join(Environment.NewLine, lines);
    }

    internal static IReadOnlyList<string> ResolveComparisonPair(
        string? input,
        IReadOnlyList<CollectionRunSnapshot> snapshots)
    {
        if (string.IsNullOrWhiteSpace(input))
            return [snapshots[0].RunId, snapshots[1].RunId];

        var parts = input
            .Split([' ', ';', ',', '\t', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Take(2)
            .ToArray();

        return parts.Length == 2 ? parts : [snapshots[0].RunId, snapshots[1].RunId];
    }
}

// === merged from FeatureCommandService.Conversion.cs (Wave 1 T-W1-UI-REDUCTION) ===
// (namespace dedup — Wave 1 merge)

public sealed partial class FeatureCommandService
{
    // ═══ KONVERTIERUNG & HASHING ════════════════════════════════════════

    private void ConversionPipeline()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten.", "WARN"); return; }
        var advisor = FeatureService.GetConversionAdvisor(_vm.LastCandidates);
        var convertibleFiles = 0;
        foreach (var item in advisor.Consoles)
            convertibleFiles += item.FileCount;

        if (convertibleFiles == 0)
        {
            _vm.AddLog("Konvertierungs-Advisor: keine konvertierbaren Dateien gefunden.", "INFO");
            _dialog.Info("Keine konvertierbaren Dateien gefunden.", "Konvertierungs-Advisor");
            return;
        }

        _vm.AddLog($"Konvertierungs-Advisor: {convertibleFiles} Dateien, Ersparnis ~{FeatureService.FormatSize(advisor.SavedBytes)}", "INFO");

        var sb = new StringBuilder();
        sb.AppendLine("Konvertierungs-Advisor");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"Konvertierbare Dateien: {convertibleFiles}");
        sb.AppendLine($"Gesamtgroesse vorher: {FeatureService.FormatSize(advisor.TotalSourceBytes)}");
        sb.AppendLine($"Gesamtgroesse nachher: {FeatureService.FormatSize(advisor.EstimatedTargetBytes)}");
        sb.AppendLine($"Gesamtersparnis: {FeatureService.FormatSize(advisor.SavedBytes)}");
        sb.AppendLine();
        sb.AppendLine("Einsparung pro Konsole");
        sb.AppendLine(new string('-', 56));
        foreach (var console in advisor.Consoles)
        {
            sb.AppendLine(
                $"{console.ConsoleKey,-14} Dateien: {console.FileCount,3}  Vorher: {FeatureService.FormatSize(console.SourceBytes),9}  Nachher: {FeatureService.FormatSize(console.EstimatedBytes),9}  Sparen: {FeatureService.FormatSize(console.SavedBytes),9}");
        }

        if (advisor.Recommendations.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("Empfehlungen");
            sb.AppendLine(new string('-', 56));
            foreach (var recommendation in advisor.Recommendations)
                sb.AppendLine($"- {recommendation}");
        }

        sb.AppendLine();
        sb.AppendLine("Aktiviere 'Konvertierung' und starte einen Move-Lauf.");
        _dialog.ShowText("Konvertierungs-Advisor", sb.ToString());
    }

    private void ConversionVerify()
    {
        var dir = _dialog.BrowseFolder("Konvertierte Dateien prüfen");
        if (dir is null) return;
        var fileSystem = new FileSystemAdapter();
        var files = fileSystem.GetFilesSafe(dir)
            .Where(f => DiscFormats.IsConversionVerificationExtension(Path.GetExtension(f).ToLowerInvariant()))
            .ToList();
        foreach (var warning in fileSystem.ConsumeScanWarnings())
            _vm.AddLog(warning, "WARN");
        var (passed, failed, missing) = FeatureService.VerifyConversions(files);
        _dialog.ShowText("Konvertierung verifizieren", $"Verifizierung: {dir}\n\n" +
            $"Bestanden: {passed}\nFehlgeschlagen: {failed}\nFehlend: {missing}\nGesamt: {files.Count}");
    }

    private void FormatPriority()
    {
        _dialog.ShowText("Format-Priorität", FeatureService.FormatFormatPriority());
    }

}
