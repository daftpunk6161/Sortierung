namespace Romulus.Contracts;

/// <summary>
/// Canonical source of truth for disc-oriented extensions and related subsets.
/// Keeps extension checks deterministic across Core, Infrastructure, and UI.
/// </summary>
public static class DiscFormats
{
    public static readonly IReadOnlySet<string> AllDiscExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".chd", ".iso", ".cue", ".bin", ".img", ".mdf", ".mds", ".ccd", ".sub", ".gdi",
        ".gcm", ".rvz", ".gcz", ".wia", ".wbf1", ".wbfs", ".wud", ".wux", ".nrg", ".cdi",
        ".cso", ".pbp", ".dax", ".jso", ".zso"
    };

    public static readonly IReadOnlySet<string> ArchiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z", ".rar"
    };

    public static readonly IReadOnlySet<string> PspImageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cso", ".pbp", ".dax", ".jso", ".zso"
    };

    public static readonly IReadOnlySet<string> SwitchPackageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".nsp", ".xci", ".nsz", ".xcz"
    };

    public static readonly IReadOnlySet<string> PatchContainerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".ecm"
    };

    /// <summary>
    /// Extensions where DAT fallback via strict name matching is acceptable.
    /// </summary>
    public static readonly IReadOnlySet<string> DatNameOnlyExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".chd", ".iso", ".gcm", ".img", ".cso", ".rvz", ".bin"
    };

    /// <summary>
    /// DAT name-only fallback extensions excluding BIN (used for generic fallback strategy).
    /// </summary>
    public static readonly IReadOnlySet<string> DatNameOnlyExtensionsWithoutBin = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".chd", ".iso", ".gcm", ".img", ".cso", ".rvz"
    };

    /// <summary>
    /// Disc-like extensions used by productization wizard analysis in UI.
    /// </summary>
    public static readonly IReadOnlySet<string> DiscLikeAnalysisExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".bin", ".cue", ".chd", ".gdi", ".cso", ".pbp", ".rvz", ".wbfs"
    };

    /// <summary>
    /// Disc extensions used by AutoProfile heuristics in UI.
    /// </summary>
    public static readonly IReadOnlySet<string> AutoProfileDiscExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".chd", ".iso", ".bin", ".cue", ".gdi"
    };

    /// <summary>
    /// Extensions that can be probed by binary disc header detectors.
    /// </summary>
    public static readonly IReadOnlySet<string> HeaderProbeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".chd", ".iso", ".gcm", ".img", ".bin"
    };

    /// <summary>
    /// Direct input formats supported by chdman conversion commands.
    /// </summary>
    public static readonly IReadOnlySet<string> ChdmanDirectInputExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".gdi", ".iso", ".bin", ".img"
    };

    /// <summary>
    /// Archive input formats that can be extracted before chdman conversion.
    /// </summary>
    public static readonly IReadOnlySet<string> ChdmanArchiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".zip", ".7z"
    };

    /// <summary>
    /// All source extensions accepted by chdman workflows.
    /// </summary>
    public static readonly IReadOnlySet<string> ChdmanSupportedSourceExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".cue", ".gdi", ".iso", ".bin", ".img", ".zip", ".7z"
    };

    /// <summary>
    /// Extensions where PS2 createDVD may need a size-based safety downgrade to createcd.
    /// </summary>
    public static readonly IReadOnlySet<string> ChdmanCreatedvdHeuristicExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".bin", ".img"
    };

    /// <summary>
    /// Direct input formats supported by Dolphin conversion workflows.
    /// </summary>
    public static readonly IReadOnlySet<string> DolphinInputExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".gcm", ".wbfs", ".rvz", ".gcz", ".wia"
    };

    /// <summary>
    /// Extensions where generic PS1 header detection is downgraded when folder evidence suggests PS2.
    /// </summary>
    public static readonly IReadOnlySet<string> PsxHeaderDowngradeSensitiveExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".iso", ".bin", ".img", ".chd"
    };

    /// <summary>
    /// Extensions that are expected as conversion outputs for quick verification flows.
    /// </summary>
    public static readonly IReadOnlySet<string> ConversionVerificationExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        ".chd", ".rvz", ".7z"
    };

    public static bool IsDiscExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && AllDiscExtensions.Contains(extension);

    public static bool IsDatNameOnlyExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && DatNameOnlyExtensions.Contains(extension);

    public static bool IsDatNameOnlyExtensionWithoutBin(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && DatNameOnlyExtensionsWithoutBin.Contains(extension);

    public static bool IsHeaderProbeExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && HeaderProbeExtensions.Contains(extension);

    public static bool IsDiscLikeAnalysisExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && DiscLikeAnalysisExtensions.Contains(extension);

    public static bool IsAutoProfileDiscExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && AutoProfileDiscExtensions.Contains(extension);

    public static bool IsChdmanArchiveExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && ChdmanArchiveExtensions.Contains(extension);

    public static bool IsChdmanSupportedSourceExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && ChdmanSupportedSourceExtensions.Contains(extension);

    public static bool IsChdmanCreatedvdHeuristicExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && ChdmanCreatedvdHeuristicExtensions.Contains(extension);

    public static bool IsDolphinSupportedSourceExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && DolphinInputExtensions.Contains(extension);

    public static bool IsPsxHeaderDowngradeSensitiveExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && PsxHeaderDowngradeSensitiveExtensions.Contains(extension);

    public static bool IsConversionVerificationExtension(string? extension)
        => !string.IsNullOrWhiteSpace(extension) && ConversionVerificationExtensions.Contains(extension);
}
