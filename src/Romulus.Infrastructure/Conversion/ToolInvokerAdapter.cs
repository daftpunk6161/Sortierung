using System.Diagnostics;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion.ToolInvokers;

namespace Romulus.Infrastructure.Conversion;

/// <summary>
/// Executes conversion capabilities through IToolRunner and performs format-specific verification.
/// </summary>
public sealed class ToolInvokerAdapter(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return !string.IsNullOrWhiteSpace(capability.Tool.ToolName);
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
        {
            return new ToolInvocationResult(false, null, -1, null, "source-not-found", 0, VerificationStatus.NotAttempted);
        }

        if (string.IsNullOrWhiteSpace(capability.Command))
        {
            return new ToolInvocationResult(false, null, -1, null, "invalid-command", 0, VerificationStatus.NotAttempted);
        }

        var toolName = capability.Tool.ToolName;
        var toolPath = _tools.FindTool(toolName);
        if (string.IsNullOrWhiteSpace(toolPath))
        {
            return new ToolInvocationResult(false, null, -1, null, $"tool-not-found:{toolName}", 0, VerificationStatus.VerifyNotAvailable);
        }

        var toolConstraintError = ValidateToolConstraints(toolPath, capability.Tool);
        if (toolConstraintError is not null)
        {
            return new ToolInvocationResult(false, null, -1, null, toolConstraintError, 0, VerificationStatus.VerifyNotAvailable);
        }

        var args = BuildArguments(sourcePath, targetPath, capability);
        if (args.Length == 1 && string.Equals(args[0], "__invalid_command__", StringComparison.Ordinal))
        {
            return new ToolInvocationResult(false, null, -1, null, "invalid-command", 0, VerificationStatus.NotAttempted);
        }

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(
            toolPath,
            args,
            toolName,
            ToolInvokerSupport.ResolveToolTimeout(toolName),
            cancellationToken);
        watch.Stop();

        cancellationToken.ThrowIfCancellationRequested();

        if (!result.Success)
        {
            return new ToolInvocationResult(
                false,
                null,
                result.ExitCode,
                result.Output,
                result.Output,
                watch.ElapsedMilliseconds,
                VerificationStatus.NotAttempted);
        }

        return new ToolInvocationResult(
            true,
            targetPath,
            result.ExitCode,
            result.Output,
            null,
            watch.ElapsedMilliseconds,
            VerificationStatus.NotAttempted);
    }

    public VerificationStatus Verify(string targetPath, ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        if (!File.Exists(targetPath))
            return VerificationStatus.VerifyFailed;

        return capability.Verification switch
        {
            VerificationMethod.None => VerificationStatus.NotAttempted,
            VerificationMethod.FileExistenceCheck => new FileInfo(targetPath).Length > 0
                ? VerificationStatus.Verified
                : VerificationStatus.VerifyFailed,
            VerificationMethod.RvzMagicByte => VerifyRvz(targetPath)
                ? VerificationStatus.Verified
                : VerificationStatus.VerifyFailed,
            VerificationMethod.SevenZipTest => VerifySevenZip(targetPath),
            VerificationMethod.ChdmanVerify => VerifyChd(targetPath),
            _ => VerificationStatus.VerifyNotAvailable
        };
    }

    private string[] BuildArguments(string sourcePath, string targetPath, ConversionCapability capability)
    {
        var toolName = capability.Tool.ToolName.ToLowerInvariant();
        var command = ReadSafeCommandToken(capability.Command);

        if (command is null)
            return ["__invalid_command__"];

        if (toolName == "chdman")
        {
            var chdCommand = ToolInvokerSupport.ResolveEffectiveChdmanCommand(command, sourcePath);

            return [chdCommand, "-i", sourcePath, "-o", targetPath];
        }

        if (toolName == "dolphintool")
        {
            return [
                command,
                "-i", sourcePath,
                "-o", targetPath,
                "-f", "rvz",
                "-c", "zstd",
                "-l", "5",
                "-b", "131072"
            ];
        }

        if (toolName == "7z")
        {
            return ["a", "-tzip", "-y", targetPath, sourcePath];
        }

        if (toolName == "psxtract")
        {
            return [command, "-i", sourcePath, "-o", targetPath];
        }

        return [command, "-i", sourcePath, "-o", targetPath];
    }

    private static string? ReadSafeCommandToken(string rawCommand)
        => ToolInvokerSupport.ReadSafeCommandToken(rawCommand);

    private static string? ValidateToolConstraints(string toolPath, ToolRequirement requirement)
        => ToolInvokerSupport.ValidateToolConstraints(toolPath, requirement);

    private VerificationStatus VerifyChd(string targetPath)
    {
        var chdman = _tools.FindTool("chdman");
        if (chdman is null)
            return VerificationStatus.VerifyNotAvailable;

        var result = _tools.InvokeProcess(chdman, ["verify", "-i", targetPath], "chdman verify");
        return result.Success ? VerificationStatus.Verified : VerificationStatus.VerifyFailed;
    }

    private VerificationStatus VerifySevenZip(string targetPath)
    {
        var sevenZip = _tools.FindTool("7z");
        if (sevenZip is null)
            return VerificationStatus.VerifyNotAvailable;

        var result = _tools.InvokeProcess(sevenZip, ["t", "-y", targetPath], "7z verify");
        return result.Success ? VerificationStatus.Verified : VerificationStatus.VerifyFailed;
    }

    private static bool VerifyRvz(string targetPath)
    {
        var info = new FileInfo(targetPath);
        if (!info.Exists || info.Length < 4)
            return false;

        try
        {
            using var fs = File.OpenRead(targetPath);
            Span<byte> magic = stackalloc byte[4];
            if (fs.ReadAtLeast(magic, 4, throwOnEndOfStream: false) < 4)
                return false;

            return magic[0] == 'R' && magic[1] == 'V' && magic[2] == 'Z' && magic[3] == 0x01;
        }
        catch (IOException)
        {
            return false;
        }
    }
}
