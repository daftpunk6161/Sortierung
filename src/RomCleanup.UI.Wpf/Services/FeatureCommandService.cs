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

    public void RegisterCommands()
    {
        var cmds = _vm.FeatureCommands;

        // ── Functional buttons ──────────────────────────────────────────
        cmds["ExportLog"] = new RelayCommand(ExportLog);
        cmds["ProfileDelete"] = new RelayCommand(ProfileDelete);
        cmds["ProfileImport"] = new RelayCommand(ProfileImport);
        cmds["ConfigDiff"] = new RelayCommand(ConfigDiff);
        cmds["ExportUnified"] = new RelayCommand(ExportUnified);
        cmds["ConfigImport"] = new RelayCommand(ConfigImport);
        cmds["AutoFindTools"] = new RelayCommand(async () => await AutoFindToolsAsync());

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
        cmds["DatAutoUpdate"] = new RelayCommand(async () => await DatAutoUpdateAsync());
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
        cmds["IntegrityMonitor"] = new RelayCommand(async () => await IntegrityMonitorAsync());
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
        var path = _dialog.SaveFile("Log exportieren", "Textdateien (*.txt)|*.txt|Alle (*.*)|*.*", "log-export.txt");
        if (path is null) return;
        try
        {
            var lines = _vm.LogEntries.Select(entry => $"[{entry.Level}] {entry.Text}");
            File.WriteAllLines(path, lines);
            _vm.AddLog($"Log exportiert: {path}", "INFO");
        }
        catch (Exception ex)
        { _vm.AddLog($"Log-Export fehlgeschlagen: {ex.Message}", "ERROR"); }
    }

    private void ProfileDelete()
    {
        if (!_dialog.Confirm("Gespeicherte Einstellungen wirklich löschen?", "Profil löschen")) return;
        if (ProfileService.Delete()) _vm.AddLog("Profil gelöscht.", "INFO");
        else _vm.AddLog("Kein gespeichertes Profil gefunden.", "WARN");
    }

    private void ProfileImport()
    {
        var path = _dialog.BrowseFile("Profil importieren", "JSON (*.json)|*.json");
        if (path is null) return;
        try
        {
            ProfileService.Import(path);
            _settings.LoadInto(_vm);
            _vm.RefreshStatus();
            _vm.AddLog($"Profil importiert: {Path.GetFileName(path)}", "INFO");
        }
        catch (JsonException) { _vm.AddLog("Import fehlgeschlagen: Ungültiges JSON-Format.", "ERROR"); }
        catch (Exception ex) { _vm.AddLog($"Import fehlgeschlagen: {ex.Message}", "ERROR"); }
    }

    private void ConfigDiff()
    {
        var current = _vm.GetCurrentConfigMap();
        var saved = ProfileService.LoadSavedConfigFlat();
        if (saved is null)
        { _dialog.Info("Keine gespeicherte Konfiguration zum Vergleichen vorhanden.", "Config-Diff"); return; }
        var diffs = FeatureService.GetConfigDiff(current, saved);
        if (diffs.Count == 0)
        { _dialog.Info("Keine Unterschiede zwischen aktueller und gespeicherter Konfiguration.", "Config-Diff"); return; }
        var sb = new StringBuilder();
        sb.AppendLine("Config-Diff (Aktuell vs. Gespeichert):\n");
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
        catch (Exception ex) { _vm.AddLog($"Export fehlgeschlagen: {ex.Message}", "ERROR"); }
    }

    private void ConfigImport()
    {
        var path = _dialog.BrowseFile("Konfiguration importieren", "JSON (*.json)|*.json");
        if (path is null) return;
        try
        {
            ProfileService.Import(path);
            _settings.LoadInto(_vm);
            _vm.RefreshStatus();
            _vm.AddLog($"Konfiguration importiert: {Path.GetFileName(path)}", "INFO");
        }
        catch (JsonException) { _vm.AddLog("Import fehlgeschlagen: Ungültiges JSON-Format.", "ERROR"); }
        catch (Exception ex) { _vm.AddLog($"Import fehlgeschlagen: {ex.Message}", "ERROR"); }
    }

    private async Task AutoFindToolsAsync()
    {
        _vm.AddLog("Suche nach Tools…", "INFO");
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
        _vm.AddLog($"Tool-Suche abgeschlossen: {found} von 5 gefunden.", found > 0 ? "INFO" : "WARN");
        _vm.RefreshStatus();
    }

    // ═══ KONFIGURATION TAB ══════════════════════════════════════════════

    private void HealthScore()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Erst einen Lauf starten, um den Health-Score zu berechnen.", "WARN"); return; }
        var total = _vm.LastCandidates.Count;
        var dupes = _vm.LastDedupeGroups.Sum(g => g.Losers.Count);
        var junk = _vm.LastCandidates.Count(c => c.Category == "JUNK");
        var verified = _vm.LastCandidates.Count(c => c.DatMatch);
        var score = FeatureService.CalculateHealthScore(total, dupes, junk, verified);
        _vm.HealthScore = $"{score}%";
        _dialog.ShowText("Health-Score", $"Sammlungs-Gesundheit: {score}/100\n\n" +
            $"Dateien: {total}\nDuplikate: {dupes} ({100.0 * dupes / total:F1}%)\n" +
            $"Junk: {junk} ({100.0 * junk / total:F1}%)\nVerifiziert: {verified} ({100.0 * verified / total:F1}%)");
    }

    private void CollectionDiff()
    {
        var fileA = _dialog.BrowseFile("Ersten Report wählen", "HTML (*.html)|*.html|CSV (*.csv)|*.csv");
        if (fileA is null) return;
        var fileB = _dialog.BrowseFile("Zweiten Report wählen", "HTML (*.html)|*.html|CSV (*.csv)|*.csv");
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
                { var mainPath = line.Split(';')[0].Trim('"'); if (!string.IsNullOrWhiteSpace(mainPath)) setA.Add(mainPath); }
                foreach (var line in File.ReadLines(fileB).Skip(1))
                { var mainPath = line.Split(';')[0].Trim('"'); if (!string.IsNullOrWhiteSpace(mainPath)) setB.Add(mainPath); }

                var added = setB.Except(setA).ToList();
                var removed = setA.Except(setB).ToList();
                var same = setA.Intersect(setB).Count();

                var sb = new StringBuilder();
                sb.AppendLine("Collection-Diff (CSV)");
                sb.AppendLine(new string('═', 50));
                sb.AppendLine($"\n  A: {Path.GetFileName(fileA)} ({setA.Count} Einträge)");
                sb.AppendLine($"  B: {Path.GetFileName(fileB)} ({setB.Count} Einträge)");
                sb.AppendLine($"\n  Gleich:     {same}");
                sb.AppendLine($"  Hinzugefügt (in B, nicht in A): {added.Count}");
                sb.AppendLine($"  Entfernt (in A, nicht in B):    {removed.Count}");
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
                _dialog.ShowText("Collection-Diff", sb.ToString());
            }
            catch (Exception ex) { _vm.AddLog($"Collection-Diff Fehler: {ex.Message}", "ERROR"); }
        }
        else
        {
            _dialog.ShowText("Collection-Diff", $"Vergleich:\n  A: {fileA}\n  B: {fileB}\n\nReport-Dateien werden verglichen – detaillierter Diff erfordert CSV-Format.");
        }
    }

    private void DuplicateInspector()
    {
        var sources = FeatureService.GetDuplicateInspector(_vm.LastAuditPath);
        if (sources.Count == 0)
        { _vm.AddLog("Keine Duplikat-Daten vorhanden (erst Move/DryRun starten).", "WARN"); return; }
        var sb = new StringBuilder();
        sb.AppendLine("Top Duplikat-Quellverzeichnisse:\n");
        foreach (var s in sources)
            sb.AppendLine($"  {s.Count,4}× │ {s.Directory}");
        _dialog.ShowText("Duplikat-Inspektor", sb.ToString());
    }

    private void DuplicateExport()
    {
        if (_vm.LastDedupeGroups.Count == 0)
        { _vm.AddLog("Keine Duplikat-Daten zum Exportieren.", "WARN"); return; }
        var path = _dialog.SaveFile("Duplikate exportieren", "CSV (*.csv)|*.csv", "duplikate.csv");
        if (path is null) return;
        var losers = _vm.LastDedupeGroups.SelectMany(g => g.Losers).ToList();
        var csv = FeatureService.ExportCollectionCsv(losers);
        File.WriteAllText(path, csv, Encoding.UTF8);
        _vm.AddLog($"Duplikate exportiert: {path} ({losers.Count} Einträge)", "INFO");
    }

    private void ExportCsv()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Keine Daten zum Exportieren.", "WARN"); return; }
        var path = _dialog.SaveFile("CSV Export", "CSV (*.csv)|*.csv", "sammlung.csv");
        if (path is null) return;
        var csv = FeatureService.ExportCollectionCsv(_vm.LastCandidates);
        File.WriteAllText(path, "\uFEFF" + csv, Encoding.UTF8);
        _vm.AddLog($"CSV exportiert: {path} ({_vm.LastCandidates.Count} Einträge)", "INFO");
    }

    private void ExportExcel()
    {
        if (_vm.LastCandidates.Count == 0)
        { _vm.AddLog("Keine Daten zum Exportieren.", "WARN"); return; }
        var path = _dialog.SaveFile("Excel Export", "Excel XML (*.xml)|*.xml", "sammlung.xml");
        if (path is null) return;
        var xml = FeatureService.ExportExcelXml(_vm.LastCandidates);
        File.WriteAllText(path, xml, Encoding.UTF8);
        _vm.AddLog($"Excel exportiert: {path}", "INFO");
    }

    private void RollbackUndo()
    {
        var auditPath = _vm.PopRollbackUndo();
        if (auditPath is null)
        { _vm.AddLog("Kein Rollback zum Rückgängig machen.", "WARN"); return; }
        _vm.AddLog($"Rollback rückgängig gemacht: {Path.GetFileName(auditPath)}", "INFO");
    }

    private void RollbackRedo()
    {
        var auditPath = _vm.PopRollbackRedo();
        if (auditPath is null)
        { _vm.AddLog("Kein Redo-Rollback verfügbar.", "WARN"); return; }
        _vm.AddLog($"Rollback Redo: {Path.GetFileName(auditPath)}", "INFO");
    }

    private void ApplyLocale()
    {
        var locale = _vm.Locale ?? "de";
        var strings = FeatureService.LoadLocale(locale);
        if (strings.Count == 0)
        { _vm.AddLog($"Sprachdatei '{locale}' nicht gefunden.", "WARN"); return; }
        _vm.AddLog($"Sprache gewechselt: {locale} ({strings.Count} Strings geladen). Hinweis: Aktuell wird nur der Fenstertitel lokalisiert.", "INFO");
        // Title update must be done in code-behind (Window property)
    }

    private void PluginManager()
    {
        var pluginDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(pluginDir))
        {
            _dialog.Info("Kein Plugin-Verzeichnis gefunden.\n\nErstelle 'plugins/' im Programmverzeichnis, um Plugins zu verwenden.", "Plugin-Manager");
            return;
        }
        var manifests = Directory.GetFiles(pluginDir, "plugin.json", SearchOption.AllDirectories);
        var sb = new StringBuilder();
        sb.AppendLine($"Plugin-Manager: {manifests.Length} Plugin(s) gefunden\n");
        foreach (var m in manifests)
        {
            var dir = Path.GetDirectoryName(m)!;
            sb.AppendLine($"  📦 {Path.GetFileName(dir)}");
            sb.AppendLine($"     Pfad: {dir}");
        }
        if (manifests.Length == 0)
            sb.AppendLine("  Keine Plugins installiert.");
        _dialog.ShowText("Plugin-Manager", sb.ToString());
    }

    private void AutoProfile()
    {
        if (_vm.Roots.Count == 0)
        { _vm.AddLog("Keine Root-Ordner – Auto-Profil nicht möglich.", "WARN"); return; }
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
            (true, true) => "Gemischt (Disc + Cartridge): Konvertierung empfohlen",
            (true, false) => "Disc-basiert: CHD-Konvertierung empfohlen, aggressive Deduplizierung",
            (false, true) => "Cartridge-basiert: ZIP-Komprimierung, leichte Deduplizierung",
            _ => "Unbekannt: Keine erkannten ROM-Formate gefunden. Bitte überprüfen Sie die Root-Ordner."
        };
        if (!hasDisc && !hasCartridge)
            _vm.AddLog("Auto-Profil: Keine bekannten ROM-Formate erkannt – Standard-Profil wird empfohlen.", "WARN");
        else
            _vm.AddLog($"Auto-Profil: {profile}", "INFO");
        _dialog.Info($"Auto-Profil-Empfehlung:\n\n{profile}\n\n" +
            "Hinweis: Die Erkennung basiert auf Dateierweiterungen der ersten 200 Dateien pro Root-Ordner.", "Auto-Profil");
    }

}
