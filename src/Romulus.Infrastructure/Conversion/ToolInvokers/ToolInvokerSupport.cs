using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Tools;

namespace Romulus.Infrastructure.Conversion.ToolInvokers;

internal static class ToolInvokerSupport
{
    private const int IsoLogicalSectorSize = 2048;
    private const int IsoPrimaryVolumeDescriptorSector = 16;
    private const int IsoPrimaryVolumeDescriptorLength = 2048;
    private const int IsoRootDirectoryRecordOffset = 156;
    private const int IsoDirectoryRecordExtentOffset = 2;
    private const int IsoDirectoryRecordDataLengthOffset = 10;
    private const int IsoDirectoryRecordFlagsOffset = 25;
    private const int IsoDirectoryRecordNameLengthOffset = 32;
    private const int IsoDirectoryRecordNameOffset = 33;
    private const int MaxRootDirectoryBytes = 256 * 1024;
    private const int MaxSystemCnfBytes = 32 * 1024;
    private static readonly IsoLayout[] IsoLayouts =
    [
        new(IsoLogicalSectorSize, 0),
        new(2352, 16),
        new(2352, 24)
    ];

    public static string? ValidateToolConstraints(
        string toolPath,
        ToolRequirement requirement,
        bool skipExpectedHashValidation = false)
    {
        if (!File.Exists(toolPath))
            return "tool-not-found-on-disk";

        if (!skipExpectedHashValidation && !string.IsNullOrWhiteSpace(requirement.ExpectedHash))
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
        return Convert.ToHexStringLower(bytes);
    }

    internal static bool FixedTimeHashEquals(string actualHash, string expectedHash)
    {
        var actual = Encoding.ASCII.GetBytes(actualHash.ToLowerInvariant());
        var expected = Encoding.ASCII.GetBytes(expectedHash.ToLowerInvariant());
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    internal static bool ShouldSkipHashConstraintValidation(IToolRunner toolRunner)
        => toolRunner is ToolRunnerAdapter;

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
    /// Treat .iso/.bin/.img files as CD images when PS2 SYSTEM.CNF proves BOOT mode,
    /// otherwise fall back to the historical 700 MB size heuristic.
    /// </summary>
    internal static bool IsLikelyCdImage(string sourcePath)
    {
        var ps2CdDetection = TryResolvePs2CdFromSystemCnf(sourcePath);
        if (ps2CdDetection.HasValue)
            return ps2CdDetection.Value;

        return IsCdImageBySizeHeuristic(sourcePath);
    }

    /// <summary>
    /// Reads PS2 SYSTEM.CNF from direct disc images when available.
    /// Returns true for CD-style BOOT, false for DVD-style BOOT2, null when undetectable.
    /// </summary>
    internal static bool? TryResolvePs2CdFromSystemCnf(string sourcePath)
    {
        try
        {
            var ext = Path.GetExtension(sourcePath).ToLowerInvariant();
            if (ext is not (".iso" or ".bin" or ".img"))
                return null;

            using var stream = File.OpenRead(sourcePath);
            if (stream.Length < (IsoPrimaryVolumeDescriptorSector + 1L) * IsoLogicalSectorSize)
                return null;

            foreach (var layout in IsoLayouts)
            {
                if (!TryReadSystemCnf(stream, layout, out var systemCnf))
                    continue;

                var mediaType = TryResolvePs2MediaTypeFromSystemCnf(systemCnf);
                if (mediaType.HasValue)
                    return mediaType.Value == Ps2MediaType.Cd;
            }
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }

        return null;
    }

    /// <summary>
    /// Resolves the effective chdman command for a source path.
    /// Uses createcd for detected PS2 CD images when createdvd was requested.
    /// Falls back to the historical size heuristic when SYSTEM.CNF is unavailable.
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

    private static bool IsCdImageBySizeHeuristic(string sourcePath)
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
            "ciso" => TimeSpan.FromMinutes(20),
            "unecm" => TimeSpan.FromMinutes(10),
            _ => TimeSpan.FromMinutes(15)
        };

    private static bool TryReadSystemCnf(Stream stream, IsoLayout layout, out string systemCnfText)
    {
        systemCnfText = string.Empty;

        var pvd = new byte[IsoPrimaryVolumeDescriptorLength];
        if (!TryReadIsoLogicalBytes(stream, layout, IsoPrimaryVolumeDescriptorSector * IsoLogicalSectorSize, pvd))
            return false;

        if (pvd[0] != 0x01 || !pvd.AsSpan(1, 5).SequenceEqual("CD001"u8))
            return false;

        if (!TryParseDirectoryRecord(
                pvd.AsSpan(IsoRootDirectoryRecordOffset),
                out var rootExtentLba,
                out var rootDirectoryLength,
                out _,
                out _))
        {
            return false;
        }

        if (rootDirectoryLength <= 0)
            return false;

        var boundedRootDirectoryLength = Math.Min(rootDirectoryLength, MaxRootDirectoryBytes);
        var rootDirectory = new byte[boundedRootDirectoryLength];
        if (!TryReadIsoLogicalBytes(stream, layout, rootExtentLba * IsoLogicalSectorSize, rootDirectory))
            return false;

        if (!TryFindDirectoryEntry(rootDirectory, "SYSTEM.CNF", out var systemCnfExtentLba, out var systemCnfLength))
            return false;

        if (systemCnfLength <= 0)
            return false;

        var boundedSystemCnfLength = Math.Min(systemCnfLength, MaxSystemCnfBytes);
        var systemCnfBytes = new byte[boundedSystemCnfLength];
        if (!TryReadIsoLogicalBytes(stream, layout, systemCnfExtentLba * IsoLogicalSectorSize, systemCnfBytes))
            return false;

        systemCnfText = Encoding.ASCII.GetString(systemCnfBytes).Replace('\0', ' ');
        return true;
    }

    private static bool TryFindDirectoryEntry(
        byte[] directoryBytes,
        string expectedName,
        out int extentLba,
        out int dataLength)
    {
        extentLba = 0;
        dataLength = 0;

        var offset = 0;
        while (offset < directoryBytes.Length)
        {
            var recordLength = directoryBytes[offset];
            if (recordLength == 0)
            {
                offset = ((offset / IsoLogicalSectorSize) + 1) * IsoLogicalSectorSize;
                continue;
            }

            if (offset + recordLength > directoryBytes.Length)
                break;

            if (!TryParseDirectoryRecord(
                    directoryBytes.AsSpan(offset, recordLength),
                    out var recordExtentLba,
                    out var recordDataLength,
                    out var isDirectory,
                    out var recordName))
            {
                offset += recordLength;
                continue;
            }

            if (!isDirectory && string.Equals(recordName, expectedName, StringComparison.OrdinalIgnoreCase))
            {
                extentLba = recordExtentLba;
                dataLength = recordDataLength;
                return true;
            }

            offset += recordLength;
        }

        return false;
    }

    private static bool TryParseDirectoryRecord(
        ReadOnlySpan<byte> record,
        out int extentLba,
        out int dataLength,
        out bool isDirectory,
        out string normalizedName)
    {
        extentLba = 0;
        dataLength = 0;
        isDirectory = false;
        normalizedName = string.Empty;

        if (record.Length < IsoDirectoryRecordNameOffset)
            return false;

        var recordLength = record[0];
        if (recordLength == 0 || recordLength > record.Length)
            return false;

        var nameLength = record[IsoDirectoryRecordNameLengthOffset];
        if (nameLength == 0 || IsoDirectoryRecordNameOffset + nameLength > recordLength)
            return false;

        extentLba = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(IsoDirectoryRecordExtentOffset, 4));
        dataLength = BinaryPrimitives.ReadInt32LittleEndian(record.Slice(IsoDirectoryRecordDataLengthOffset, 4));
        isDirectory = (record[IsoDirectoryRecordFlagsOffset] & 0x02) != 0;

        var rawName = record.Slice(IsoDirectoryRecordNameOffset, nameLength);
        if (rawName.Length == 1 && (rawName[0] == 0x00 || rawName[0] == 0x01))
            return true;

        normalizedName = NormalizeIsoFileIdentifier(Encoding.ASCII.GetString(rawName));
        return normalizedName.Length > 0;
    }

    private static string NormalizeIsoFileIdentifier(string rawName)
    {
        var normalized = rawName.Trim();
        var versionSeparator = normalized.IndexOf(';');
        if (versionSeparator >= 0)
            normalized = normalized[..versionSeparator];
        return normalized.TrimEnd('.');
    }

    private static Ps2MediaType? TryResolvePs2MediaTypeFromSystemCnf(string systemCnfText)
    {
        foreach (var rawLine in systemCnfText.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries))
        {
            var line = rawLine.TrimStart();
            if (StartsWithSystemCnfKey(line, "BOOT2"))
                return Ps2MediaType.Dvd;
            if (StartsWithSystemCnfKey(line, "BOOT"))
                return Ps2MediaType.Cd;
        }

        return null;
    }

    private static bool StartsWithSystemCnfKey(string line, string key)
    {
        if (!line.StartsWith(key, StringComparison.OrdinalIgnoreCase))
            return false;

        if (line.Length == key.Length)
            return true;

        var next = line[key.Length];
        return char.IsWhiteSpace(next) || next == '=';
    }

    private static bool TryReadIsoLogicalBytes(Stream stream, IsoLayout layout, int logicalOffset, Span<byte> destination)
    {
        var remaining = destination.Length;
        var written = 0;

        while (remaining > 0)
        {
            var sectorIndex = logicalOffset / IsoLogicalSectorSize;
            var sectorOffset = logicalOffset % IsoLogicalSectorSize;
            var bytesThisSector = Math.Min(IsoLogicalSectorSize - sectorOffset, remaining);
            var rawOffset = ((long)sectorIndex * layout.RawSectorSize) + layout.DataOffset + sectorOffset;

            if (rawOffset < 0 || rawOffset + bytesThisSector > stream.Length)
                return false;

            stream.Position = rawOffset;
            if (!TryReadExact(stream, destination.Slice(written, bytesThisSector)))
                return false;

            logicalOffset += bytesThisSector;
            written += bytesThisSector;
            remaining -= bytesThisSector;
        }

        return true;
    }

    private static bool TryReadExact(Stream stream, Span<byte> destination)
    {
        var read = 0;
        while (read < destination.Length)
        {
            var chunk = stream.Read(destination[read..]);
            if (chunk <= 0)
                return false;

            read += chunk;
        }

        return true;
    }

    private readonly record struct IsoLayout(int RawSectorSize, int DataOffset);

    private enum Ps2MediaType
    {
        Cd,
        Dvd
    }
}
