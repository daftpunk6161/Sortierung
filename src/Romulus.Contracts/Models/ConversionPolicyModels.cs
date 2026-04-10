namespace Romulus.Contracts.Models;

/// <summary>
/// System-wide policy controlling whether and how conversion is allowed.
/// </summary>
public enum ConversionPolicy
{
    /// <summary>Automatic conversion is allowed without extra confirmation.</summary>
    Auto,
    /// <summary>Only archive normalization/repackaging is allowed.</summary>
    ArchiveOnly,
    /// <summary>Conversion requires explicit user confirmation.</summary>
    ManualOnly,
    /// <summary>Conversion is blocked for this system.</summary>
    None
}

/// <summary>
/// Classification of source data integrity before conversion.
/// </summary>
public enum SourceIntegrity
{
    /// <summary>Source is considered lossless and complete.</summary>
    Lossless,
    /// <summary>Source is known to be lossy or potentially incomplete.</summary>
    Lossy,
    /// <summary>Integrity cannot be determined reliably.</summary>
    Unknown
}

/// <summary>
/// Safety classification of a conversion path.
/// </summary>
public enum ConversionSafety
{
    /// <summary>Lossless, verifiable path.</summary>
    Safe,
    /// <summary>Technically usable but source is lossy.</summary>
    Acceptable,
    /// <summary>Requires review due to elevated risk.</summary>
    Risky,
    /// <summary>Conversion must be blocked.</summary>
    Blocked
}

/// <summary>
/// Runtime conditions used to activate capability edges.
/// </summary>
public enum ConversionCondition
{
    /// <summary>No condition.</summary>
    None,
    /// <summary>Source file size is below 700 MB.</summary>
    FileSizeLessThan700MB,
    /// <summary>Source file size is at least 700 MB.</summary>
    FileSizeGreaterEqual700MB,
    /// <summary>Source appears to be an NKit image.</summary>
    IsNKitSource,
    /// <summary>Source extension is .wad.</summary>
    IsWadFile,
    /// <summary>Source extension is .cdi.</summary>
    IsCdiSource,
    /// <summary>Source is an encrypted PBP container.</summary>
    IsEncryptedPbp
}

/// <summary>
/// Shared constants for conversion thresholds.
/// </summary>
public static class ConversionThresholds
{
    /// <summary>
    /// CD image threshold: files below 700 MB are treated as CD rather than DVD.
    /// Used by chdman (createcd vs createdvd), ConversionConditionEvaluator, and IsLikelyCdImage.
    /// </summary>
    public const long CdImageThresholdBytes = 700L * 1024 * 1024;
}
