using System.Diagnostics;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion.ToolInvokers;

public sealed class EcmInvoker(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return string.Equals(capability.Tool.ToolName, "unecm", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability.SourceExtension, ".ecm", StringComparison.OrdinalIgnoreCase);
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

        var toolPath = _tools.FindTool("unecm");
        if (string.IsNullOrWhiteSpace(toolPath))
            return ToolInvokerSupport.ToolNotFound("unecm");

        var skipHashConstraintValidation = ToolInvokerSupport.ShouldSkipHashConstraintValidation(_tools);
        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool, skipHashConstraintValidation);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(
            toolPath,
            [sourcePath, targetPath],
            capability.Tool,
            "unecm",
            ToolInvokerSupport.ResolveToolTimeout("unecm"),
            cancellationToken);
        watch.Stop();

        return ToolInvokerSupport.FromToolResult(targetPath, result, watch);
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

            // Decompressed output must not still be an ECM payload.
            if (LooksLikeEcmPayload(targetPath))
                return VerificationStatus.VerifyFailed;

            if (string.Equals(capability.TargetExtension, ".iso", StringComparison.OrdinalIgnoreCase)
                && info.Length < 2048)
            {
                return VerificationStatus.VerifyFailed;
            }

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

    private static bool LooksLikeEcmPayload(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 4)
                return false;

            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);
            return read == 4 && header.SequenceEqual("ECM\0"u8);
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
}
