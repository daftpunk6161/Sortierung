using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.Deduplication;
using Romulus.Infrastructure.Deduplication;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Linking;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Quarantine;
using Romulus.Infrastructure.Reporting;

namespace Romulus.Infrastructure.Orchestration;

public sealed partial class RunOrchestrator
{
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _onProgress?.Invoke($"[CrossRoot] Analyse übersprungen: {ex.Message}");
        }

        try
        {
            ExecuteFolderDedupePreview(options, candidates, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _onProgress?.Invoke($"[FolderDedupe] Analyse übersprungen: {ex.Message}");
        }

        try
        {
            ExecuteQuarantinePreview(candidates);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _onProgress?.Invoke($"[Quarantine] Analyse übersprungen: {ex.Message}");
        }

        try
        {
            ExecuteHardlinkSupportPreview(options);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
        {
            _onProgress?.Invoke($"[Hardlink] Analyse übersprungen: {ex.Message}");
        }
    }

    private RunResult FinalizeCompletedRun(
        RunOptions options,
        RunResultBuilder result,
        PipelineState pipelineState,
        PhaseMetricsCollector metrics,
        System.Diagnostics.Stopwatch sw,
        CancellationToken cancellationToken)
    {
        sw.Stop();
        result.DurationMs = sw.ElapsedMilliseconds;
        result.PhaseMetrics = metrics.GetMetrics();

        var runOutcome = ResolveRunOutcome(result);
        if (!TryGenerateRequestedReport(result, options, pipelineState, cancellationToken) && runOutcome == RunOutcome.Ok)
            runOutcome = RunOutcome.CompletedWithErrors;

        result.Status = runOutcome.ToStatusString();
        result.ExitCode = runOutcome.ToExitCode();

        try
        {
            new AuditSealPhaseStep(() => WriteCompletedAuditSidecar(options, result, sw.ElapsedMilliseconds, runOutcome))
                .Execute(pipelineState, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _onProgress?.Invoke($"[Audit] Sidecar write failed: {ex.GetType().Name}: {ex.Message}");
            if (runOutcome == RunOutcome.Ok)
            {
                result.Status = RunOutcome.CompletedWithErrors.ToStatusString();
                result.ExitCode = RunOutcome.CompletedWithErrors.ToExitCode();
            }
        }

        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Done.Pipeline",
            "[Fertig] Pipeline abgeschlossen in {0}ms — {1} Dateien, {2} Gruppen",
            sw.ElapsedMilliseconds,
            result.TotalFilesScanned,
            result.GroupCount));
        return result.Build();
    }

    private bool TryGenerateRequestedReport(
        RunResultBuilder result,
        RunOptions options,
        PipelineState pipelineState,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(options.ReportPath))
            return true;

        var reportStep = new ReportPhaseStep(() =>
        {
            _onProgress?.Invoke(RunProgressLocalization.Format(
                "Report.Generate",
                "[Report] Generiere HTML-Report…"));
            return GenerateReport(result, options);
        });

        result.ReportPath = reportStep.Execute(pipelineState, cancellationToken).TypedResult as string;
        if (!string.IsNullOrEmpty(result.ReportPath))
        {
            _onProgress?.Invoke(RunProgressLocalization.Format(
                "Report.Created",
                "[Report] Report erstellt: {0}",
                result.ReportPath));
            return true;
        }

        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Report.Failed",
            "[Report] Angeforderter Report konnte weder am Zielpfad noch im Fallback geschrieben werden."));
        return false;
    }

    internal static RunOutcome ResolveRunOutcome(RunResultBuilder result)
    {
        var hasErrors = result.ConvertErrorCount > 0
                        || result.ConvertVerifyFailedCount > 0
                        || (result.MoveResult is { FailCount: > 0 })
                        || (result.JunkMoveResult is { FailCount: > 0 })
                        || result.DatRenameFailedCount > 0
                        || (result.ConsoleSortResult is { Failed: > 0 });

        return hasErrors ? RunOutcome.CompletedWithErrors : RunOutcome.Ok;
    }

    private void WriteCompletedAuditSidecar(RunOptions options, RunResultBuilder result, long elapsedMs, RunOutcome? outcome = null)
    {
        _onProgress?.Invoke(RunProgressLocalization.Format(
            "Audit.WriteSidecar",
            "[Audit] Schreibe Audit-Sidecar…"));
        if (string.IsNullOrEmpty(options.AuditPath) || !File.Exists(options.AuditPath))
            return;

        var auditLines = File.ReadAllLines(options.AuditPath);
        var rowCount = Math.Max(0, auditLines.Length - 1);
        _audit.WriteMetadataSidecar(options.AuditPath, Romulus.Infrastructure.Audit.AuditRollbackRootMetadata.WithAllowedRoots(options, new Dictionary<string, object>
        {
            ["RowCount"] = rowCount,
            ["Mode"] = options.Mode,
            // TASK-145: Reflect actual RunOutcome instead of always "completed"
            ["Status"] = outcome?.ToStatusString() ?? "completed",
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
        }));
    }

    private void WritePartialAuditSidecar(
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        long elapsedMs)
    {
        if (string.IsNullOrEmpty(options.AuditPath))
            return;

        // Write sidecar even if CSV doesn't exist yet (e.g. cancel during scan before any moves)
        var rowCount = 0;
        if (File.Exists(options.AuditPath))
        {
            var auditLines = File.ReadAllLines(options.AuditPath);
            rowCount = Math.Max(0, auditLines.Length - 1);
        }
        else
        {
            // Create empty audit CSV with header so WriteMetadataSidecar can hash it
            var auditDir = Path.GetDirectoryName(options.AuditPath);
            if (!string.IsNullOrEmpty(auditDir))
                Directory.CreateDirectory(auditDir);
            var tempAuditPath = options.AuditPath + ".tmp";
            File.WriteAllText(tempAuditPath,
                "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n",
                System.Text.Encoding.UTF8);
            File.Move(tempAuditPath, options.AuditPath, overwrite: true);
        }
        _audit.WriteMetadataSidecar(options.AuditPath, Romulus.Infrastructure.Audit.AuditRollbackRootMetadata.WithAllowedRoots(options, new Dictionary<string, object>
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
        }));
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
            .OrderBy(c => c.MainPath, StringComparer.Ordinal)
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
                    Region = c.Region,
                    Extension = c.Extension,
                    SizeBytes = c.SizeBytes,
                    RegionScore = c.RegionScore,
                    FormatScore = c.FormatScore,
                    VersionScore = c.VersionScore,
                    HeaderScore = c.HeaderScore,
                    CompletenessScore = c.CompletenessScore,
                    SizeTieBreakScore = c.SizeTieBreakScore,
                    DatMatch = c.DatMatch,
                    Category = c.Category
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

    private async Task<List<RomCandidate>> MaterializeEnrichedCandidatesAsync(
        IAsyncEnumerable<ScannedFileEntry> scannedFiles,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        if (_collectionIndex is null || string.IsNullOrWhiteSpace(_enrichmentFingerprint))
            return await MaterializeEnrichedCandidatesWithoutIndexAsync(scannedFiles, context, cancellationToken);

        const int deltaBatchSize = 64;
        var candidates = new List<RomCandidate>();
        var scannedFilesByPath = new Dictionary<string, ScannedFileEntry>(StringComparer.OrdinalIgnoreCase);
        var changedBatch = new List<ScannedFileEntry>(deltaBatchSize);
        var currentHashType = CollectionIndexCandidateMapper.NormalizeHashType(context.Options.HashType);
        var scanCompleted = true;
        var indexLookupsEnabled = true;

        try
        {
            await foreach (var scannedFile in scannedFiles.WithCancellation(cancellationToken))
            {
                scannedFilesByPath[scannedFile.Path] = scannedFile;

                CollectionIndexEntry? persistedEntry = null;
                if (indexLookupsEnabled)
                {
                    var lookup = await TryGetReusableCollectionIndexEntryAsync(scannedFile, currentHashType, cancellationToken);
                    persistedEntry = lookup.Entry;
                    indexLookupsEnabled = !lookup.DisableLookups;
                }

                if (persistedEntry is not null)
                {
                    FlushChangedBatch(changedBatch, candidates, context, cancellationToken);
                    candidates.Add(CollectionIndexCandidateMapper.ToCandidate(persistedEntry));
                    continue;
                }

                changedBatch.Add(scannedFile);
                if (changedBatch.Count >= deltaBatchSize)
                    FlushChangedBatch(changedBatch, candidates, context, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            scanCompleted = false;
        }

        if (changedBatch.Count > 0)
        {
            var flushToken = scanCompleted ? cancellationToken : CancellationToken.None;
            FlushChangedBatch(changedBatch, candidates, context, flushToken);
        }

        if (scanCompleted && !cancellationToken.IsCancellationRequested)
            await PersistCandidatesToCollectionIndexAsync(candidates, scannedFilesByPath, context.Options, currentHashType, cancellationToken);

        return candidates;
    }

    private async Task<List<RomCandidate>> MaterializeEnrichedCandidatesWithoutIndexAsync(
        IAsyncEnumerable<ScannedFileEntry> scannedFiles,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var enrichmentPhase = new EnrichmentPipelinePhase();
        var enrichedStream = enrichmentPhase.ExecuteStreamingAsync(
            new EnrichmentPhaseStreamingInput(scannedFiles, _consoleDetector, _hashService, _archiveHashService, _datIndex, _headerlessHasher, _knownBiosHashes),
            context,
            cancellationToken);

        var candidates = new List<RomCandidate>();
        try
        {
            await foreach (var candidate in enrichedStream.WithCancellation(cancellationToken))
                candidates.Add(candidate);
        }
        catch (OperationCanceledException)
        {
            // Scan cancelled mid-way; return whatever was collected.
            // The caller checks cancellationToken.IsCancellationRequested to detect early exit.
        }

        return candidates;
    }

    private (CollectionIndexEntry? Entry, bool DisableLookups) TryGetReusableCollectionIndexEntry(
        ScannedFileEntry scannedFile,
        string hashType,
        CancellationToken cancellationToken)
    {
        if (_collectionIndex is null || string.IsNullOrWhiteSpace(_enrichmentFingerprint))
            return (null, false);

        try
        {
            var entry = _collectionIndex.TryGetByPathAsync(scannedFile.Path, cancellationToken)
                .GetAwaiter()
                .GetResult();
            if (entry is null)
                return (null, false);

            return CollectionIndexCandidateMapper.CanReuseCandidate(entry, scannedFile, hashType, _enrichmentFingerprint)
                ? (entry, false)
                : (null, false);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _onProgress?.Invoke($"[CollectionIndex] Delta lookups disabled for this run: {ex.Message}");
            return (null, true);
        }
    }

    private Task<(CollectionIndexEntry? Entry, bool DisableLookups)> TryGetReusableCollectionIndexEntryAsync(
        ScannedFileEntry scannedFile,
        string hashType,
        CancellationToken cancellationToken)
        => Task.FromResult(TryGetReusableCollectionIndexEntry(scannedFile, hashType, cancellationToken));

    private void FlushChangedBatch(
        List<ScannedFileEntry> changedBatch,
        List<RomCandidate> candidates,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        if (changedBatch.Count == 0)
            return;

        candidates.AddRange(EnrichBatch(changedBatch, context, cancellationToken));
        changedBatch.Clear();
    }

    private IReadOnlyList<RomCandidate> EnrichBatch(
        IReadOnlyList<ScannedFileEntry> files,
        PipelineContext context,
        CancellationToken cancellationToken)
    {
        var enrichmentPhase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(files, _consoleDetector, _hashService, _archiveHashService, _datIndex, _headerlessHasher, _knownBiosHashes, _familyDatStrategyResolver, _familyPipelineSelector);

        try
        {
            return enrichmentPhase.Execute(input, context, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return enrichmentPhase.Execute(input, context, CancellationToken.None);
        }
    }

    private async Task PersistCandidatesToCollectionIndexAsync(
        IReadOnlyList<RomCandidate> candidates,
        IReadOnlyDictionary<string, ScannedFileEntry> scannedFilesByPath,
        RunOptions options,
        string hashType,
        CancellationToken cancellationToken)
    {
        if (_collectionIndex is null || string.IsNullOrWhiteSpace(_enrichmentFingerprint))
            return;

        try
        {
            var lastScannedUtc = DateTime.UtcNow;
            var entries = new List<CollectionIndexEntry>(candidates.Count);
            foreach (var candidate in candidates)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!scannedFilesByPath.TryGetValue(candidate.MainPath, out var scannedFile))
                    continue;

                entries.Add(CollectionIndexCandidateMapper.ToEntry(
                    candidate,
                    scannedFile,
                    hashType,
                    _enrichmentFingerprint,
                    lastScannedUtc));
            }

            if (entries.Count > 0)
                await _collectionIndex.UpsertEntriesAsync(entries, cancellationToken);

            await RemoveStaleCollectionIndexEntriesAsync(scannedFilesByPath, options, cancellationToken);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            _onProgress?.Invoke($"[CollectionIndex] Candidate persist skipped: {ex.Message}");
        }
    }

    private async Task RemoveStaleCollectionIndexEntriesAsync(
        IReadOnlyDictionary<string, ScannedFileEntry> scannedFilesByPath,
        RunOptions options,
        CancellationToken cancellationToken)
    {
        if (_collectionIndex is null || options.Roots.Count == 0 || options.Extensions.Count == 0)
            return;

        var scopedEntries = await _collectionIndex.ListEntriesInScopeAsync(options.Roots, options.Extensions, cancellationToken);
        if (scopedEntries.Count == 0)
            return;

        var livePaths = scannedFilesByPath.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var stalePaths = scopedEntries
            .Where(entry => !livePaths.Contains(entry.Path))
            .Select(entry => entry.Path)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (stalePaths.Length == 0)
            return;

        await _collectionIndex.RemovePathsAsync(stalePaths, cancellationToken);
        _onProgress?.Invoke($"[CollectionIndex] {stalePaths.Length} veraltete Eintraege entfernt");
    }

    private DedupePhaseResult ExecuteDedupePhase(
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

        return new DedupePhaseResult(output.Groups, output.GameGroups, output.LoserCount);
    }

    private JunkPhaseResult ExecuteJunkPhaseIfEnabled(
        IReadOnlyList<DedupeGroup> groups,
        RunOptions options,
        RunResultBuilder result,
        PhaseMetricsCollector metrics,
        CancellationToken cancellationToken)
    {
        var junkRemovedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!options.RemoveJunk)
            return new JunkPhaseResult(new MovePhaseResult(0, 0, 0, 0), junkRemovedPaths);

        var mode = options.Mode?.Trim();
        var isMoveMode = string.Equals(mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase);
        var isDryRunMode = string.Equals(mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase);
        if (!isMoveMode && !isDryRunMode)
            return new JunkPhaseResult(new MovePhaseResult(0, 0, 0, 0), junkRemovedPaths);

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
        return new JunkPhaseResult(output.MoveResult, output.RemovedPaths);
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
        result.ConvertBlockedCount = output.ConvertBlocked;
        ApplyConversionReport(output.ConversionResults, result);
    }

    private void ExecuteWinnerConversionPhase(
        IReadOnlyList<DedupeGroup> gameGroups,
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
        result.ConvertBlockedCount = output.ConvertBlocked;
        ApplyConversionReport(output.ConversionResults, result);
    }

    internal static void ApplyConversionReport(IReadOnlyList<ConversionResult> results, RunResultBuilder builder)
    {
        var reviewCount = results.Count(r =>
            r.Plan?.RequiresReview == true
            || r.Safety == ConversionSafety.Risky
            || r.SourceIntegrity == SourceIntegrity.Lossy);
        var lossyWarningCount = results.Count(r => r.SourceIntegrity == SourceIntegrity.Lossy);
        var verifyPassedCount = results.Count(r => r.VerificationResult == VerificationStatus.Verified);
        var verifyFailedCount = results.Count(r => r.VerificationResult == VerificationStatus.VerifyFailed);
        long savedBytes = 0;
        foreach (var r in results)
        {
            if (r.Outcome == ConversionOutcome.Success && r.SourcePath is not null && r.TargetPath is not null)
            {
                try
                {
                    long? sourceBytes = r.SourceBytes;
                    if (!sourceBytes.HasValue)
                    {
                        var sourceInfo = new FileInfo(r.SourcePath);
                        if (sourceInfo.Exists)
                            sourceBytes = sourceInfo.Length;
                    }

                    var targetBytes = ResolveTotalTargetBytes(r);

                    if (sourceBytes.HasValue && targetBytes.HasValue)
                        savedBytes += sourceBytes.Value - targetBytes.Value;
                }
                catch (IOException) { /* best-effort size delta — files may be locked or missing */ }
                catch (UnauthorizedAccessException) { /* best-effort size delta — permissions may block file info */ }
            }
        }

        builder.ConvertReviewCount = reviewCount;
        builder.ConvertLossyWarningCount = lossyWarningCount;
        builder.ConvertVerifyPassedCount = verifyPassedCount;
        builder.ConvertVerifyFailedCount = verifyFailedCount;
        builder.ConvertSavedBytes = savedBytes;
        builder.ConversionReport = new ConversionReport
        {
            TotalPlanned = results.Count,
            Converted = builder.ConvertedCount,
            Skipped = builder.ConvertSkippedCount,
            Errors = builder.ConvertErrorCount,
            Blocked = builder.ConvertBlockedCount,
            RequiresReview = reviewCount,
            TotalSavedBytes = savedBytes,
            Results = results
        };
    }

    internal static long? ResolveTotalTargetBytes(ConversionResult result)
    {
        long totalBytes = 0;
        var hasMeasuredBytes = false;

        if (result.TargetBytes.HasValue)
        {
            totalBytes += result.TargetBytes.Value;
            hasMeasuredBytes = true;
        }
        else if (!string.IsNullOrWhiteSpace(result.TargetPath))
        {
            var targetInfo = new FileInfo(result.TargetPath);
            if (targetInfo.Exists)
            {
                totalBytes += targetInfo.Length;
                hasMeasuredBytes = true;
            }
        }

        foreach (var additionalTargetPath in result.AdditionalTargetPaths)
        {
            if (string.IsNullOrWhiteSpace(additionalTargetPath))
                continue;

            var additionalInfo = new FileInfo(additionalTargetPath);
            if (!additionalInfo.Exists)
                continue;

            totalBytes += additionalInfo.Length;
            hasMeasuredBytes = true;
        }

        return hasMeasuredBytes ? totalBytes : null;
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
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            _onProgress?.Invoke($"ERROR: Report generation failed at target path: {ex.GetType().Name}: {ex.Message}");
            System.Diagnostics.Debug.WriteLine($"Report generation failed (primary): {ex}");

            // Fallback to user profile directory if primary report target is not writable.
            try
            {
                var fallbackDir = AppStoragePathResolver.ResolveRoamingPath("reports");
                Directory.CreateDirectory(fallbackDir);

                var fallbackFileName = Path.GetFileName(options.ReportPath!) ?? $"report-{DateTime.UtcNow:yyyyMMdd-HHmmss}.html";
                var fallbackPath = Path.Combine(fallbackDir, fallbackFileName);

                var actualPath = RunReportWriter.WriteReport(fallbackPath, result.Build(), options.Mode);
                _onProgress?.Invoke($"Report written (fallback): {actualPath}");
                return actualPath;
            }
            catch (Exception fallbackEx) when (fallbackEx is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
            {
                _onProgress?.Invoke($"ERROR: Report fallback failed: {fallbackEx.GetType().Name}: {fallbackEx.Message}");
                System.Diagnostics.Debug.WriteLine($"Report generation failed (fallback): {fallbackEx}");
                return null;
            }
        }
    }
}
