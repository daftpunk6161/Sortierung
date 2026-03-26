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
    private static readonly HashSet<string> SafeShellFileExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".html", ".htm", ".csv", ".json", ".xml", ".txt", ".log", ".lpl"
    };

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
        cmds["DuplicateAnalysis"] = new RelayCommand(DuplicateAnalysis);
        cmds["ExportCollection"] = new RelayCommand(ExportCollection);
        var rollbackHistoryBack = new RelayCommand(RollbackHistoryBack);
        var rollbackHistoryForward = new RelayCommand(RollbackHistoryForward);
        cmds["RollbackHistoryBack"] = rollbackHistoryBack;
        cmds["RollbackHistoryForward"] = rollbackHistoryForward;
        cmds["RollbackUndo"] = rollbackHistoryBack;
        cmds["RollbackRedo"] = rollbackHistoryForward;
        cmds["ApplyLocale"] = new RelayCommand(ApplyLocale);
        cmds["AutoProfile"] = new RelayCommand(AutoProfile);

        // ── Analyse & Berichte ──────────────────────────────────────────

        cmds["JunkReport"] = new RelayCommand(JunkReport);
        cmds["RomFilter"] = new RelayCommand(RomFilter);
        cmds["MissingRom"] = new RelayCommand(MissingRom);
        cmds["HeaderAnalysis"] = new RelayCommand(HeaderAnalysis);
        cmds["Completeness"] = new RelayCommand(Completeness);
        cmds["DryRunCompare"] = new RelayCommand(DryRunCompare);


        // ── Konvertierung & Hashing ─────────────────────────────────────
        cmds["ConversionPipeline"] = new RelayCommand(ConversionPipeline);
        cmds["NKitConvert"] = new RelayCommand(NKitConvert);
        cmds["ConversionVerify"] = new RelayCommand(ConversionVerify);
        cmds["FormatPriority"] = new RelayCommand(FormatPriority);

        // ── DAT & Verifizierung ─────────────────────────────────────────
        cmds["DatAutoUpdate"] = new AsyncRelayCommand(DatAutoUpdateAsync);
        cmds["DatDiffViewer"] = new RelayCommand(DatDiffViewer);
        cmds["CustomDatEditor"] = new RelayCommand(CustomDatEditor);
        cmds["HashDatabaseExport"] = new RelayCommand(HashDatabaseExport);

        // ── Sammlungsverwaltung ─────────────────────────────────────────
        cmds["CollectionManager"] = new RelayCommand(CollectionManager);
        cmds["CloneListViewer"] = new RelayCommand(CloneListViewer);
        cmds["VirtualFolderPreview"] = new RelayCommand(VirtualFolderPreview);

        // ── Sicherheit & Integrität ─────────────────────────────────────
        cmds["IntegrityMonitor"] = new AsyncRelayCommand(IntegrityMonitorAsync);
        cmds["BackupManager"] = new RelayCommand(BackupManager);
        cmds["Quarantine"] = new RelayCommand(Quarantine);
        cmds["RuleEngine"] = new RelayCommand(RuleEngine);
        cmds["PatchEngine"] = new RelayCommand(PatchEngine);
        cmds["HeaderRepair"] = new RelayCommand(HeaderRepair);

        // ── Workflow & Automatisierung ───────────────────────────────────

        cmds["FilterBuilder"] = new RelayCommand(FilterBuilder);
        cmds["SortTemplates"] = new RelayCommand(SortTemplates);
        cmds["PipelineEngine"] = new RelayCommand(PipelineEngine);
        cmds["CronTester"] = new RelayCommand(CronTester);
        cmds["SchedulerApply"] = new RelayCommand(() => _vm.ApplyScheduler());
        cmds["RulePackSharing"] = new RelayCommand(RulePackSharing);
        cmds["ArcadeMergeSplit"] = new RelayCommand(ArcadeMergeSplit);

        // ── Export & Integration ────────────────────────────────────────
        cmds["HtmlReport"] = new RelayCommand(HtmlReport);
        cmds["LauncherIntegration"] = new RelayCommand(LauncherIntegration);
        cmds["DatImport"] = new RelayCommand(DatImport);

        // ── Infrastruktur & Deployment ──────────────────────────────────
        cmds["StorageTiering"] = new RelayCommand(StorageTiering);
        cmds["NasOptimization"] = new RelayCommand(NasOptimization);
        cmds["PortableMode"] = new RelayCommand(PortableMode);
        cmds["HardlinkMode"] = new RelayCommand(HardlinkMode);
        cmds["MultiInstanceSync"] = new RelayCommand(MultiInstanceSync);

        // ── Window-level commands (need IWindowHost) ────────────────────
        if (_windowHost is not null)
        {
            cmds["CommandPalette"] = new RelayCommand(CommandPalette);
            cmds["SystemTray"] = new RelayCommand(() => _windowHost.ToggleSystemTray());
            cmds["ApiServer"] = new RelayCommand(ApiServer);
            cmds["Accessibility"] = new RelayCommand(Accessibility);
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
            var roots = _vm.Roots.Select(r => Path.GetFullPath(r).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)).ToList();
            string? GetRoot(string filePath)
            {
                var full = Path.GetFullPath(filePath);
                return roots.FirstOrDefault(r => full.Length > r.Length && full.StartsWith(r, StringComparison.OrdinalIgnoreCase) && full[r.Length] is '\\' or '/');
            }

            var crossRootGroups = new List<DedupeGroup>();
            foreach (var g in _vm.LastDedupeGroups)
            {
                var allPaths = new[] { g.Winner }.Concat(g.Losers);
                var distinctRoots = allPaths.Select(c => GetRoot(c.MainPath)).Where(r => r is not null).Distinct().Count();
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

        _dialog.ShowText(_vm.Loc["Cmd.DupeInspectorTitle"], sb.ToString());
    }

    private void ExportCollection()
    {
        // Consolidated handler: CSV / Excel XML / Duplikate-CSV via format selection
        var choices = new List<string> { "CSV (Sammlung)", "Excel-XML (Sammlung)", "CSV (nur Duplikate)" };
        var choice = _dialog.ShowInputBox("Export-Format wählen:\n\n  1 — CSV (Sammlung)\n  2 — Excel-XML (Sammlung)\n  3 — CSV (nur Duplikate)\n\nNummer eingeben:", "Sammlung exportieren");
        if (string.IsNullOrWhiteSpace(choice)) return;

        switch (choice.Trim())
        {
            case "1":
                if (_vm.LastCandidates.Count == 0)
                { _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN"); return; }
                var csvPath = _dialog.SaveFile(_vm.Loc["Cmd.CsvExportTitle"], _vm.Loc["Cmd.FilterCsv"], "sammlung.csv");
                if (csvPath is null) return;
                var csv = FeatureService.ExportCollectionCsv(_vm.LastCandidates);
                File.WriteAllText(csvPath, "\uFEFF" + csv, Encoding.UTF8);
                _vm.AddLog(_vm.Loc.Format("Cmd.CsvExported", csvPath, _vm.LastCandidates.Count), "INFO");
                break;

            case "2":
                if (_vm.LastCandidates.Count == 0)
                { _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN"); return; }
                var xlPath = _dialog.SaveFile(_vm.Loc["Cmd.ExcelExportTitle"], _vm.Loc["Cmd.FilterExcel"], "sammlung.xml");
                if (xlPath is null) return;
                var xml = FeatureService.ExportExcelXml(_vm.LastCandidates);
                File.WriteAllText(xlPath, xml, Encoding.UTF8);
                _vm.AddLog(_vm.Loc.Format("Cmd.ExcelExported", xlPath), "INFO");
                break;

            case "3":
                if (_vm.LastDedupeGroups.Count == 0)
                { _vm.AddLog(_vm.Loc["Cmd.NoExportData"], "WARN"); return; }
                var dupePath = _dialog.SaveFile(_vm.Loc["Cmd.DupeExportTitle"], _vm.Loc["Cmd.FilterCsv"], "duplikate.csv");
                if (dupePath is null) return;
                var losers = _vm.LastDedupeGroups.SelectMany(g => g.Losers).ToList();
                var dupeCsv = FeatureService.ExportCollectionCsv(losers);
                File.WriteAllText(dupePath, dupeCsv, Encoding.UTF8);
                _vm.AddLog(_vm.Loc.Format("Cmd.DupeExported", dupePath, losers.Count), "INFO");
                break;

            default:
                _vm.AddLog("Ungültige Auswahl. Bitte 1, 2 oder 3 eingeben.", "WARN");
                break;
        }
    }

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
