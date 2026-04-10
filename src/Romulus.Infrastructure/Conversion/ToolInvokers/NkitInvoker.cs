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

        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (string.IsNullOrWhiteSpace(targetDirectory))
            return new ToolInvocationResult(false, targetPath, -1, null, "invalid-target-directory", 0, VerificationStatus.NotAttempted);

        Directory.CreateDirectory(targetDirectory);

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(
            toolPath,
            ["-task", "expand", "-verify", "y", "-in", sourcePath, "-out", targetDirectory],
            capability.Tool,
            "nkit",
            ToolInvokerSupport.ResolveToolTimeout("nkit"),
            cancellationToken);
        watch.Stop();

        if (!result.Success)
            return new ToolInvocationResult(false, targetPath, result.ExitCode, result.Output, result.Output, watch.ElapsedMilliseconds, VerificationStatus.NotAttempted);

        if (!File.Exists(targetPath))
            return new ToolInvocationResult(false, targetPath, result.ExitCode, result.Output, "nkit-output-not-found", watch.ElapsedMilliseconds, VerificationStatus.VerifyFailed);

        return new ToolInvocationResult(true, targetPath, result.ExitCode, result.Output, null, watch.ElapsedMilliseconds, VerificationStatus.Verified);
    }

    public VerificationStatus Verify(string targetPath, ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        try
        {
            var info = new FileInfo(targetPath);
            return info.Exists && info.Length > 0
                ? VerificationStatus.Verified
                : VerificationStatus.VerifyFailed;
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
}
