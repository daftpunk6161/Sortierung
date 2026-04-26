using System.Diagnostics;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion.ToolInvokers;

public sealed class CisoInvoker(IToolRunner tools) : IToolInvoker
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    public bool CanHandle(ConversionCapability capability)
    {
        ArgumentNullException.ThrowIfNull(capability);
        return string.Equals(capability.Tool.ToolName, "ciso", StringComparison.OrdinalIgnoreCase)
            || string.Equals(capability.SourceExtension, ".cso", StringComparison.OrdinalIgnoreCase);
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
        if (!string.Equals(commandToken, "decompress", StringComparison.OrdinalIgnoreCase))
            return ToolInvokerSupport.InvalidCommand();

        var toolPath = _tools.FindTool("ciso");
        if (string.IsNullOrWhiteSpace(toolPath))
            return ToolInvokerSupport.ToolNotFound("ciso");

        var skipHashConstraintValidation = ToolInvokerSupport.ShouldSkipHashConstraintValidation(_tools);
        var constraintError = ToolInvokerSupport.ValidateToolConstraints(toolPath, capability.Tool, skipHashConstraintValidation);
        if (constraintError is not null)
            return ToolInvokerSupport.ConstraintFailure(constraintError);

        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
            Directory.CreateDirectory(targetDirectory);

        var watch = Stopwatch.StartNew();
        var result = _tools.InvokeProcess(
            toolPath,
            BuildArguments(toolPath, sourcePath, targetPath),
            capability.Tool,
            "ciso",
            ToolInvokerSupport.ResolveToolTimeout("ciso"),
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
            if (!info.Exists || info.Length < 16)
                return VerificationStatus.VerifyFailed;

            if (LooksLikeCsoPayload(targetPath))
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

    private static string[] BuildArguments(string toolPath, string sourcePath, string targetPath)
    {
        var executableName = Path.GetFileName(toolPath);
        if (executableName.Contains("maxcso", StringComparison.OrdinalIgnoreCase))
            return ["--decompress", sourcePath, "-o", targetPath];

        return ["0", sourcePath, targetPath];
    }

    private static bool LooksLikeCsoPayload(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            if (stream.Length < 4)
                return false;

            Span<byte> header = stackalloc byte[4];
            var read = stream.Read(header);
            return read == 4 && header.SequenceEqual("CISO"u8);
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
