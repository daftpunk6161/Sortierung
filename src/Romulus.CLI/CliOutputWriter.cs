using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.CLI;

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
        ConversionReport? conversionReport = null,
        IReadOnlyList<string>? preflightWarnings = null)
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
            ConsoleSortUnknown = projection.ConsoleSortUnknown,
            FailCount = projection.FailCount,
            SavedBytes = projection.SavedBytes,
            DurationMs = projection.DurationMs,
            PreflightWarnings = preflightWarnings?.ToArray() ?? Array.Empty<string>(),
            Results = groups.Select(r => new CliDedupeGroup
            {
                GameKey = r.GameKey,
                Winner = r.Winner.MainPath,
                WinnerDatMatch = r.Winner.DatMatch,
                WinnerDecisionClass = r.Winner.DecisionClass.ToString(),
                WinnerEvidenceTier = r.Winner.EvidenceTier.ToString(),
                WinnerPrimaryMatchKind = r.Winner.PrimaryMatchKind.ToString(),
                WinnerPlatformFamily = r.Winner.PlatformFamily.ToString(),
                Losers = r.Losers.Select(l => l.MainPath).ToArray(),
                LoserDetails = r.Losers.Select(l => new CliDedupeLoser
                {
                    Path = l.MainPath,
                    DecisionClass = l.DecisionClass.ToString(),
                    EvidenceTier = l.EvidenceTier.ToString(),
                    PrimaryMatchKind = l.PrimaryMatchKind.ToString(),
                    PlatformFamily = l.PlatformFamily.ToString(),
                }).ToArray()
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
        var defaultPreferRegions = string.Join(",", RunConstants.DefaultPreferRegions);

        stdout.WriteLine($@"Romulus — Your Collection, Perfected.

Usage:
  romulus --roots ""D:\Roms"" [--mode DryRun|Move] [--prefer EU,US,JP]

Subcommands:
  romulus analyze --roots <path>              Collection health score and heatmap
    romulus export --roots <path> [--format csv|json|excel|retroarch|m3u|launchbox|emulationstation|playnite|mister|analoguepocket|onionos] [-o <file>]
  romulus profiles list
  romulus profiles show --id <profile-id>
  romulus profiles import --input <file>
  romulus profiles export --id <profile-id> --output <file>
  romulus profiles delete --id <profile-id>
  romulus workflows [--id <workflow-id>]
  romulus diff --left-roots <paths> --right-roots <paths> [--offset <n>] [--limit <n>] [-o <file>]
  romulus merge --left-roots <paths> --right-roots <paths> --target-root <path> [--plan|--apply] [--allow-moves] [--audit <file>] [-o <file>]
  romulus compare --run <run-id> --compare-to <run-id> [-o <file>]
  romulus trends [--limit <n>] [-o <file>]
  romulus dat diff --old <path> --new <path>  Compare two Logiqx DAT files
    romulus dat fixdat --roots <path> [--dat-root <path>] [--name <title>] [-o <file>]
  romulus integrity baseline --roots <path>   Create integrity baseline
  romulus integrity check                     Check files against baseline
  romulus history [--offset <n>] [--limit <n>] [-o <file>]
  romulus watch --roots <path> [--interval <min>|--cron <expr>]
  romulus convert --input <file|dir> [--console PS1] [--target chd] [--approve-conversion-review]
  romulus header --input <file>               Analyze ROM header
  romulus junk-report --roots <path> [--aggressive]
  romulus completeness --roots <path> [--dat-root <path>] [-o <file>]

Run Options:
  --roots <paths>        Semicolon-separated root paths (required)
  --mode <mode>          DryRun (default) or Move
  --workflow <id>        Apply guided workflow defaults
  --profile <id>         Apply a built-in or saved run profile
  --profile-file <file>  Import and apply an external profile document
  --prefer <regions>     Comma-separated region priority (default: {defaultPreferRegions})
  --extensions <exts>    Comma-separated extensions filter
  --trashroot <path>     Custom trash folder for duplicates
  --removejunk           Move junk files (demos, betas, hacks) to trash
  --gamesonly            Keep only GAME category files in dedupe pipeline
  --keepunknown          With --gamesonly, keep UNKNOWN files for manual review (default)
  --dropunknown          With --gamesonly, exclude UNKNOWN files as well
  --aggressivejunk       Also flag WIP/dev builds as junk
  --sortconsole          Sort winners into console-specific subfolders
  --enabledat            Enable DAT verification (hash-match against No-Intro/Redump)
  --dat-audit            Emit DAT audit counters in DryRun output
  --datrename            Rename DAT-verified mismatches before move (Move mode only)
  --datroot <path>       DAT file directory (overrides settings.json)
  --hashtype <type>      Hash algorithm: SHA1|SHA256|MD5 (default: SHA1)
  --update-dats          Download/update DATs from dat-catalog.json (no --roots required)
  --import-packs-from    Import local No-Intro DAT packs from folder (optional)
  --force-dat-update     Re-download DATs even if target id.dat already exists
  --smart-dat-update     Update only missing and stale id.dat files
  --dat-stale-days       Stale threshold in days for --smart-dat-update (default: 365)
  --convertformat        Convert winners to optimal format (CHD/RVZ/ZIP)
  --convertonly          Convert all candidates only (skip dedupe/move)
  --approve-reviews      Reuse persisted review approvals during the run
  --approve-conversion-review
                         Erlaubt review-pflichtige Conversion-Pfade (z.B. NKit)
  --conflictpolicy       Move conflict handling: Rename|Skip|Overwrite (default: Rename)
  --yes                  Confirm destructive Move in non-interactive runs
  --report <path>        Output HTML, CSV, or JSON report (.html, .csv, or .json)
  --audit <path>         Write audit CSV log for Move operations
  --log <path>           Write structured JSONL log file
  --loglevel <level>     Log level: Debug|Info|Warning|Error (default: Info)
  --help                 Show this help

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

    internal static string FormatRunHistoryJson(CollectionRunHistoryPage page)
        => JsonSerializer.Serialize(page, JsonOptions);

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
    public int ConsoleSortUnknown { get; init; }
    public int FailCount { get; init; }
    public long SavedBytes { get; init; }
    public long DurationMs { get; init; }
    public string[] PreflightWarnings { get; init; } = Array.Empty<string>();
    public CliDedupeGroup[] Results { get; init; } = Array.Empty<CliDedupeGroup>();
    public CliConversionPlan[] ConversionPlans { get; init; } = Array.Empty<CliConversionPlan>();
    public CliConversionBlocked[] ConversionBlocked { get; init; } = Array.Empty<CliConversionBlocked>();
}

internal sealed class CliDedupeGroup
{
    public string GameKey { get; init; } = "";
    public string Winner { get; init; } = "";
    public bool WinnerDatMatch { get; init; }
    public string WinnerDecisionClass { get; init; } = "Unknown";
    public string WinnerEvidenceTier { get; init; } = "Tier4_Unknown";
    public string WinnerPrimaryMatchKind { get; init; } = "None";
    public string WinnerPlatformFamily { get; init; } = "Unknown";
    public string[] Losers { get; init; } = Array.Empty<string>();
    public CliDedupeLoser[] LoserDetails { get; init; } = Array.Empty<CliDedupeLoser>();
}

internal sealed class CliDedupeLoser
{
    public string Path { get; init; } = "";
    public string DecisionClass { get; init; } = "Unknown";
    public string EvidenceTier { get; init; } = "Tier4_Unknown";
    public string PrimaryMatchKind { get; init; } = "None";
    public string PlatformFamily { get; init; } = "Unknown";
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
