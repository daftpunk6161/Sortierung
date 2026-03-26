using System.Diagnostics;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Conversion.ToolInvokers;

public sealed class SevenZipInvoker(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return string.Equals(capability.Tool.ToolName, "7z", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability.TargetExtension, ".zip", StringComparison.OrdinalIgnoreCase);
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

        var toolPath = _tools.FindTool("7z");
        if (string.IsNullOrWhiteSpace(toolPath))
            return ToolInvokerSupport.ToolNotFound("7z");

        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(toolPath, ["a", "-tzip", "-y", targetPath, sourcePath], "7z");
        watch.Stop();

        return ToolInvokerSupport.FromToolResult(targetPath, result, watch);
    }

    public VerificationStatus Verify(string targetPath, ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        if (!File.Exists(targetPath))
            return VerificationStatus.VerifyFailed;

        var sevenZipPath = _tools.FindTool("7z");
        if (string.IsNullOrWhiteSpace(sevenZipPath))
            return VerificationStatus.VerifyNotAvailable;

        var result = _tools.InvokeProcess(sevenZipPath, ["t", "-y", targetPath], "7z verify");
        return result.Success ? VerificationStatus.Verified : VerificationStatus.VerifyFailed;
    }
}
