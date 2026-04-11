using Romulus.Contracts;

namespace Romulus.Core.Scoring;

/// <summary>
/// Format and region scoring for ROM deduplication winner selection.
/// Port of Get-FormatScore, Get-RegionScore, Get-SizeTieBreakScore,
/// Get-HeaderVariantScore from FormatScoring.ps1.
/// </summary>
public static class FormatScorer
{
    private const int DefaultUnknownFormatScore = 300;

    private sealed record ScoreState(
        IReadOnlyDictionary<string, int> FormatScores,
        IReadOnlyDictionary<string, int> SetTypeScores,
        IReadOnlySet<string> DiscExtensions);

    private static readonly object Sync = new();
    private static volatile ScoreState? _registeredState;
    private static readonly object RegionScoreCacheSync = new();
    private static readonly Dictionary<string, IReadOnlyDictionary<string, int>> RegionScoreCache = new(StringComparer.Ordinal);
    private static Func<(
        IReadOnlyDictionary<string, int> FormatScores,
        IReadOnlyDictionary<string, int> SetTypeScores,
        IReadOnlyCollection<string> DiscExtensions)>? _scoreFactory;

    // Fallback scores preserve current behavior when no external profile is registered.
    private static readonly IReadOnlyDictionary<string, int> FallbackFormatScores =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            [".chd"] = 850,
            [".m3u"] = 800,
            [".gdi"] = 790,
            [".cue"] = 790,
            [".ccd"] = 780,
            [".iso"] = 700,
            [".bin"] = 695,
            [".cso"] = 680,
            [".pbp"] = 680,
            [".gcz"] = 680,
            [".rvz"] = 680,
            [".wia"] = 670,
            [".wbf1"] = 660,
            [".wbfs"] = 650,
            [".nsp"] = 650,
            [".xci"] = 650,
            [".3ds"] = 650,
            [".wud"] = 650,
            [".wux"] = 650,
            [".dax"] = 650,
            [".jso"] = 650,
            [".zso"] = 650,
            [".rpx"] = 645,
            [".pkg"] = 645,
            [".cia"] = 640,
            [".nsz"] = 640,
            [".xcz"] = 640,
            [".nrg"] = 620,
            [".mdf"] = 610,
            [".mds"] = 610,
            [".cdi"] = 610,
            [".nds"] = 600,
            [".gba"] = 600,
            [".gbc"] = 600,
            [".gb"] = 600,
            [".nes"] = 600,
            [".sfc"] = 600,
            [".smc"] = 600,
            [".n64"] = 600,
            [".z64"] = 600,
            [".v64"] = 600,
            [".md"] = 600,
            [".gen"] = 600,
            [".sms"] = 600,
            [".gg"] = 600,
            [".pce"] = 600,
            [".fds"] = 600,
            [".32x"] = 600,
            [".a26"] = 600,
            [".a52"] = 600,
            [".a78"] = 600,
            [".lnx"] = 600,
            [".jag"] = 600,
            [".snes"] = 600,
            [".ngp"] = 600,
            [".ws"] = 600,
            [".wsc"] = 600,
            [".vb"] = 600,
            [".ndd"] = 600,
            [".dsi"] = 600,
            [".wad"] = 600,
            [".bs"] = 600,
            [".sg"] = 600,
            [".sc"] = 600,
            [".sgx"] = 600,
            [".pcfx"] = 600,
            [".j64"] = 600,
            [".st"] = 600,
            [".stx"] = 600,
            [".atr"] = 600,
            [".xex"] = 600,
            [".xfd"] = 600,
            [".col"] = 600,
            [".int"] = 600,
            [".o2"] = 600,
            [".vec"] = 600,
            [".min"] = 600,
            [".tgc"] = 600,
            [".tzx"] = 600,
            [".adf"] = 600,
            [".d64"] = 600,
            [".t64"] = 600,
            [".mx1"] = 600,
            [".mx2"] = 600,
            [".vpk"] = 600,
            [".app"] = 600,
            [".gpe"] = 600,
            [".st2"] = 600,
            [".p00"] = 600,
            [".prc"] = 600,
            [".pdb"] = 600,
            [".dmg"] = 600,
            [".gxb"] = 600,
            [".img"] = 600,
            [".ecm"] = 550,
            [".zip"] = 500,
            [".7z"] = 480,
            [".rar"] = 400
        };

    private static readonly IReadOnlyDictionary<string, int> FallbackSetTypeScores =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["M3USET"] = 900,
            ["GDISET"] = 800,
            ["CUESET"] = 800,
            ["CCDSET"] = 750
        };

    private static readonly IReadOnlySet<string> FallbackDiscExtensions =
        new HashSet<string>(DiscFormats.AllDiscExtensions, StringComparer.OrdinalIgnoreCase);

    public static void RegisterScoreFactory(Func<(
        IReadOnlyDictionary<string, int> FormatScores,
        IReadOnlyDictionary<string, int> SetTypeScores,
        IReadOnlyCollection<string> DiscExtensions)> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        lock (Sync)
        {
            _scoreFactory = factory;
            _registeredState = null;
        }
    }

    public static void RegisterScoreProfile(
        IReadOnlyDictionary<string, int> formatScores,
        IReadOnlyDictionary<string, int> setTypeScores,
        IReadOnlyCollection<string> discExtensions)
    {
        ArgumentNullException.ThrowIfNull(formatScores);
        ArgumentNullException.ThrowIfNull(setTypeScores);
        ArgumentNullException.ThrowIfNull(discExtensions);

        lock (Sync)
        {
            _registeredState = new ScoreState(
                new Dictionary<string, int>(formatScores, StringComparer.OrdinalIgnoreCase),
                new Dictionary<string, int>(setTypeScores, StringComparer.OrdinalIgnoreCase),
                new HashSet<string>(discExtensions, StringComparer.OrdinalIgnoreCase));
        }
    }

    private static ScoreState EnsureScoresLoaded()
    {
        var cached = _registeredState;
        if (cached is not null)
            return cached;

        lock (Sync)
        {
            cached = _registeredState;
            if (cached is not null)
                return cached;

            if (_scoreFactory is not null)
            {
                var loaded = _scoreFactory();
                _registeredState = new ScoreState(
                    new Dictionary<string, int>(loaded.FormatScores, StringComparer.OrdinalIgnoreCase),
                    new Dictionary<string, int>(loaded.SetTypeScores, StringComparer.OrdinalIgnoreCase),
                    new HashSet<string>(loaded.DiscExtensions, StringComparer.OrdinalIgnoreCase));
            }

            _registeredState ??= new ScoreState(
                FallbackFormatScores,
                FallbackSetTypeScores,
                FallbackDiscExtensions);

            return _registeredState;
        }
    }

    /// <summary>
    /// Returns the format score for a file extension or set type.
    /// Higher = better for emulator compatibility.
    /// Port of Get-FormatScore from FormatScoring.ps1.
    /// </summary>
    public static int GetFormatScore(string extension, string? type = null)
    {
        var state = EnsureScoresLoaded();

        if (!string.IsNullOrWhiteSpace(type) && state.SetTypeScores.TryGetValue(type, out var setTypeScore))
            return setTypeScore;

        if (string.IsNullOrWhiteSpace(extension))
            return DefaultUnknownFormatScore;

        return state.FormatScores.TryGetValue(extension, out var score)
            ? score
            : DefaultUnknownFormatScore;
    }

    /// <summary>
    /// Returns the region priority score based on user preference order.
    /// Port of Get-RegionScore from FormatScoring.ps1.
    /// </summary>
    public static int GetRegionScore(string region, IReadOnlyList<string> preferOrder)
    {
        var rankMap = GetRegionRankMap(preferOrder);
        var idx = rankMap.TryGetValue(region, out var rank) ? rank : -1;

        if (idx >= 0) return 1000 - idx;

        return region.ToUpperInvariant() switch
        {
            "WORLD" => 500,
            "UNKNOWN" => 100,
            _ => 200
        };
    }

    private static IReadOnlyDictionary<string, int> GetRegionRankMap(IReadOnlyList<string> preferOrder)
    {
        if (preferOrder.Count == 0)
            return EmptyRegionRankMap;

        var cacheKey = string.Join('\0', preferOrder.Select(static value => value?.Trim().ToUpperInvariant() ?? string.Empty));

        lock (RegionScoreCacheSync)
        {
            if (RegionScoreCache.TryGetValue(cacheKey, out var cached))
                return cached;

            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < preferOrder.Count; i++)
            {
                var region = preferOrder[i];
                if (string.IsNullOrWhiteSpace(region))
                    continue;

                if (!map.ContainsKey(region))
                    map[region] = i;
            }

            RegionScoreCache[cacheKey] = map;
            return map;
        }
    }

    private static readonly IReadOnlyDictionary<string, int> EmptyRegionRankMap =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Returns size tiebreak score: positive for disc (larger = better),
    /// negative for cartridge (smaller = better).
    /// Port of Get-SizeTieBreakScore from FormatScoring.ps1.
    /// </summary>
    public static long GetSizeTieBreakScore(string? type, string? extension, long sizeBytes)
    {
        var state = EnsureScoresLoaded();
        var ext = extension?.ToLowerInvariant() ?? "";

        if (type is "M3USET" or "GDISET" or "CUESET" or "CCDSET" or "DOSDIR")
            return sizeBytes;

        if (state.DiscExtensions.Contains(ext))
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
        => EnsureScoresLoaded().DiscExtensions.Contains(extension);

    /// <summary>
    /// Returns whether the given extension has a known format score (not the default 300).
    /// Callers can use this to log warnings for unknown formats.
    /// </summary>
    public static bool IsKnownFormat(string extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
            return false;

        return EnsureScoresLoaded().FormatScores.ContainsKey(extension);
    }
}
