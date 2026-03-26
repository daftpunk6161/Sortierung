using System.Diagnostics;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Conversion.ToolInvokers;

public sealed class DolphinToolInvoker(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return string.Equals(capability.Tool.ToolName, "dolphintool", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability.TargetExtension, ".rvz", StringComparison.OrdinalIgnoreCase);
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

        var toolPath = _tools.FindTool("dolphintool");
        if (string.IsNullOrWhiteSpace(toolPath))
            return ToolInvokerSupport.ToolNotFound("dolphintool");

        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        var args = new[]
        {
            commandToken,
            "-i", sourcePath,
            "-o", targetPath,
            "-f", "rvz",
            "-c", "zstd",
            "-l", "5",
            "-b", "131072"
        };

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(toolPath, args, "dolphintool");
        watch.Stop();

        return ToolInvokerSupport.FromToolResult(targetPath, result, watch);
    }

    public VerificationStatus Verify(string targetPath, ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        var info = new FileInfo(targetPath);
        if (!info.Exists || info.Length < 4)
            return VerificationStatus.VerifyFailed;

        // TASK-053: Prefer structural verification via `dolphintool verify` when available.
        var toolPath = _tools.FindTool("dolphintool");
        if (!string.IsNullOrWhiteSpace(toolPath))
        {
            try
            {
                var result = _tools.InvokeProcess(toolPath, ["verify", "-i", targetPath], "dolphintool verify");
                if (result.Success)
                    return VerificationStatus.Verified;
                return VerificationStatus.VerifyFailed;
            }
            catch (IOException) { /* fall through to magic byte check */ }
        }

        // Fallback: RVZ magic byte check when dolphintool is not available.
        try
        {
            using var fs = File.OpenRead(targetPath);
            Span<byte> magic = stackalloc byte[4];
            if (fs.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4)
                return VerificationStatus.VerifyFailed;

            return magic[0] == 'R' && magic[1] == 'V' && magic[2] == 'Z' && magic[3] == 0x01
                ? VerificationStatus.Verified
                : VerificationStatus.VerifyFailed;
        }
        catch (IOException)
        {
            return VerificationStatus.VerifyFailed;
        }
    }
}
