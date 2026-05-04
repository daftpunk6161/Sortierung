using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Policy;
using Romulus.Infrastructure.Provenance;

namespace Romulus.CLI;

internal static partial class Program
{
    private static async Task<int> SubcommandAnalyzeAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Analyze] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = await CliOptionsMapper.MapAsync(opts, settings, dataDir).ConfigureAwait(false);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var runEnvironmentFactory = serviceProvider.GetRequiredService<IRunEnvironmentFactory>();
            using var env = runEnvironmentFactory.Create(runOptions, SafeErrorWriteLine);
            using var reviewDecisionService = CreateReviewDecisionService(SafeErrorWriteLine);
            using var orchestrator = new RunOrchestrator(
                env.FileSystem,
                env.AuditStore,
                env.ConsoleDetector,
                env.HashService,
                env.Converter,
                env.DatIndex,
                headerlessHasher: env.HeaderlessHasher,
                onProgress: SafeErrorWriteLine,
                archiveHashService: env.ArchiveHashService,
                knownBiosHashes: env.KnownBiosHashes,
                collectionIndex: env.CollectionIndex,
                enrichmentFingerprint: env.EnrichmentFingerprint,
                reviewDecisionService: reviewDecisionService);

            var result = orchestrator.Execute(runOptions);
            var projection = RunProjectionFactory.Create(result);

        var healthScore = CollectionAnalysisService.CalculateHealthScore(
            projection.TotalFiles, projection.Dupes, projection.Junk, projection.DatMatches);

        var output = new
        {
            healthScore,
            totalFiles = projection.TotalFiles,
            candidates = projection.Candidates,
            groups = projection.Groups,
            winners = projection.Keep,
            duplicates = projection.Dupes,
            games = projection.Games,
            unknown = projection.Unknown,
            junk = projection.Junk,
            bios = projection.Bios,
            datMatches = projection.DatMatches
        };
        SafeStandardWriteLine(CliOutputWriter.SerializeJson(output));

            if (result.DedupeGroups.Count > 0)
            {
                var heatmap = CollectionAnalysisService.GetDuplicateHeatmap(result.DedupeGroups);
                SafeErrorWriteLine("\n[Heatmap]");
                foreach (var h in heatmap.Take(15))
                    SafeErrorWriteLine($"  {h.Console,-15} {h.Total,5} total, {h.Duplicates,5} dupes ({h.DuplicatePercent:F1}%)");
            }

            return 0;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// T-W5-BEFORE-AFTER-SIMULATOR pass 3 — headless equivalent of the GUI
    /// Simulator view. Wraps the canonical <see cref="RunOrchestrator"/> via
    /// <see cref="BeforeAfterSimulator"/> (which forces DryRun) and emits a
    /// deterministic JSON projection of items + summary. Side-effect-free even
    /// when callers pass <c>--mode Move</c>; the simulator's <c>ForceDryRun</c>
    /// chokepoint is single-source-of-truth.
    /// </summary>
    internal static async Task<int> SubcommandSimulateAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Simulate] Projecting Before/After for {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = await CliOptionsMapper.MapAsync(opts, settings, dataDir).ConfigureAwait(false);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var runEnvironmentFactory = serviceProvider.GetRequiredService<IRunEnvironmentFactory>();
            using var env = runEnvironmentFactory.Create(runOptions, SafeErrorWriteLine);
            using var reviewDecisionService = CreateReviewDecisionService(SafeErrorWriteLine);

            // Plan executor is captured in a delegate so the BeforeAfterSimulator
            // forces DryRun via its own canonical ForceDryRun helper instead of
            // us duplicating the override here. This keeps the SoT invariant
            // (BeforeAfterSimulatorTests.Simulate_PlanMatchesDirectDryRun_*).
            BeforeAfterSimulationResult simulation;
            using (var orchestrator = new RunOrchestrator(
                env.FileSystem,
                env.AuditStore,
                env.ConsoleDetector,
                env.HashService,
                env.Converter,
                env.DatIndex,
                headerlessHasher: env.HeaderlessHasher,
                onProgress: SafeErrorWriteLine,
                archiveHashService: env.ArchiveHashService,
                knownBiosHashes: env.KnownBiosHashes,
                collectionIndex: env.CollectionIndex,
                enrichmentFingerprint: env.EnrichmentFingerprint,
                reviewDecisionService: reviewDecisionService))
            {
                var simulator = new BeforeAfterSimulator((effectiveOptions, ct) => orchestrator.Execute(effectiveOptions, ct));
                simulation = simulator.Simulate(runOptions);
            }

            var output = new
            {
                items = simulation.Items.Select(item => new
                {
                    sourcePath = item.SourcePath,
                    targetPath = item.TargetPath,
                    action = item.Action.ToString(),
                    sizeBytes = item.SizeBytes,
                    reason = item.Reason
                }).ToArray(),
                summary = new
                {
                    totalBefore = simulation.Summary.TotalBefore,
                    totalAfter = simulation.Summary.TotalAfter,
                    kept = simulation.Summary.Kept,
                    removed = simulation.Summary.Removed,
                    converted = simulation.Summary.Converted,
                    renamed = simulation.Summary.Renamed,
                    potentialSavedBytes = simulation.Summary.PotentialSavedBytes
                }
            };

            var json = CliOutputWriter.SerializeJson(output);
            if (!string.IsNullOrEmpty(opts.OutputPath))
            {
                File.WriteAllText(opts.OutputPath, json);
                SafeErrorWriteLine($"[Simulate] Wrote {simulation.Items.Count} item(s) to {opts.OutputPath}");
            }
            else
            {
                SafeStandardWriteLine(json);
            }

            return 0;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// Wave 4 — T-W4-DECISION-EXPLAINER. Headless equivalent of the GUI
    /// Decision-Drawer. Runs a DryRun analysis through the canonical
    /// <see cref="RunOrchestrator"/>, projects <see cref="RunResult.WinnerReasons"/>
    /// through <see cref="Romulus.Infrastructure.Reporting.DecisionExplainerProjection"/>
    /// and emits the result as a JSON envelope.
    ///
    /// <para>
    /// Single Source of Truth: GUI / CLI / API all consume the same
    /// projection. Filtering by <c>--console-key</c> + <c>--game-key</c>
    /// uses <see cref="Romulus.Infrastructure.Reporting.DecisionExplainerProjection.Find"/>.
    /// </para>
    /// </summary>
    internal static async Task<int> SubcommandExplainAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Explain] Projecting decisions for {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = await CliOptionsMapper.MapAsync(opts, settings, dataDir).ConfigureAwait(false);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        // Note: explain stays read-only because CliOptionsMapper defaults to DryRun
        // when the CLI never set --apply-move; we deliberately do not allow Move
        // here (no flag binds to it).

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var runEnvironmentFactory = serviceProvider.GetRequiredService<IRunEnvironmentFactory>();
            using var env = runEnvironmentFactory.Create(runOptions, SafeErrorWriteLine);
            using var reviewDecisionService = CreateReviewDecisionService(SafeErrorWriteLine);
            using var orchestrator = new RunOrchestrator(
                env.FileSystem,
                env.AuditStore,
                env.ConsoleDetector,
                env.HashService,
                env.Converter,
                env.DatIndex,
                headerlessHasher: env.HeaderlessHasher,
                onProgress: SafeErrorWriteLine,
                archiveHashService: env.ArchiveHashService,
                knownBiosHashes: env.KnownBiosHashes,
                collectionIndex: env.CollectionIndex,
                enrichmentFingerprint: env.EnrichmentFingerprint,
                reviewDecisionService: reviewDecisionService);

            var result = orchestrator.Execute(runOptions);
            IReadOnlyList<DecisionExplanation> explanations =
                Romulus.Infrastructure.Reporting.DecisionExplainerProjection.Project(result);

            // Optional filters
            if (!string.IsNullOrWhiteSpace(opts.ConsoleKey) && !string.IsNullOrWhiteSpace(opts.GameKey))
            {
                var hit = Romulus.Infrastructure.Reporting.DecisionExplainerProjection.Find(
                    explanations, opts.ConsoleKey!, opts.GameKey!);
                explanations = hit is null
                    ? Array.Empty<DecisionExplanation>()
                    : new[] { hit };
            }
            else if (!string.IsNullOrWhiteSpace(opts.ConsoleKey))
            {
                var key = opts.ConsoleKey!;
                explanations = explanations
                    .Where(e => string.Equals(e.ConsoleKey, key, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var output = new
            {
                count = explanations.Count,
                explanations = explanations.Select(e => new
                {
                    consoleKey = e.ConsoleKey,
                    gameKey = e.GameKey,
                    winnerFileName = e.WinnerFileName,
                    winnerExtension = e.WinnerExtension,
                    winnerCategory = e.WinnerCategory,
                    winnerRegion = e.WinnerRegion,
                    datMatch = e.DatMatch,
                    multiDatResolution = e.MultiDatResolution,
                    loserCount = e.LoserCount,
                    scores = e.Scores.Select(s => new { axis = s.Axis, value = s.Value }).ToArray(),
                    tiebreakerOrder = e.TiebreakerOrder,
                    summary = e.Summary,
                }).ToArray(),
            };

            var json = CliOutputWriter.SerializeJson(output);
            if (!string.IsNullOrEmpty(opts.OutputPath))
            {
                File.WriteAllText(opts.OutputPath, json);
                SafeErrorWriteLine($"[Explain] Wrote {explanations.Count} explanation(s) to {opts.OutputPath}");
            }
            else
            {
                SafeStandardWriteLine(json);
            }

            return 0;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    internal static int SubcommandProvenance(CliRunOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.Fingerprint))
        {
            SafeErrorWriteLine("[Error] provenance requires --fingerprint <hex>");
            return 3;
        }

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var store = serviceProvider.GetRequiredService<IProvenanceStore>();
            ProvenanceTrail trail;
            try
            {
                trail = ProvenanceTrailProjection.Project(store, opts.Fingerprint);
            }
            catch (ArgumentException ex)
            {
                SafeErrorWriteLine($"[Error] Invalid fingerprint: {ex.Message}");
                return 3;
            }

            var output = new
            {
                fingerprint = trail.Fingerprint,
                isValid = trail.IsValid,
                failureReason = trail.FailureReason,
                trustScore = trail.TrustScore,
                count = trail.Entries.Count,
                entries = trail.Entries.Select(entry => new
                {
                    eventKind = entry.EventKind.ToString(),
                    timestampUtc = entry.TimestampUtc,
                    auditRunId = entry.AuditRunId,
                    sha256 = entry.Sha256,
                    consoleKey = entry.ConsoleKey,
                    datMatchId = entry.DatMatchId,
                    detail = entry.Detail,
                    previousEntryHmac = entry.PreviousEntryHmac,
                    entryHmac = entry.EntryHmac
                }).ToArray()
            };

            var json = CliOutputWriter.SerializeJson(output);
            if (!string.IsNullOrWhiteSpace(opts.OutputPath))
            {
                if (!TryWriteSafeOutputFile(opts.OutputPath, json, "provenance JSON", out var safeOutputPath))
                    return 3;
                SafeErrorWriteLine($"[Provenance] Wrote {trail.Entries.Count} entries to {safeOutputPath}");
            }
            else
            {
                SafeStandardWriteLine(json);
            }

            return trail.IsValid ? 0 : 4;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    internal static async Task<int> SubcommandValidatePolicyAsync(CliRunOptions opts)
    {
        if (string.IsNullOrWhiteSpace(opts.PolicyPath))
        {
            SafeErrorWriteLine("[Error] validate-policy requires --policy <file>.");
            return 3;
        }

        if (!File.Exists(opts.PolicyPath))
        {
            SafeErrorWriteLine($"[Error] Policy file not found: {opts.PolicyPath}");
            return 3;
        }

        if (opts.Roots.Length == 0)
        {
            SafeErrorWriteLine("[Error] validate-policy requires --roots <paths>.");
            return 3;
        }

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var policyText = await File.ReadAllTextAsync(opts.PolicyPath).ConfigureAwait(false);
            LibraryPolicy policy;
            try
            {
                policy = PolicyDocumentLoader.Parse(policyText);
            }
            catch (FormatException ex)
            {
                SafeErrorWriteLine($"[Error] Invalid policy: {ex.Message}");
                return 3;
            }

            var collectionIndex = serviceProvider.GetRequiredService<ICollectionIndex>();
            var policyEngine = serviceProvider.GetRequiredService<IPolicyEngine>();
            var auditSigningService = serviceProvider.GetRequiredService<AuditSigningService>();
            if (opts.SignPolicy)
            {
                var signaturePath = PolicyDocumentLoader.WriteSignatureFile(
                    opts.PolicyPath,
                    policyText,
                    auditSigningService,
                    TimeProvider.UtcNow.UtcDateTime);
                SafeErrorWriteLine($"[Policy] Signature written: {signaturePath}");
            }

            var entries = await collectionIndex.ListEntriesInScopeAsync(opts.Roots, opts.Extensions).ConfigureAwait(false);
            var snapshot = LibrarySnapshotProjection.FromCollectionIndex(
                entries,
                opts.Roots,
                TimeProvider.UtcNow.UtcDateTime);
            var fingerprint = PolicyDocumentLoader.ComputeFingerprint(policyText);
            var signature = PolicyDocumentLoader.VerifySignatureFile(
                opts.PolicyPath,
                policyText,
                auditSigningService);
            var report = policyEngine.Validate(snapshot, policy, fingerprint) with
            {
                Signature = signature
            };
            var serialized = opts.OutputPath?.EndsWith(".csv", StringComparison.OrdinalIgnoreCase) == true
                ? PolicyValidationReportExporter.ToCsv(report)
                : PolicyValidationReportExporter.ToJson(report);

            if (!string.IsNullOrWhiteSpace(opts.OutputPath))
            {
                if (!TryWriteSafeOutputFile(opts.OutputPath, serialized, "policy validation report", out var safeOutputPath))
                    return 3;
                SafeErrorWriteLine($"[Policy] Wrote {report.Violations.Length} violation(s) to {safeOutputPath}");
            }
            else
            {
                SafeStandardWriteLine(serialized);
            }

            return report.IsCompliant ? 0 : 4;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            SafeErrorWriteLine($"[Error] Failed to validate policy: {ex.Message}");
            return 3;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    private static int SubcommandDatDiff(CliRunOptions opts)
    {
        if (!File.Exists(opts.DatFileA))
        {
            SafeErrorWriteLine($"[Error] File not found: {opts.DatFileA}");
            return 1;
        }

        if (!File.Exists(opts.DatFileB))
        {
            SafeErrorWriteLine($"[Error] File not found: {opts.DatFileB}");
            return 1;
        }

        var diff = DatAnalysisService.CompareDatFiles(opts.DatFileA!, opts.DatFileB!);
        var report = DatAnalysisService.FormatDatDiffReport(opts.DatFileA!, opts.DatFileB!, diff);
        SafeStandardWriteLine(report);
        return 0;
    }

    private static async Task<int> SubcommandDatFixAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[FixDAT] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        opts.EnableDat = true;

        var (runOptions, mapErrors) = await CliOptionsMapper.MapAsync(opts, settings, dataDir).ConfigureAwait(false);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var runEnvironmentFactory = serviceProvider.GetRequiredService<IRunEnvironmentFactory>();
            using var env = runEnvironmentFactory.Create(runOptions, SafeErrorWriteLine);
            if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
            {
                SafeErrorWriteLine("[Error] No DAT index available. Import DAT files or configure DatRoot in settings.");
                return 1;
            }

            var completeness = await CompletenessReportService.BuildAsync(
                env.DatIndex,
                runOptions.Roots,
                env.CollectionIndex,
                runOptions.Extensions,
                fileSystem: env.FileSystem).ConfigureAwait(false);

            var generatedUtc = DateTime.UtcNow;
            var datName = string.IsNullOrWhiteSpace(opts.DatName)
                ? $"Romulus-FixDAT-{generatedUtc:yyyyMMdd-HHmmss}"
                : opts.DatName.Trim();

            var fixDat = DatAnalysisService.BuildFixDatFromCompleteness(env.DatIndex, completeness, datName, generatedUtc);

            var targetPath = opts.OutputPath;
            if (string.IsNullOrWhiteSpace(targetPath))
            {
                targetPath = Path.Combine(
                    ArtifactPathResolver.GetArtifactDirectory(runOptions.Roots, AppIdentity.ArtifactDirectories.Reports),
                    $"fixdat-{generatedUtc:yyyyMMdd-HHmmss}.dat");
            }

            if (!TryWriteSafeOutputFile(targetPath, fixDat.XmlContent, "fixdat output", out var safeTargetPath))
                return 3;

            SafeStandardWriteLine(DatAnalysisService.FormatFixDatReport(fixDat));
            SafeErrorWriteLine($"[FixDAT] Written: {safeTargetPath}");

            return 0;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    private static async Task<int> SubcommandJunkReportAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[JunkReport] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = await CliOptionsMapper.MapAsync(opts, settings, dataDir).ConfigureAwait(false);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var runEnvironmentFactory = serviceProvider.GetRequiredService<IRunEnvironmentFactory>();
            using var env = runEnvironmentFactory.Create(runOptions, SafeErrorWriteLine);
            using var reviewDecisionService = CreateReviewDecisionService(SafeErrorWriteLine);
            using var orchestrator = new RunOrchestrator(
                env.FileSystem,
                env.AuditStore,
                env.ConsoleDetector,
                env.HashService,
                env.Converter,
                env.DatIndex,
                headerlessHasher: env.HeaderlessHasher,
                onProgress: SafeErrorWriteLine,
                archiveHashService: env.ArchiveHashService,
                knownBiosHashes: env.KnownBiosHashes,
                collectionIndex: env.CollectionIndex,
                enrichmentFingerprint: env.EnrichmentFingerprint,
                reviewDecisionService: reviewDecisionService);

            var result = orchestrator.Execute(runOptions);
            var report = CollectionExportService.BuildJunkReport(result.AllCandidates, opts.AggressiveJunk);
            SafeStandardWriteLine(report);
            return 0;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }

    private static async Task<int> SubcommandCompletenessAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Completeness] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        // Force DAT on for completeness
        opts.EnableDat = true;

        var (runOptions, mapErrors) = await CliOptionsMapper.MapAsync(opts, settings, dataDir).ConfigureAwait(false);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var serviceProvider = CreateCliServiceProvider(SafeErrorWriteLine);
        try
        {
            var runEnvironmentFactory = serviceProvider.GetRequiredService<IRunEnvironmentFactory>();
            using var env = runEnvironmentFactory.Create(runOptions, SafeErrorWriteLine);
            if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
            {
                SafeErrorWriteLine("[Error] No DAT index available. Import DAT files or configure DatRoot in settings.");
                return 1;
            }

            var report = await CompletenessReportService.BuildAsync(
                env.DatIndex,
                runOptions.Roots,
                env.CollectionIndex,
                runOptions.Extensions,
                fileSystem: env.FileSystem).ConfigureAwait(false);
            SafeStandardWriteLine(CompletenessReportService.FormatReport(report));

            // Also output JSON summary for machine consumption
            var json = CliOutputWriter.SerializeJson(
                report.Entries.Select(e => new
                {
                    e.ConsoleKey,
                    e.TotalInDat,
                    e.Verified,
                    e.MissingCount,
                    e.Percentage
                }).ToArray());

            if (!string.IsNullOrWhiteSpace(opts.OutputPath))
            {
                if (!TryWriteSafeOutputFile(opts.OutputPath, json, "completeness JSON", out var safeOutputPath))
                    return 3;

                SafeErrorWriteLine($"[Completeness] JSON written to {safeOutputPath}");
            }

            return 0;
        }
        finally
        {
            (serviceProvider as IDisposable)?.Dispose();
        }
    }
}
