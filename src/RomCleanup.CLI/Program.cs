using RomCleanup.Contracts.Errors;
using RomCleanup.Infrastructure.Audit;
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
        if (string.Equals(cliOpts.Mode, "Move", StringComparison.OrdinalIgnoreCase)
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
            env.Converter, env.DatIndex, onProgress: SafeErrorWriteLine, archiveHashService: env.ArchiveHashService);

        var result = orchestrator.Execute(runOptions, cts.Token);
        var projection = RunProjectionFactory.Create(result);

        log?.Info("CLI", "scan-complete", $"{result.TotalFilesScanned} files scanned", "scan");
        log?.Info("CLI", "dedupe-complete",
            $"{result.GroupCount} groups: Keep={result.WinnerCount}, Move={result.LoserCount}", "dedupe");

        // Output
        if (cliOpts.Mode == "DryRun")
        {
            SafeStandardWriteLine(CliOutputWriter.FormatDryRunJson(projection, result.DedupeGroups, result.ConversionReport));
        }
        else if (cliOpts.Mode == "Move")
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
            var trashDir = Path.Combine(root, "_TRASH");
            if (Directory.Exists(trashDir))
                currentRoots.Add(trashDir);
            var trashConv = Path.Combine(root, "_TRASH_CONVERTED");
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
                var firstComma = line.IndexOf(',');
                if (firstComma <= 0) continue;
                var rootField = line[..firstComma].Trim().Trim('"');
                if (!string.IsNullOrWhiteSpace(rootField) && Directory.Exists(rootField))
                    roots.Add(rootField);
            }
        }
        catch (IOException) { /* best-effort */ }
        return roots.ToArray();
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
