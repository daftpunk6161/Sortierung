using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// TASK-111: All feature button logic extracted from MainWindow code-behind.
/// Each method maps 1:1 to a former On* event handler.
/// Commands are exposed via MainViewModel.FeatureCommands dictionary.
/// </summary>
public sealed partial class FeatureCommandService
{
    private readonly MainViewModel _vm;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly IWindowHost? _windowHost;
    private volatile bool _datUpdateRunning;

    public FeatureCommandService(MainViewModel vm, ISettingsService settings, IDialogService dialog, IWindowHost? windowHost = null)
    {
        _vm = vm;
        _settings = settings;
        _dialog = dialog;
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

    public void RegisterCommands()
    {
        var cmds = _vm.FeatureCommands;
        // V2-WPF-M02: Clear previous registrations to prevent orphaned command references
        cmds.Clear();

        // ── Functional buttons ──────────────────────────────────────────
        cmds["ExportLog"] = new RelayCommand(ExportLog);
        cmds["ProfileDelete"] = new RelayCommand(ProfileDelete);
        cmds["ProfileImport"] = new RelayCommand(ProfileImport);
        cmds["ProfileShare"] = new RelayCommand(ProfileShare);
        cmds["CliCommandCopy"] = new RelayCommand(CliCommandCopy);
        cmds["ConfigDiff"] = new RelayCommand(ConfigDiff);
        cmds["ExportUnified"] = new RelayCommand(ExportUnified);
        cmds["ConfigImport"] = new RelayCommand(ConfigImport);
        cmds["AutoFindTools"] = new AsyncRelayCommand(AutoFindToolsAsync);

        // ── Konfiguration tab misc ──────────────────────────────────────
        cmds["HealthScore"] = new RelayCommand(HealthScore);
        cmds["CollectionDiff"] = new RelayCommand(CollectionDiff);
        cmds["DuplicateInspector"] = new RelayCommand(DuplicateInspector);
        cmds["DuplicateExport"] = new RelayCommand(DuplicateExport);
        cmds["ExportCsv"] = new RelayCommand(ExportCsv);
        cmds["ExportExcel"] = new RelayCommand(ExportExcel);
        cmds["RollbackUndo"] = new RelayCommand(RollbackUndo);
        cmds["RollbackRedo"] = new RelayCommand(RollbackRedo);
        cmds["ApplyLocale"] = new RelayCommand(ApplyLocale);
        cmds["PluginManager"] = new RelayCommand(PluginManager);
        cmds["AutoProfile"] = new RelayCommand(AutoProfile);

        // ── Analyse & Berichte ──────────────────────────────────────────
        cmds["ConversionEstimate"] = new RelayCommand(ConversionEstimate);
        cmds["JunkReport"] = new RelayCommand(JunkReport);
        cmds["RomFilter"] = new RelayCommand(RomFilter);
        cmds["DuplicateHeatmap"] = new RelayCommand(DuplicateHeatmap);
        cmds["MissingRom"] = new RelayCommand(MissingRom);
        cmds["CrossRootDupe"] = new RelayCommand(CrossRootDupe);
        cmds["HeaderAnalysis"] = new RelayCommand(HeaderAnalysis);
        cmds["Completeness"] = new RelayCommand(Completeness);
        cmds["DryRunCompare"] = new RelayCommand(DryRunCompare);
        cmds["TrendAnalysis"] = new RelayCommand(TrendAnalysis);
        cmds["EmulatorCompat"] = new RelayCommand(EmulatorCompat);

        // ── Konvertierung & Hashing ─────────────────────────────────────
        cmds["ConversionPipeline"] = new RelayCommand(ConversionPipeline);
        cmds["NKitConvert"] = new RelayCommand(NKitConvert);
        cmds["ConvertQueue"] = new RelayCommand(ConvertQueue);
        cmds["ConversionVerify"] = new RelayCommand(ConversionVerify);
        cmds["FormatPriority"] = new RelayCommand(FormatPriority);
        cmds["ParallelHashing"] = new RelayCommand(ParallelHashing);
        cmds["GpuHashing"] = new RelayCommand(GpuHashing);

        // ── DAT & Verifizierung ─────────────────────────────────────────
        cmds["DatAutoUpdate"] = new AsyncRelayCommand(DatAutoUpdateAsync);
        cmds["DatDiffViewer"] = new RelayCommand(DatDiffViewer);
        cmds["TosecDat"] = new RelayCommand(TosecDat);
        cmds["CustomDatEditor"] = new RelayCommand(CustomDatEditor);
        cmds["HashDatabaseExport"] = new RelayCommand(HashDatabaseExport);

        // ── Sammlungsverwaltung ─────────────────────────────────────────
        cmds["CollectionManager"] = new RelayCommand(CollectionManager);
        cmds["CloneListViewer"] = new RelayCommand(CloneListViewer);
        cmds["CoverScraper"] = new RelayCommand(CoverScraper);
        cmds["GenreClassification"] = new RelayCommand(GenreClassification);
        cmds["PlaytimeTracker"] = new RelayCommand(PlaytimeTracker);
        cmds["CollectionSharing"] = new RelayCommand(CollectionSharing);
        cmds["VirtualFolderPreview"] = new RelayCommand(VirtualFolderPreview);

        // ── Sicherheit & Integrität ─────────────────────────────────────
        cmds["IntegrityMonitor"] = new AsyncRelayCommand(IntegrityMonitorAsync);
        cmds["BackupManager"] = new RelayCommand(BackupManager);
        cmds["Quarantine"] = new RelayCommand(Quarantine);
        cmds["RuleEngine"] = new RelayCommand(RuleEngine);
        cmds["PatchEngine"] = new RelayCommand(PatchEngine);
        cmds["HeaderRepair"] = new RelayCommand(HeaderRepair);

        // ── Workflow & Automatisierung ───────────────────────────────────
        cmds["SplitPanelPreview"] = new RelayCommand(SplitPanelPreview);
        cmds["FilterBuilder"] = new RelayCommand(FilterBuilder);
        cmds["SortTemplates"] = new RelayCommand(SortTemplates);
        cmds["PipelineEngine"] = new RelayCommand(PipelineEngine);
        cmds["SchedulerAdvanced"] = new RelayCommand(SchedulerAdvanced);
        cmds["SchedulerApply"] = new RelayCommand(() => _vm.ApplyScheduler());
        cmds["RulePackSharing"] = new RelayCommand(RulePackSharing);
        cmds["ArcadeMergeSplit"] = new RelayCommand(ArcadeMergeSplit);

        // ── Export & Integration ────────────────────────────────────────
        cmds["PdfReport"] = new RelayCommand(PdfReport);
        cmds["LauncherIntegration"] = new RelayCommand(LauncherIntegration);
        cmds["ToolImport"] = new RelayCommand(ToolImport);

        // ── Infrastruktur & Deployment ──────────────────────────────────
        cmds["StorageTiering"] = new RelayCommand(StorageTiering);
        cmds["NasOptimization"] = new RelayCommand(NasOptimization);
        cmds["FtpSource"] = new RelayCommand(FtpSource);
        cmds["CloudSync"] = new RelayCommand(CloudSync);
        cmds["PluginMarketplaceFeature"] = new RelayCommand(PluginMarketplace);
        cmds["PortableMode"] = new RelayCommand(PortableMode);
        cmds["DockerContainer"] = new RelayCommand(DockerContainer);
        cmds["WindowsContextMenu"] = new RelayCommand(WindowsContextMenu);
        cmds["HardlinkMode"] = new RelayCommand(HardlinkMode);
        cmds["MultiInstanceSync"] = new RelayCommand(MultiInstanceSync);

        // ── Window-level commands (need IWindowHost) ────────────────────
        if (_windowHost is not null)
        {
            cmds["CommandPalette"] = new RelayCommand(CommandPalette);
            cmds["SystemTray"] = new RelayCommand(() => _windowHost.ToggleSystemTray());
            cmds["MobileWebUI"] = new RelayCommand(MobileWebUI);
            cmds["Accessibility"] = new RelayCommand(Accessibility);
            cmds["ThemeEngine"] = new RelayCommand(ThemeEngine);
        }
    }

    // ═══ FUNCTIONAL BUTTONS ═════════════════════════════════════════════

    private void ExportLog()
    {
        var path = _dialog.SaveFile(_vm.Loc["Cmd.ExportLog"], _vm.Loc["Cmd.FilterTxt"], "log-export.txt");
        if (path is null) return;
        try
        {
            var lines = _vm.LogEntries.Select(entry => $"[{entry.Level}] {entry.Text}");
            File.WriteAllLines(path, lines);
            _vm.AddLog(_vm.Loc.Format("Cmd.LogExported", path), "INFO");
        }
        catch (Exception ex)
        { LogError("IO-EXPORT", _vm.Loc.Format("Cmd.ExportFailed", ex.Message)); }
    }

    private void ProfileDelete()
    {
        if (!_dialog.Confirm(_vm.Loc["Cmd.ProfileDeleteConfirm"], _vm.Loc["Cmd.ProfileDeleteTitle"])) return;
        if (ProfileService.Delete()) _vm.AddLog(_vm.Loc["Cmd.ProfileDeleted"], "INFO");
        else _vm.AddLog(_vm.Loc["Cmd.ProfileNotFound"], "WARN");
    }

    private void ProfileImport()
    {
        var path = _dialog.BrowseFile(_vm.Loc["Cmd.ProfileImportTitle"], _vm.Loc["Cmd.FilterJson"]);
        if (path is null) return;
        try
        {
            ProfileService.Import(path);
            _settings.LoadInto(_vm);
            _vm.RefreshStatus();
            _vm.AddLog(_vm.Loc.Format("Cmd.ProfileImported", Path.GetFileName(path)), "INFO");
        }
        catch (JsonException) { LogError("GUI-IMPORT", _vm.Loc["Cmd.ImportInvalidJson"], _vm.Loc["Cmd.ImportJsonHint"]); }
        catch (Exception ex) { LogError("GUI-IMPORT", _vm.Loc.Format("Cmd.ImportFailed", ex.Message)); }
    }

    /// <summary>GUI-107: Copy current profile as JSON to clipboard for sharing.</summary>
    private void ProfileShare()
    {
        var configMap = _vm.GetCurrentConfigMap();
        var json = JsonSerializer.Serialize(configMap, new JsonSerializerOptions { WriteIndented = true });
        System.Windows.Clipboard.SetText(json);
        _vm.AddLog(_vm.Loc["Cmd.ProfileCopied"], "INFO");
    }

    /// <summary>GUI-108: Build equivalent CLI command from current settings and copy to clipboard.</summary>
    private void CliCommandCopy()
    {
        var parts = new List<string> { "dotnet run --project src/RomCleanup.CLI --" };

        // Roots
        if (_vm.Roots.Count > 0)
            parts.Add($"--roots \"{string.Join(";", _vm.Roots)}\"");

        // Mode
        parts.Add(_vm.DryRun ? "--mode DryRun" : "--mode Move");

        // Regions
        var regions = _vm.GetPreferredRegions();
        if (regions.Length > 0)
            parts.Add($"--prefer {string.Join(",", regions)}");

        // Flags
        if (_vm.SortConsole) parts.Add("--sortconsole");
        if (_vm.AggressiveJunk) parts.Add("--aggressivejunk");
        if (_vm.UseDat) parts.Add("--enabledat");
        if (_vm.ConvertEnabled) parts.Add("--convertformat");

        // Paths
        if (!string.IsNullOrWhiteSpace(_vm.DatRoot)) parts.Add($"--datroot \"{_vm.DatRoot}\"");
        if (!string.IsNullOrWhiteSpace(_vm.TrashRoot)) parts.Add($"--trashroot \"{_vm.TrashRoot}\"");
        if (!string.IsNullOrWhiteSpace(_vm.DatHashType)) parts.Add($"--hashtype {_vm.DatHashType}");
        if (!string.IsNullOrWhiteSpace(_vm.LogLevel) && _vm.LogLevel != "Info") parts.Add($"--loglevel {_vm.LogLevel}");

        var command = string.Join(" ", parts);
        System.Windows.Clipboard.SetText(command);
        _vm.AddLog(_vm.Loc["Cmd.CliCommandCopied"], "INFO");
    }

    private void ConfigDiff()
    {
        var current = _vm.GetCurrentConfigMap();
        var saved = ProfileService.LoadSavedConfigFlat();
        if (saved is null)
        { _dialog.Info(_vm.Loc["Cmd.ConfigDiffNoSaved"], _vm.Loc["Cmd.ConfigDiffTitle"]); return; }
        var diffs = FeatureService.GetConfigDiff(current, saved);
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
        var path = _dialog.SaveFile("Konfiguration exportieren", "JSON (*.json)|*.json", "romcleanup-config.json");
        if (path is null) return;
        try
        {
            ProfileService.Export(path, _vm.GetCurrentConfigMap());
            _vm.AddLog($"Konfiguration exportiert: {path} — Hinweis: Enthält lokale Pfade (Roots, ToolPaths). Vor dem Teilen prüfen.", "INFO");
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
        var junk = _vm.LastCandidates.Count(c => c.Category == "JUNK");
        var verified = _vm.LastCandidates.Count(c => c.DatMatch);
        var score = FeatureService.CalculateHealthScore(total, dupes, junk, verified);
        _vm.HealthScore = $"{score}%";
        _dialog.ShowText(_vm.Loc["Cmd.HealthScoreTitle"], _vm.Loc.Format("Cmd.HealthScoreBody", score, total, dupes, 100.0 * dupes / total, junk, 100.0 * junk / total, verified, 100.0 * verified / total));
    }

    private void CollectionDiff()
    {
        var fileA = _dialog.BrowseFile(_vm.Loc["Cmd.DiffSelectFirst"], _vm.Loc["Cmd.FilterReport"]);
        if (fileA is null) return;
        var fileB = _dialog.BrowseFile(_vm.Loc["Cmd.DiffSelectSecond"], _vm.Loc["Cmd.FilterReport"]);
        if (fileB is null) return;
        _vm.AddLog($"Collection-Diff: {Path.GetFileName(fileA)} vs. {Path.GetFileName(fileB)}", "INFO");

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
                sb.AppendLine(_vm.Loc["Cmd.DiffCsvHeader"]);
                sb.AppendLine(new string('═', 50));
                sb.AppendLine($"\n  {_vm.Loc.Format("Cmd.DiffEntryA", Path.GetFileName(fileA), setA.Count)}");
                sb.AppendLine($"  {_vm.Loc.Format("Cmd.DiffEntryB", Path.GetFileName(fileB), setB.Count)}");
                sb.AppendLine($"\n  {_vm.Loc.Format("Cmd.DiffSame", same)}");
                sb.AppendLine($"  {_vm.Loc.Format("Cmd.DiffAdded", added.Count)}");
                sb.AppendLine($"  {_vm.Loc.Format("Cmd.DiffRemoved", removed.Count)}");
                if (added.Count > 0)
                {
                    sb.AppendLine($"\n  {_vm.Loc.Format("Cmd.DiffAddedHeader", Math.Min(30, added.Count))}");
                    foreach (var entry in added.Take(30)) sb.AppendLine($"    + {Path.GetFileName(entry)}");
                    if (added.Count > 30) sb.AppendLine($"    {_vm.Loc.Format("Cmd.DiffMore", added.Count - 30)}");
                }
                if (removed.Count > 0)
                {
                    sb.AppendLine($"\n  {_vm.Loc.Format("Cmd.DiffRemovedHeader", Math.Min(30, removed.Count))}");
                    foreach (var entry in removed.Take(30)) sb.AppendLine($"    - {Path.GetFileName(entry)}");
                    if (removed.Count > 30) sb.AppendLine($"    {_vm.Loc.Format("Cmd.DiffMore", removed.Count - 30)}");
                }
                _dialog.ShowText("Collection-Diff", sb.ToString());
            }
            catch (Exception ex) { LogError("GUI-DIFF", _vm.Loc.Format("Cmd.DiffError", ex.Message)); }
        }
        else
        {
            _dialog.ShowText("Collection-Diff", _vm.Loc.Format("Cmd.DiffNonCsv", fileA, fileB));
        }
    }

    private void DuplicateInspector()
    {
        var sources = FeatureService.GetDuplicateInspector(_vm.LastAuditPath);
        if (sources.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.DupeNoData"], "WARN"); return; }
        var sb = new StringBuilder();
        sb.AppendLine(_vm.Loc["Cmd.DupeTopDirs"]);
        foreach (var s in sources)
            sb.AppendLine($"  {s.Count,4}× │ {s.Directory}");
        _dialog.ShowText(_vm.Loc["Cmd.DupeInspectorTitle"], sb.ToString());
    }

    private void DuplicateExport()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN"); return; }
        var path = _dialog.SaveFile(_vm.Loc["Cmd.DupeExportTitle"], _vm.Loc["Cmd.FilterCsv"], "duplikate.csv");
        if (path is null) return;
        var losers = _vm.LastDedupeGroups.SelectMany(g => g.Losers).ToList();
        var csv = FeatureService.ExportCollectionCsv(losers);
        File.WriteAllText(path, csv, Encoding.UTF8);
        _vm.AddLog(_vm.Loc.Format("Cmd.DupeExported", path, losers.Count), "INFO");
    }

    private void ExportCsv()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN"); return; }
        var path = _dialog.SaveFile(_vm.Loc["Cmd.CsvExportTitle"], _vm.Loc["Cmd.FilterCsv"], "sammlung.csv");
        if (path is null) return;
        var csv = FeatureService.ExportCollectionCsv(_vm.LastCandidates);
        File.WriteAllText(path, "\uFEFF" + csv, Encoding.UTF8);
        _vm.AddLog(_vm.Loc.Format("Cmd.CsvExported", path, _vm.LastCandidates.Count), "INFO");
    }

    private void ExportExcel()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN"); return; }
        var path = _dialog.SaveFile(_vm.Loc["Cmd.ExcelExportTitle"], _vm.Loc["Cmd.FilterExcel"], "sammlung.xml");
        if (path is null) return;
        var xml = FeatureService.ExportExcelXml(_vm.LastCandidates);
        File.WriteAllText(path, xml, Encoding.UTF8);
        _vm.AddLog(_vm.Loc.Format("Cmd.ExcelExported", path), "INFO");
    }

    private void RollbackUndo()
    {
        var auditPath = _vm.PopRollbackUndo();
        if (auditPath is null)
        { _vm.AddLog(_vm.Loc["Cmd.NoRollbackUndo"], "WARN"); return; }
        _vm.AddLog(_vm.Loc.Format("Cmd.RollbackUndone", Path.GetFileName(auditPath)), "INFO");
    }

    private void RollbackRedo()
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

    private void PluginManager()
    {
        var pluginDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(pluginDir))
        {
            _dialog.Info(_vm.Loc["Cmd.PluginNoDir"], _vm.Loc["Cmd.PluginTitle"]);
            return;
        }
        var manifests = Directory.GetFiles(pluginDir, "plugin.json", SearchOption.AllDirectories);
        var sb = new StringBuilder();
        sb.AppendLine(_vm.Loc.Format("Cmd.PluginHeader", manifests.Length));
        foreach (var m in manifests)
        {
            var dir = Path.GetDirectoryName(m)!;
            sb.AppendLine($"  \U0001F4E6 {Path.GetFileName(dir)}");
            sb.AppendLine($"     Pfad: {dir}");
        }
        if (manifests.Length == 0)
            sb.AppendLine(_vm.Loc["Cmd.PluginNone"]);
        _dialog.ShowText(_vm.Loc["Cmd.PluginTitle"], sb.ToString());
    }

    private void AutoProfile()
    {
        if (_vm.Roots.Count == 0)
        { _vm.AddLog(_vm.Loc["Cmd.AutoProfileNoRoots"], "WARN"); return; }
        var hasDisc = false;
        var hasCartridge = false;
        foreach (var root in _vm.Roots)
        {
            if (!Directory.Exists(root)) continue;
            foreach (var f in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories).Take(200))
            {
                var ext = Path.GetExtension(f).ToLowerInvariant();
                if (ext is ".chd" or ".iso" or ".bin" or ".cue" or ".gdi") hasDisc = true;
                if (ext is ".nes" or ".sfc" or ".gba" or ".nds" or ".z64" or ".gb") hasCartridge = true;
            }
        }
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

}
