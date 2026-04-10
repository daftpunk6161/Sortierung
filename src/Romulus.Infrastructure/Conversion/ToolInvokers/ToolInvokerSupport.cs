using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Conversion.ToolInvokers;

internal static class ToolInvokerSupport
{
    public static string? ValidateToolConstraints(string toolPath, ToolRequirement requirement)
    {
        if (!File.Exists(toolPath))
            return "tool-not-found-on-disk";

        if (!string.IsNullOrWhiteSpace(requirement.ExpectedHash))
        {
            var actualHash = ComputeSha256(toolPath);
            if (!FixedTimeHashEquals(actualHash, requirement.ExpectedHash))
                return "tool-hash-mismatch";
        }

        if (!string.IsNullOrWhiteSpace(requirement.MinVersion))
        {
            var actualVersion = TryReadFileVersion(toolPath);
            if (actualVersion is null)
                return "tool-version-unavailable";

            if (!System.Version.TryParse(requirement.MinVersion, out var requiredVersion))
                return "tool-minversion-invalid";

            if (actualVersion < requiredVersion)
                return "tool-version-too-old";
        }

        return null;
    }

    public static string? ReadSafeCommandToken(string rawCommand)
    {
        if (string.IsNullOrWhiteSpace(rawCommand))
            return null;

        var token = rawCommand.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0];
        if (token.IndexOfAny(['/', '\\']) >= 0)
            return null;

        return token;
    }

    public static ToolInvocationResult SourceNotFound()
        => new(false, null, -1, null, "source-not-found", 0, VerificationStatus.NotAttempted);

    public static ToolInvocationResult InvalidCommand()
        => new(false, null, -1, null, "invalid-command", 0, VerificationStatus.NotAttempted);

    public static ToolInvocationResult ToolNotFound(string toolName)
        => new(false, null, -1, null, $"tool-not-found:{toolName}", 0, VerificationStatus.VerifyNotAvailable);

    public static ToolInvocationResult ConstraintFailure(string error)
        => new(false, null, -1, null, error, 0, VerificationStatus.VerifyNotAvailable);

    public static ToolInvocationResult FromToolResult(string targetPath, ToolResult result, Stopwatch watch)
    {
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

        return new ToolInvocationResult(
            true,
            targetPath,
            result.ExitCode,
            result.Output,
            null,
            watch.ElapsedMilliseconds,
            VerificationStatus.NotAttempted);
    }

    private static string ComputeSha256(string filePath)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(filePath);
        var bytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    internal static bool FixedTimeHashEquals(string actualHash, string expectedHash)
    {
        var actual = Encoding.ASCII.GetBytes(actualHash.ToLowerInvariant());
        var expected = Encoding.ASCII.GetBytes(expectedHash.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static System.Version? TryReadFileVersion(string toolPath)
    {
        var versionInfo = FileVersionInfo.GetVersionInfo(toolPath);

        if (System.Version.TryParse(versionInfo.FileVersion, out var fileVersion))
            return fileVersion;

        if (System.Version.TryParse(versionInfo.ProductVersion, out var productVersion))
            return productVersion;

        return null;
    }

    /// <summary>
    /// CD image threshold: files below 700 MB are treated as CD rather than DVD.
    /// Delegates to the canonical Contracts constant.
    /// </summary>
    internal const long CdImageThresholdBytes = Romulus.Contracts.Models.ConversionThresholds.CdImageThresholdBytes;

    /// <summary>
    /// Heuristic: treat .iso/.bin/.img files below 700 MB as CD images rather than DVD.
    /// Used by chdman (createcd vs createdvd) and general conversion decisions.
    /// </summary>
    internal static bool IsLikelyCdImage(string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext is not (".iso" or ".bin" or ".img"))
                return false;

            var size = new FileInfo(sourcePath).Length;
            return size > 0 && size < CdImageThresholdBytes;
        }
        catch (IOException)
        {
            return false;
        }
    }

    /// <summary>
    /// Resolves the effective chdman command for a source path.
    /// Uses createcd for likely CD images when createdvd was requested.
    /// </summary>
    internal static string ResolveEffectiveChdmanCommand(string requestedCommand, string sourcePath)
    {
        if (string.Equals(requestedCommand, "createdvd", StringComparison.OrdinalIgnoreCase)
            && IsLikelyCdImage(sourcePath))
        {
            return "createcd";
        }

        return requestedCommand;
    }

    /// <summary>
    /// Returns the execution timeout for a tool invocation.
    /// </summary>
    internal static TimeSpan ResolveToolTimeout(string toolName)
        => toolName.ToLowerInvariant() switch
        {
            "chdman" => TimeSpan.FromMinutes(30),
            "7z" => TimeSpan.FromMinutes(10),
            "dolphintool" => TimeSpan.FromMinutes(20),
            "psxtract" => TimeSpan.FromMinutes(20),
            "nkit" => TimeSpan.FromMinutes(30),
            "unecm" => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(15)
        };
}
