using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using Romulus.Contracts;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Safety;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// TASK-111: All feature button logic extracted from MainWindow code-behind.
/// Each method maps 1:1 to a former On* event handler.
/// Commands are exposed via MainViewModel.FeatureCommands dictionary.
/// </summary>
public sealed partial class FeatureCommandService
{
    private static readonly HashSet<string> SafeShellFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".csv", ".json", ".xml", ".txt", ".log", ".lpl", ".m3u"
    };

    private readonly MainViewModel _vm;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly IFileSystem _fileSystem;
    private readonly IAuditStore _auditStore;
    private readonly IHeaderRepairService _headerRepairService;
    private readonly IResultExportService _resultExport;
    private IWindowHost? _windowHost;
    private volatile bool _datUpdateRunning;

    public FeatureCommandService(
        MainViewModel vm,
        ISettingsService settings,
        IDialogService dialog,
        IFileSystem fileSystem,
        IAuditStore auditStore,
        IHeaderRepairService headerRepairService,
        IResultExportService? resultExport = null)
    {
        _vm = vm;
        _settings = settings;
        _dialog = dialog;
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _auditStore = auditStore ?? throw new ArgumentNullException(nameof(auditStore));
        _headerRepairService = headerRepairService ?? throw new ArgumentNullException(nameof(headerRepairService));
        _resultExport = resultExport ?? new ResultExportService();
    }

    public void AttachWindowHost(IWindowHost windowHost)
    {
        _windowHost = windowHost;
    }

    /// <summary>GUI-045: Log error to both AddLog and ErrorSummaryItems for structured tracking.</summary>
    private void LogError(string code, string message, string? fixHint = null)
    {
        _vm.AddLog(message, "ERROR");
        _vm.ErrorSummaryItems.Add(new UiError(code, message, UiErrorSeverity.Error, fixHint));
    }

    /// <summary>GUI-045: Log warning to both AddLog and ErrorSummaryItems.</summary>
    private void LogWarning(string code, string message, string? fixHint = null)
    {
        _vm.AddLog(message, "WARN");
        _vm.ErrorSummaryItems.Add(new UiError(code, message, UiErrorSeverity.Warning, fixHint));
    }

    private bool TryResolveSafeOutputPath(string? selectedPath, string purpose, out string safePath)
    {
        safePath = string.Empty;
        if (string.IsNullOrWhiteSpace(selectedPath))
            return false;

        try
        {
            safePath = SafetyValidator.EnsureSafeOutputPath(selectedPath);
            return true;
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-OUTPUT", $"{purpose} blockiert: {ex.Message}");
            _dialog.Error($"{purpose} blockiert:\n\n{ex.Message}", purpose);
            return false;
        }
    }

    private void TryOpenWithShell(string path, string purpose, bool allowDirectory = false)
    {
        if (string.IsNullOrWhiteSpace(path))
            return;

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(path);
        }
        catch
        {
            LogWarning("SEC-SHELL-OPEN", $"{purpose} blockiert: ungültiger Pfad.");
            return;
        }

        if (allowDirectory && Directory.Exists(fullPath))
        {
            try
            {
                var dirInfo = new DirectoryInfo(fullPath);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                {
                    LogWarning("SEC-SHELL-OPEN", $"{purpose} blockiert: Reparse-Point-Verzeichnis nicht erlaubt.");
                    return;
                }
            }
            catch
            {
                LogWarning("SEC-SHELL-OPEN", $"{purpose} blockiert: Verzeichnisattribute konnten nicht geprüft werden.");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogWarning("GUI-SHELL", $"{purpose} konnte nicht geöffnet werden: {ex.Message}");
            }
            return;
        }

        if (!File.Exists(fullPath))
        {
            LogWarning("SEC-SHELL-OPEN", $"{purpose} blockiert: Datei nicht gefunden.");
            return;
        }

        try
        {
            var attrs = File.GetAttributes(fullPath);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                LogWarning("SEC-SHELL-OPEN", $"{purpose} blockiert: Reparse-Point-Datei nicht erlaubt.");
                return;
            }
        }
        catch
        {
            LogWarning("SEC-SHELL-OPEN", $"{purpose} blockiert: Dateiattribute konnten nicht geprüft werden.");
            return;
        }

        var extension = Path.GetExtension(fullPath);
        if (!SafeShellFileExtensions.Contains(extension))
        {
            LogWarning("SEC-SHELL-OPEN", $"{purpose} blockiert: Dateityp '{extension}' nicht erlaubt.");
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(fullPath) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            LogWarning("GUI-SHELL", $"{purpose} konnte nicht geöffnet werden: {ex.Message}");
        }
    }

    public void RegisterCommands()
    {
        var cmds = _vm.FeatureCommands;
        // V2-WPF-M02: Clear previous registrations to prevent orphaned command references
        cmds.Clear();

        // ── Functional buttons ──────────────────────────────────────────
        cmds[FeatureCommandKeys.ExportLog] = new RelayCommand(ExportLog);
        cmds[FeatureCommandKeys.ProfileSave] = new AsyncRelayCommand(ProfileSaveAsync);
        cmds[FeatureCommandKeys.ProfileLoad] = new AsyncRelayCommand(ProfileLoadAsync);
        cmds[FeatureCommandKeys.ProfileDelete] = new AsyncRelayCommand(ProfileDeleteAsync);
        cmds[FeatureCommandKeys.ProfileImport] = new AsyncRelayCommand(ProfileImportAsync);
        cmds[FeatureCommandKeys.ProfileShare] = new AsyncRelayCommand(ProfileShareAsync);
        cmds[FeatureCommandKeys.CliCommandCopy] = new RelayCommand(CliCommandCopy);
        cmds[FeatureCommandKeys.ConfigDiff] = new AsyncRelayCommand(ConfigDiffAsync);
        cmds[FeatureCommandKeys.ExportUnified] = new RelayCommand(ExportUnified);
        cmds[FeatureCommandKeys.ConfigImport] = new RelayCommand(ConfigImport);
        cmds[FeatureCommandKeys.AutoFindTools] = new AsyncRelayCommand(AutoFindToolsAsync);

        // ── Konfiguration tab misc ──────────────────────────────────────
        cmds[FeatureCommandKeys.HealthScore] = new RelayCommand(HealthScore);
        cmds[FeatureCommandKeys.DuplicateAnalysis] = new RelayCommand(DuplicateAnalysis);
        cmds[FeatureCommandKeys.ExportCollection] = new AsyncRelayCommand(ExportCollectionAsync);
        cmds[FeatureCommandKeys.RollbackQuick] = _vm.RollbackCommand;
        cmds[FeatureCommandKeys.RollbackHistoryBack] = new RelayCommand(RollbackHistoryBack);
        cmds[FeatureCommandKeys.RollbackHistoryForward] = new RelayCommand(RollbackHistoryForward);
        cmds[FeatureCommandKeys.ApplyLocale] = new RelayCommand(ApplyLocale);
        cmds[FeatureCommandKeys.AutoProfile] = new AsyncRelayCommand(AutoProfileAsync);

        // ── Analyse & Berichte ──────────────────────────────────────────

        cmds[FeatureCommandKeys.JunkReport] = new RelayCommand(JunkReport);
        cmds[FeatureCommandKeys.RomFilter] = new RelayCommand(RomFilter);
        cmds[FeatureCommandKeys.MissingRom] = new RelayCommand(MissingRom);
        cmds[FeatureCommandKeys.HeaderAnalysis] = new RelayCommand(HeaderAnalysis);
        cmds[FeatureCommandKeys.Completeness] = new AsyncRelayCommand(CompletenessAsync);
        cmds[FeatureCommandKeys.DryRunCompare] = new AsyncRelayCommand(DryRunCompareAsync);


        // ── Konvertierung & Hashing ─────────────────────────────────────
        cmds[FeatureCommandKeys.ConversionPipeline] = new RelayCommand(ConversionPipeline);
        cmds[FeatureCommandKeys.ConversionVerify] = new RelayCommand(ConversionVerify);
        cmds[FeatureCommandKeys.FormatPriority] = new RelayCommand(FormatPriority);

        // ── DAT & Verifizierung ─────────────────────────────────────────
        cmds[FeatureCommandKeys.DatAutoUpdate] = new AsyncRelayCommand(DatAutoUpdateAsync);
        cmds[FeatureCommandKeys.DatDiffViewer] = new RelayCommand(DatDiffViewer);
        cmds[FeatureCommandKeys.CustomDatEditor] = new RelayCommand(CustomDatEditor);
        cmds[FeatureCommandKeys.HashDatabaseExport] = new RelayCommand(HashDatabaseExport);

        // ── Sammlungsverwaltung ─────────────────────────────────────────
        cmds[FeatureCommandKeys.CollectionManager] = new RelayCommand(CollectionManager);
        cmds[FeatureCommandKeys.CloneListViewer] = new RelayCommand(CloneListViewer);
        cmds[FeatureCommandKeys.VirtualFolderPreview] = new RelayCommand(VirtualFolderPreview);
        cmds[FeatureCommandKeys.CollectionMerge] = new AsyncRelayCommand(CollectionMergeAsync);

        // ── Sicherheit & Integrität ─────────────────────────────────────
        cmds[FeatureCommandKeys.IntegrityMonitor] = new AsyncRelayCommand(IntegrityMonitorAsync);
        cmds[FeatureCommandKeys.BackupManager] = new RelayCommand(BackupManager);
        cmds[FeatureCommandKeys.Quarantine] = new RelayCommand(Quarantine);
        cmds[FeatureCommandKeys.RuleEngine] = new RelayCommand(RuleEngine);
        cmds[FeatureCommandKeys.HeaderRepair] = new RelayCommand(HeaderRepair);

        // ── Workflow & Automatisierung ───────────────────────────────────

        cmds[FeatureCommandKeys.FilterBuilder] = new RelayCommand(FilterBuilder);
        cmds[FeatureCommandKeys.SortTemplates] = new RelayCommand(SortTemplates);
        cmds[FeatureCommandKeys.PipelineEngine] = new RelayCommand(PipelineEngine);
        cmds[FeatureCommandKeys.SchedulerApply] = new RelayCommand(() => _vm.ApplyScheduler());
        cmds[FeatureCommandKeys.RulePackSharing] = new RelayCommand(RulePackSharing);
        cmds[FeatureCommandKeys.ArcadeMergeSplit] = new RelayCommand(ArcadeMergeSplit);

        // ── Export & Integration ────────────────────────────────────────
        cmds[FeatureCommandKeys.HtmlReport] = new RelayCommand(HtmlReport);
        cmds[FeatureCommandKeys.DatImport] = new RelayCommand(DatImport);

        // ── Infrastruktur & Deployment ──────────────────────────────────
        cmds[FeatureCommandKeys.StorageTiering] = new AsyncRelayCommand(StorageTieringAsync);
        cmds[FeatureCommandKeys.NasOptimization] = new RelayCommand(NasOptimization);
        cmds[FeatureCommandKeys.PortableMode] = new RelayCommand(PortableMode);
        cmds[FeatureCommandKeys.HardlinkMode] = new RelayCommand(HardlinkMode);

        // ── Window-level commands (need IWindowHost) ────────────────────
        if (_windowHost is not null)
        {
            cmds[FeatureCommandKeys.CommandPalette] = new RelayCommand(CommandPalette);
            cmds[FeatureCommandKeys.SystemTray] = new RelayCommand(() => _windowHost.ToggleSystemTray());
            cmds[FeatureCommandKeys.ApiServer] = new RelayCommand(ApiServer);
            cmds[FeatureCommandKeys.Accessibility] = new RelayCommand(Accessibility);
        }
    }

    // ═══ FUNCTIONAL BUTTONS ═════════════════════════════════════════════

    private void ExportLog()
    {
        var path = _dialog.SaveFile(_vm.Loc["Cmd.ExportLog"], _vm.Loc["Cmd.FilterTxt"], "log-export.txt");
        if (!TryResolveSafeOutputPath(path, "Log-Export", out var safePath)) return;
        try
        {
            var lines = _vm.LogEntries.Select(entry => $"[{entry.Level}] {entry.Text}");
            File.WriteAllLines(safePath, lines);
            _vm.AddLog(_vm.Loc.Format("Cmd.LogExported", safePath), "INFO");
        }
        catch (Exception ex)
        { LogError("IO-EXPORT", _vm.Loc.Format("Cmd.ExportFailed", ex.Message)); }
    }

    private async Task ProfileDeleteAsync()
    {
        if (string.IsNullOrWhiteSpace(_vm.SelectedRunProfileId))
        {
            _vm.AddLog(_vm.Loc["Cmd.ProfileDeleteNoSelection"], "WARN");
            return;
        }

        if (!_dialog.Confirm(_vm.Loc["Cmd.ProfileDeleteConfirm"], _vm.Loc["Cmd.ProfileDeleteTitle"]))
            return;

        try
        {
            var deleted = await _vm.RunProfileService.DeleteAsync(_vm.SelectedRunProfileId);
            if (!deleted)
            {
                _vm.AddLog(_vm.Loc["Cmd.ProfileNotFound"], "WARN");
                return;
            }

            await _vm.RefreshRunConfigurationCatalogsAsync();
            _vm.RestoreRunConfigurationSelection(_vm.SelectedWorkflowScenarioId, null);
            _vm.AddLog(_vm.Loc["Cmd.ProfileDeleted"], "INFO");
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-PROFILE", ex.Message);
        }
    }

    private async Task ProfileSaveAsync()
    {
        if (!TryPromptProfileDocument(out var document) || document is null)
            return;

        try
        {
            var saved = await _vm.RunProfileService.SaveAsync(document);
            await _vm.RefreshRunConfigurationCatalogsAsync();
            _vm.RestoreRunConfigurationSelection(_vm.SelectedWorkflowScenarioId, saved.Id);
            _vm.ProfileName = saved.Name;
            _vm.AddLog(_vm.Loc.Format("Cmd.ProfileSaved", saved.Name, saved.Id), "INFO");
        }
        catch (InvalidOperationException ex)
        {
            LogWarning("GUI-PROFILE", _vm.Loc.Format("Cmd.ProfileSaveFailed", ex.Message));
        }
    }

    private async Task ProfileLoadAsync()
    {
        var (success, materialized) = await TryCreateSelectedMaterializedRunConfigurationAsync();
        if (!success || materialized is null)
            return;

        _vm.ApplyMaterializedRunConfiguration(materialized);
        var workflowName = materialized.Workflow?.Name ?? _vm.Loc["Cmd.ProfileLoadedNoWorkflow"];
        var profileName = materialized.Profile?.Name ?? _vm.Loc["Cmd.ProfileLoadedNoProfile"];
        _vm.AddLog(_vm.Loc.Format("Cmd.ProfileLoaded", workflowName, profileName), "INFO");
    }

    private async Task ProfileImportAsync()
    {
        var path = _dialog.BrowseFile(_vm.Loc["Cmd.ProfileImportTitle"], _vm.Loc["Cmd.FilterJson"]);
        if (path is null) return;
        try
        {
            var imported = await _vm.RunProfileService.ImportAsync(path);
            await _vm.RefreshRunConfigurationCatalogsAsync();
            _vm.RestoreRunConfigurationSelection(_vm.SelectedWorkflowScenarioId, imported.Id);
            _vm.ProfileName = imported.Name;
            _vm.AddLog(_vm.Loc.Format("Cmd.ProfileImported", Path.GetFileName(path)), "INFO");
        }
        catch (JsonException) { LogError("GUI-IMPORT", _vm.Loc["Cmd.ImportInvalidJson"], _vm.Loc["Cmd.ImportJsonHint"]); }
        catch (InvalidOperationException ex) { LogError("GUI-IMPORT", ex.Message); }
        catch (Exception ex) { LogError("GUI-IMPORT", _vm.Loc.Format("Cmd.ImportFailed", ex.Message)); }
    }

    /// <summary>GUI-107: Copy current profile as JSON to clipboard for sharing.</summary>
    private async Task ProfileShareAsync()
    {
        var selected = await TryGetSelectedProfileDocumentAsync();
        var document = selected ?? _vm.BuildCurrentRunProfileDocument(
            NormalizeProfileId(string.IsNullOrWhiteSpace(_vm.ProfileName) ? "custom-profile" : _vm.ProfileName),
            string.IsNullOrWhiteSpace(_vm.ProfileName) ? "Custom Profile" : _vm.ProfileName,
            _vm.HasSelectedRunProfile ? _vm.SelectedRunProfileDescription : null);
        var json = JsonSerializer.Serialize(document, ProfileJsonOptions);
        TryCopyToClipboard(json, _vm.Loc["Cmd.ProfileCopied"]);
    }

    /// <summary>GUI-108: Build equivalent CLI command from current settings and copy to clipboard.</summary>
    private void CliCommandCopy()
    {
        var draft = _vm.BuildCurrentRunConfigurationDraft();
        var parts = new List<string> { "dotnet run --project src/Romulus.CLI --" };

        if (_vm.Roots.Count > 0)
            parts.Add($"--roots \"{string.Join(";", draft.Roots)}\"");

        if (!string.IsNullOrWhiteSpace(draft.WorkflowScenarioId))
            parts.Add($"--workflow {draft.WorkflowScenarioId}");

        if (!string.IsNullOrWhiteSpace(draft.ProfileId))
            parts.Add($"--profile {draft.ProfileId}");

        parts.Add(string.Equals(draft.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase)
            ? "--mode Move"
            : "--mode DryRun");

        if (draft.PreferRegions is { Length: > 0 })
            parts.Add($"--prefer {string.Join(",", draft.PreferRegions)}");

        if (draft.Extensions is { Length: > 0 })
            parts.Add($"--extensions {string.Join(",", draft.Extensions)}");

        if (draft.RemoveJunk == true) parts.Add("--removejunk");
        if (draft.OnlyGames == true) parts.Add("--gamesonly");
        if (draft.KeepUnknownWhenOnlyGames == false) parts.Add("--dropunknown");
        if (draft.AggressiveJunk == true) parts.Add("--aggressivejunk");
        if (draft.SortConsole == true) parts.Add("--sortconsole");
        if (draft.EnableDat == true) parts.Add("--enabledat");
        if (draft.EnableDatAudit == true) parts.Add("--dat-audit");
        if (draft.EnableDatRename == true) parts.Add("--datrename");
        if (!string.IsNullOrWhiteSpace(draft.DatRoot)) parts.Add($"--datroot \"{draft.DatRoot}\"");
        if (!string.IsNullOrWhiteSpace(draft.HashType)) parts.Add($"--hashtype {draft.HashType}");
        if (!string.IsNullOrWhiteSpace(draft.ConvertFormat)) parts.Add($"--convertformat {draft.ConvertFormat}");
        if (draft.ConvertOnly == true) parts.Add("--convertonly");
        if (draft.ApproveReviews == true) parts.Add("--approve-reviews");
        if (!string.IsNullOrWhiteSpace(draft.ConflictPolicy)) parts.Add($"--conflictpolicy {draft.ConflictPolicy}");
        if (!string.IsNullOrWhiteSpace(draft.TrashRoot)) parts.Add($"--trashroot \"{draft.TrashRoot}\"");
        if (!string.IsNullOrWhiteSpace(_vm.LogLevel) && !_vm.LogLevel.Equals("Info", StringComparison.OrdinalIgnoreCase))
            parts.Add($"--loglevel {_vm.LogLevel}");

        var command = string.Join(" ", parts);
        TryCopyToClipboard(command, _vm.Loc["Cmd.CliCommandCopied"]);
    }

    private async Task ConfigDiffAsync()
    {
        var current = _vm.GetCurrentRunConfigurationMap();
        IReadOnlyDictionary<string, string>? comparison = null;

        var (success, materialized) = await TryCreateSelectedMaterializedRunConfigurationAsync();
        if (success && materialized is not null)
            comparison = MainViewModel.BuildRunConfigurationMap(materialized.EffectiveDraft);
        else
            comparison = ProfileService.LoadSavedConfigFlat();

        if (comparison is null)
        { _dialog.Info(_vm.Loc["Cmd.ConfigDiffNoSaved"], _vm.Loc["Cmd.ConfigDiffTitle"]); return; }

        var diffs = FeatureService.GetConfigDiff(
            new Dictionary<string, string>(current, StringComparer.Ordinal),
            new Dictionary<string, string>(comparison, StringComparer.Ordinal));
        if (diffs.Count == 0)
        { _dialog.Info(_vm.Loc["Cmd.ConfigDiffNoDiff"], _vm.Loc["Cmd.ConfigDiffTitle"]); return; }
        var sb = new StringBuilder();
        sb.AppendLine(_vm.Loc["Cmd.ConfigDiffHeader"]);
        foreach (var d in diffs)
            sb.AppendLine($"  {d.Key}: \"{d.SavedValue}\" → \"{d.CurrentValue}\"");
        _dialog.ShowText("Config-Diff", sb.ToString());
    }

    private void ExportUnified()
    {
        var path = _dialog.SaveFile("Konfiguration exportieren", "JSON (*.json)|*.json", "romulus-config.json");
        if (!TryResolveSafeOutputPath(path, "Konfigurations-Export", out var safePath)) return;
        try
        {
            ProfileService.Export(safePath, _vm.GetCurrentConfigMap());
            _vm.AddLog($"Konfiguration exportiert: {safePath} — Hinweis: Enthält lokale Pfade (Roots, ToolPaths). Vor dem Teilen prüfen.", "INFO");
        }
        catch (Exception ex) { LogError("IO-EXPORT", _vm.Loc.Format("Cmd.ExportFailed", ex.Message)); }
    }

    private void ConfigImport()
    {
        var path = _dialog.BrowseFile(_vm.Loc["Cmd.ConfigImportTitle"], _vm.Loc["Cmd.FilterJson"]);
        if (path is null) return;
        try
        {
            ProfileService.Import(path);
            _settings.LoadInto(_vm);
            _vm.RefreshStatus();
            _vm.AddLog(_vm.Loc.Format("Cmd.ConfigImported", Path.GetFileName(path)), "INFO");
        }
        catch (JsonException) { LogError("GUI-IMPORT", _vm.Loc["Cmd.ImportInvalidJson"], _vm.Loc["Cmd.ImportJsonHint"]); }
        catch (Exception ex) { LogError("GUI-IMPORT", _vm.Loc.Format("Cmd.ImportFailed", ex.Message)); }
    }

    private async Task AutoFindToolsAsync()
    {
        _vm.AddLog(_vm.Loc["Cmd.ToolSearching"], "INFO");
        var results = await Task.Run(() =>
        {
            var runner = new ToolRunnerAdapter(null);
            return new Dictionary<string, string?>
            {
                ["chdman"] = runner.FindTool("chdman"),
                ["dolphintool"] = runner.FindTool("dolphintool"),
                ["7z"] = runner.FindTool("7z"),
                ["psxtract"] = runner.FindTool("psxtract"),
                ["ciso"] = runner.FindTool("ciso")
            };
        });
        int found = 0;
        if (!string.IsNullOrEmpty(results["chdman"])) { _vm.ToolChdman = results["chdman"]!; found++; }
        if (!string.IsNullOrEmpty(results["dolphintool"])) { _vm.ToolDolphin = results["dolphintool"]!; found++; }
        if (!string.IsNullOrEmpty(results["7z"])) { _vm.Tool7z = results["7z"]!; found++; }
        if (!string.IsNullOrEmpty(results["psxtract"])) { _vm.ToolPsxtract = results["psxtract"]!; found++; }
        if (!string.IsNullOrEmpty(results["ciso"])) { _vm.ToolCiso = results["ciso"]!; found++; }
        _vm.AddLog(_vm.Loc.Format("Cmd.ToolSearchDone", found), found > 0 ? "INFO" : "WARN");
        _vm.RefreshStatus();
        _vm.SaveSettings();
    }

    // ═══ KONFIGURATION TAB ══════════════════════════════════════════════

    private void HealthScore()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.HealthScoreNoData"], "WARN"); return; }
        var total = _vm.LastCandidates.Count;
        var dupes = _vm.LastDedupeGroups.Sum(g => g.Losers.Count);
        var junk = _vm.LastCandidates.Count(c => c.Category == FileCategory.Junk);
        var verified = _vm.LastCandidates.Count(c => c.DatMatch);
        var score = FeatureService.CalculateHealthScore(total, dupes, junk, verified);
        _vm.HealthScore = $"{score}%";
        _dialog.ShowText(_vm.Loc["Cmd.HealthScoreTitle"], _vm.Loc.Format("Cmd.HealthScoreBody", score, total, dupes, 100.0 * dupes / total, junk, 100.0 * junk / total, verified, 100.0 * verified / total));
    }

    private void DuplicateAnalysis()
    {
        // Consolidated handler: Inspector + Heatmap + CrossRoot in one dialog
        var sb = new StringBuilder();

        // Section 1: Verzeichnis-Analyse (Inspector)
        sb.AppendLine("═══ Verzeichnis-Analyse ═══\n");
        var sources = FeatureService.GetDuplicateInspector(_vm.LastAuditPath);
        if (sources.Count == 0)
            sb.AppendLine("  Keine Audit-Daten vorhanden.\n");
        else
        {
            sb.AppendLine(_vm.Loc["Cmd.DupeTopDirs"]);
            foreach (var s in sources)
                sb.AppendLine($"  {s.Count,4}× │ {s.Directory}");
            sb.AppendLine();
        }

        // Section 2: Konsolen-Heatmap
        sb.AppendLine("═══ Konsolen-Heatmap ═══\n");
        if (_vm.LastDedupeGroups.Count == 0)
            sb.AppendLine("  Keine Deduplizierungs-Daten vorhanden.\n");
        else
        {
            var heatmap = FeatureService.GetDuplicateHeatmap(_vm.LastDedupeGroups);
            foreach (var h in heatmap)
            {
                var bar = new string('█', (int)(h.DuplicatePercent / 5));
                sb.AppendLine($"  {h.Console,-25} {h.Duplicates,4} Dupes ({h.DuplicatePercent:F1}%) {bar}");
            }
            sb.AppendLine();
        }

        // Section 3: Cross-Root-Duplikate
        sb.AppendLine("═══ Cross-Root-Duplikate ═══\n");
        if (_vm.Roots.Count < 2)
            sb.AppendLine("  Mindestens 2 Root-Ordner erforderlich.\n");
        else if (_vm.LastDedupeGroups.Count == 0)
            sb.AppendLine("  Keine Deduplizierungs-Daten vorhanden.\n");
        else
        {
            var roots = _vm.Roots.Select(ArtifactPathResolver.NormalizeRoot).ToList();

            var crossRootGroups = new List<DedupeGroup>();
            foreach (var g in _vm.LastDedupeGroups)
            {
                var allPaths = new[] { g.Winner }.Concat(g.Losers);
                var distinctRoots = allPaths.Select(c => ArtifactPathResolver.FindContainingRoot(c.MainPath, roots)).Where(r => r is not null).Distinct().Count();
                if (distinctRoots > 1) crossRootGroups.Add(g);
            }

            sb.AppendLine($"  Roots: {_vm.Roots.Count}");
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
        }

        // Section 4: Gruppen-Inspektor (Winner/Loser Score-Breakdown)
        sb.AppendLine();
        sb.AppendLine("═══ Gruppen-Inspektor ═══\n");
        if (_vm.LastDedupeGroups.Count == 0)
        {
            sb.AppendLine("  Keine Deduplizierungs-Daten vorhanden.\n");
        }
        else
        {
            foreach (var group in _vm.LastDedupeGroups
                         .OrderByDescending(static item => item.Losers.Count)
                         .ThenBy(static item => item.GameKey, StringComparer.OrdinalIgnoreCase)
                         .Take(20))
            {
                var winnerScore = GetCandidateTotalScore(group.Winner);
                sb.AppendLine($"  [{group.GameKey}] ({group.Losers.Count} Loser)");
                sb.AppendLine($"    Winner: {Path.GetFileName(group.Winner.MainPath)} [{group.Winner.Region}] Score={winnerScore}");
                sb.AppendLine($"      Kriterien: {FormatScoreBreakdown(group.Winner)}");

                foreach (var loser in group.Losers
                             .OrderByDescending(GetCandidateTotalScore)
                             .ThenBy(static item => item.MainPath, StringComparer.OrdinalIgnoreCase)
                             .Take(3))
                {
                    var loserScore = GetCandidateTotalScore(loser);
                    var delta = winnerScore - loserScore;
                    sb.AppendLine($"    Loser:  {Path.GetFileName(loser.MainPath)} [{loser.Region}] Score={loserScore} (Δ {delta:+#;-#;0})");
                    sb.AppendLine($"      Kriterien: {FormatScoreBreakdown(loser)}");
                }
            }

            if (_vm.LastDedupeGroups.Count > 20)
                sb.AppendLine($"\n  … und {_vm.LastDedupeGroups.Count - 20} weitere Gruppen");
        }

        _dialog.ShowText(_vm.Loc["Cmd.DupeInspectorTitle"], sb.ToString());
    }

    internal static long GetCandidateTotalScore(RomCandidate candidate)
        => (long)candidate.RegionScore
           + candidate.FormatScore
           + candidate.VersionScore
           + candidate.HeaderScore
           + candidate.CompletenessScore
           + candidate.SizeTieBreakScore;

    internal static string FormatScoreBreakdown(RomCandidate candidate)
        => $"Region={candidate.RegionScore}, Format={candidate.FormatScore}, Version={candidate.VersionScore}, Header={candidate.HeaderScore}, Completeness={candidate.CompletenessScore}, SizeTieBreak={candidate.SizeTieBreakScore}";

    private void RollbackHistoryBack()
    {
        var auditPath = _vm.PopRollbackUndo();
        if (auditPath is null)
        { _vm.AddLog(_vm.Loc["Cmd.NoRollbackUndo"], "WARN"); return; }
        _vm.AddLog(_vm.Loc.Format("Cmd.RollbackUndone", Path.GetFileName(auditPath)), "INFO");
    }

    private void RollbackHistoryForward()
    {
        var auditPath = _vm.PopRollbackRedo();
        if (auditPath is null)
        { _vm.AddLog(_vm.Loc["Cmd.NoRollbackRedo"], "WARN"); return; }
        _vm.AddLog(_vm.Loc.Format("Cmd.RollbackRedone", Path.GetFileName(auditPath)), "INFO");
    }

    private void ApplyLocale()
    {
        var locale = _vm.Locale ?? "de";
        var strings = FeatureService.LoadLocale(locale);
        if (strings.Count == 0)
        { _vm.AddLog(_vm.Loc.Format("Cmd.LocaleNotFound", locale), "WARN"); return; }
        _vm.Loc.SetLocale(locale);
        _vm.AddLog(_vm.Loc.Format("Cmd.LocaleChanged", locale, strings.Count), "INFO");
        // Title update must be done in code-behind (Window property)
    }

    /// <summary>
    /// Wave-8 F-T03: enumerates each root's files off the UI thread to avoid
    /// Dispatcher freezes on slow volumes (NAS/USB). Profile decision and
    /// dialog presentation stay on the calling (UI) context.
    /// </summary>
    private async Task AutoProfileAsync()
    {
        if (_vm.Roots.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.AutoProfileNoRoots"], "WARN"); return; }

        var roots = _vm.Roots.ToArray();
        var (hasDisc, hasCartridge, warnings) = await Task.Run(() =>
        {
            var fileSystem = new FileSystemAdapter();
            var disc = false;
            var cart = false;
            var warns = new List<string>();
            foreach (var root in roots)
            {
                if (!fileSystem.TestPath(root, "Container")) continue;
                foreach (var f in fileSystem.GetFilesSafe(root).Take(200))
                {
                    var ext = Path.GetExtension(f).ToLowerInvariant();
                    if (DiscFormats.IsAutoProfileDiscExtension(ext)) disc = true;
                    if (ext is ".nes" or ".sfc" or ".gba" or ".nds" or ".z64" or ".gb") cart = true;
                }

                warns.AddRange(fileSystem.ConsumeScanWarnings());
            }
            return (disc, cart, (IReadOnlyList<string>)warns);
        }).ConfigureAwait(true);

        foreach (var warning in warnings)
            _vm.AddLog(warning, "WARN");

        var profile = (hasDisc, hasCartridge) switch
        {
            (true, true) => _vm.Loc["Cmd.ProfileMixed"],
            (true, false) => _vm.Loc["Cmd.ProfileDisc"],
            (false, true) => _vm.Loc["Cmd.ProfileCartridge"],
            _ => _vm.Loc["Cmd.ProfileUnknown"]
        };
        if (!hasDisc && !hasCartridge)
            _vm.AddLog(_vm.Loc["Cmd.AutoProfileNoFormats"], "WARN");
        else
            _vm.AddLog(_vm.Loc.Format("Cmd.AutoProfileResult", profile), "INFO");
        _dialog.Info(_vm.Loc.Format("Cmd.AutoProfileDialog", profile), _vm.Loc["Cmd.AutoProfileTitle"]);
    }

    // Generic Collection-Export (CSV / Excel-XML / Duplicates-CSV).
    // Bleibt nach T-W1-FRONTEND-CULL bewusst erhalten: das ist der neutrale
    // Tabellen-Export der bereinigten Sammlung, nicht der gestrichene
    // Frontend-Export (RetroArch/LaunchBox/EmulationStation/Playnite/MiSTer/
    // Analogue Pocket/OnionOS), der vollstaendig entfernt wurde.
    private System.Threading.Tasks.Task ExportCollectionAsync()
    {
        var formatChoice = _dialog.ShowInputBox(
            "Format wählen: 1 = CSV, 2 = Excel-XML, 3 = Duplikate-CSV",
            "Export Collection",
            "1");
        if (string.IsNullOrWhiteSpace(formatChoice))
            return System.Threading.Tasks.Task.CompletedTask;

        var trimmed = formatChoice.Trim();
        string filter;
        string defaultName;
        System.Func<string>? produceContent;

        switch (trimmed)
        {
            case "1":
                filter = "CSV (*.csv)|*.csv";
                defaultName = "collection-export.csv";
                produceContent = () => FeatureService.ExportCollectionCsv(_vm.LastCandidates?.ToList()
                    ?? new System.Collections.Generic.List<Romulus.Contracts.Models.RomCandidate>());
                break;
            case "2":
                filter = "Excel XML (*.xml)|*.xml";
                defaultName = "collection-export.xml";
                produceContent = () => FeatureService.ExportExcelXml(_vm.LastCandidates?.ToList()
                    ?? new System.Collections.Generic.List<Romulus.Contracts.Models.RomCandidate>());
                break;
            case "3":
                filter = "CSV (*.csv)|*.csv";
                defaultName = "duplicates-export.csv";
                produceContent = () =>
                {
                    var losers = _vm.LastDedupeGroups
                        .SelectMany(static group => group.Losers)
                        .ToList();
                    return FeatureService.ExportCollectionCsv(losers);
                };
                break;
            default:
                _vm.AddLog($"Export Collection: ungültige Auswahl '{trimmed}'.", "WARN");
                return System.Threading.Tasks.Task.CompletedTask;
        }

        var savePath = _dialog.SaveFile("Collection exportieren", filter, defaultName);
        if (string.IsNullOrWhiteSpace(savePath))
            return System.Threading.Tasks.Task.CompletedTask;

        try
        {
            var content = produceContent();
            System.IO.File.WriteAllText(savePath, content, System.Text.Encoding.UTF8);
            _vm.AddLog($"Collection exportiert: {savePath}", "INFO");
        }
        catch (System.Exception ex)
        {
            _vm.AddLog($"Export fehlgeschlagen: {ex.Message}", "ERROR");
            _dialog.Info($"Export fehlgeschlagen: {ex.Message}", "Export Collection");
        }

        return System.Threading.Tasks.Task.CompletedTask;
    }

}
