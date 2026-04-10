using System.Diagnostics;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion.ToolInvokers;

public sealed class ChdmanInvoker(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return string.Equals(capability.Tool.ToolName, "chdman", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability.TargetExtension, ".chd", StringComparison.OrdinalIgnoreCase);
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
        if (commandToken is null)
            return ToolInvokerSupport.InvalidCommand();

        var toolPath = _tools.FindTool("chdman");
        if (string.IsNullOrWhiteSpace(toolPath))
            return ToolInvokerSupport.ToolNotFound("chdman");

        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        commandToken = ToolInvokerSupport.ResolveEffectiveChdmanCommand(commandToken, sourcePath);

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(
            toolPath,
            [commandToken, "-i", sourcePath, "-o", targetPath],
            capability.Tool,
            "chdman",
            ToolInvokerSupport.ResolveToolTimeout("chdman"),
            cancellationToken);
        watch.Stop();

        return ToolInvokerSupport.FromToolResult(targetPath, result, watch);
    }

    public VerificationStatus Verify(string targetPath, ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        if (!File.Exists(targetPath))
            return VerificationStatus.VerifyFailed;

        var chdmanPath = _tools.FindTool("chdman");
        if (string.IsNullOrWhiteSpace(chdmanPath))
            return VerificationStatus.VerifyNotAvailable;

        var result = _tools.InvokeProcess(chdmanPath, ["verify", "-i", targetPath], "chdman verify");
        return result.Success ? VerificationStatus.Verified : VerificationStatus.VerifyFailed;
    }

}
