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
using RomCleanup.Infrastructure.Paths;
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
        onProgress?.Invoke("[Init] Initialisiere Infrastruktur…");
        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs, onProgress, AuditSecurityPaths.GetDefaultSigningKeyPath());

        var dataDir = FeatureService.ResolveDataDirectory()
                      ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        onProgress?.Invoke($"[Init] Datenverzeichnis: {dataDir}");

        var toolHashesPath = Path.Combine(dataDir, "tool-hashes.json");
        var toolRunner = new ToolRunnerAdapter(File.Exists(toolHashesPath) ? toolHashesPath : null);

        ConsoleDetector? consoleDetector = null;
        var discHeaderDetector = new DiscHeaderDetector();
        var consolesJsonPath = Path.Combine(dataDir, "consoles.json");
        if (File.Exists(consolesJsonPath))
        {
            var consolesJson = File.ReadAllText(consolesJsonPath);
            consoleDetector = ConsoleDetector.LoadFromJson(consolesJson, discHeaderDetector);
            onProgress?.Invoke($"[Init] Konsolen-Datenbank geladen: {consoleDetector.AllConsoleKeys.Count} Konsolen");
        }
        else
        {
            onProgress?.Invoke("[Init] Warnung: consoles.json nicht gefunden — Konsolen-Erkennung deaktiviert");
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
        if (vm.ConvertEnabled || vm.ConvertOnly)
        {
            converter = new FormatConverterAdapter(toolRunner);
            onProgress?.Invoke("[Init] Formatkonvertierung aktiviert");
        }

        string? auditPath = null;
        if ((!vm.DryRun || vm.ConvertOnly) && vm.Roots.Count > 0)
        {
            var auditDir = !string.IsNullOrWhiteSpace(vm.AuditRoot)
                ? vm.AuditRoot
                : ArtifactPathResolver.GetArtifactDirectory(vm.Roots, "audit-logs");
            auditDir = Path.GetFullPath(auditDir);
            auditPath = Path.Combine(auditDir, $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss}.csv");
        }

        string? reportPath = null;
        if (vm.Roots.Count > 0)
        {
            var reportDir = ArtifactPathResolver.GetArtifactDirectory(vm.Roots, "reports");
            reportDir = Path.GetFullPath(reportDir);
            Directory.CreateDirectory(reportDir);
            reportPath = Path.Combine(reportDir, $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.html");
        }

        var selectedExts = vm.GetSelectedExtensions();
        var runOptions = new RunOptions
        {
            Roots = vm.Roots.ToList(),
            Mode = vm.DryRun ? "DryRun" : "Move",
            PreferRegions = vm.GetPreferredRegions(),
            Extensions = selectedExts.Length > 0 ? selectedExts : RunOptions.DefaultExtensions,
            RemoveJunk = vm.RemoveJunk,
            OnlyGames = vm.OnlyGames,
            KeepUnknownWhenOnlyGames = vm.KeepUnknownWhenOnlyGames,
            AggressiveJunk = vm.AggressiveJunk,
            SortConsole = vm.SortConsole,
            EnableDat = vm.UseDat,
            HashType = vm.DatHashType,
            ConvertFormat = (vm.ConvertEnabled || vm.ConvertOnly) ? "auto" : null,
            ConvertOnly = vm.ConvertOnly,
            TrashRoot = string.IsNullOrWhiteSpace(vm.TrashRoot) ? null : vm.TrashRoot,
            AuditPath = auditPath,
            ReportPath = reportPath,
            ConflictPolicy = vm.ConflictPolicy.ToString()
        };

        onProgress?.Invoke($"[Init] Konfiguration: Modus={runOptions.Mode}, {runOptions.Extensions.Count} Extension(s), {runOptions.Roots.Count} Root(s)");

        var orchestrator = new RunOrchestrator(
            fs, audit, consoleDetector, hashService, converter, datIndex, onProgress);

        return (orchestrator, runOptions, auditPath, reportPath);
    }

    /// <summary>
    /// Execute the pipeline.
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

        return new RunServiceResult
        {
            Result = result,
            AuditPath = auditPath,
            ReportPath = result.ReportPath
        };
    }

    /// <summary>
    /// Get a directory at the same level as <paramref name="rootPath"/>.
    /// Falls back to a subdirectory within root for drive roots (C:\).
    /// </summary>
    public string GetSiblingDirectory(string rootPath, string siblingName)
    {
        var fullRoot = ArtifactPathResolver.NormalizeRoot(rootPath);
        return ArtifactPathResolver.GetSiblingDirectory(fullRoot, siblingName);
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
