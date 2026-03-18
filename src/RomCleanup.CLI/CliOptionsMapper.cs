using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Paths;

namespace RomCleanup.CLI;

/// <summary>
/// Maps CliRunOptions + RomCleanupSettings → RunOptions.
/// Handles: settings merge, extensions merge, audit path default, root validation.
/// ADR-008 §C-02.
/// </summary>
internal static class CliOptionsMapper
{
    /// <summary>
    /// Merge CLI options with loaded settings into RunOptions.
    /// Returns null + errors when post-merge validation fails.
    /// </summary>
    internal static (RunOptions? runOptions, IReadOnlyList<string>? errors) Map(
        CliRunOptions cli, RomCleanupSettings settings)
    {
        // Settings merge: CLI overrides settings
        if (cli.PreferRegions.Length > 0)
            settings.General.PreferredRegions = new List<string>(cli.PreferRegions);
        settings.General.AggressiveJunk = cli.AggressiveJunk;

        // Extensions merge: explicit CLI → use CLI; not explicit → settings → DefaultExtensions
        if (!cli.ExtensionsExplicit && !string.IsNullOrWhiteSpace(settings.General.Extensions))
        {
            var settingsExts = settings.General.Extensions
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(e => e.Trim())
                .Where(e => e.Length > 0)
                .Select(e => e.StartsWith('.') ? e : "." + e);
            foreach (var ext in settingsExts)
                cli.Extensions.Add(ext);
        }

        // DAT merge
        var enableDat = cli.EnableDat || settings.Dat.UseDat;
        var hashType = !string.IsNullOrWhiteSpace(cli.HashType) ? cli.HashType : settings.Dat.HashType;
        var datRoot = !string.IsNullOrWhiteSpace(cli.DatRoot) ? cli.DatRoot : settings.Dat.DatRoot;

        // Audit path default for Move mode
        var auditPath = cli.AuditPath;
        if (string.IsNullOrEmpty(auditPath) && cli.Mode == "Move")
        {
            var auditDir = ArtifactPathResolver.GetArtifactDirectory(cli.Roots, "audit-logs");
            auditPath = Path.Combine(Path.GetFullPath(auditDir),
                $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.csv");
        }

        var runOptions = new RunOptions
        {
            Roots = cli.Roots,
            Mode = cli.Mode,
            PreferRegions = cli.PreferRegions.Length > 0
                ? cli.PreferRegions
                : settings.General.PreferredRegions.ToArray(),
            Extensions = cli.Extensions.ToArray(),
            RemoveJunk = cli.RemoveJunk,
            OnlyGames = cli.OnlyGames,
            KeepUnknownWhenOnlyGames = cli.KeepUnknownWhenOnlyGames,
            AggressiveJunk = cli.AggressiveJunk,
            SortConsole = cli.SortConsole,
            EnableDat = enableDat,
            DatRoot = datRoot,
            HashType = hashType,
            ConvertFormat = cli.ConvertFormat ? "auto" : null,
            ConvertOnly = cli.ConvertOnly,
            TrashRoot = cli.TrashRoot,
            AuditPath = auditPath,
            ReportPath = cli.ReportPath,
            ConflictPolicy = cli.ConflictPolicy
        };

        return (runOptions, null);
    }
}
