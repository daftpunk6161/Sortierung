using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Shared normalization and validation entry point for run options across CLI, API, and UI.
/// </summary>
public static class RunOptionsBuilder
{
    /// <summary>
    /// Validate run options for semantic correctness.
    /// Returns an empty list when all options are valid.
    /// Centralises guards that previously lived only in the API (TASK-159).
    /// </summary>
    public static IReadOnlyList<string> Validate(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var errors = new List<string>();

        // TASK-159: OnlyGames guard — !OnlyGames && !KeepUnknownWhenOnlyGames is semantically invalid
        if (!options.OnlyGames && !options.KeepUnknownWhenOnlyGames)
            errors.Add("KeepUnknownWhenOnlyGames=false is only valid when OnlyGames=true.");

        return errors;
    }

    /// <summary>
    /// Returns warnings for features that are silently skipped in DryRun mode (TASK-163).
    /// These features require Mode=Move to have any effect.
    /// </summary>
    public static IReadOnlyList<string> GetDryRunFeatureWarnings(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(options.Mode, "DryRun", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var warnings = new List<string>();

        if (options.SortConsole)
            warnings.Add("SortConsole is enabled but will be skipped in DryRun mode. Use Mode=Move to apply.");

        if (options.ConvertFormat is not null)
            warnings.Add("ConvertFormat is set but will be skipped in DryRun mode. Use Mode=Move to apply.");

        if (options.EnableDatRename)
            warnings.Add("EnableDatRename is enabled but will be skipped in DryRun mode. Use Mode=Move to apply.");

        return warnings;
    }

    public static RunOptions Normalize(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedRoots = options.Roots
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedExtensions = options.Extensions
            .Where(static e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // TASK-144/F-08: Normalize PreferRegions — dedup, trim, uppercase, filter empty
        var normalizedPreferRegions = options.PreferRegions
            .Where(static r => !string.IsNullOrWhiteSpace(r))
            .Select(static r => r.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (normalizedPreferRegions.Length == 0)
            normalizedPreferRegions = RunConstants.DefaultPreferRegions;

        return new RunOptions
        {
            Roots = normalizedRoots,
            Mode = string.IsNullOrWhiteSpace(options.Mode) ? "DryRun" : options.Mode,
            ConflictPolicy = string.IsNullOrWhiteSpace(options.ConflictPolicy) ? RunConstants.DefaultConflictPolicy : options.ConflictPolicy,
            Extensions = normalizedExtensions,
            PreferRegions = normalizedPreferRegions,
            RemoveJunk = options.RemoveJunk,
            OnlyGames = options.OnlyGames,
            KeepUnknownWhenOnlyGames = options.KeepUnknownWhenOnlyGames,
            AggressiveJunk = options.AggressiveJunk,
            SortConsole = options.SortConsole,
            ConvertOnly = options.ConvertOnly,
            ConvertFormat = options.ConvertFormat,
            EnableDat = options.EnableDat,
            EnableDatAudit = options.EnableDatAudit,
            EnableDatRename = options.EnableDatRename,
            DatRoot = options.DatRoot,
            TrashRoot = options.TrashRoot,
            ReportPath = options.ReportPath,
            AuditPath = options.AuditPath,
            HashType = string.IsNullOrWhiteSpace(options.HashType) ? "SHA1" : options.HashType,
            DiscBasedConsoles = new HashSet<string>(options.DiscBasedConsoles, StringComparer.OrdinalIgnoreCase)
        };
    }
}
