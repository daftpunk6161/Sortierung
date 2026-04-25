using System.Text.RegularExpressions;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Extracts the data SHA1 from CHD files by invoking chdman info and parsing its output.
/// The data SHA1 is the SHA1 of the actual data sectors inside the CHD, useful for
/// matching No-Intro DAT entries that store the inner data hash rather than the
/// container file hash.
/// </summary>
public sealed partial class ChdTrackHashExtractor(IToolRunner tools) : IChdTrackHashExtractor
{
    private readonly IToolRunner _tools = tools ?? throw new ArgumentNullException(nameof(tools));

    // Matches "Data sha1:   <40 hex chars>" (case-insensitive, leading whitespace/colons tolerated)
    [GeneratedRegex(@"data\s+sha1\s*:\s*([0-9a-f]{40})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex DataSha1Pattern();

    public string? ExtractDataSha1(
        string chdPath,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(chdPath) || !File.Exists(chdPath))
            return null;

        cancellationToken.ThrowIfCancellationRequested();

        var chdmanPath = _tools.FindTool("chdman");
        if (string.IsNullOrWhiteSpace(chdmanPath))
            return null;

        var result = _tools.InvokeProcess(
            chdmanPath,
            ["info", "-i", chdPath],
            errorLabel: "chdman-info",
            timeout: timeout ?? TimeSpan.FromMinutes(2),
            cancellationToken: cancellationToken);
        if (!result.Success || string.IsNullOrWhiteSpace(result.Output))
            return null;

        var match = DataSha1Pattern().Match(result.Output);
        return match.Success ? match.Groups[1].Value.ToLowerInvariant() : null;
    }
}
