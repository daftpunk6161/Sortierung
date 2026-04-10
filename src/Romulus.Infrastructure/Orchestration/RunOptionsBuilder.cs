using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Profiles;

namespace Romulus.Infrastructure.Orchestration;

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

        var datRootError = RunProfileValidator.ValidateOptionalSafePath(options.DatRoot, "datRoot");
        if (datRootError is not null)
            errors.Add(datRootError);

        var trashRootError = RunProfileValidator.ValidateOptionalSafePath(options.TrashRoot, "trashRoot");
        if (trashRootError is not null)
            errors.Add(trashRootError);

        var auditPathError = RunProfileValidator.ValidateOptionalSafePath(options.AuditPath, "auditPath");
        if (auditPathError is not null)
            errors.Add(auditPathError);

        var reportPathError = RunProfileValidator.ValidateOptionalSafePath(options.ReportPath, "reportPath");
        if (reportPathError is not null)
            errors.Add(reportPathError);

        var normalizedRoots = NormalizeAndPruneRoots(options.Roots);
        ValidateOutputOutsideRoots(options.TrashRoot, "trashRoot", normalizedRoots, errors);
        ValidateOutputOutsideRoots(options.DatRoot, "datRoot", normalizedRoots, errors);
        ValidateOutputOutsideRoots(options.AuditPath, "auditPath", normalizedRoots, errors);
        ValidateOutputOutsideRoots(options.ReportPath, "reportPath", normalizedRoots, errors);

        return errors;
    }

    /// <summary>
    /// Returns warnings for features that are still move-only in DryRun mode (TASK-163).
    /// These features require Mode=Move to have any effect.
    /// </summary>
    public static IReadOnlyList<string> GetDryRunFeatureWarnings(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (!string.Equals(options.Mode, "DryRun", StringComparison.OrdinalIgnoreCase))
            return Array.Empty<string>();

        var warnings = new List<string>();

        if (options.ConvertFormat is not null)
            warnings.Add("ConvertFormat is set but will be skipped in DryRun mode. Use Mode=Move to apply.");

        if (options.EnableDatRename)
            warnings.Add("EnableDatRename is enabled but will be skipped in DryRun mode. Use Mode=Move to apply.");

        if (options.ConvertOnly)
            warnings.Add("ConvertOnly is enabled but conversion will be skipped in DryRun mode. Use Mode=Move to apply.");

        return warnings;
    }

    public static RunOptions Normalize(RunOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var normalizedRoots = NormalizeAndPruneRoots(options.Roots);

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
            Mode = string.IsNullOrWhiteSpace(options.Mode)
                ? RunConstants.ModeDryRun
                : (string.Equals(options.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase)
                    ? RunConstants.ModeMove
                    : RunConstants.ModeDryRun),
            ConflictPolicy = string.IsNullOrWhiteSpace(options.ConflictPolicy) ? RunConstants.DefaultConflictPolicy : options.ConflictPolicy,
            Extensions = normalizedExtensions,
            PreferRegions = normalizedPreferRegions,
            RemoveJunk = options.RemoveJunk,
            OnlyGames = options.OnlyGames,
            KeepUnknownWhenOnlyGames = options.KeepUnknownWhenOnlyGames,
            AggressiveJunk = options.AggressiveJunk,
            SortConsole = options.SortConsole,
            ApproveReviews = options.ApproveReviews,
            ApproveConversionReview = options.ApproveConversionReview,
            ConvertOnly = options.ConvertOnly,
            ConvertFormat = options.ConvertFormat,
            EnableDat = options.EnableDat,
            EnableDatAudit = options.EnableDatAudit,
            EnableDatRename = options.EnableDatRename,
            DatRoot = options.DatRoot,
            TrashRoot = options.TrashRoot,
            ReportPath = options.ReportPath,
            AuditPath = options.AuditPath,
            HashType = string.IsNullOrWhiteSpace(options.HashType) ? "SHA1" : options.HashType
        };
    }

    private static string[] NormalizeAndPruneRoots(IEnumerable<string> roots)
    {
        var normalizedRoots = roots
            .Where(static p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static p => p.Length)
            .ThenBy(static p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var prunedRoots = new List<string>(normalizedRoots.Length);
        foreach (var candidate in normalizedRoots)
        {
            if (prunedRoots.Any(existingRoot => IsSameOrNestedPath(candidate, existingRoot)))
                continue;

            prunedRoots.Add(candidate);
        }

        return prunedRoots.ToArray();
    }

    private static void ValidateOutputOutsideRoots(
        string? outputPath,
        string label,
        IReadOnlyList<string> roots,
        ICollection<string> errors)
    {
        if (string.IsNullOrWhiteSpace(outputPath) || roots.Count == 0)
            return;

        string normalizedOutput;
        try
        {
            normalizedOutput = Path.GetFullPath(outputPath);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return;
        }

        if (roots.Any(root => IsSameOrNestedPath(normalizedOutput, root)))
            errors.Add($"{label} must not be inside any configured root path.");
    }

    private static bool IsSameOrNestedPath(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            return true;

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    public static RunOptions WithApproveConversionReview(RunOptions options, bool approveConversionReview)
    {
        ArgumentNullException.ThrowIfNull(options);

        return new RunOptions
        {
            Roots = options.Roots,
            Mode = options.Mode,
            PreferRegions = options.PreferRegions,
            Extensions = options.Extensions,
            RemoveJunk = options.RemoveJunk,
            OnlyGames = options.OnlyGames,
            KeepUnknownWhenOnlyGames = options.KeepUnknownWhenOnlyGames,
            AggressiveJunk = options.AggressiveJunk,
            SortConsole = options.SortConsole,
            EnableDat = options.EnableDat,
            EnableDatAudit = options.EnableDatAudit,
            EnableDatRename = options.EnableDatRename,
            DatRoot = options.DatRoot,
            HashType = options.HashType,
            ConvertFormat = options.ConvertFormat,
            ConvertOnly = options.ConvertOnly,
            ApproveReviews = options.ApproveReviews,
            ApproveConversionReview = approveConversionReview,
            TrashRoot = options.TrashRoot,
            AuditPath = options.AuditPath,
            ReportPath = options.ReportPath,
            ConflictPolicy = options.ConflictPolicy
        };
    }
}
