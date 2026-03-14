using System.IO;
using System.Text.Json;
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
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Extracted from MainWindow.xaml.cs RunCoreAsync — builds infrastructure,
/// executes the RunOrchestrator pipeline, and generates the HTML report.
/// All methods run on a background thread (no Dispatcher calls).
/// RF-003 from gui-ux-deep-audit.md.
/// </summary>
public sealed class RunService : IRunService
{
    /// <summary>Result of a single pipeline run.</summary>
    public sealed class RunServiceResult
    {
        public required RunResult Result { get; init; }
        public string? AuditPath { get; init; }
        public string? ReportPath { get; init; }
    }

    /// <summary>
    /// Build infrastructure and RunOptions from current ViewModel state.
    /// Must be called on a background thread — performs file I/O.
    /// </summary>
    public (RunOrchestrator Orchestrator, RunOptions Options, string? AuditPath, string? ReportPath)
        BuildOrchestrator(MainViewModel vm, Action<string>? onProgress = null)
    {
        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore();

        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");

        var toolHashesPath = Path.Combine(dataDir, "tool-hashes.json");
        var toolRunner = new ToolRunnerAdapter(File.Exists(toolHashesPath) ? toolHashesPath : null);

        ConsoleDetector? consoleDetector = null;
        var discHeaderDetector = new DiscHeaderDetector();
        var consolesJsonPath = Path.Combine(dataDir, "consoles.json");
        if (File.Exists(consolesJsonPath))
        {
            var consolesJson = File.ReadAllText(consolesJsonPath);
            consoleDetector = ConsoleDetector.LoadFromJson(consolesJson, discHeaderDetector);
        }

        DatIndex? datIndex = null;
        FileHashService? hashService = null;
        if (vm.UseDat && !string.IsNullOrWhiteSpace(vm.DatRoot) && Directory.Exists(vm.DatRoot))
        {
            var datRepo = new DatRepositoryAdapter();
            hashService = new FileHashService();
            var consoleMap = BuildConsoleMap(dataDir, vm.DatRoot);
            onProgress?.Invoke($"DAT: {consoleMap.Count} Konsolen-Zuordnungen in {vm.DatRoot}");
            if (consoleMap.Count > 0)
            {
                datIndex = datRepo.GetDatIndex(vm.DatRoot, consoleMap, vm.DatHashType);
                onProgress?.Invoke($"DAT: {datIndex.TotalEntries} Hashes für {datIndex.ConsoleCount} Konsolen geladen");
            }
            else
            {
                onProgress?.Invoke("DAT: Keine DAT-Dateien gefunden – DAT-Verifizierung übersprungen");
            }
        }

        FormatConverterAdapter? converter = null;
        if (vm.ConvertEnabled)
            converter = new FormatConverterAdapter(toolRunner);

        string? auditPath = null;
        if (!vm.DryRun && vm.Roots.Count > 0)
        {
            var auditDir = !string.IsNullOrWhiteSpace(vm.AuditRoot)
                ? vm.AuditRoot
                : GetSiblingDirectory(vm.Roots[0], "audit-logs");
            auditDir = Path.GetFullPath(auditDir);
            auditPath = Path.Combine(auditDir, $"audit-{DateTime.Now:yyyyMMdd-HHmmss}.csv");
        }

        string? reportPath = null;
        if (vm.Roots.Count > 0)
        {
            var reportDir = GetSiblingDirectory(vm.Roots[0], "reports");
            reportDir = Path.GetFullPath(reportDir);
            Directory.CreateDirectory(reportDir);
            reportPath = Path.Combine(reportDir, $"report-{DateTime.Now:yyyyMMdd-HHmmss}.html");
        }

        var selectedExts = vm.GetSelectedExtensions();
        var runOptions = new RunOptions
        {
            Roots = vm.Roots.ToList(),
            Mode = vm.DryRun ? "DryRun" : "Move",
            PreferRegions = vm.GetPreferredRegions(),
            Extensions = selectedExts.Length > 0 ? selectedExts : RunOptions.DefaultExtensions,
            RemoveJunk = true,
            AggressiveJunk = vm.AggressiveJunk,
            SortConsole = vm.SortConsole,
            EnableDat = vm.UseDat,
            HashType = vm.DatHashType,
            ConvertFormat = vm.ConvertEnabled ? "auto" : null,
            TrashRoot = string.IsNullOrWhiteSpace(vm.TrashRoot) ? null : vm.TrashRoot,
            AuditPath = auditPath,
            ReportPath = reportPath,
            ConflictPolicy = vm.ConflictPolicy.ToString()
        };

        var orchestrator = new RunOrchestrator(
            fs, audit, consoleDetector, hashService, converter, datIndex, onProgress);

        return (orchestrator, runOptions, auditPath, reportPath);
    }

    /// <summary>
    /// Execute the pipeline and generate the HTML report.
    /// Must be called on a background thread.
    /// </summary>
    public RunServiceResult ExecuteRun(
        RunOrchestrator orchestrator,
        RunOptions options,
        string? auditPath,
        string? reportPath,
        CancellationToken ct)
    {
        var result = orchestrator.Execute(options, ct);

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
                        VersionScore = (int)g.Winner.VersionScore, DatMatch = g.Winner.DatMatch
                    });
                    foreach (var l in g.Losers)
                        list.Add(new ReportEntry
                        {
                            GameKey = l.GameKey, Action = "MOVE", Category = l.Category,
                            Region = l.Region, FilePath = l.MainPath,
                            FileName = Path.GetFileName(l.MainPath),
                            Extension = l.Extension, SizeBytes = l.SizeBytes,
                            RegionScore = l.RegionScore, FormatScore = l.FormatScore,
                            VersionScore = (int)l.VersionScore, DatMatch = l.DatMatch
                        });
                    return list;
                }).ToList();

                var summary = new ReportSummary
                {
                    Mode = options.Mode,
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
            }
            catch
            {
                // Report generation failure is non-fatal; caller handles logging
            }
        }

        return new RunServiceResult
        {
            Result = result,
            AuditPath = auditPath,
            ReportPath = reportPath
        };
    }

    /// <summary>
    /// Get a directory at the same level as <paramref name="rootPath"/>.
    /// Falls back to a subdirectory within root for drive roots (C:\).
    /// </summary>
    public string GetSiblingDirectory(string rootPath, string siblingName)
    {
        var fullRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(fullRoot);
        if (string.IsNullOrEmpty(parent))
            return Path.Combine(fullRoot, siblingName);
        return Path.Combine(parent, siblingName);
    }

    /// <summary>
    /// Build console-key → DAT-file mapping using dat-catalog.json and filesystem scan.
    /// Matches CLI BuildConsoleMap logic: catalog-based mapping first, then fallback scan.
    /// </summary>
    private Dictionary<string, string> BuildConsoleMap(string dataDir, string datRoot)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        if (File.Exists(catalogPath))
        {
            try
            {
                var json = File.ReadAllText(catalogPath);
                var entries = JsonSerializer.Deserialize<List<DatCatalogEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (entries != null)
                {
                    foreach (var entry in entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.ConsoleKey))
                            continue;

                        var candidates = new[]
                        {
                            Path.Combine(datRoot, entry.Id + ".dat"),
                            Path.Combine(datRoot, entry.Id + ".xml"),
                            Path.Combine(datRoot, entry.System + ".dat"),
                            Path.Combine(datRoot, entry.System + ".xml"),
                            Path.Combine(datRoot, entry.ConsoleKey + ".dat"),
                            Path.Combine(datRoot, entry.ConsoleKey + ".xml")
                        };

                        foreach (var candidate in candidates)
                        {
                            if (File.Exists(candidate))
                            {
                                map[entry.ConsoleKey] = candidate;
                                break;
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed catalog — fall through to directory scan
            }
        }

        // Fallback: scan datRoot for any .dat/.xml files not yet mapped
        if (Directory.Exists(datRoot))
        {
            foreach (var datFile in Directory.GetFiles(datRoot, "*.dat", SearchOption.AllDirectories)
                         .Concat(Directory.GetFiles(datRoot, "*.xml", SearchOption.AllDirectories)))
            {
                var stem = Path.GetFileNameWithoutExtension(datFile).ToUpperInvariant();
                if (!map.ContainsKey(stem))
                    map[stem] = datFile;
            }
        }

        return map;
    }

    private sealed class DatCatalogEntry
    {
        public string Group { get; set; } = "";
        public string System { get; set; } = "";
        public string Id { get; set; } = "";
        public string ConsoleKey { get; set; } = "";
    }
}
