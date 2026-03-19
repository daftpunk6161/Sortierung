using RomCleanup.Contracts.Errors;
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
            env.Converter, env.DatIndex, onProgress: SafeErrorWriteLine);

        var result = orchestrator.Execute(runOptions, cts.Token);
        var projection = RunProjectionFactory.Create(result);

        log?.Info("CLI", "scan-complete", $"{result.TotalFilesScanned} files scanned", "scan");
        log?.Info("CLI", "dedupe-complete",
            $"{result.GroupCount} groups: Keep={result.WinnerCount}, Move={result.LoserCount}", "dedupe");

        // Output
        if (cliOpts.Mode == "DryRun")
        {
            SafeStandardWriteLine(CliOutputWriter.FormatDryRunJson(projection, result.DedupeGroups));
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

        return result.ExitCode;
    }

    // --- Backward-compatible delegates for tests ---

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
                try { fallbackWriter.WriteLine(message); } catch { }
            }
        }
        catch (IOException)
        {
            if (!ReferenceEquals(writer, fallbackWriter))
            {
                try { fallbackWriter.WriteLine(message); } catch { }
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
