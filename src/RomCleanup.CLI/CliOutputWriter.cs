using System.Text.Json;
using System.Text.Json.Serialization;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.CLI;

/// <summary>
/// Formatted CLI output: DryRun JSON, Move summary, Help, Errors.
/// ADR-008 §C-06: DryRun JSON uses typed record, not anonymous object.
/// </summary>
internal static class CliOutputWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never
    };

    /// <summary>
    /// DryRun: Formats RunProjection + DedupeGroups as JSON string.
    /// ADR-008 §C-03/C-06: CLI reads only RunProjection, never RunResult internals.
    /// ConversionReport is the exception: provides per-file conversion details.
    /// </summary>
    internal static string FormatDryRunJson(RunProjection projection,
        IReadOnlyList<DedupeGroup> groups,
        ConversionReport? conversionReport = null)
    {
        var output = new CliDryRunOutput
        {
            Status = projection.Status,
            ExitCode = projection.ExitCode,
            Mode = "DryRun",
            TotalFiles = projection.TotalFiles,
            Candidates = projection.Candidates,
            Groups = projection.Groups,
            Keep = projection.Keep,
            Winners = projection.Keep,
            Dupes = projection.Dupes,
            Losers = projection.Dupes,
            Duplicates = projection.Dupes,
            Games = projection.Games,
            Unknown = projection.Unknown,
            Junk = projection.Junk,
            Bios = projection.Bios,
            DatMatches = projection.DatMatches,
            HealthScore = projection.HealthScore,
            ConvertedCount = projection.ConvertedCount,
            ConvertErrorCount = projection.ConvertErrorCount,
            ConvertSkippedCount = projection.ConvertSkippedCount,
            ConvertBlockedCount = projection.ConvertBlockedCount,
            ConvertReviewCount = projection.ConvertReviewCount,
            ConvertLossyWarningCount = projection.ConvertLossyWarningCount,
            ConvertVerifyPassedCount = projection.ConvertVerifyPassedCount,
            ConvertVerifyFailedCount = projection.ConvertVerifyFailedCount,
            ConvertSavedBytes = projection.ConvertSavedBytes,
            DatHaveCount = projection.DatHaveCount,
            DatHaveWrongNameCount = projection.DatHaveWrongNameCount,
            DatMissCount = projection.DatMissCount,
            DatUnknownCount = projection.DatUnknownCount,
            DatAmbiguousCount = projection.DatAmbiguousCount,
            DatRenameProposedCount = projection.DatRenameProposedCount,
            DatRenameExecutedCount = projection.DatRenameExecutedCount,
            DatRenameSkippedCount = projection.DatRenameSkippedCount,
            DatRenameFailedCount = projection.DatRenameFailedCount,
            JunkRemovedCount = projection.JunkRemovedCount,
            FilteredNonGameCount = projection.FilteredNonGameCount,
            MoveCount = projection.MoveCount,
            SkipCount = projection.SkipCount,
            JunkFailCount = projection.JunkFailCount,
            ConsoleSortMoved = projection.ConsoleSortMoved,
            ConsoleSortFailed = projection.ConsoleSortFailed,
            ConsoleSortReviewed = projection.ConsoleSortReviewed,
            ConsoleSortBlocked = projection.ConsoleSortBlocked,
            FailCount = projection.FailCount,
            SavedBytes = projection.SavedBytes,
            DurationMs = projection.DurationMs,
            Results = groups.Select(r => new CliDedupeGroup
            {
                GameKey = r.GameKey,
                Winner = r.Winner.MainPath,
                WinnerDatMatch = r.Winner.DatMatch,
                Losers = r.Losers.Select(l => l.MainPath).ToArray()
            }).ToArray(),
            ConversionPlans = BuildConversionPlans(conversionReport),
            ConversionBlocked = BuildConversionBlocked(conversionReport)
        };

        var json = JsonSerializer.Serialize(output, JsonOptions);
        return json;
    }

    /// <summary>
    /// Move: Writes summary + sidecar paths to stderr.
    /// </summary>
    internal static void WriteMoveSummary(TextWriter stderr, RunProjection projection,
        string? auditPath, string? reportPath, int convertedCount)
    {
        stderr.WriteLine($"[Done] Moved {projection.MoveCount} files ({projection.SavedBytes:N0} bytes saved), {projection.FailCount} failed");

        if (convertedCount > 0)
            stderr.WriteLine($"[Convert] {convertedCount} files converted");

        if (!string.IsNullOrEmpty(auditPath) && File.Exists(auditPath))
            stderr.WriteLine($"[Audit] {auditPath}");

        if (!string.IsNullOrEmpty(reportPath))
            stderr.WriteLine($"[Report] {reportPath}");
    }

    /// <summary>Help: Outputs usage text to stdout.</summary>
    internal static void WriteUsage(TextWriter stdout)
    {
        stdout.WriteLine(@"Romulus — Your Collection, Perfected.

Usage:
  romulus -Roots ""D:\Roms"" [-Mode DryRun|Move] [-Prefer EU,US,JP]

Options:
  -Roots <paths>     Semicolon-separated root paths (required)
  -Mode <mode>       DryRun (default) or Move
  -Prefer <regions>  Comma-separated region priority (default: EU,US,WORLD,JP)
  -Extensions <exts> Comma-separated extensions filter
  -TrashRoot <path>  Custom trash folder for duplicates
  -RemoveJunk        Move junk files (demos, betas, hacks) to trash
    -GamesOnly         Keep only GAME category files in dedupe pipeline
    -KeepUnknown       With -GamesOnly, keep UNKNOWN files for manual review (default)
    -DropUnknown       With -GamesOnly, exclude UNKNOWN files as well
  -AggressiveJunk    Also flag WIP/dev builds as junk
  -SortConsole       Sort winners into console-specific subfolders
  -EnableDat         Enable DAT verification (hash-match against No-Intro/Redump)
        -DatAudit          Emit DAT audit counters in DryRun output
    -DatRename         Rename DAT-verified mismatches before move (Move mode only)
  -DatRoot <path>    DAT file directory (overrides settings.json)
  -HashType <type>   Hash algorithm: SHA1|SHA256|MD5 (default: SHA1)
  -ConvertFormat     Convert winners to optimal format (CHD/RVZ/ZIP)
    -ConvertOnly       Convert all candidates only (skip dedupe/move)
    -ConflictPolicy    Move conflict handling: Rename|Skip|Overwrite (default: Rename)
    -Yes               Confirm destructive Move in non-interactive runs
  -Report <path>     Output HTML or CSV report (.html or .csv)
  -Audit <path>      Write audit CSV log for Move operations
  -Log <path>        Write structured JSONL log file
  -LogLevel <level>  Log level: Debug|Info|Warning|Error (default: Info)
  -Help              Show this help

Exit codes:
  0  Success
  1  Runtime error
  2  Cancelled
  3  Preflight / validation failure");
    }

    /// <summary>Errors: Outputs error messages to stderr.</summary>
    internal static void WriteErrors(TextWriter stderr, IReadOnlyList<string> errors)
    {
        foreach (var error in errors)
            stderr.WriteLine(error);
    }

    private static CliConversionPlan[] BuildConversionPlans(ConversionReport? report)
    {
        if (report?.Results is null || report.Results.Count == 0)
            return Array.Empty<CliConversionPlan>();

        return report.Results
            .Where(r => r.Outcome == ConversionOutcome.Success || r.Outcome == ConversionOutcome.Skipped)
            .Select(r => new CliConversionPlan
            {
                SourcePath = r.SourcePath,
                TargetExtension = r.TargetPath is not null ? Path.GetExtension(r.TargetPath) : null,
                Safety = r.Safety.ToString(),
                Outcome = r.Outcome.ToString(),
                Verification = r.VerificationResult.ToString()
            })
            .ToArray();
    }

    private static CliConversionBlocked[] BuildConversionBlocked(ConversionReport? report)
    {
        if (report?.Results is null || report.Results.Count == 0)
            return Array.Empty<CliConversionBlocked>();

        return report.Results
            .Where(r => r.Outcome == ConversionOutcome.Blocked || r.Outcome == ConversionOutcome.Error)
            .Select(r => new CliConversionBlocked
            {
                SourcePath = r.SourcePath,
                Reason = r.Reason ?? r.Outcome.ToString(),
                Safety = r.Safety.ToString()
            })
            .ToArray();
    }
}

/// <summary>
/// Typed DryRun output — guarantees all RunProjection fields are serialized.
/// ADR-008 §C-06.
/// </summary>
internal sealed class CliDryRunOutput
{
    public string Status { get; init; } = "";
    public int ExitCode { get; init; }
    public string Mode { get; init; } = "DryRun";
    public int TotalFiles { get; init; }
    public int Candidates { get; init; }
    public int Groups { get; init; }
    public int Keep { get; init; }
    public int Winners { get; init; }
    public int Dupes { get; init; }
    public int Losers { get; init; }
    public int Duplicates { get; init; }
    public int Games { get; init; }
    public int Unknown { get; init; }
    public int Junk { get; init; }
    public int Bios { get; init; }
    public int DatMatches { get; init; }
    public int HealthScore { get; init; }
    public int ConvertedCount { get; init; }
    public int ConvertErrorCount { get; init; }
    public int ConvertSkippedCount { get; init; }
    public int ConvertBlockedCount { get; init; }
    public int ConvertReviewCount { get; init; }
    public int ConvertLossyWarningCount { get; init; }
    public int ConvertVerifyPassedCount { get; init; }
    public int ConvertVerifyFailedCount { get; init; }
    public long ConvertSavedBytes { get; init; }
    public int DatHaveCount { get; init; }
    public int DatHaveWrongNameCount { get; init; }
    public int DatMissCount { get; init; }
    public int DatUnknownCount { get; init; }
    public int DatAmbiguousCount { get; init; }
    public int DatRenameProposedCount { get; init; }
    public int DatRenameExecutedCount { get; init; }
    public int DatRenameSkippedCount { get; init; }
    public int DatRenameFailedCount { get; init; }
    public int JunkRemovedCount { get; init; }
    public int FilteredNonGameCount { get; init; }
    public int MoveCount { get; init; }
    public int SkipCount { get; init; }
    public int JunkFailCount { get; init; }
    public int ConsoleSortMoved { get; init; }
    public int ConsoleSortFailed { get; init; }
    public int ConsoleSortReviewed { get; init; }
    public int ConsoleSortBlocked { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long DurationMs { get; init; }
    public CliDedupeGroup[] Results { get; init; } = Array.Empty<CliDedupeGroup>();
    public CliConversionPlan[] ConversionPlans { get; init; } = Array.Empty<CliConversionPlan>();
    public CliConversionBlocked[] ConversionBlocked { get; init; } = Array.Empty<CliConversionBlocked>();
}

internal sealed class CliDedupeGroup
{
    public string GameKey { get; init; } = "";
    public string Winner { get; init; } = "";
    public bool WinnerDatMatch { get; init; }
    public string[] Losers { get; init; } = Array.Empty<string>();
}

internal sealed class CliConversionPlan
{
    public string SourcePath { get; init; } = "";
    public string? TargetExtension { get; init; }
    public string Safety { get; init; } = "";
    public string Outcome { get; init; } = "";
    public string Verification { get; init; } = "";
}

internal sealed class CliConversionBlocked
{
    public string SourcePath { get; init; } = "";
    public string Reason { get; init; } = "";
    public string Safety { get; init; } = "";
}
