using System.Text;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Export;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Safety;

namespace Romulus.CLI;

internal static partial class Program
{
    private static int SubcommandAnalyze(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Analyze] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings, dataDir);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        using var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        using var reviewDecisionService = CreateReviewDecisionService(SafeErrorWriteLine);
        using var orchestrator = new RunOrchestrator(
            env.FileSystem,
            env.AuditStore,
            env.ConsoleDetector,
            env.HashService,
            env.Converter,
            env.DatIndex,
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
        SafeStandardWriteLine(JsonSerializer.Serialize(output, new JsonSerializerOptions { WriteIndented = true }));

        if (result.DedupeGroups.Count > 0)
        {
            var heatmap = CollectionAnalysisService.GetDuplicateHeatmap(result.DedupeGroups);
            SafeErrorWriteLine("\n[Heatmap]");
            foreach (var h in heatmap.Take(15))
                SafeErrorWriteLine($"  {h.Console,-15} {h.Total,5} total, {h.Duplicates,5} dupes ({h.DuplicatePercent:F1}%)");
        }

        return 0;
    }

    private static async Task<int> SubcommandExportAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Export] Preparing {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings, dataDir);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        using var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        var exportResult = await FrontendExportService.ExportAsync(
            new FrontendExportRequest(
                opts.ExportFormat ?? FrontendExportTargets.Csv,
                opts.OutputPath ?? Path.Combine(
                    ArtifactPathResolver.GetArtifactDirectory(runOptions.Roots, AppIdentity.ArtifactDirectories.Reports),
                    $"frontend-export-{DateTime.UtcNow:yyyyMMdd-HHmmss}.out"),
                string.IsNullOrWhiteSpace(opts.CollectionName) ? "Romulus" : opts.CollectionName.Trim(),
                runOptions.Roots,
                runOptions.Extensions),
            env.FileSystem,
            env.CollectionIndex,
            env.EnrichmentFingerprint,
            fallbackCandidateFactory: exportCt => LoadExportCandidatesAsync(runOptions, env, exportCt),
            ct: CancellationToken.None).ConfigureAwait(false);

        SafeStandardWriteLine(JsonSerializer.Serialize(exportResult, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var artifact in exportResult.Artifacts)
            SafeErrorWriteLine($"[Export] {artifact.Label}: {artifact.Path} ({artifact.ItemCount} item(s))");

        return 0;
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

        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings, dataDir);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        using var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
        {
            SafeErrorWriteLine("[Error] No DAT index available. Configure DatRoot in settings or use --dat-root.");
            return 1;
        }

        var completeness = await CompletenessReportService.BuildAsync(
            env.DatIndex,
            runOptions.Roots,
            env.CollectionIndex,
            runOptions.Extensions).ConfigureAwait(false);

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

        string safeTargetPath;
        try
        {
            safeTargetPath = SafetyValidator.EnsureSafeOutputPath(targetPath, allowUnc: false);
        }
        catch (InvalidOperationException ex)
        {
            SafeErrorWriteLine($"[Error] Invalid fixdat output path: {ex.Message}");
            return 3;
        }

        var outputDirectory = Path.GetDirectoryName(safeTargetPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        File.WriteAllText(safeTargetPath, fixDat.XmlContent, Encoding.UTF8);
        SafeStandardWriteLine(DatAnalysisService.FormatFixDatReport(fixDat));
        SafeErrorWriteLine($"[FixDAT] Written: {safeTargetPath}");

        return 0;
    }

    private static int SubcommandJunkReport(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[JunkReport] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings, dataDir);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        using var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        using var reviewDecisionService = CreateReviewDecisionService(SafeErrorWriteLine);
        using var orchestrator = new RunOrchestrator(
            env.FileSystem,
            env.AuditStore,
            env.ConsoleDetector,
            env.HashService,
            env.Converter,
            env.DatIndex,
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

    private static async Task<int> SubcommandCompletenessAsync(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Completeness] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        // Force DAT on for completeness
        opts.EnableDat = true;

        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings, dataDir);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        using var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
        {
            SafeErrorWriteLine("[Error] No DAT index available. Configure DatRoot in settings or use --dat-root.");
            return 1;
        }

        var report = await CompletenessReportService.BuildAsync(
            env.DatIndex,
            runOptions.Roots,
            env.CollectionIndex,
            runOptions.Extensions).ConfigureAwait(false);
        SafeStandardWriteLine(CompletenessReportService.FormatReport(report));

        // Also output JSON summary for machine consumption
        var json = JsonSerializer.Serialize(
            report.Entries.Select(e => new
            {
                e.ConsoleKey,
                e.TotalInDat,
                e.Verified,
                e.MissingCount,
                e.Percentage
            }).ToArray(),
            new JsonSerializerOptions { WriteIndented = true });

        if (opts.OutputPath is not null)
        {
            File.WriteAllText(opts.OutputPath, json);
            SafeErrorWriteLine($"[Completeness] JSON written to {opts.OutputPath}");
        }

        return 0;
    }

    private static Task<IReadOnlyList<RomCandidate>> LoadExportCandidatesAsync(
        RunOptions runOptions,
        IRunEnvironment env,
        CancellationToken ct)
    {
        using var reviewDecisionService = CreateReviewDecisionService(SafeErrorWriteLine);
        using var orchestrator = new RunOrchestrator(
            env.FileSystem,
            env.AuditStore,
            env.ConsoleDetector,
            env.HashService,
            env.Converter,
            env.DatIndex,
            onProgress: SafeErrorWriteLine,
            archiveHashService: env.ArchiveHashService,
            knownBiosHashes: env.KnownBiosHashes,
            collectionIndex: env.CollectionIndex,
            enrichmentFingerprint: env.EnrichmentFingerprint,
            reviewDecisionService: reviewDecisionService);

        var result = orchestrator.Execute(runOptions, ct);
        return Task.FromResult<IReadOnlyList<RomCandidate>>(result.AllCandidates);
    }
}
