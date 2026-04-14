using Romulus.Contracts.Ports;

namespace Romulus.Core.SetParsing;

/// <summary>
/// Resolves Alcohol 120% (.mds) related files: .mdf with same base name.
/// Mirrors Get-MdsRelatedFiles from SetParsing.ps1.
/// </summary>
public static class MdsSetParser
{
    public static IReadOnlyList<string> GetRelatedFiles(string mdsPath, ISetParserIo? io = null)
    {
        var parserIo = SetParserIoResolver.Resolve(io);
        if (string.IsNullOrWhiteSpace(mdsPath) || !parserIo.Exists(mdsPath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(mdsPath) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(mdsPath);
        var mdfPath = Path.GetFullPath(Path.Combine(dir, baseName + ".mdf"));

        return parserIo.Exists(mdfPath) ? new[] { mdfPath } : Array.Empty<string>();
    }

    public static IReadOnlyList<string> GetMissingFiles(string mdsPath, ISetParserIo? io = null)
    {
        var parserIo = SetParserIoResolver.Resolve(io);
        if (string.IsNullOrWhiteSpace(mdsPath))
            return Array.Empty<string>();

        var dir = Path.GetDirectoryName(Path.GetFullPath(mdsPath)) ?? "";
        var baseName = Path.GetFileNameWithoutExtension(mdsPath);
        var mdfPath = Path.GetFullPath(Path.Combine(dir, baseName + ".mdf"));

        return !parserIo.Exists(mdfPath) ? new[] { mdfPath } : Array.Empty<string>();
    }
}
