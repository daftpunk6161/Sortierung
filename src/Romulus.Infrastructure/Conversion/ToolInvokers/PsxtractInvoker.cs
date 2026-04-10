using System.Diagnostics;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion.ToolInvokers;

public sealed class PsxtractInvoker(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return string.Equals(capability.Tool.ToolName, "psxtract", StringComparison.OrdinalIgnoreCase);
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

        var toolPath = _tools.FindTool("psxtract");
        if (string.IsNullOrWhiteSpace(toolPath))
            return ToolInvokerSupport.ToolNotFound("psxtract");

        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(
            toolPath,
            [commandToken, "-i", sourcePath, "-o", targetPath],
            capability.Tool,
            "psxtract",
            ToolInvokerSupport.ResolveToolTimeout("psxtract"),
            cancellationToken);
        watch.Stop();

        if (!result.Success)
        {
            return new ToolInvocationResult(
                false,
                targetPath,
                result.ExitCode,
                result.Output,
                result.Output,
                watch.ElapsedMilliseconds,
                VerificationStatus.NotAttempted);
        }

        return ToolInvokerSupport.FromToolResult(targetPath, result, watch);
    }

    public VerificationStatus Verify(string targetPath, ConversionCapability capability)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(targetPath);
        ArgumentNullException.ThrowIfNull(capability);

        if (!File.Exists(targetPath))
            return VerificationStatus.VerifyFailed;

        // Psxtract can emit ISO-like output depending on command/profile.
        // Verify by non-empty file and, when available, ISO9660 marker at sector 16.
        try
        {
            using var stream = File.OpenRead(targetPath);
            if (stream.Length <= 0)
                return VerificationStatus.VerifyFailed;

            const long isoMagicOffset = 0x8001;
            const int isoMagicLength = 5;

            if (stream.Length >= isoMagicOffset + isoMagicLength)
            {
                stream.Seek(isoMagicOffset, SeekOrigin.Begin);
                Span<byte> isoMagic = stackalloc byte[isoMagicLength];
                var read = stream.ReadAtLeast(isoMagic, isoMagicLength, throwOnEndOfStream: false);
                if (read == isoMagicLength)
                {
                    var hasIso9660Magic = isoMagic[0] == (byte)'C'
                        && isoMagic[1] == (byte)'D'
                        && isoMagic[2] == (byte)'0'
                        && isoMagic[3] == (byte)'0'
                        && isoMagic[4] == (byte)'1';

                    if (hasIso9660Magic)
                        return VerificationStatus.Verified;
                }
            }

            return VerificationStatus.Verified;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return VerificationStatus.VerifyFailed;
        }
    }
}
