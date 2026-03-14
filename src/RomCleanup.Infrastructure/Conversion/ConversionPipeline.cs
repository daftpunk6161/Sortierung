using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Conversion;

/// <summary>
/// Multi-step conversion pipeline orchestrator.
/// Port of ConversionPipeline.ps1 — handles CSO→ISO→CHD chains and disk space validation.
/// </summary>
public sealed class ConversionPipeline
{
    private readonly IToolRunner _tools;
    private readonly IFileSystem _fs;
    private readonly Action<string>? _log;

    public ConversionPipeline(IToolRunner tools, IFileSystem fs, Action<string>? log = null)
    {
        _tools = tools;
        _fs = fs;
        _log = log;
    }

    /// <summary>
    /// Check if disk space is sufficient for a conversion (default 3x multiplier).
    /// </summary>
    public static DiskSpaceCheckResult CheckDiskSpace(string sourcePath, string targetDir, double multiplier = 3.0)
    {
        try
        {
            var fileInfo = new FileInfo(sourcePath);
            if (!fileInfo.Exists)
                return new DiskSpaceCheckResult { Ok = false, Reason = "Source file not found" };

            var required = (long)(fileInfo.Length * multiplier);
            long available;
            var pathRoot = Path.GetPathRoot(targetDir);
            if (pathRoot is not null && pathRoot.StartsWith(@"\\"))
            {
                // UNC path: DriveInfo does not support UNC — use Win32 API via .NET
                try
                {
                    var dirFull = Path.GetFullPath(targetDir);
                    // Use FileInfo on a temp probe — fallback for UNC free space
                    var drive = DriveInfo.GetDrives()
                        .FirstOrDefault(d => dirFull.StartsWith(d.RootDirectory.FullName, StringComparison.OrdinalIgnoreCase));
                    if (drive is null)
                        return new DiskSpaceCheckResult
                        {
                            Ok = true,
                            Reason = "UNC path: free space unknown, proceeding with caution",
                            RequiredBytes = required,
                            AvailableBytes = -1
                        };
                    available = drive.AvailableFreeSpace;
                }
                catch
                {
                    available = long.MaxValue; // UNC space check failed — proceed with warning
                    return new DiskSpaceCheckResult
                    {
                        Ok = true,
                        Reason = "UNC path: free space check failed, proceeding with caution",
                        RequiredBytes = required,
                        AvailableBytes = -1
                    };
                }
            }
            else
            {
                var driveInfo = new DriveInfo(pathRoot!);
                available = driveInfo.AvailableFreeSpace;
            }

            if (available < required)
            {
                return new DiskSpaceCheckResult
                {
                    Ok = false,
                    Reason = $"Insufficient disk space: need {required / 1048576}MB, have {available / 1048576}MB",
                    RequiredBytes = required,
                    AvailableBytes = available
                };
            }

            return new DiskSpaceCheckResult
            {
                Ok = true,
                RequiredBytes = required,
                AvailableBytes = available
            };
        }
        catch (Exception ex)
        {
            return new DiskSpaceCheckResult { Ok = false, Reason = $"Disk space check failed: {ex.Message}" };
        }
    }

    /// <summary>
    /// Build a CSO→ISO→CHD pipeline (ciso decompress + chdman createcd).
    /// </summary>
    public static ConversionPipelineDef BuildCsoToChdPipeline(string sourcePath, string outputDir)
    {
        var baseName = Path.GetFileNameWithoutExtension(sourcePath);
        var tempIso = Path.Combine(outputDir, baseName + ".iso");
        var finalChd = Path.Combine(outputDir, baseName + ".chd");

        return new ConversionPipelineDef
        {
            SourcePath = sourcePath,
            Steps =
            [
                new ConversionPipelineStep
                {
                    Tool = "ciso",
                    Action = "decompress",
                    Input = sourcePath,
                    Output = tempIso,
                    IsTemp = true
                },
                new ConversionPipelineStep
                {
                    Tool = "chdman",
                    Action = "createcd",
                    Input = tempIso,
                    Output = finalChd,
                    IsTemp = false
                }
            ],
            CleanupTemps = true
        };
    }

    /// <summary>
    /// Execute an entire pipeline definition step by step.
    /// </summary>
    public ConversionPipelineResult Execute(
        ConversionPipelineDef pipeline,
        string mode = "DryRun",
        CancellationToken ct = default,
        TimeSpan? pipelineTimeout = null)
    {
        var results = new List<PipelineStepResult>();
        var tempFiles = new List<string>();

        _log?.Invoke($"Pipeline '{pipeline.Id}': {pipeline.Steps.Count} steps for {pipeline.SourcePath}");

        // V2-H04: Pipeline-level timeout (default 60 min) to prevent infinite hangs
        using var timeoutCts = pipelineTimeout.HasValue
            ? CancellationTokenSource.CreateLinkedTokenSource(ct)
            : null;
        timeoutCts?.CancelAfter(pipelineTimeout!.Value);
        var effectiveCt = timeoutCts?.Token ?? ct;

        try
        {
            foreach (var step in pipeline.Steps)
            {
                if (effectiveCt.IsCancellationRequested)
                {
                    results.Add(new PipelineStepResult
                    {
                        Status = "cancelled",
                        Tool = step.Tool,
                        Action = step.Action,
                        Input = step.Input,
                        Output = step.Output,
                        Skipped = true
                    });
                    break;
                }

                if (mode == "DryRun")
                {
                    results.Add(new PipelineStepResult
                    {
                        Status = "dryrun",
                        Tool = step.Tool,
                        Action = step.Action,
                        Input = step.Input,
                        Output = step.Output,
                        Skipped = true
                    });
                    _log?.Invoke($"  DRYRUN: {step.Tool} {step.Action} {Path.GetFileName(step.Input)} -> {Path.GetFileName(step.Output)}");
                    continue;
                }

                // Disk space check for the output directory
                var spaceCheck = CheckDiskSpace(step.Input, Path.GetDirectoryName(step.Output)!);
                if (!spaceCheck.Ok)
                {
                    results.Add(new PipelineStepResult
                    {
                        Status = "error",
                        Tool = step.Tool,
                        Action = step.Action,
                        Input = step.Input,
                        Output = step.Output,
                        Error = spaceCheck.Reason
                    });
                    _log?.Invoke($"  ABORTED: {spaceCheck.Reason}");
                    break;
                }

                // Skip if final output already exists (don't overwrite)
                if (!step.IsTemp && File.Exists(step.Output))
                {
                    results.Add(new PipelineStepResult
                    {
                        Status = "skipped",
                        Tool = step.Tool,
                        Action = step.Action,
                        Input = step.Input,
                        Output = step.Output,
                        Skipped = true,
                        Error = "target-exists"
                    });
                    _log?.Invoke($"  SKIPPED: {Path.GetFileName(step.Output)} already exists");
                    break;
                }

                var stepResult = ExecuteStep(step);
                results.Add(stepResult);

                if (step.IsTemp && stepResult.Status == "ok")
                    tempFiles.Add(step.Output);

                if (stepResult.Status != "ok")
                {
                    _log?.Invoke($"  FAILED: {step.Tool} {step.Action}: {stepResult.Error}");
                    break;
                }

                _log?.Invoke($"  OK: {step.Tool} {step.Action} -> {Path.GetFileName(step.Output)}");
            }
        }
        finally
        {
            // Cleanup temp files (always runs, even on cancellation/exception)
            if (pipeline.CleanupTemps)
            {
                foreach (var tempFile in tempFiles)
                {
                    try { if (File.Exists(tempFile)) File.Delete(tempFile); }
                    catch { /* best effort cleanup */ }
                }
            }
        }

        var status = results.All(r => r.Status is "ok" or "dryrun") ? "completed" : "failed";
        return new ConversionPipelineResult { Status = status, Steps = results };
    }

    private PipelineStepResult ExecuteStep(ConversionPipelineStep step)
    {
        var toolPath = _tools.FindTool(step.Tool);
        if (toolPath is null)
        {
            return new PipelineStepResult
            {
                Status = "error",
                Tool = step.Tool,
                Action = step.Action,
                Input = step.Input,
                Output = step.Output,
                Error = $"Tool '{step.Tool}' not found"
            };
        }

        // Build arguments based on tool and action
        var args = BuildToolArguments(step);
        var result = _tools.InvokeProcess(toolPath, args, $"{step.Tool} {step.Action}");

        return new PipelineStepResult
        {
            Status = result.Success ? "ok" : "error",
            Tool = step.Tool,
            Action = step.Action,
            Input = step.Input,
            Output = step.Output,
            Error = result.Success ? null : $"Exit code {result.ExitCode}: {result.Output}"
        };
    }

    private static string[] BuildToolArguments(ConversionPipelineStep step)
    {
        return step.Tool.ToLowerInvariant() switch
        {
            "ciso" => [step.Action, step.Input, step.Output],
            "chdman" => [step.Action, "-i", step.Input, "-o", step.Output],
            "dolphintool" => ["convert", "-i", step.Input, "-o", step.Output, "-f", "rvz"],
            "7z" => [step.Action, step.Input, step.Output],
            _ => throw new InvalidOperationException($"Unknown conversion tool: '{step.Tool}'")
        };
    }
}
