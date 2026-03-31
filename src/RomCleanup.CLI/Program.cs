using System.Text.Json;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Errors;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Analysis;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.Dat;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Logging;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.CLI;

/// <summary>
/// Headless CLI entry point for ROM Cleanup.
/// Thin adapter wiring CliArgsParser → CliOptionsMapper → RunEnvironmentBuilder → RunOrchestrator → CliOutputWriter.
/// ADR-008.
/// Exit codes: 0=Success, 1=Error, 2=Cancelled, 3=Preflight failed.
/// </summary>
internal static class Program
{
    private static readonly AsyncLocal<TextWriter?> StdoutOverride = new();
    private static readonly AsyncLocal<TextWriter?> StderrOverride = new();
    private static readonly AsyncLocal<bool> ConsoleOverrideEnabled = new();
    private static readonly AsyncLocal<bool?> NonInteractiveOverride = new();

    private static int Main(string[] args)
    {
        try
        {
            var result = CliArgsParser.Parse(args);

            switch (result.Command)
            {
                case CliCommand.Help:
                    if (result.Errors.Count > 0)
                    {
                        CliOutputWriter.WriteErrors(GetStderr(), result.Errors);
                        return result.ExitCode;
                    }
                    CliOutputWriter.WriteUsage(GetStdout());
                    return 0;

                case CliCommand.Version:
                    SafeStandardWriteLine(typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0");
                    return 0;

                case CliCommand.Run when result.ExitCode != 0:
                    CliOutputWriter.WriteErrors(GetStderr(), result.Errors);
                    return result.ExitCode;

                case CliCommand.Run:
                    return Run(result.Options!);

                case CliCommand.Rollback:
                    return Rollback(result.Options!);

                case CliCommand.UpdateDats:
                    return UpdateDats(result.Options!);

                // Subcommands
                case CliCommand.Analyze:
                    return SubcommandAnalyze(result.Options!);

                case CliCommand.Export:
                    return SubcommandExport(result.Options!);

                case CliCommand.DatDiff:
                    return SubcommandDatDiff(result.Options!);

                case CliCommand.IntegrityCheck:
                    return SubcommandIntegrityCheck();

                case CliCommand.IntegrityBaseline:
                    return SubcommandIntegrityBaseline(result.Options!);

                case CliCommand.Convert:
                    return SubcommandConvert(result.Options!);

                case CliCommand.Header:
                    return SubcommandHeader(result.Options!);

                case CliCommand.JunkReport:
                    return SubcommandJunkReport(result.Options!);

                case CliCommand.Completeness:
                    return SubcommandCompleteness(result.Options!);

                default:
                    return result.ExitCode;
            }
        }
        catch (OperationCanceledException)
        {
            SafeErrorWriteLine("[Cancelled]");
            return 2;
        }
        catch (Exception ex)
        {
            var error = ErrorClassifier.FromException(ex, "CLI");
            SafeErrorWriteLine($"[{error.Kind}] {error.Code}: {error.Message}");
            return 1;
        }
    }

    private static int Run(CliRunOptions cliOpts)
    {
        if (string.Equals(cliOpts.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase)
            && IsNonInteractiveExecution()
            && !cliOpts.Yes)
        {
            SafeErrorWriteLine("[Error] Non-interactive Move requires --yes confirmation.");
            return 3;
        }

        using var cts = new CancellationTokenSource();
        int cancelCount = 0;
        Console.CancelKeyPress += (_, e) =>
        {
            cancelCount++;
            if (cancelCount >= 2)
            {
                e.Cancel = true;
                cts.Cancel();
                SafeErrorWriteLine("Force-cancel requested.");
                return;
            }
            e.Cancel = true;
            cts.Cancel();
            SafeErrorWriteLine("Cancelling… press Ctrl+C again to force exit.");
        };

        // JSONL logging
        JsonlLogWriter? log = null;
        if (!string.IsNullOrEmpty(cliOpts.LogPath))
        {
            var logLevel = Enum.Parse<LogLevel>(cliOpts.LogLevel, ignoreCase: true);
            log = new JsonlLogWriter(cliOpts.LogPath, logLevel);
        }

        // Load settings + map to RunOptions
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = CliOptionsMapper.Map(cliOpts, settings);

        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        if (cliOpts.ConvertFormat)
            log?.Info("CLI", "convert-init", "Format conversion enabled", "init");

        // Build environment (DAT, ConsoleDetector, Converter, etc.)
        var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);

        log?.Info("CLI", "start", $"Run started: Mode={cliOpts.Mode}, Roots={string.Join(";", cliOpts.Roots)}", "scan");

        var orchestrator = new RunOrchestrator(env.FileSystem, env.AuditStore, env.ConsoleDetector, env.HashService,
            env.Converter, env.DatIndex, onProgress: SafeErrorWriteLine, archiveHashService: env.ArchiveHashService,
            knownBiosHashes: env.KnownBiosHashes);

        var result = orchestrator.Execute(runOptions, cts.Token);
        var projection = RunProjectionFactory.Create(result);

        log?.Info("CLI", "scan-complete", $"{result.TotalFilesScanned} files scanned", "scan");
        log?.Info("CLI", "dedupe-complete",
            $"{result.GroupCount} groups: Keep={result.WinnerCount}, Move={result.LoserCount}", "dedupe");

        // Output
        if (cliOpts.Mode == RunConstants.ModeDryRun)
        {
            SafeStandardWriteLine(CliOutputWriter.FormatDryRunJson(
                projection,
                result.DedupeGroups,
                result.ConversionReport,
                result.Preflight?.Warnings));
        }
        else if (cliOpts.Mode == RunConstants.ModeMove)
        {
            CliOutputWriter.WriteMoveSummary(GetStderr(), projection,
                runOptions.AuditPath, result.ReportPath, result.ConvertedCount);
        }

        if (!string.IsNullOrEmpty(cliOpts.ReportPath) && !string.IsNullOrEmpty(result.ReportPath))
        {
            SafeErrorWriteLine($"[Report] {result.ReportPath}");
            log?.Info("CLI", "report", $"Report written: {result.ReportPath}", "report");
        }
        else if (!string.IsNullOrEmpty(cliOpts.ReportPath))
        {
            SafeErrorWriteLine("[Warning] Report requested but not written");
            log?.Warning("CLI", "Report requested but not written", "report");
        }

        if (log != null)
        {
            log.Info("CLI", "done", $"Run completed in {result.DurationMs}ms", "done");
            log.Dispose();
            if (!string.IsNullOrEmpty(cliOpts.LogPath))
                JsonlLogRotation.Rotate(cliOpts.LogPath);
        }

        // SEC-CLI-01: Normalize exit code to documented range [0-3]
        return result.ExitCode switch
        {
            0 => 0,
            2 => 2,
            3 => 3,
            _ => 1
        };
    }

    // --- Backward-compatible delegates for tests ---

    private static int UpdateDats(CliRunOptions cliOpts)
        => Task.Run(() => UpdateDatsAsync(cliOpts)).Result;

    private static async Task<int> UpdateDatsAsync(CliRunOptions cliOpts)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        var datRoot = cliOpts.DatRoot;
        if (string.IsNullOrWhiteSpace(datRoot))
            datRoot = settings.Dat?.DatRoot;

        if (string.IsNullOrWhiteSpace(datRoot))
        {
            SafeErrorWriteLine("[Error] DAT root is required. Use --datroot <path> or configure datRoot in settings/defaults.");
            return 3;
        }

        Directory.CreateDirectory(datRoot);

        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        var catalog = DatSourceService.LoadCatalog(catalogPath);
        if (catalog.Count == 0)
        {
            SafeErrorWriteLine("[Error] DAT catalog is empty or missing.");
            return 3;
        }

        SafeErrorWriteLine($"[DatUpdate] Catalog entries: {catalog.Count}");
        SafeErrorWriteLine($"[DatUpdate] DAT root: {datRoot}");

        int skippedExisting = 0;
        int downloaded = 0;
        int failed = 0;

        using (var datService = new DatSourceService(datRoot))
        {
            // Download only URL-based catalog entries. nointro-pack entries are imported from local folders.
            var downloadEntries = catalog
                .Where(e => !string.IsNullOrWhiteSpace(e.Url)
                    && !string.Equals(e.Format, "nointro-pack", StringComparison.OrdinalIgnoreCase))
                .ToList();

            SafeErrorWriteLine($"[DatUpdate] URL-based entries: {downloadEntries.Count}");

            foreach (var entry in downloadEntries)
            {
                var fileName = entry.Id + ".dat";
                var targetPath = Path.Combine(datRoot, fileName);

                if (!cliOpts.ForceDatUpdate && File.Exists(targetPath))
                {
                    skippedExisting++;
                    continue;
                }

                try
                {
                    var result = await datService.DownloadDatByFormatAsync(entry.Url, fileName, entry.Format)
                        .ConfigureAwait(false);
                    if (result is null)
                    {
                        failed++;
                        SafeErrorWriteLine($"[DatUpdate][Warn] Failed: {entry.Id} ({entry.Group})");
                    }
                    else
                    {
                        downloaded++;
                        SafeErrorWriteLine($"[DatUpdate][OK] {entry.Id}");
                    }
                }
                catch (InvalidOperationException ex)
                {
                    failed++;
                    SafeErrorWriteLine($"[DatUpdate][Warn] {entry.Id}: {ex.Message}");
                }
                catch (HttpRequestException ex)
                {
                    failed++;
                    SafeErrorWriteLine($"[DatUpdate][Warn] {entry.Id}: {ex.Message}");
                }
                catch (IOException ex)
                {
                    failed++;
                    SafeErrorWriteLine($"[DatUpdate][Warn] {entry.Id}: {ex.Message}");
                }
            }

            var importPath = cliOpts.ImportPacksFrom;
            if (!string.IsNullOrWhiteSpace(importPath))
            {
                var imported = datService.ImportLocalDatPacks(importPath, catalog);
                SafeErrorWriteLine($"[DatUpdate] Imported local pack DATs: {imported}");
            }
        }

        SafeErrorWriteLine($"[DatUpdate] Downloaded: {downloaded}");
        SafeErrorWriteLine($"[DatUpdate] Skipped existing: {skippedExisting}");
        SafeErrorWriteLine($"[DatUpdate] Failed: {failed}");

        // Non-zero when at least one attempted download failed.
        return failed > 0 ? 1 : 0;
    }

    private static int Rollback(CliRunOptions cliOpts)
    {
        var auditPath = cliOpts.RollbackAuditPath!;
        var dryRun = cliOpts.RollbackDryRun;

        SafeErrorWriteLine($"[Rollback] {(dryRun ? "DryRun" : "Execute")} — Audit: {auditPath}");

        if (!dryRun && !cliOpts.Yes)
        {
            if (IsNonInteractiveExecution())
            {
                SafeErrorWriteLine("[Error] Non-interactive rollback execute requires --yes confirmation.");
                return 3;
            }

            SafeErrorWriteLine("[Rollback] Execute mode will restore files. Continue? (y/N)");
            var response = Console.ReadLine();
            if (!string.Equals(response?.Trim(), "y", StringComparison.OrdinalIgnoreCase))
            {
                SafeErrorWriteLine("[Rollback] Aborted by user.");
                return 2;
            }
        }

        var fs = new FileSystemAdapter();
        var keyPath = AuditSecurityPaths.GetDefaultSigningKeyPath();
        var signing = new AuditSigningService(fs, keyFilePath: keyPath);

        // Derive allowed roots from audit CSV — same roots that were used in the original run
        var roots = DeriveRootsFromAudit(auditPath);
        if (roots.Length == 0)
        {
            SafeErrorWriteLine("[Error] Could not determine root paths from audit file.");
            return 1;
        }

        // Current roots: original roots + trash paths (files may be in trash now)
        var currentRoots = roots.ToList();
        if (!string.IsNullOrWhiteSpace(cliOpts.TrashRoot))
            currentRoots.Add(cliOpts.TrashRoot);
        // Also add default trash folders within each root
        foreach (var root in roots)
        {
            var trashDir = Path.Combine(root, RunConstants.WellKnownFolders.TrashGeneric);
            if (Directory.Exists(trashDir))
                currentRoots.Add(trashDir);
            var trashConv = Path.Combine(root, RunConstants.WellKnownFolders.TrashConverted);
            if (Directory.Exists(trashConv))
                currentRoots.Add(trashConv);
        }

        var result = signing.Rollback(auditPath, roots, currentRoots.Distinct(StringComparer.OrdinalIgnoreCase).ToArray(), dryRun);

        SafeErrorWriteLine($"[Rollback] Total rows: {result.TotalRows}");
        SafeErrorWriteLine($"[Rollback] Eligible: {result.EligibleRows}");
        if (dryRun)
            SafeErrorWriteLine($"[Rollback] Planned: {result.DryRunPlanned}");
        else
            SafeErrorWriteLine($"[Rollback] Restored: {result.RolledBack}");
        SafeErrorWriteLine($"[Rollback] Skipped (unsafe): {result.SkippedUnsafe}");
        SafeErrorWriteLine($"[Rollback] Skipped (collision): {result.SkippedCollision}");
        SafeErrorWriteLine($"[Rollback] Skipped (missing): {result.SkippedMissingDest}");
        SafeErrorWriteLine($"[Rollback] Failed: {result.Failed}");

        if (!string.IsNullOrWhiteSpace(result.RollbackAuditPath))
            SafeErrorWriteLine($"[Rollback] Trail: {result.RollbackAuditPath}");

        return result.Failed > 0 ? 1 : 0;
    }

    // ═══ SUBCOMMAND HANDLERS ═══════════════════════════════════════════

    private static int SubcommandAnalyze(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Analyze] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        var orchestrator = new RunOrchestrator(env.FileSystem, env.AuditStore, env.ConsoleDetector, env.HashService,
            env.Converter, env.DatIndex, onProgress: SafeErrorWriteLine, archiveHashService: env.ArchiveHashService,
            knownBiosHashes: env.KnownBiosHashes);

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

    private static int SubcommandExport(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Export] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        var orchestrator = new RunOrchestrator(env.FileSystem, env.AuditStore, env.ConsoleDetector, env.HashService,
            env.Converter, env.DatIndex, onProgress: SafeErrorWriteLine, archiveHashService: env.ArchiveHashService,
            knownBiosHashes: env.KnownBiosHashes);

        var result = orchestrator.Execute(runOptions);
        var candidates = result.AllCandidates;

        var format = opts.ExportFormat ?? "csv";
        string content = format switch
        {
            "csv" => CollectionExportService.ExportCollectionCsv(candidates),
            "excel" => CollectionExportService.ExportExcelXml(candidates),
            "json" => JsonSerializer.Serialize(candidates.Select(c => new
            {
                c.MainPath, c.GameKey, c.Region, c.Extension,
                sizeMb = c.SizeBytes / 1048576.0,
                category = CollectionAnalysisService.ToCategoryLabel(c.Category),
                c.DatMatch
            }), new JsonSerializerOptions { WriteIndented = true }),
            _ => CollectionExportService.ExportCollectionCsv(candidates)
        };

        if (!string.IsNullOrWhiteSpace(opts.OutputPath))
        {
            File.WriteAllText(opts.OutputPath, content);
            SafeErrorWriteLine($"[Export] Written to {opts.OutputPath}");
        }
        else
        {
            SafeStandardWriteLine(content);
        }

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

    private static int SubcommandIntegrityCheck()
    {
        SafeErrorWriteLine("[Integrity] Checking against baseline...");
        var resultTask = IntegrityService.CheckIntegrity(
            new Progress<string>(SafeErrorWriteLine));
        var result = resultTask.GetAwaiter().GetResult();

        if (result.Message is not null)
        {
            SafeErrorWriteLine($"[Integrity] {result.Message}");
            return result.BitRotRisk ? 1 : 0;
        }

        SafeStandardWriteLine($"Intact:   {result.Intact.Count}");
        SafeStandardWriteLine($"Changed:  {result.Changed.Count}");
        SafeStandardWriteLine($"Missing:  {result.Missing.Count}");
        SafeStandardWriteLine($"Bit rot:  {(result.BitRotRisk ? "YES" : "No")}");

        if (result.Changed.Count > 0)
        {
            SafeErrorWriteLine("\n[Changed files]");
            foreach (var f in result.Changed.Take(20))
                SafeErrorWriteLine($"  {f}");
            if (result.Changed.Count > 20)
                SafeErrorWriteLine($"  ... and {result.Changed.Count - 20} more");
        }

        return result.BitRotRisk ? 1 : 0;
    }

    private static int SubcommandIntegrityBaseline(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Integrity] Creating baseline from {opts.Roots.Length} root(s)...");
        var files = new List<string>();
        foreach (var root in opts.Roots)
        {
            if (!Directory.Exists(root))
            {
                SafeErrorWriteLine($"[Warning] Root not found: {root}");
                continue;
            }
            files.AddRange(Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories));
        }

        if (files.Count == 0)
        {
            SafeErrorWriteLine("[Error] No files found in specified roots.");
            return 1;
        }

        SafeErrorWriteLine($"[Integrity] Found {files.Count} files.");
        var task = IntegrityService.CreateBaseline(files,
            new Progress<string>(SafeErrorWriteLine));
        var entries = task.GetAwaiter().GetResult();
        SafeStandardWriteLine($"Baseline created: {entries.Count} entries");
        return 0;
    }

    private static int SubcommandConvert(CliRunOptions opts)
    {
        var inputPath = opts.InputPath!;
        if (!File.Exists(inputPath) && !Directory.Exists(inputPath))
        {
            SafeErrorWriteLine($"[Error] Input not found: {inputPath}");
            return 1;
        }

        var service = StandaloneConversionService.Create(inputPath, SafeErrorWriteLine);
        if (service is null)
        {
            SafeErrorWriteLine("[Error] No converter available.");
            return 1;
        }

        if (File.Exists(inputPath))
        {
            var result = service.ConvertFile(inputPath, opts.ConsoleKey, opts.TargetFormat);
            SafeErrorWriteLine($"[Convert] {Path.GetFileName(inputPath)} -> {result.Outcome}");
            if (result.Outcome != ConversionOutcome.Success && result.Outcome != ConversionOutcome.Skipped)
                SafeErrorWriteLine($"  [{result.Outcome}] {result.Reason}");

            var c = result.Outcome == ConversionOutcome.Success ? 1 : 0;
            var s = result.Outcome == ConversionOutcome.Skipped ? 1 : 0;
            var e = (result.Outcome != ConversionOutcome.Success && result.Outcome != ConversionOutcome.Skipped) ? 1 : 0;
            SafeStandardWriteLine($"Converted: {c}, Skipped: {s}, Errors: {e}");
            return e > 0 ? 1 : 0;
        }
        else
        {
            var report = service.ConvertDirectory(inputPath, opts.ConsoleKey, opts.TargetFormat);
            foreach (var r in report.Results.Where(r => r.Outcome != ConversionOutcome.Success && r.Outcome != ConversionOutcome.Skipped))
                SafeErrorWriteLine($"  [{r.Outcome}] {Path.GetFileName(r.SourcePath)}: {r.Reason}");

            SafeStandardWriteLine($"Converted: {report.Converted}, Skipped: {report.Skipped}, Errors: {report.Errors}");
            return report.Errors > 0 ? 1 : 0;
        }
    }

    private static int SubcommandHeader(CliRunOptions opts)
    {
        var inputPath = opts.InputPath!;
        if (!File.Exists(inputPath))
        {
            SafeErrorWriteLine($"[Error] File not found: {inputPath}");
            return 1;
        }

        var header = IntegrityService.AnalyzeHeader(inputPath);
        if (header is null)
        {
            SafeStandardWriteLine("No header detected or unsupported format.");
            return 0;
        }

        SafeStandardWriteLine($"Platform: {header.Platform}");
        SafeStandardWriteLine($"Format:   {header.Format}");
        SafeStandardWriteLine($"Details:  {header.Details}");
        return 0;
    }

    private static int SubcommandJunkReport(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[JunkReport] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        var orchestrator = new RunOrchestrator(env.FileSystem, env.AuditStore, env.ConsoleDetector, env.HashService,
            env.Converter, env.DatIndex, onProgress: SafeErrorWriteLine, archiveHashService: env.ArchiveHashService,
            knownBiosHashes: env.KnownBiosHashes);

        var result = orchestrator.Execute(runOptions);
        var report = CollectionExportService.BuildJunkReport(result.AllCandidates, opts.AggressiveJunk);
        SafeStandardWriteLine(report);
        return 0;
    }

    private static int SubcommandCompleteness(CliRunOptions opts)
    {
        SafeErrorWriteLine($"[Completeness] Scanning {opts.Roots.Length} root(s)...");
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        // Force DAT on for completeness
        opts.EnableDat = true;

        var (runOptions, mapErrors) = CliOptionsMapper.Map(opts, settings);
        if (runOptions is null)
        {
            CliOutputWriter.WriteErrors(GetStderr(), mapErrors!);
            return 3;
        }

        var env = new RunEnvironmentFactory().Create(runOptions, SafeErrorWriteLine);
        if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
        {
            SafeErrorWriteLine("[Error] No DAT index available. Configure DatRoot in settings or use --dat-root.");
            return 1;
        }

        var report = CompletenessReportService.Build(env.DatIndex, opts.Roots);
        SafeStandardWriteLine(CompletenessReportService.FormatReport(report));

        // Also output JSON summary for machine consumption
        var json = System.Text.Json.JsonSerializer.Serialize(
            report.Entries.Select(e => new
            {
                e.ConsoleKey,
                e.TotalInDat,
                e.Verified,
                e.MissingCount,
                e.Percentage
            }).ToArray(),
            new System.Text.Json.JsonSerializerOptions { WriteIndented = true });

        if (opts.OutputPath is not null)
        {
            File.WriteAllText(opts.OutputPath, json);
            SafeErrorWriteLine($"[Completeness] JSON written to {opts.OutputPath}");
        }

        return 0;
    }

    /// <summary>
    /// Extract unique root paths from the first column of an audit CSV.
    /// </summary>
    private static string[] DeriveRootsFromAudit(string auditCsvPath)
    {
        var roots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            foreach (var line in File.ReadLines(auditCsvPath).Skip(1)) // skip header
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                var rootField = ExtractFirstCsvField(line);
                if (!string.IsNullOrWhiteSpace(rootField) && Directory.Exists(rootField))
                    roots.Add(rootField);
            }
        }
        catch (IOException) { /* best-effort */ }
        return roots.ToArray();
    }

    /// <summary>BUG-10: Extract first CSV field with RFC-4180 quoting support.</summary>
    internal static string ExtractFirstCsvField(string line)
    {
        if (string.IsNullOrEmpty(line)) return "";

        if (line[0] == '"')
        {
            // Quoted field — find matching close-quote (escaped "" inside)
            var sb = new System.Text.StringBuilder();
            for (int i = 1; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        sb.Append('"');
                        i++;
                    }
                    else
                    {
                        break;
                    }
                }
                else
                {
                    sb.Append(line[i]);
                }
            }
            return sb.ToString();
        }

        var firstComma = line.IndexOf(',');
        return firstComma <= 0 ? line.Trim() : line[..firstComma].Trim();
    }

    internal static int RunForTests(CliRunOptions opts)
    {
        var hadOverrides = ConsoleOverrideEnabled.Value;
        var previousStdout = StdoutOverride.Value;
        var previousStderr = StderrOverride.Value;

        if (!hadOverrides)
        {
            StdoutOverride.Value = Console.Out;
            StderrOverride.Value = Console.Error;
            ConsoleOverrideEnabled.Value = true;
        }

        try
        {
            return Run(opts);
        }
        finally
        {
            if (!hadOverrides)
            {
                ConsoleOverrideEnabled.Value = false;
                StdoutOverride.Value = null;
                StderrOverride.Value = null;
            }
            else
            {
                StdoutOverride.Value = previousStdout;
                StderrOverride.Value = previousStderr;
            }
        }
    }

    /// <summary>
    /// Backward-compatible ParseArgs: delegates to CliArgsParser.Parse + converts back.
    /// </summary>
    internal static (CliRunOptions?, int exitCode) ParseArgs(string[] args)
    {
        var result = CliArgsParser.Parse(args);

        switch (result.Command)
        {
            case CliCommand.Help when result.Errors.Count > 0:
                foreach (var err in result.Errors)
                    SafeErrorWriteLine(err);
                return (null, result.ExitCode);

            case CliCommand.Help:
                return (null, 0);

            case CliCommand.Version:
                return (null, -1);

            case CliCommand.Run when result.ExitCode != 0:
                foreach (var err in result.Errors)
                    SafeErrorWriteLine(err);
                return (null, result.ExitCode);

            case CliCommand.Run:
                return (result.Options!, 0);

            case CliCommand.UpdateDats:
                return (result.Options!, 0);

            default:
                return (null, result.ExitCode);
        }
    }

    private static TextWriter GetStdout()
        => ConsoleOverrideEnabled.Value ? (StdoutOverride.Value ?? Console.Out) : Console.Out;

    private static TextWriter GetStderr()
        => ConsoleOverrideEnabled.Value ? (StderrOverride.Value ?? Console.Error) : Console.Error;

    private static void SafeStandardWriteLine(string message)
        => SafeWriteLine(GetStdout(), Console.Out, message);

    private static void SafeErrorWriteLine(string message)
        => SafeWriteLine(GetStderr(), Console.Error, message);

    private static void SafeWriteLine(TextWriter writer, TextWriter fallbackWriter, string message)
    {
        try
        {
            writer.WriteLine(message);
        }
        catch (ObjectDisposedException)
        {
            if (!ReferenceEquals(writer, fallbackWriter))
            {
                try { fallbackWriter.WriteLine(message); }
                catch { System.Diagnostics.Debug.WriteLine($"CLI fallback write failed for: {message}"); }
            }
        }
        catch (IOException)
        {
            if (!ReferenceEquals(writer, fallbackWriter))
            {
                try { fallbackWriter.WriteLine(message); }
                catch { System.Diagnostics.Debug.WriteLine($"CLI fallback write failed for: {message}"); }
            }
        }
    }

    /// <summary>
    /// Activates AsyncLocal-based Console overrides for thread-safe test isolation.
    /// </summary>
    internal static void SetConsoleOverrides(TextWriter? stdout, TextWriter? stderr)
    {
        StdoutOverride.Value = stdout;
        StderrOverride.Value = stderr;
        ConsoleOverrideEnabled.Value = stdout is not null || stderr is not null;
    }

    internal static void SetNonInteractiveOverride(bool? isNonInteractive)
    {
        NonInteractiveOverride.Value = isNonInteractive;
    }

    private static bool IsNonInteractiveExecution()
    {
        if (NonInteractiveOverride.Value is bool forced)
            return forced;

        // Test harnesses use console overrides for deterministic capture;
        // do not treat that as non-interactive unless explicitly forced.
        if (ConsoleOverrideEnabled.Value)
            return false;

        return Console.IsInputRedirected || !Environment.UserInteractive;
    }

}
