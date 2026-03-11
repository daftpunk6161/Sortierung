using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.Dat;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf;

public partial class MainWindow : Window
{
    private static readonly string[] DefaultExtensions =
    [
        ".zip", ".7z", ".chd", ".iso", ".bin", ".cue", ".gdi", ".ccd",
        ".rvz", ".gcz", ".wbfs", ".nsp", ".xci", ".nes", ".snes",
        ".sfc", ".smc", ".gb", ".gbc", ".gba", ".nds", ".3ds",
        ".n64", ".z64", ".v64", ".md", ".gen", ".sms", ".gg",
        ".pce", ".ngp", ".ws", ".rom", ".pbp", ".pkg"
    ];

    private readonly MainViewModel _vm;
    private readonly ThemeService _theme;
    private readonly SettingsService _settings = new();
    private string? _lastAuditPath;

    public MainWindow()
    {
        _theme = new ThemeService();
        _vm = new MainViewModel(_theme);
        DataContext = _vm;

        InitializeComponent();

        Loaded += OnLoaded;
        Closing += OnClosing;

        // Wire orchestration events
        _vm.RunRequested += OnRunRequested;
        _vm.RollbackRequested += OnRollbackRequested;

        // Drag-drop on root list
        listRoots.DragEnter += OnRootsDragEnter;
        listRoots.Drop += OnRootsDrop;

        // Browse buttons (code-behind — not bindable in lightweight MVVM)
        btnBrowseChdman.Click += (_, _) => BrowseToolPath(path => _vm.ToolChdman = path);
        btnBrowseDolphin.Click += (_, _) => BrowseToolPath(path => _vm.ToolDolphin = path);
        btnBrowse7z.Click += (_, _) => BrowseToolPath(path => _vm.Tool7z = path);
        btnBrowsePsxtract.Click += (_, _) => BrowseToolPath(path => _vm.ToolPsxtract = path);
        btnBrowseCiso.Click += (_, _) => BrowseToolPath(path => _vm.ToolCiso = path);
        btnBrowseDat.Click += (_, _) => BrowseFolderPath(path => _vm.DatRoot = path);
        btnBrowseTrash.Click += (_, _) => BrowseFolderPath(path => _vm.TrashRoot = path);
        btnBrowseAudit.Click += (_, _) => BrowseFolderPath(path => _vm.AuditRoot = path);
        btnBrowsePs3.Click += (_, _) => BrowseFolderPath(path => _vm.Ps3DupesRoot = path);
    }

    // ═══ LIFECYCLE ══════════════════════════════════════════════════════

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _settings.LoadInto(_vm);
        _vm.RefreshStatus();
    }

    private void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        _settings.SaveFrom(_vm);
    }

    // ═══ DRAG & DROP ════════════════════════════════════════════════════

    private void OnRootsDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Link
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRootsDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
        {
            if (Directory.Exists(path) && !_vm.Roots.Contains(path))
                _vm.Roots.Add(path);
        }
    }

    // ═══ BROWSE HELPERS ═════════════════════════════════════════════════

    private static void BrowseToolPath(Action<string> setter)
    {
        var path = DialogService.BrowseFile("Executable auswählen", "Executables (*.exe)|*.exe|Alle (*.*)|*.*");
        if (path is not null) setter(path);
    }

    private static void BrowseFolderPath(Action<string> setter)
    {
        var path = DialogService.BrowseFolder("Ordner auswählen");
        if (path is not null) setter(path);
    }

    // ═══ RUN ORCHESTRATION ══════════════════════════════════════════════

    private async void OnRunRequested(object? sender, EventArgs e)
    {
        var ct = _vm.CreateRunCancellation();
        try
        {
            _vm.AddLog("Initialisierung…", "INFO");

            // Build infrastructure
            var fs = new FileSystemAdapter();
            var audit = new AuditCsvStore();

            // Data directory resolution
            var dataDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "data");
            if (!Directory.Exists(dataDir))
                dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");

            // ToolRunner
            var toolHashesPath = Path.Combine(dataDir, "tool-hashes.json");
            var toolRunner = new ToolRunnerAdapter(File.Exists(toolHashesPath) ? toolHashesPath : null);

            // ConsoleDetector
            ConsoleDetector? consoleDetector = null;
            var discHeaderDetector = new DiscHeaderDetector();
            var consolesJsonPath = Path.Combine(dataDir, "consoles.json");
            if (File.Exists(consolesJsonPath))
            {
                var consolesJson = File.ReadAllText(consolesJsonPath);
                consoleDetector = ConsoleDetector.LoadFromJson(consolesJson, discHeaderDetector);
            }

            // DAT setup
            DatIndex? datIndex = null;
            FileHashService? hashService = null;
            if (_vm.UseDat && !string.IsNullOrWhiteSpace(_vm.DatRoot) && Directory.Exists(_vm.DatRoot))
            {
                var datRepo = new DatRepositoryAdapter();
                hashService = new FileHashService();
                // Build console→DAT mapping by scanning datRoot for .dat files
                var consoleMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                foreach (var datFile in Directory.GetFiles(_vm.DatRoot, "*.dat"))
                {
                    var key = Path.GetFileNameWithoutExtension(datFile);
                    consoleMap.TryAdd(key, datFile);
                }
                if (consoleMap.Count > 0)
                {
                    datIndex = datRepo.GetDatIndex(_vm.DatRoot, consoleMap, _vm.DatHashType);
                    Dispatcher.Invoke(() =>
                        _vm.AddLog($"DAT geladen: {datIndex.TotalEntries} Hashes für {datIndex.ConsoleCount} Konsolen", "INFO"));
                }
            }

            // FormatConverter (optional)
            FormatConverterAdapter? converter = null;
            if (_vm.ConvertEnabled)
                converter = new FormatConverterAdapter(toolRunner);

            // Audit path
            string? auditPath = null;
            if (!_vm.DryRun && _vm.Roots.Count > 0)
            {
                var auditDir = !string.IsNullOrWhiteSpace(_vm.AuditRoot)
                    ? _vm.AuditRoot
                    : Path.Combine(_vm.Roots[0], "..", "audit-logs");
                auditDir = Path.GetFullPath(auditDir);
                auditPath = Path.Combine(auditDir, $"audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
            }

            // Report path
            string? reportPath = null;
            if (_vm.Roots.Count > 0)
            {
                var reportDir = Path.Combine(_vm.Roots[0], "..", "reports");
                reportDir = Path.GetFullPath(reportDir);
                Directory.CreateDirectory(reportDir);
                reportPath = Path.Combine(reportDir, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
            }

            // Build RunOptions
            var runOptions = new RunOptions
            {
                Roots = _vm.Roots.ToList(),
                Mode = _vm.DryRun ? "DryRun" : "Move",
                PreferRegions = _vm.GetPreferredRegions(),
                Extensions = DefaultExtensions,
                RemoveJunk = true,
                AggressiveJunk = _vm.AggressiveJunk,
                SortConsole = _vm.SortConsole,
                EnableDat = _vm.UseDat,
                HashType = _vm.DatHashType,
                ConvertFormat = _vm.ConvertEnabled ? "auto" : null,
                TrashRoot = string.IsNullOrWhiteSpace(_vm.TrashRoot) ? null : _vm.TrashRoot,
                AuditPath = auditPath,
                ReportPath = reportPath
            };

            // Build and run orchestrator
            var orchestrator = new RunOrchestrator(
                fs, audit, consoleDetector, hashService, converter, datIndex,
                onProgress: msg => Dispatcher.Invoke(() =>
                {
                    _vm.ProgressText = msg;
                    _vm.AddLog(msg, "INFO");
                }));

            await Task.Run(() =>
            {
                var result = orchestrator.Execute(runOptions, ct);

                Dispatcher.Invoke(() =>
                {
                    _vm.Progress = 100;
                    _vm.DashWinners = result.WinnerCount.ToString();
                    _vm.DashDupes = result.LoserCount.ToString();
                    var junkCount = result.AllCandidates.Count(c => c.Category == "JUNK");
                    _vm.DashJunk = junkCount.ToString();
                    _vm.DashDuration = $"{result.DurationMs / 1000.0:F1}s";
                    var total = result.AllCandidates.Count;
                    _vm.HealthScore = total > 0
                        ? $"{100.0 * result.WinnerCount / total:F0}%"
                        : "–";

                    if (result.Status == "blocked")
                    {
                        _vm.AddLog($"Preflight blockiert: {result.Preflight?.Reason}", "ERROR");
                    }
                    else
                    {
                        _vm.AddLog($"Scan: {result.TotalFilesScanned} Dateien", "INFO");
                        _vm.AddLog($"Dedupe: Keep={result.WinnerCount}, Move={result.LoserCount}, Junk={junkCount}", "INFO");
                        if (result.MoveResult is { } mv)
                            _vm.AddLog($"Verschoben: {mv.MoveCount}, Fehler: {mv.FailCount}", mv.FailCount > 0 ? "WARN" : "INFO");
                        if (result.ConvertedCount > 0)
                            _vm.AddLog($"Konvertiert: {result.ConvertedCount}", "INFO");
                    }

                    // Generate HTML report
                    if (reportPath is not null && result.DedupeGroups.Count > 0)
                    {
                        try
                        {
                            var entries = result.DedupeGroups.SelectMany(g =>
                            {
                                var list = new List<ReportEntry>();
                                list.Add(new ReportEntry
                                {
                                    GameKey = g.Winner.GameKey, Action = "KEEP", Category = g.Winner.Category,
                                    Region = g.Winner.Region, FilePath = g.Winner.MainPath,
                                    FileName = Path.GetFileName(g.Winner.MainPath),
                                    Extension = g.Winner.Extension, SizeBytes = g.Winner.SizeBytes,
                                    RegionScore = g.Winner.RegionScore, FormatScore = g.Winner.FormatScore,
                                    VersionScore = g.Winner.VersionScore, DatMatch = g.Winner.DatMatch
                                });
                                foreach (var l in g.Losers)
                                    list.Add(new ReportEntry
                                    {
                                        GameKey = l.GameKey, Action = "MOVE", Category = l.Category,
                                        Region = l.Region, FilePath = l.MainPath,
                                        FileName = Path.GetFileName(l.MainPath),
                                        Extension = l.Extension, SizeBytes = l.SizeBytes,
                                        RegionScore = l.RegionScore, FormatScore = l.FormatScore,
                                        VersionScore = l.VersionScore, DatMatch = l.DatMatch
                                    });
                                return list;
                            }).ToList();

                            var summary = new ReportSummary
                            {
                                Mode = runOptions.Mode,
                                TotalFiles = result.TotalFilesScanned,
                                KeepCount = result.WinnerCount,
                                MoveCount = result.LoserCount,
                                JunkCount = result.AllCandidates.Count(c => c.Category == "JUNK"),
                                BiosCount = result.AllCandidates.Count(c => c.Category == "BIOS"),
                                GroupCount = result.GroupCount,
                                Duration = TimeSpan.FromMilliseconds(result.DurationMs)
                            };

                            ReportGenerator.WriteHtmlToFile(
                                reportPath, Path.GetDirectoryName(reportPath) ?? ".", summary, entries);
                            _vm.AddLog($"Report: {reportPath}", "INFO");
                        }
                        catch (Exception rex)
                        {
                            _vm.AddLog($"Report-Fehler: {rex.Message}", "WARN");
                        }
                    }

                    _lastAuditPath = auditPath;
                });
            }, ct);

            if (!ct.IsCancellationRequested)
            {
                _vm.AddLog("Lauf abgeschlossen.", "INFO");
                _vm.CompleteRun(true, reportPath);
            }
            else
            {
                _vm.AddLog("Lauf abgebrochen.", "WARN");
                _vm.CompleteRun(false);
            }
        }
        catch (OperationCanceledException)
        {
            _vm.AddLog("Lauf abgebrochen.", "WARN");
            _vm.CompleteRun(false);
        }
        catch (Exception ex)
        {
            _vm.AddLog($"Fehler: {ex.Message}", "ERROR");
            _vm.CompleteRun(false);
        }
    }

    private void OnRollbackRequested(object? sender, EventArgs e)
    {
        if (!DialogService.Confirm("Letzten Lauf rückgängig machen?", "Rollback bestätigen"))
            return;

        if (string.IsNullOrEmpty(_lastAuditPath) || !File.Exists(_lastAuditPath))
        {
            _vm.AddLog("Keine Audit-Datei gefunden — Rollback nicht möglich.", "WARN");
            return;
        }

        try
        {
            var audit = new AuditCsvStore();
            var roots = _vm.Roots.ToArray();
            var restored = audit.Rollback(_lastAuditPath, roots, roots, dryRun: false);
            _vm.AddLog($"Rollback: {restored.Count} Dateien wiederhergestellt.", "INFO");
            _vm.CanRollback = false;
            _vm.ShowMoveCompleteBanner = false;
        }
        catch (Exception ex)
        {
            _vm.AddLog($"Rollback-Fehler: {ex.Message}", "ERROR");
        }
    }

    // ═══ LOG AUTO-SCROLL ════════════════════════════════════════════════

    // Auto-scroll log ListBox to bottom when new items arrive.
    // Wired once via collection change in OnLoaded would be cleaner,
    // but for simplicity we can also use an attached behavior later.
    // For now, the ListBox virtualizes so scrolling is efficient.
}
