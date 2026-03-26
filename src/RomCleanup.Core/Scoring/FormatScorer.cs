namespace RomCleanup.Core.Scoring;

/// <summary>
/// Format and region scoring for ROM deduplication winner selection.
/// Port of Get-FormatScore, Get-RegionScore, Get-SizeTieBreakScore,
/// Get-HeaderVariantScore from FormatScoring.ps1.
/// </summary>
public static class FormatScorer
{
    private static readonly HashSet<string> DiscExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".bin", ".img", ".cue", ".gdi", ".ccd", ".chd", ".rvz", ".gcz", ".m3u",
        ".wbfs", ".wia", ".wbf1", ".cso", ".pbp", ".nrg", ".mdf", ".mds", ".cdi"
    };

    /// <summary>
    /// Returns the format score for a file extension or set type.
    /// Higher = better for emulator compatibility.
    /// Port of Get-FormatScore from FormatScoring.ps1.
    /// </summary>
    public static int GetFormatScore(string extension, string? type = null)
    {
        // Set types get priority scores
        if (type is not null)
        {
            switch (type.ToUpperInvariant())
            {
                case "M3USET": return 900;
                case "GDISET": return 800;
                case "CUESET": return 800;
                case "CCDSET": return 750;
            }
        }

        var ext = extension.ToLowerInvariant();
        return ext switch
        {
            ".chd" => 850,
            ".m3u" => 800,
            ".gdi" or ".cue" => 790,
            ".ccd" => 780,
            ".iso" => 700,
            ".bin" => 695,
            ".cso" => 680, ".pbp" => 680, ".gcz" => 680, ".rvz" => 680,
            ".wia" => 670,
            ".wbf1" => 660,
            ".wbfs" => 650, ".nsp" => 650, ".xci" => 650, ".3ds" => 650,
            ".dax" => 650, ".jso" => 650, ".zso" => 650,
            ".pkg" => 645,
            ".cia" => 640, ".nsz" => 640, ".xcz" => 640,
            ".nrg" => 620,
            ".mdf" or ".mds" or ".cdi" => 610,
            ".nds" or ".gba" or ".gbc" or ".gb" or ".nes" or ".sfc" or ".smc"
            or ".n64" or ".z64" or ".v64" or ".md" or ".gen" or ".sms"
            or ".gg" or ".pce" or ".fds" or ".32x" or ".a26" or ".a52" or ".a78"
            or ".snes" or ".ngp" or ".ws" => 600,
            ".ecm" => 550,
            ".zip" => 500,
            ".7z" => 480,
            ".rar" => 400,
            _ => 300
        };
    }

    /// <summary>
    /// Returns the region priority score based on user preference order.
    /// Port of Get-RegionScore from FormatScoring.ps1.
    /// </summary>
    public static int GetRegionScore(string region, IReadOnlyList<string> preferOrder)
    {
        var idx = -1;
        for (var i = 0; i < preferOrder.Count; i++)
        {
            if (string.Equals(preferOrder[i], region, StringComparison.OrdinalIgnoreCase))
            {
                idx = i;
                break;
            }
        }

        if (idx >= 0) return 1000 - idx;

        return region.ToUpperInvariant() switch
        {
            "WORLD" => 500,
            "UNKNOWN" => 100,
            _ => 200
        };
    }

    /// <summary>
    /// Returns size tiebreak score: positive for disc (larger = better),
    /// negative for cartridge (smaller = better).
    /// Port of Get-SizeTieBreakScore from FormatScoring.ps1.
    /// </summary>
    public static long GetSizeTieBreakScore(string? type, string? extension, long sizeBytes)
    {
        var ext = extension?.ToLowerInvariant() ?? "";

        if (type is "M3USET" or "GDISET" or "CUESET" or "CCDSET" or "DOSDIR")
            return sizeBytes;

        if (DiscExtensions.Contains(ext))
            return sizeBytes;

        return -1 * sizeBytes;
    }

    /// <summary>
    /// Returns header variant score: headered = +10, headerless = -10.
    /// Port of Get-HeaderVariantScore from FormatScoring.ps1.
    /// </summary>
    public static int GetHeaderVariantScore(string root, string mainPath)
    {
        var hint = $"{root} {mainPath}".ToLowerInvariant();
        if (hint.Contains("headered")) return 10;
        if (hint.Contains("headerless")) return -10;
        return 0;
    }

    /// <summary>
    /// Returns whether the given extension is a disc image format.
    /// </summary>
    public static bool IsDiscExtension(string extension)
        => DiscExtensions.Contains(extension);

    /// <summary>
    /// Returns whether the given extension has a known format score (not the default 300).
    /// Callers can use this to log warnings for unknown formats.
    /// </summary>
    public static bool IsKnownFormat(string extension)
    {
        var ext = extension.ToLowerInvariant();
        return ext switch
        {
            ".chd" or ".iso" or ".cso" or ".pbp" or ".gcz" or ".rvz"
            or ".wia" or ".wbf1" or ".wbfs" or ".nsp" or ".xci" or ".3ds"
            or ".dax" or ".jso" or ".zso" or ".cia" or ".nsz" or ".xcz"
            or ".nrg" or ".mdf" or ".mds" or ".cdi" or ".bin" or ".cue" or ".gdi" or ".ccd" or ".pkg"
            or ".nds" or ".gba" or ".gbc" or ".gb" or ".nes" or ".sfc" or ".smc"
            or ".n64" or ".z64" or ".v64" or ".md" or ".gen" or ".sms"
            or ".gg" or ".pce" or ".fds" or ".32x" or ".a26" or ".a52" or ".a78" or ".snes" or ".ngp" or ".ws"
            or ".ecm" or ".zip" or ".7z" or ".rar" or ".m3u" => true,
            _ => false
        };
    }
}
