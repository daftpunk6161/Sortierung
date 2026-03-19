using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Core.Deduplication;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Deduplication;
using RomCleanup.Infrastructure.Linking;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Quarantine;
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
    private readonly IPhasePlanBuilder _phasePlanBuilder;

    public RunOrchestrator(
        IFileSystem fs,
        IAuditStore audit,
        ConsoleDetector? consoleDetector = null,
        FileHashService? hashService = null,
        IFormatConverter? converter = null,
        DatIndex? datIndex = null,
        Action<string>? onProgress = null,
        IPhasePlanBuilder? phasePlanBuilder = null)
    {
        _fs = fs;
        _audit = audit;
        _consoleDetector = consoleDetector;
        _hashService = hashService;
        _converter = converter;
        _datIndex = datIndex;
        _onProgress = onProgress;
        _phasePlanBuilder = phasePlanBuilder ?? new PhasePlanBuilder();
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
        var pipelineState = new PipelineState();

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
        IAsyncEnumerable<ScannedFileEntry> scannedFiles = new StreamingScanPipelinePhase(scanContext)
            .EnumerateFilesAsync(options.Roots, options.Extensions, cancellationToken);

        var candidates = MaterializeEnrichedCandidates(scannedFiles, scanContext, cancellationToken);

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

        pipelineState.SetScanOutput(candidates, processingCandidates);

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
                var reportStep = new ReportPhaseStep(() =>
                {
                    _onProgress?.Invoke("[Report] Generiere HTML-Report…");
                    return GenerateReport(result, options);
                });
                var reportOutcome = reportStep.Execute(pipelineState, cancellationToken);
                result.ReportPath = reportOutcome.TypedResult as string;
                if (!string.IsNullOrEmpty(result.ReportPath))
                    _onProgress?.Invoke($"[Report] Report erstellt: {result.ReportPath}");
            }
            return result.Build();
        }

        // Integrate deferred services in a non-destructive analysis pass.
        new DeferredAnalysisPhaseStep((state, ct) => ExecuteDeferredServiceAnalysis(state, options, ct))
            .Execute(pipelineState, cancellationToken);

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
            result.ReportPath = new ReportPhaseStep(() => GenerateReport(result, options))
                .Execute(pipelineState, cancellationToken).TypedResult as string;
            var convertOnlyHasErrors = result.ConvertErrorCount > 0;
            var convertOnlyOutcome = convertOnlyHasErrors ? RunOutcome.CompletedWithErrors : RunOutcome.Ok;
            result.Status = convertOnlyOutcome.ToStatusString();
            result.ExitCode = convertOnlyHasErrors ? 1 : 0;
            return result.Build();
        }

        var phasePlan = _phasePlanBuilder.BuildStandard(options, new StandardPhaseStepActions
        {
            Deduplicate = (state, ct) =>
            {
                var output = ExecuteDedupePhase(state.ProcessingCandidates!, state.AllCandidates!, options, result, metrics, ct);
                state.SetDedupeOutput(output.Groups, output.GameGroups);
                return PhaseStepResult.Ok(output.GameGroups.Count, output);
            },
            JunkRemoval = (state, ct) =>
            {
                var removed = ExecuteJunkPhaseIfEnabled(state.AllGroups ?? Array.Empty<DedupeResult>(), options, result, metrics, ct);
                state.SetJunkPaths(removed);
                return PhaseStepResult.Ok(removed.Count, removed);
            },
            Move = (state, ct) =>
            {
                metrics.StartPhase("Move");
                var groups = state.GameGroups ?? Array.Empty<DedupeResult>();
                var totalLosers = groups.Sum(g => g.Losers.Count);
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
                var moveResult = movePhase.Execute(new MovePhaseInput(groups, options), moveContext, ct);
                _onProgress?.Invoke($"[Move] Abgeschlossen: {moveResult.MoveCount} verschoben, {moveResult.FailCount} Fehler");
                result.MoveResult = moveResult;
                metrics.CompletePhase(moveResult.MoveCount);
                return PhaseStepResult.Ok(moveResult.MoveCount, moveResult);
            },
            ConsoleSort = (_, ct) =>
            {
                if (!options.SortConsole || options.Mode != "Move" || _consoleDetector is null)
                    return PhaseStepResult.Skipped();

                metrics.StartPhase("ConsoleSort");
                _onProgress?.Invoke("[Sort] Sortiere Dateien nach Konsole…");
                var sorter = new ConsoleSorter(_fs, _consoleDetector, _audit, options.AuditPath);
                result.ConsoleSortResult = sorter.Sort(
                    options.Roots, options.Extensions, dryRun: false, ct);
                _onProgress?.Invoke("[Sort] Konsolen-Sortierung abgeschlossen");
                metrics.CompletePhase();
                return PhaseStepResult.Ok(result.ConsoleSortResult?.Moved ?? 0, result.ConsoleSortResult);
            },
            WinnerConversion = (state, ct) =>
            {
                if (options.ConvertFormat is null || options.Mode != "Move" || _converter is null)
                    return PhaseStepResult.Skipped();

                ExecuteWinnerConversionPhase(
                    state.GameGroups ?? Array.Empty<DedupeResult>(),
                    options,
                    state.JunkRemovedPaths ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                    result,
                    metrics,
                    ct);
                return PhaseStepResult.Ok(result.ConvertedCount);
            }
        });

        foreach (var phase in phasePlan)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _onProgress?.Invoke($"[Plan] Phase: {phase.Name}");
            phase.Execute(pipelineState, cancellationToken);
        }

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
            var reportStep = new ReportPhaseStep(() =>
            {
                _onProgress?.Invoke("[Report] Generiere HTML-Report…");
                return GenerateReport(result, options);
            });
            result.ReportPath = reportStep.Execute(pipelineState, cancellationToken).TypedResult as string;
            if (!string.IsNullOrEmpty(result.ReportPath))
                _onProgress?.Invoke($"[Report] Report erstellt: {result.ReportPath}");
        }

        // FEAT-03: Write final audit sidecar with HMAC signature after all phases.
        new AuditSealPhaseStep(() => WriteCompletedAuditSidecar(options, result, sw.ElapsedMilliseconds))
            .Execute(pipelineState, cancellationToken);

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
            WritePartialAuditSidecar(options, result, metrics, sw.ElapsedMilliseconds);

            return result.Build();
        }
    }

    private void ExecuteDeferredServiceAnalysis(
        PipelineState state,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var candidates = state.ProcessingCandidates ?? Array.Empty<RomCandidate>();

        try
        {
            ExecuteCrossRootPreview(candidates, options);
        }
        catch (Exception ex)
        {
            _onProgress?.Invoke($"[CrossRoot] Analyse übersprungen: {ex.Message}");
        }

        try
        {
            ExecuteFolderDedupePreview(options, candidates, cancellationToken);
        }
        catch (Exception ex)
        {
            _onProgress?.Invoke($"[FolderDedupe] Analyse übersprungen: {ex.Message}");
        }

        try
        {
            ExecuteQuarantinePreview(candidates);
        }
        catch (Exception ex)
        {
            _onProgress?.Invoke($"[Quarantine] Analyse übersprungen: {ex.Message}");
        }

        try
        {
            ExecuteHardlinkSupportPreview(options);
        }
        catch (Exception ex)
        {
            _onProgress?.Invoke($"[Hardlink] Analyse übersprungen: {ex.Message}");
        }
    }

    private void WriteCompletedAuditSidecar(RunOptions options, RunResultBuilder result, long elapsedMs)
    {
        _onProgress?.Invoke("[Audit] Schreibe Audit-Sidecar…");
        if (string.IsNullOrEmpty(options.AuditPath) || !File.Exists(options.AuditPath))
            return;

        var auditLines = File.ReadAllLines(options.AuditPath);
        var rowCount = Math.Max(0, auditLines.Length - 1);
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
            ["DurationMs"] = elapsedMs
        });
    }

    private void WritePartialAuditSidecar(
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        long elapsedMs)
    {
        if (string.IsNullOrEmpty(options.AuditPath) || !File.Exists(options.AuditPath))
            return;

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
            ["DurationMs"] = elapsedMs
        });
    }

    private void ExecuteFolderDedupePreview(
        RunOptions options,
        IReadOnlyList<RomCandidate> candidates,
        CancellationToken cancellationToken)
    {
        if (options.Roots.Count == 0)
            return;

        var candidatesByRoot = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        foreach (var root in options.Roots)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var consoleKey = candidates
                .Where(c =>
                {
                    var fullPath = Path.GetFullPath(c.MainPath);
                    return fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
                })
                .Select(c => c.ConsoleKey)
                .FirstOrDefault(k => !string.IsNullOrWhiteSpace(k));

            candidatesByRoot[root] = consoleKey;
        }

        var dedupe = new FolderDeduplicator(_fs, msg => _onProgress?.Invoke($"[FolderDedupe] {msg}"));
        var preview = dedupe.AutoDeduplicate(
            options.Roots,
            mode: "DryRun",
            consoleKeyDetector: root => candidatesByRoot.TryGetValue(root, out var key) ? key : null,
            ct: cancellationToken);

        _onProgress?.Invoke(
            $"[FolderDedupe] Preview: {preview.Results.Count} Analyse-Ergebnis(se), PS3-Roots={preview.Ps3Roots.Count}, BaseName-Roots={preview.FolderRoots.Count}");
    }

    private void ExecuteCrossRootPreview(IReadOnlyList<RomCandidate> candidates, RunOptions options)
    {
        if (_hashService is null || options.Roots.Count < 2)
            return;

        var sample = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c.MainPath) && File.Exists(c.MainPath))
            .Take(400)
            .Select(c =>
            {
                var root = PipelinePhaseHelpers.FindRootForPath(c.MainPath, options.Roots) ?? string.Empty;
                var hash = _hashService.GetHash(c.MainPath, options.HashType);
                return new CrossRootFile
                {
                    Path = c.MainPath,
                    Root = root,
                    Hash = hash ?? string.Empty,
                    Extension = c.Extension,
                    SizeBytes = c.SizeBytes
                };
            })
            .Where(f => !string.IsNullOrWhiteSpace(f.Root) && !string.IsNullOrWhiteSpace(f.Hash))
            .ToList();

        if (sample.Count == 0)
            return;

        var groups = CrossRootDeduplicator.FindDuplicates(sample);
        _onProgress?.Invoke($"[CrossRoot] Preview: {groups.Count} root-übergreifende Hash-Gruppen (Sample={sample.Count})");
    }

    private void ExecuteQuarantinePreview(IReadOnlyList<RomCandidate> candidates)
    {
        if (candidates.Count == 0)
            return;

        var service = new QuarantineService(_fs);
        var sample = candidates.Take(2000);
        var quarantineCandidates = 0;

        foreach (var candidate in sample)
        {
            var result = service.TestCandidate(new QuarantineItem
            {
                FilePath = candidate.MainPath,
                Console = string.IsNullOrWhiteSpace(candidate.ConsoleKey) ? "Unknown" : candidate.ConsoleKey,
                Format = candidate.Extension,
                DatStatus = candidate.DatMatch ? "Match" : "NoMatch",
                Category = candidate.Category.ToString().ToUpperInvariant(),
                HeaderStatus = "Ok"
            });

            if (result.IsCandidate)
                quarantineCandidates++;
        }

        _onProgress?.Invoke(
            $"[Quarantine] Preview: {quarantineCandidates} verdächtige Datei(en) im Sample von {sample.Count()} Kandidaten");
    }

    private void ExecuteHardlinkSupportPreview(RunOptions options)
    {
        if (options.Roots.Count == 0)
            return;

        var supportedRoots = options.Roots.Count(HardlinkService.IsHardlinkSupported);
        _onProgress?.Invoke($"[Hardlink] NTFS-Hardlink support: {supportedRoots}/{options.Roots.Count} Root(s)");
    }

    private List<RomCandidate> MaterializeEnrichedCandidates(
        IAsyncEnumerable<ScannedFileEntry> scannedFiles,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var enrichmentPhase = new EnrichmentPipelinePhase();
        var enrichedStream = enrichmentPhase.ExecuteStreamingAsync(
            new EnrichmentPhaseStreamingInput(scannedFiles, _consoleDetector, _hashService, _datIndex),
            context,
            cancellationToken);

        var candidates = new List<RomCandidate>();
        var enumerator = enrichedStream.GetAsyncEnumerator(cancellationToken);

        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
            {
                cancellationToken.ThrowIfCancellationRequested();
                candidates.Add(enumerator.Current);
            }
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        return candidates;
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
            _onProgress?.Invoke($"ERROR: Report generation failed at target path: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Report generation failed (primary): {ex}");

            // Fallback to user profile directory if primary report target is not writable.
            try
            {
                var fallbackDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "RomCleanupRegionDedupe",
                    "reports");
                Directory.CreateDirectory(fallbackDir);

                var fallbackFileName = Path.GetFileName(options.ReportPath!) ?? $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.html";
                var fallbackPath = Path.Combine(fallbackDir, fallbackFileName);

                var actualPath = RunReportWriter.WriteReport(fallbackPath, result.Build(), options.Mode);
                _onProgress?.Invoke($"Report written (fallback): {actualPath}");
                return actualPath;
            }
            catch (Exception fallbackEx)
            {
                _onProgress?.Invoke($"ERROR: Report fallback failed: {fallbackEx.GetType().Name}: {fallbackEx.Message}");
                System.Diagnostics.Debug.WriteLine($"Report generation failed (fallback): {fallbackEx}");
                return null;
            }
        }
    }
}

/// <summary>Options for a run execution.</summary>
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

