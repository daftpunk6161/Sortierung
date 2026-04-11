namespace Romulus.Contracts;

/// <summary>
/// Central run-option constants shared across CLI, API, GUI and Orchestration.
/// Single source of truth — prevents hard-coded string/value duplication between entry points.
/// </summary>
public static class RunConstants
{
    /// <summary>Valid conflict policy names (case-insensitive comparison recommended).</summary>
    public static readonly IReadOnlySet<string> ValidConflictPolicies =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rename", "Skip", "Overwrite" };

    /// <summary>Default conflict policy when none is specified.</summary>
    public const string DefaultConflictPolicy = "Rename";

    /// <summary>Maximum number of PreferRegions entries accepted by CLI and API.</summary>
    public const int MaxPreferRegions = 20;

    /// <summary>
    /// TASK-144: Central default for PreferRegions — single source of truth.
    /// Order: EU, US, JP, WORLD (matches defaults.json).
    /// Referenced by RunOptions, CLI, API, and GUI.
    /// </summary>
    public static readonly string[] DefaultPreferRegions = ["EU", "US", "JP", "WORLD"];

    /// <summary>Valid hash type names.</summary>
    public static readonly IReadOnlySet<string> ValidHashTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SHA1", "SHA256", "MD5" };

    /// <summary>Default hash type.</summary>
    public const string DefaultHashType = "SHA1";

    // ── Convert format constants ────────────────────────────────────

    public const string ConvertFormatAuto = "auto";
    public const string ConvertFormatChd = "chd";
    public const string ConvertFormatRvz = "rvz";
    public const string ConvertFormatZip = "zip";
    public const string ConvertFormat7z = "7z";

    /// <summary>Valid convert format names.</summary>
    public static readonly IReadOnlySet<string> ValidConvertFormats =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ConvertFormatAuto,
            ConvertFormatChd,
            ConvertFormatRvz,
            ConvertFormatZip,
            ConvertFormat7z
        };

    // ── Run Mode constants ──────────────────────────────────────────

    /// <summary>DryRun mode — preview only, no file operations.</summary>
    public const string ModeDryRun = "DryRun";

    /// <summary>Move mode — execute file operations (move, trash, convert).</summary>
    public const string ModeMove = "Move";

    /// <summary>Valid run modes.</summary>
    public static readonly IReadOnlySet<string> ValidModes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ModeDryRun, ModeMove };

    // ── Run Status constants (match RunOutcome.ToStatusString()) ─────

    /// <summary>Run is currently executing.</summary>
    public const string StatusRunning = "running";

    /// <summary>Run completed successfully (API lifecycle status).</summary>
    public const string StatusCompleted = "completed";

    // ── Phase Metric Status constants (title-case, used by PhaseMetricsCollector) ──

    /// <summary>Phase is currently executing.</summary>
    public const string PhaseStatusRunning = "Running";

    /// <summary>Phase completed successfully.</summary>
    public const string PhaseStatusCompleted = "Completed";

    /// <summary>Run completed successfully.</summary>
    public const string StatusOk = "ok";

    /// <summary>Run completed with some non-fatal errors.</summary>
    public const string StatusCompletedWithErrors = "completed_with_errors";

    /// <summary>Run was blocked before execution.</summary>
    public const string StatusBlocked = "blocked";

    /// <summary>Run was cancelled by user.</summary>
    public const string StatusCancelled = "cancelled";

    /// <summary>Run failed with fatal error.</summary>
    public const string StatusFailed = "failed";

    /// <summary>
    /// Canonical sort-decision names used across enrichment, sorting, and projections.
    /// </summary>
    public static class SortDecisions
    {
        public const string Sort = "Sort";
        public const string Review = "Review";
        public const string Blocked = "Blocked";
        public const string Unknown = "Unknown";
        public const string DatVerified = "DatVerified";
    }

    /// <summary>
    /// Canonical phase prefixes emitted by pipeline progress messages.
    /// </summary>
    public static class Phases
    {
        public const string Preflight = "[Preflight]";
        public const string Scan = "[Scan]";
        public const string Filter = "[Filter]";
        public const string Dedupe = "[Dedupe]";
        public const string Junk = "[Junk]";
        public const string Move = "[Move]";
        public const string Sort = "[Sort]";
        public const string Convert = "[Convert]";
        public const string Report = "[Report]";
        public const string Finished = "[Fertig]";
        public const string Done = "[Done]";
    }

    /// <summary>
    /// Canonical audit action names used in CSV rows and rollback parsing.
    /// Keep values centralized to avoid drift between writers and rollback readers.
    /// </summary>
    public static class AuditActions
    {
        public const string Copy = "COPY";
        public const string CopyPending = "COPY_PENDING";
        public const string Move = "MOVE";
        public const string MovePending = "MOVE_PENDING";
        public const string Moved = "MOVED";
        public const string MoveFailed = "MOVE_FAILED";
        public const string JunkRemove = "JUNK_REMOVE";
        public const string JunkPreview = "JUNK_PREVIEW";
        public const string ConsoleSort = "CONSOLE_SORT";
        public const string Convert = "CONVERT";
        public const string ConvertSource = "CONVERT_SOURCE";
        public const string DatRenamePending = "DAT_RENAME_PENDING";
        public const string DatRename = "DAT_RENAME";
        public const string DatRenameFailed = "DAT_RENAME_FAILED";
    }

    // ── Well-known folder names ─────────────────────────────────────
    // Single source of truth for special directory names used across orchestration,
    // sorting, blocklists, and CLI rollback discovery.

    /// <summary>
    /// Canonical names for trash, staging, and special-purpose directories.
    /// Prevents scattered magic strings across pipeline phases, sorting, and CLI.
    /// </summary>
    public static class WellKnownFolders
    {
        /// <summary>Trash folder for region-dedupe losers.</summary>
        public const string TrashRegionDedupe = "_TRASH_REGION_DEDUPE";

        /// <summary>Trash folder for junk files.</summary>
        public const string TrashJunk = "_TRASH_JUNK";

        /// <summary>Trash folder for pre-conversion source files.</summary>
        public const string TrashConverted = "_TRASH_CONVERTED";

        /// <summary>Generic trash folder (used for rollback discovery).</summary>
        public const string TrashGeneric = "_TRASH";

        /// <summary>Duplicate PS3 folder-based games.</summary>
        public const string Ps3Dupes = "PS3_DUPES";

        /// <summary>Duplicate folder-based games (generic).</summary>
        public const string FolderDupes = "_FOLDER_DUPES";

        /// <summary>BIOS files directory.</summary>
        public const string Bios = "_BIOS";

        /// <summary>Junk staging directory (sorting).</summary>
        public const string Junk = "_JUNK";

        /// <summary>Review staging directory (uncertain classification).</summary>
        public const string Review = "_REVIEW";

        /// <summary>Blocked staging directory (unsafe conflicts / hard blocks).</summary>
        public const string Blocked = "_BLOCKED";

        /// <summary>Unknown staging directory (insufficient evidence).</summary>
        public const string Unknown = "_UNKNOWN";

        /// <summary>Quarantine directory.</summary>
        public const string Quarantine = "_QUARANTINE";

        /// <summary>Backup directory.</summary>
        public const string Backup = "_BACKUP";
    }
}
