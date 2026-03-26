using RomCleanup.Contracts.Models;
using RomCleanup.Core.Classification;
using RomCleanup.Core.Deduplication;
using RomCleanup.Infrastructure.Deduplication;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Linking;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Quarantine;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.Infrastructure.Orchestration;

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

    private void WriteCompletedAuditSidecar(RunOptions options, RunResultBuilder result, long elapsedMs, RunOutcome? outcome = null)
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
        });
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
            File.WriteAllText(options.AuditPath,
                "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n",
                System.Text.Encoding.UTF8);
        }
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
            new EnrichmentPhaseStreamingInput(scannedFiles, _consoleDetector, _hashService, _archiveHashService, _datIndex, _headerlessHasher),
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
        if (!options.RemoveJunk || options.Mode != "Move")
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

    private static void ApplyConversionReport(IReadOnlyList<ConversionResult> results, RunResultBuilder builder)
    {
        var reviewCount = results.Count(r => r.Safety == ConversionSafety.Risky);
        long savedBytes = 0;
        foreach (var r in results)
        {
            if (r.Outcome == ConversionOutcome.Success && r.SourcePath is not null && r.TargetPath is not null)
            {
                try
                {
                    var sourceInfo = new FileInfo(r.SourcePath);
                    var targetInfo = new FileInfo(r.TargetPath);
                    if (sourceInfo.Exists && targetInfo.Exists)
                        savedBytes += sourceInfo.Length - targetInfo.Length;
                }
                catch (IOException) { /* best-effort size delta — files may be locked or missing */ }
            }
        }

        builder.ConvertReviewCount = reviewCount;
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
                    Contracts.AppIdentity.AppFolderName,
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