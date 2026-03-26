using System.Diagnostics;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Conversion;

/// <summary>
/// Executes planned conversion steps and emits enriched conversion results.
/// </summary>
public sealed class ConversionExecutor(IEnumerable<IToolInvoker> invokers) : IConversionExecutor
{
    private readonly IReadOnlyList<IToolInvoker> _invokers = (invokers ?? throw new ArgumentNullException(nameof(invokers))).ToArray();

    public ConversionResult Execute(
        ConversionPlan plan,
        Action<ConversionStep, ConversionStepResult>? onStepComplete = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(plan);

        var totalWatch = Stopwatch.StartNew();

        if (!File.Exists(plan.SourcePath))
        {
            return BuildResult(
                plan,
                null,
                ConversionOutcome.Error,
                "source-not-found",
                -1,
                VerificationStatus.NotAttempted,
                totalWatch.ElapsedMilliseconds);
        }

        if (plan.Safety == ConversionSafety.Blocked)
        {
            return BuildResult(
                plan,
                null,
                ConversionOutcome.Blocked,
                plan.SkipReason ?? "plan-blocked",
                0,
                VerificationStatus.NotAttempted,
                totalWatch.ElapsedMilliseconds);
        }

        if (!plan.IsExecutable)
        {
            return BuildResult(
                plan,
                null,
                ConversionOutcome.Skipped,
                plan.SkipReason ?? "plan-not-executable",
                0,
                VerificationStatus.NotAttempted,
                totalWatch.ElapsedMilliseconds);
        }

        var sourceDirectory = Path.GetDirectoryName(plan.SourcePath);
        if (string.IsNullOrWhiteSpace(sourceDirectory))
        {
            return BuildResult(
                plan,
                null,
                ConversionOutcome.Error,
                "invalid-source-directory",
                -1,
                VerificationStatus.NotAttempted,
                totalWatch.ElapsedMilliseconds);
        }

        var fullSourceDirectory = Path.GetFullPath(sourceDirectory);
        var baseName = Path.GetFileNameWithoutExtension(plan.SourcePath);
        var currentInputPath = plan.SourcePath;
        var intermediateArtifacts = new List<string>();
        var exitCode = 0;
        var finalVerification = VerificationStatus.NotAttempted;

        var steps = plan.Steps.OrderBy(s => s.Order).ToArray();
        if (!HasContiguousStepOrder(steps))
        {
            return BuildResult(
                plan,
                null,
                ConversionOutcome.Error,
                "invalid-step-order",
                -1,
                VerificationStatus.NotAttempted,
                totalWatch.ElapsedMilliseconds);
        }

        try
        {
            foreach (var step in steps)
            {
                try
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var outputPath = BuildOutputPath(fullSourceDirectory, baseName, step);
                    if (File.Exists(outputPath))
                    {
                        CleanupArtifacts(intermediateArtifacts);
                        return BuildResult(
                            plan,
                            null,
                            ConversionOutcome.Skipped,
                            "target-exists",
                            0,
                            VerificationStatus.NotAttempted,
                            totalWatch.ElapsedMilliseconds);
                    }

                    var invoker = _invokers.FirstOrDefault(i => i.CanHandle(step.Capability));
                    if (invoker is null)
                    {
                        CleanupArtifacts(intermediateArtifacts);
                        return BuildResult(
                            plan,
                            null,
                            ConversionOutcome.Error,
                            $"invoker-not-found:{step.Capability.Tool.ToolName}",
                            -1,
                            VerificationStatus.VerifyNotAvailable,
                            totalWatch.ElapsedMilliseconds);
                    }

                    // SEC-CONV-05: Register intermediate artifact BEFORE invocation so that
                    // cancellation between Invoke and registration still gets cleaned up.
                    if (step.IsIntermediate)
                        intermediateArtifacts.Add(outputPath);

                    var invokeResult = invoker.Invoke(currentInputPath, outputPath, step.Capability, cancellationToken);
                    exitCode = invokeResult.ExitCode;

                    var verifyStatus = invokeResult.Verification == VerificationStatus.NotAttempted
                        ? invoker.Verify(outputPath, step.Capability)
                        : invokeResult.Verification;

                    var stepResult = new ConversionStepResult(
                        step.Order,
                        outputPath,
                        invokeResult.Success,
                        verifyStatus,
                        invokeResult.Success ? null : invokeResult.StdErr,
                        invokeResult.DurationMs);
                    onStepComplete?.Invoke(step, stepResult);

                    if (!invokeResult.Success)
                    {
                        CleanupArtifacts(intermediateArtifacts);
                        CleanupPath(outputPath);
                        return BuildResult(
                            plan,
                            null,
                            ConversionOutcome.Error,
                            "conversion-step-failed",
                            exitCode,
                            verifyStatus,
                            totalWatch.ElapsedMilliseconds);
                    }

                    if (verifyStatus == VerificationStatus.VerifyFailed)
                    {
                        CleanupArtifacts(intermediateArtifacts);
                        CleanupPath(outputPath);
                        return BuildResult(
                            plan,
                            null,
                            ConversionOutcome.Error,
                            "verification-failed",
                            exitCode,
                            verifyStatus,
                            totalWatch.ElapsedMilliseconds);
                    }

                    finalVerification = verifyStatus;
                    currentInputPath = outputPath;
                }
                catch (InvalidOperationException ex)
                {
                    CleanupArtifacts(intermediateArtifacts);
                    return BuildResult(
                        plan,
                        null,
                        ConversionOutcome.Error,
                        ex.Message,
                        -1,
                        VerificationStatus.NotAttempted,
                        totalWatch.ElapsedMilliseconds);
                }
            }

            return BuildResult(
                plan,
                currentInputPath,
                ConversionOutcome.Success,
                null,
                exitCode,
                finalVerification,
                totalWatch.ElapsedMilliseconds);
        }
        finally
        {
            CleanupArtifacts(intermediateArtifacts);
            totalWatch.Stop();
        }
    }

    private static string BuildOutputPath(string sourceDirectory, string baseName, ConversionStep step)
    {
        var extension = step.OutputExtension ?? string.Empty;
        if (!IsSafeExtension(extension))
            throw new InvalidOperationException("invalid-output-extension");

        var fileName = step.IsIntermediate
            ? $"{baseName}.tmp.step{step.Order + 1}{extension}"
            : baseName + extension;

        var combined = Path.GetFullPath(Path.Combine(sourceDirectory, fileName));
        if (!combined.StartsWith(sourceDirectory, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("output-path-outside-source-root");

        return combined;
    }

    private static bool HasContiguousStepOrder(IReadOnlyList<ConversionStep> steps)
    {
        for (var i = 0; i < steps.Count; i++)
        {
            if (steps[i].Order != i)
                return false;
        }

        return true;
    }

    private static bool IsSafeExtension(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension) || !extension.StartsWith(".", StringComparison.Ordinal))
            return false;

        if (extension.IndexOfAny(['/', '\\']) >= 0)
            return false;

        for (var i = 1; i < extension.Length; i++)
        {
            var ch = extension[i];
            if (!(char.IsLetterOrDigit(ch) || ch is '.' or '-' or '_'))
                return false;
        }

        return true;
    }

    private static void CleanupArtifacts(IEnumerable<string> artifacts)
    {
        foreach (var artifact in artifacts)
            CleanupPath(artifact);
    }

    private static void CleanupPath(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // Best effort cleanup only.
        }
    }

    private static ConversionResult BuildResult(
        ConversionPlan plan,
        string? targetPath,
        ConversionOutcome outcome,
        string? reason,
        int exitCode,
        VerificationStatus verification,
        long durationMs)
    {
        return new ConversionResult(plan.SourcePath, targetPath, outcome, reason, exitCode)
        {
            Plan = plan,
            SourceIntegrity = plan.SourceIntegrity,
            Safety = plan.Safety,
            VerificationResult = verification,
            DurationMs = durationMs
        };
    }
}
