namespace Romulus.Contracts.Models;

/// <summary>
/// Target format specification for a console type.
/// Port of $script:BEST_FORMAT from Convert.ps1.
/// </summary>
public sealed record ConversionTarget(
    string Extension,
    string ToolName,
    string Command);

/// <summary>
/// Result of a single file conversion operation.
/// </summary>
public sealed record ConversionResult(
    string SourcePath,
    string? TargetPath,
    ConversionOutcome Outcome,
    string? Reason = null,
    int ExitCode = 0)
{
    /// <summary>
    /// Optional conversion plan that was executed.
    /// </summary>
    public ConversionPlan? Plan { get; init; }

    /// <summary>
    /// Integrity classification of the source file.
    /// </summary>
    public SourceIntegrity SourceIntegrity { get; init; } = SourceIntegrity.Unknown;

    /// <summary>
    /// Safety classification of the conversion path.
    /// </summary>
    public ConversionSafety Safety { get; init; } = ConversionSafety.Blocked;

    /// <summary>
    /// Verification status after conversion.
    /// </summary>
    public VerificationStatus VerificationResult { get; init; } = VerificationStatus.NotAttempted;

    /// <summary>
    /// End-to-end conversion duration in milliseconds.
    /// </summary>
    public long DurationMs { get; init; }

    /// <summary>
    /// Source file size captured during conversion processing.
    /// </summary>
    public long? SourceBytes { get; init; }

    /// <summary>
    /// Target file size captured during conversion processing.
    /// </summary>
    public long? TargetBytes { get; init; }

    /// <summary>
    /// Additional output paths for conversions that produce multiple primary artifacts.
    /// </summary>
    public IReadOnlyList<string> AdditionalTargetPaths { get; init; } = Array.Empty<string>();
}

/// <summary>
/// Outcome classification for a conversion operation.
/// </summary>
public enum ConversionOutcome
{
    /// <summary>Successfully converted.</summary>
    Success,
    /// <summary>Skipped (already target format, unsupported source, etc.).</summary>
    Skipped,
    /// <summary>Conversion failed (tool error, verification failure).</summary>
    Error,
    /// <summary>Blocked by safety or policy constraints.</summary>
    Blocked
}

/// <summary>
/// Verification strategy to apply after a conversion step.
/// </summary>
public enum VerificationMethod
{
    /// <summary>Run chdman verify against target output.</summary>
    ChdmanVerify,
    /// <summary>Validate RVZ magic bytes and minimum file size.</summary>
    RvzMagicByte,
    /// <summary>Run 7z archive integrity check.</summary>
    SevenZipTest,
    /// <summary>Verify output file exists and has non-zero length.</summary>
    FileExistenceCheck,
    /// <summary>No verification configured for this conversion step.</summary>
    None
}

/// <summary>
/// Status of a verification attempt.
/// </summary>
public enum VerificationStatus
{
    /// <summary>Verification completed successfully.</summary>
    Verified,
    /// <summary>Verification command or checks failed.</summary>
    VerifyFailed,
    /// <summary>No verifier available for this format or tool.</summary>
    VerifyNotAvailable,
    /// <summary>Verification was not attempted.</summary>
    NotAttempted
}

/// <summary>
/// Per-step execution result emitted by the conversion executor.
/// </summary>
public sealed record ConversionStepResult(
    int StepOrder,
    string OutputPath,
    bool Success,
    VerificationStatus Verification,
    string? ErrorReason,
    long DurationMs);

/// <summary>
/// Tool execution result used by conversion step invokers.
/// </summary>
public sealed record ToolInvocationResult(
    bool Success,
    string? OutputPath,
    int ExitCode,
    string? StdOut,
    string? StdErr,
    long DurationMs,
    VerificationStatus Verification);

/// <summary>
/// Aggregated conversion metrics for one run.
/// </summary>
public sealed record ConversionReport
{
    public required int TotalPlanned { get; init; }
    public required int Converted { get; init; }
    public required int Skipped { get; init; }
    public required int Errors { get; init; }
    public required int Blocked { get; init; }
    public required int RequiresReview { get; init; }
    public required long TotalSavedBytes { get; init; }
    public required IReadOnlyList<ConversionResult> Results { get; init; }
}
