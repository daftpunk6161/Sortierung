using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Core.Deduplication;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Sorting;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Central pipeline orchestrator for ROM cleanup operations.
/// Port of Invoke-CliRunAdapter / RunHelpers.Execution.ps1.
/// Encapsulates: Preflight → Scan → Dedupe → JunkRemoval → Move → Sort → Convert → Report.
/// Used by both CLI and API entry points.
/// </summary>
public sealed class RunOrchestrator
{
    private readonly IFileSystem _fs;
    private readonly IAuditStore _audit;
    private readonly ConsoleDetector? _consoleDetector;
    private readonly FileHashService? _hashService;
    private readonly IFormatConverter? _converter;
    private readonly DatIndex? _datIndex;
    private readonly Action<string>? _onProgress;

    public RunOrchestrator(
        IFileSystem fs,
        IAuditStore audit,
        ConsoleDetector? consoleDetector = null,
        FileHashService? hashService = null,
        IFormatConverter? converter = null,
        DatIndex? datIndex = null,
        Action<string>? onProgress = null)
    {
        _fs = fs;
        _audit = audit;
        _consoleDetector = consoleDetector;
        _hashService = hashService;
        _converter = converter;
        _datIndex = datIndex;
        _onProgress = onProgress;
    }

    /// <summary>
    /// Validate all prerequisites before a run.
    /// Port of Invoke-RunPreflight from RunHelpers.Execution.ps1.
    /// Checks: roots exist, audit dir writable (F-06), tools available.
    /// </summary>
    public OperationResult Preflight(RunOptions options)
    {
        var warnings = new List<string>();

        if (options.Roots.Count == 0)
            return OperationResult.Blocked("No roots specified");

        foreach (var root in options.Roots)
        {
            if (!_fs.TestPath(root, "Container"))
                return OperationResult.Blocked($"Root does not exist: {root}");
        }

        // F-06: Audit directory write test
        if (!string.IsNullOrEmpty(options.AuditPath))
        {
            var auditDir = Path.GetDirectoryName(options.AuditPath);
            if (!string.IsNullOrEmpty(auditDir))
            {
                try
                {
                    _fs.EnsureDirectory(auditDir);
                    var testFile = Path.Combine(auditDir, $".write_test_{Guid.NewGuid():N}");
                    File.WriteAllText(testFile, "");
                    File.Delete(testFile);
                }
                catch (Exception ex)
                {
                    return OperationResult.Blocked($"Audit directory not writable: {ex.Message}");
                }
            }
        }

        if (options.EnableDat && _datIndex is null)
            warnings.Add("DAT enabled but no DatIndex loaded");

        if (options.Extensions.Count == 0)
            warnings.Add("No file extensions specified — scan will find nothing");

        var result = OperationResult.Ok("preflight-passed");
        result.Warnings.AddRange(warnings);
        result.Meta["RootCount"] = options.Roots.Count;
        result.Meta["ExtensionCount"] = options.Extensions.Count;
        return result;
    }

    /// <summary>
    /// Execute the full pipeline: Scan → Dedupe → JunkRemoval → Move → Sort → (optional Convert) → Report.
    /// </summary>
    public RunResult Execute(RunOptions options, CancellationToken cancellationToken = default)
    {
        var result = new RunResultBuilder();
        var sw = System.Diagnostics.Stopwatch.StartNew();

        // V2-M09: Structured phase metrics collection
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();

        try
        {

        // Phase 1: Preflight
        metrics.StartPhase("Preflight");
        _onProgress?.Invoke("[Preflight] Voraussetzungen prüfen…");
        var preflight = Preflight(options);
        result.Preflight = preflight;
        metrics.CompletePhase();
        if (preflight.ShouldReturn)
        {
            _onProgress?.Invoke($"[Preflight] Blockiert: {preflight.Reason}");
            result.Status = RunOutcome.Blocked.ToStatusString();
            result.ExitCode = 3;
            return result.Build();
        }
        _onProgress?.Invoke($"[Preflight] OK — {options.Roots.Count} Root(s), {options.Extensions.Count} Extension(s), Modus: {options.Mode}");
        if (preflight.Warnings.Count > 0)
            foreach (var w in preflight.Warnings)
                _onProgress?.Invoke($"[Preflight] Warnung: {w}");

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 2: Scan
        // V2-H01: Report scan progress (file count) for large collections
        metrics.StartPhase("Scan");
        _onProgress?.Invoke($"[Scan] Scanne {options.Roots.Count} Root-Ordner…");
        foreach (var root in options.Roots)
            _onProgress?.Invoke($"[Scan] Root: {root}");
        var scanSw = System.Diagnostics.Stopwatch.StartNew();
        var scanContext = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };
        var scanPhase = new ScanPipelinePhase();
        var scannedFiles = scanPhase.Execute(options, scanContext, cancellationToken);

        var enrichmentPhase = new EnrichmentPipelinePhase();
        var candidates = enrichmentPhase.Execute(
            new EnrichmentPhaseInput(scannedFiles, _consoleDetector, _hashService, _datIndex),
            scanContext,
            cancellationToken);

        var unknownReasonCounts = candidates
            .Where(c => c.Category == FileCategory.Unknown)
            .GroupBy(c => string.IsNullOrWhiteSpace(c.ClassificationReasonCode) ? "unknown" : c.ClassificationReasonCode,
                StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        result.UnknownCount = candidates.Count(c => c.Category == FileCategory.Unknown);
        result.UnknownReasonCounts = unknownReasonCounts;

        var processingCandidates = candidates;
        if (options.OnlyGames)
        {
            var keepUnknown = options.KeepUnknownWhenOnlyGames;
            processingCandidates = candidates
                .Where(c => c.Category == FileCategory.Game || (keepUnknown && c.Category == FileCategory.Unknown))
                .ToList();

            result.FilteredNonGameCount = candidates.Count - processingCandidates.Count;
            _onProgress?.Invoke($"[Filter] OnlyGames aktiv: {result.FilteredNonGameCount} Nicht-Spiel-Dateien ausgeschlossen (KeepUnknown={keepUnknown})");
        }

        scanSw.Stop();
        result.TotalFilesScanned = candidates.Count;
        _onProgress?.Invoke($"[Scan] Abgeschlossen: {candidates.Count} Dateien in {scanSw.ElapsedMilliseconds}ms");
        metrics.CompletePhase(candidates.Count);

        // V2-H01: Memory warning for very large scans
        if (candidates.Count > 100_000)
            _onProgress?.Invoke($"WARNING: {candidates.Count:N0} files scanned — high memory usage. Consider scanning fewer roots.");

        if (processingCandidates.Count == 0)
        {
            result.Status = RunOutcome.Ok.ToStatusString();
            result.ExitCode = 0;
            result.AllCandidates = candidates;
            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();
            if (!string.IsNullOrEmpty(options.ReportPath))
            {
                _onProgress?.Invoke("[Report] Generiere HTML-Report…");
                result.ReportPath = GenerateReport(result, options);
                if (!string.IsNullOrEmpty(result.ReportPath))
                    _onProgress?.Invoke($"[Report] Report erstellt: {result.ReportPath}");
            }
            return result.Build();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // ConvertOnly mode: skip Dedupe/Junk/Move/Sort — go straight to conversion
        if (options.ConvertOnly && _converter is not null)
        {
            ExecuteConvertOnlyPhase(processingCandidates, options, result, metrics, cancellationToken);
            result.AllCandidates = candidates;
            result.TotalFilesScanned = candidates.Count;

            sw.Stop();
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();
            result.ReportPath = GenerateReport(result, options);
            var convertOnlyHasErrors = result.ConvertErrorCount > 0;
            var convertOnlyOutcome = convertOnlyHasErrors ? RunOutcome.CompletedWithErrors : RunOutcome.Ok;
            result.Status = convertOnlyOutcome.ToStatusString();
            result.ExitCode = convertOnlyHasErrors ? 1 : 0;
            return result.Build();
        }

        // Phase 3: Deduplicate
        var (groups, gameGroups) = ExecuteDedupePhase(processingCandidates, candidates, options, result, metrics, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 3b: Remove junk (if enabled)
        var junkRemovedPaths = ExecuteJunkPhaseIfEnabled(groups, options, result, metrics, cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 4: Move (if Mode=Move)
        if (options.Mode == "Move")
        {
            metrics.StartPhase("Move");
            var totalLosers = gameGroups.Sum(g => g.Losers.Count);
            _onProgress?.Invoke($"[Move] Verschiebe {totalLosers} Duplikate in Trash…");
            var movePhase = new MovePipelinePhase();
            var moveContext = new PipelineContext
            {
                Options = options,
                FileSystem = _fs,
                AuditStore = _audit,
                Metrics = metrics,
                OnProgress = _onProgress
            };
            var moveResult = movePhase.Execute(new MovePhaseInput(gameGroups, options), moveContext, cancellationToken);
            _onProgress?.Invoke($"[Move] Abgeschlossen: {moveResult.MoveCount} verschoben, {moveResult.FailCount} Fehler");
            result.MoveResult = moveResult;
            metrics.CompletePhase(moveResult.MoveCount);
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 5: Console Sort (optional, Move mode only)
        if (options.SortConsole && options.Mode == "Move" && _consoleDetector is not null)
        {
            metrics.StartPhase("ConsoleSort");
            _onProgress?.Invoke("[Sort] Sortiere Dateien nach Konsole…");
            var sorter = new ConsoleSorter(_fs, _consoleDetector, _audit, options.AuditPath);
            result.ConsoleSortResult = sorter.Sort(
                options.Roots, options.Extensions, dryRun: false, cancellationToken);
            _onProgress?.Invoke($"[Sort] Konsolen-Sortierung abgeschlossen");
            metrics.CompletePhase();
        }

        cancellationToken.ThrowIfCancellationRequested();

        // Phase 6: Format Conversion (optional, Move mode only)
        // Issue #16: Use actual file paths — after Sort, files may have moved.
        if (options.ConvertFormat is not null && options.Mode == "Move" && _converter is not null)
            ExecuteWinnerConversionPhase(gameGroups, options, junkRemovedPaths, result, metrics, cancellationToken);

        sw.Stop();
        // Derive status based on actual errors
        var hasErrors = result.ConvertErrorCount > 0
                     || (result.MoveResult is { FailCount: > 0 })
                     || (result.JunkMoveResult is { FailCount: > 0 });
        var runOutcome = hasErrors ? RunOutcome.CompletedWithErrors : RunOutcome.Ok;
        result.Status = runOutcome.ToStatusString();
        result.ExitCode = hasErrors ? 1 : 0;
        result.DurationMs = sw.ElapsedMilliseconds;
        result.PhaseMetrics = metrics.GetMetrics();

        // FEAT-02: Generate report at end of pipeline
        if (!string.IsNullOrEmpty(options.ReportPath))
        {
            _onProgress?.Invoke("[Report] Generiere HTML-Report…");
            result.ReportPath = GenerateReport(result, options);
            if (!string.IsNullOrEmpty(result.ReportPath))
                _onProgress?.Invoke($"[Report] Report erstellt: {result.ReportPath}");
        }

        // FEAT-03: Write final audit sidecar with HMAC signature after all phases.
        _onProgress?.Invoke("[Audit] Schreibe Audit-Sidecar…");
        if (!string.IsNullOrEmpty(options.AuditPath) && File.Exists(options.AuditPath))
        {
            var auditLines = File.ReadAllLines(options.AuditPath);
            var rowCount = Math.Max(0, auditLines.Length - 1); // exclude header
            _audit.WriteMetadataSidecar(options.AuditPath, new Dictionary<string, object>
            {
                ["RowCount"] = rowCount,
                ["Mode"] = options.Mode,
                ["Status"] = "completed",
                ["TotalFilesScanned"] = result.TotalFilesScanned,
                ["GroupCount"] = result.GroupCount,
                ["WinnerCount"] = result.WinnerCount,
                ["LoserCount"] = result.LoserCount,
                ["MoveCount"] = result.MoveResult?.MoveCount ?? 0,
                ["FailCount"] = result.MoveResult?.FailCount ?? 0,
                ["SkipCount"] = result.MoveResult?.SkipCount ?? 0,
                ["JunkRemovedCount"] = result.JunkRemovedCount,
                ["ConvertedCount"] = result.ConvertedCount,
                ["ConvertErrorCount"] = result.ConvertErrorCount,
                ["ConsoleSortMoved"] = result.ConsoleSortResult?.Moved ?? 0,
                ["ConsoleSortFailed"] = result.ConsoleSortResult?.Failed ?? 0,
                ["DurationMs"] = sw.ElapsedMilliseconds
            });
        }

        _onProgress?.Invoke($"[Fertig] Pipeline abgeschlossen in {sw.ElapsedMilliseconds}ms — {result.TotalFilesScanned} Dateien, {result.GroupCount} Gruppen");
        return result.Build();
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            result.Status = RunOutcome.Cancelled.ToStatusString();
            result.ExitCode = 2;
            result.DurationMs = sw.ElapsedMilliseconds;
            result.PhaseMetrics = metrics.GetMetrics();

            // Issue #19: Write partial audit sidecar so rollback is possible after cancel
            if (!string.IsNullOrEmpty(options.AuditPath) && File.Exists(options.AuditPath))
            {
                var auditLines = File.ReadAllLines(options.AuditPath);
                var rowCount = Math.Max(0, auditLines.Length - 1);
                _audit.WriteMetadataSidecar(options.AuditPath, new Dictionary<string, object>
                {
                    ["RowCount"] = rowCount,
                    ["Mode"] = options.Mode,
                    ["Status"] = "partial",
                    ["CancelledAtUtc"] = DateTime.UtcNow.ToString("o"),
                    ["LastPhase"] = metrics.GetCurrentPhaseName() ?? "unknown",
                    ["PhaseProgressPct"] = result.PhaseMetrics?.Phases.LastOrDefault()?.PercentOfTotal ?? 0,
                    ["TotalFilesScanned"] = result.TotalFilesScanned,
                    ["GroupCount"] = result.GroupCount,
                    ["MoveCount"] = result.MoveResult?.MoveCount ?? 0,
                    ["FailCount"] = result.MoveResult?.FailCount ?? 0,
                    ["ConvertedCount"] = result.ConvertedCount,
                    ["ConvertErrorCount"] = result.ConvertErrorCount,
                    ["DurationMs"] = sw.ElapsedMilliseconds
                });
            }

            return result.Build();
        }
    }

    private (IReadOnlyList<DedupeResult> Groups, List<DedupeResult> GameGroups) ExecuteDedupePhase(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyList<RomCandidate> allCandidates,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        var phase = new DeduplicatePipelinePhase();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };

        var output = phase.Execute(candidates, context, cancellationToken);

        result.GroupCount = output.GameGroups.Count;
        result.WinnerCount = output.GameGroups.Count;
        result.LoserCount = output.LoserCount;
        result.AllCandidates = allCandidates;
        result.DedupeGroups = output.GameGroups;

        return (output.Groups, output.GameGroups);
    }

    private HashSet<string> ExecuteJunkPhaseIfEnabled(
        IReadOnlyList<DedupeResult> groups,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        var junkRemovedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!options.RemoveJunk || options.Mode != "Move")
            return junkRemovedPaths;

        var phase = new JunkRemovalPipelinePhase();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };
        var output = phase.Execute(new JunkRemovalPhaseInput(groups, options), context, cancellationToken);

        result.JunkRemovedCount = output.MoveResult.MoveCount;
        result.JunkMoveResult = output.MoveResult;
        return output.RemovedPaths;
    }

    private void ExecuteConvertOnlyPhase(
        IReadOnlyList<RomCandidate> candidates,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        var phase = new ConvertOnlyPipelinePhase();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };
        var output = phase.Execute(new ConvertOnlyPhaseInput(candidates, options, _converter!), context, cancellationToken);

        result.ConvertedCount = output.Converted;
        result.ConvertErrorCount = output.ConvertErrors;
        result.ConvertSkippedCount = output.ConvertSkipped;
    }

    private void ExecuteWinnerConversionPhase(
        IReadOnlyList<DedupeResult> gameGroups,
        RunOptions options,
        IReadOnlySet<string> junkRemovedPaths,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        var phase = new WinnerConversionPipelinePhase();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = _fs,
            AuditStore = _audit,
            Metrics = metrics,
            OnProgress = _onProgress
        };
        var output = phase.Execute(new WinnerConversionPhaseInput(gameGroups, options, junkRemovedPaths, _converter!), context, cancellationToken);

        result.ConvertedCount = output.Converted;
        result.ConvertErrorCount = output.ConvertErrors;
        result.ConvertSkippedCount = output.ConvertSkipped;
    }

    /// <summary>FEAT-02: Generate HTML and CSV reports from pipeline results.</summary>
    private string? GenerateReport(RunResultBuilder result, RunOptions options)
    {
        try
        {
            var actualPath = RunReportWriter.WriteReport(options.ReportPath!, result.Build(), options.Mode);
            _onProgress?.Invoke($"Report written: {actualPath}");
            return actualPath;
        }
        catch (Exception ex)
        {
            // Log full error details — the throttled _onProgress may swallow short-lived messages
            _onProgress?.Invoke($"ERROR: Report generation failed: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Report generation failed: {ex}");
            return null;
        }
    }
}

/// <summary>Options for a run execution.</summary>
public sealed class RunOptions
{
    /// <summary>
    /// Default file extensions for ROM scanning. Shared across CLI, API, and WPF entry points.
    /// </summary>
    public static readonly string[] DefaultExtensions =
    {
        ".zip", ".7z", ".chd", ".iso", ".bin", ".cue", ".gdi", ".ccd",
        ".rvz", ".gcz", ".wbfs", ".nsp", ".xci", ".nes", ".snes",
        ".sfc", ".smc", ".gb", ".gbc", ".gba", ".nds", ".3ds",
        ".n64", ".z64", ".v64", ".md", ".gen", ".sms", ".gg",
        ".pce", ".ngp", ".ws", ".rom", ".pbp", ".pkg"
    };

    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();
    public string Mode { get; init; } = "DryRun";
    public string[] PreferRegions { get; init; } = { "EU", "US", "WORLD", "JP" };
    public IReadOnlyList<string> Extensions { get; init; } = Array.Empty<string>();
    public bool RemoveJunk { get; init; } = true;
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; } = true;
    public bool AggressiveJunk { get; init; }
    public bool SortConsole { get; init; }
    public bool EnableDat { get; init; }
    public string? DatRoot { get; init; }
    public string HashType { get; init; } = "SHA1";
    public string? ConvertFormat { get; init; }
    public bool ConvertOnly { get; init; }
    public string? TrashRoot { get; init; }
    public string? AuditPath { get; init; }
    public string? ReportPath { get; init; }
    public string ConflictPolicy { get; init; } = "Rename";
    public HashSet<string> DiscBasedConsoles { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Full result of a pipeline execution.</summary>
public sealed class RunResult
{
    public string Status { get; init; } = "ok";
    public int ExitCode { get; init; }
    public OperationResult? Preflight { get; init; }
    public int TotalFilesScanned { get; init; }
    public int GroupCount { get; init; }
    public int WinnerCount { get; init; }
    public int LoserCount { get; init; }
    public MovePhaseResult? MoveResult { get; init; }
    public MovePhaseResult? JunkMoveResult { get; init; }
    public ConsoleSortResult? ConsoleSortResult { get; init; }
    public int JunkRemovedCount { get; init; }
    public int FilteredNonGameCount { get; init; }
    public int UnknownCount { get; init; }
    public IReadOnlyDictionary<string, int> UnknownReasonCounts { get; init; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public int ConvertedCount { get; init; }
    public int ConvertErrorCount { get; init; }
    public int ConvertSkippedCount { get; init; }
    public long DurationMs { get; init; }
    public string? ReportPath { get; init; }

    /// <summary>All scanned candidates (for report generation).</summary>
    public IReadOnlyList<RomCandidate> AllCandidates { get; init; } = Array.Empty<RomCandidate>();

    /// <summary>Dedupe group results (for DryRun JSON output and reports).</summary>
    public IReadOnlyList<DedupeResult> DedupeGroups { get; init; } = Array.Empty<DedupeResult>();

    /// <summary>V2-M09: Structured phase timing metrics.</summary>
    public PhaseMetricsResult? PhaseMetrics { get; init; }
}

/// <summary>
/// Mutable builder for assembling a RunResult across orchestrator phases.
/// </summary>
public sealed class RunResultBuilder
{
    public string Status { get; set; } = "ok";
    public int ExitCode { get; set; }
    public OperationResult? Preflight { get; set; }
    public int TotalFilesScanned { get; set; }
    public int GroupCount { get; set; }
    public int WinnerCount { get; set; }
    public int LoserCount { get; set; }
    public MovePhaseResult? MoveResult { get; set; }
    public MovePhaseResult? JunkMoveResult { get; set; }
    public ConsoleSortResult? ConsoleSortResult { get; set; }
    public int JunkRemovedCount { get; set; }
    public int FilteredNonGameCount { get; set; }
    public int UnknownCount { get; set; }
    public IReadOnlyDictionary<string, int> UnknownReasonCounts { get; set; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
    public int ConvertedCount { get; set; }
    public int ConvertErrorCount { get; set; }
    public int ConvertSkippedCount { get; set; }
    public long DurationMs { get; set; }
    public string? ReportPath { get; set; }
    public IReadOnlyList<RomCandidate> AllCandidates { get; set; } = Array.Empty<RomCandidate>();
    public IReadOnlyList<DedupeResult> DedupeGroups { get; set; } = Array.Empty<DedupeResult>();
    public PhaseMetricsResult? PhaseMetrics { get; set; }

    public RunResult Build() => new()
    {
        Status = Status,
        ExitCode = ExitCode,
        Preflight = Preflight,
        TotalFilesScanned = TotalFilesScanned,
        GroupCount = GroupCount,
        WinnerCount = WinnerCount,
        LoserCount = LoserCount,
        MoveResult = MoveResult,
        JunkMoveResult = JunkMoveResult,
        ConsoleSortResult = ConsoleSortResult,
        JunkRemovedCount = JunkRemovedCount,
        FilteredNonGameCount = FilteredNonGameCount,
        UnknownCount = UnknownCount,
        UnknownReasonCounts = UnknownReasonCounts,
        ConvertedCount = ConvertedCount,
        ConvertErrorCount = ConvertErrorCount,
        ConvertSkippedCount = ConvertSkippedCount,
        DurationMs = DurationMs,
        ReportPath = ReportPath,
        AllCandidates = AllCandidates,
        DedupeGroups = DedupeGroups,
        PhaseMetrics = PhaseMetrics
    };
}

/// <summary>Result of the move phase.</summary>
public sealed record MovePhaseResult(int MoveCount, int FailCount, long SavedBytes, int SkipCount = 0);
