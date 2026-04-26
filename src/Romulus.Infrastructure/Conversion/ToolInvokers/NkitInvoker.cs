using System.Diagnostics;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion.ToolInvokers;

public sealed class NkitInvoker(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return string.Equals(capability.Tool.ToolName, "nkit", StringComparison.OrdinalIgnoreCase)
            || capability.SourceExtension.StartsWith(".nkit.", StringComparison.OrdinalIgnoreCase);
    }

    public ToolInvocationResult Invoke(
        string sourcePath,
        string targetPath,
        ConversionCapability capability,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourcePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        cancellationToken.ThrowIfCancellationRequested();

        if (!File.Exists(sourcePath))
            return ToolInvokerSupport.SourceNotFound();

        var commandToken = ToolInvokerSupport.ReadSafeCommandToken(capability.Command);
        if (!string.Equals(commandToken, "expand", StringComparison.OrdinalIgnoreCase))
            return ToolInvokerSupport.InvalidCommand();

        var toolPath = _tools.FindTool("nkit");
        if (string.IsNullOrWhiteSpace(toolPath))
            return ToolInvokerSupport.ToolNotFound("nkit");

        var skipHashConstraintValidation = ToolInvokerSupport.ShouldSkipHashConstraintValidation(_tools);
        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool, skipHashConstraintValidation);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return new ToolInvocationResult(false, targetPath, -1, null, "invalid-target-directory", 0, VerificationStatus.NotAttempted);

        Directory.CreateDirectory(targetDirectory);
        var stagingDirectory = Path.Combine(targetDirectory, $".romulus-nkit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            var watch = Stopwatch.StartNew();
            var result = _tools.InvokeProcess(
                toolPath,
                ["-task", "expand", "-verify", "y", "-in", sourcePath, "-out", stagingDirectory],
                capability.Tool,
                "nkit",
                ToolInvokerSupport.ResolveToolTimeout("nkit"),
                cancellationToken);
            watch.Stop();

            if (!result.Success)
                return new ToolInvocationResult(false, targetPath, result.ExitCode, result.Output, result.Output, watch.ElapsedMilliseconds, VerificationStatus.NotAttempted);

            if (!TryPromoteExpandedOutput(sourcePath, stagingDirectory, targetPath, out var failureReason))
                return new ToolInvocationResult(false, targetPath, result.ExitCode, result.Output, failureReason, watch.ElapsedMilliseconds, VerificationStatus.VerifyFailed);

            return new ToolInvocationResult(true, targetPath, result.ExitCode, result.Output, null, watch.ElapsedMilliseconds, VerificationStatus.Verified);
        }
        finally
        {
            TryDeleteDirectory(stagingDirectory);
        }
    }

    public VerificationStatus Verify(string targetPath, ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        try
        {
            var info = new FileInfo(targetPath);
            if (!info.Exists || info.Length <= 0)
                return VerificationStatus.VerifyFailed;

            // Expanded output must not still carry an NKit marker header.
            if (LooksLikeNkitPayload(targetPath))
                return VerificationStatus.VerifyFailed;

            return VerificationStatus.Verified;
        }
        catch (IOException)
        {
            return VerificationStatus.VerifyFailed;
        }
        catch (UnauthorizedAccessException)
        {
            return VerificationStatus.VerifyFailed;
        }
    }

    private static bool LooksLikeNkitPayload(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 4)
                return false;

            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);
            if (read != 4)
                return false;

            return header[0] == (byte)'N'
                   && header[1] == (byte)'K'
                   && header[2] == (byte)'I'
                   && header[3] == (byte)'T';
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool TryPromoteExpandedOutput(
        string sourcePath,
        string stagingDirectory,
        string targetPath,
        out string failureReason)
    {
        failureReason = "nkit-output-not-found";

        var candidates = EnumerateExpectedOutputCandidates(sourcePath, stagingDirectory)
            .Where(File.Exists)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (candidates.Length == 0)
        {
            candidates = Directory.GetFiles(stagingDirectory, "*.iso", SearchOption.TopDirectoryOnly)
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (candidates.Length != 1)
        {
            failureReason = candidates.Length == 0
                ? "nkit-output-not-found"
                : "nkit-output-ambiguous";
            return false;
        }

        var sourceOutput = candidates[0];
        try
        {
            if (File.Exists(targetPath))
            {
                failureReason = "target-exists";
                return false;
            }

            var targetDirectory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(targetDirectory))
                Directory.CreateDirectory(targetDirectory);

            File.Move(sourceOutput, targetPath);
            failureReason = string.Empty;
            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            failureReason = "nkit-output-promote-failed";
            return false;
        }
    }

    private static IEnumerable<string> EnumerateExpectedOutputCandidates(string sourcePath, string stagingDirectory)
    {
        var fileName = Path.GetFileName(sourcePath);
        if (fileName.EndsWith(".nkit.iso", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(stagingDirectory, fileName[..^".nkit.iso".Length] + ".iso");
            yield return Path.Combine(stagingDirectory, fileName[..^".nkit.iso".Length] + ".dec.iso");
            yield break;
        }

        if (fileName.EndsWith(".nkit.gcz", StringComparison.OrdinalIgnoreCase))
        {
            yield return Path.Combine(stagingDirectory, fileName[..^".nkit.gcz".Length] + ".iso");
            yield return Path.Combine(stagingDirectory, fileName[..^".nkit.gcz".Length] + ".dec.iso");
            yield break;
        }

        yield return Path.Combine(stagingDirectory, Path.GetFileNameWithoutExtension(sourcePath) + ".iso");
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
