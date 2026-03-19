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
        hashType = string.IsNullOrWhiteSpace(hashType) ? "SHA1" : hashType.Trim().ToUpperInvariant();
        var datRoot = !string.IsNullOrWhiteSpace(cli.DatRoot) ? cli.DatRoot : settings.Dat.DatRoot;

        // Audit path default for Move mode
        var auditPath = cli.AuditPath;
        if (string.IsNullOrEmpty(auditPath) && cli.Mode == "Move")
        {
            var auditDir = ArtifactPathResolver.GetArtifactDirectory(cli.Roots, "audit-logs");
            auditPath = Path.Combine(Path.GetFullPath(auditDir),
                $"audit-{DateTime.UtcNow:yyyyMMdd-HHmmss-fff}-{Guid.NewGuid():N}.csv");
        }

        var source = new CliRunOptionsSource(
            roots: cli.Roots,
            mode: cli.Mode,
            preferRegions: cli.PreferRegions.Length > 0
                ? cli.PreferRegions
                : settings.General.PreferredRegions.ToArray(),
            extensions: cli.Extensions.ToArray(),
            removeJunk: cli.RemoveJunk,
            onlyGames: cli.OnlyGames,
            keepUnknownWhenOnlyGames: cli.KeepUnknownWhenOnlyGames,
            aggressiveJunk: cli.AggressiveJunk,
            sortConsole: cli.SortConsole,
            enableDat: enableDat,
            datRoot: datRoot,
            hashType: hashType,
            convertFormat: cli.ConvertFormat ? "auto" : null,
            convertOnly: cli.ConvertOnly,
            trashRoot: cli.TrashRoot,
            conflictPolicy: cli.ConflictPolicy);

        var runOptions = new RunOptionsFactory().Create(source, auditPath, cli.ReportPath);

        return (runOptions, null);
    }

    private sealed class CliRunOptionsSource : IRunOptionsSource
    {
        public CliRunOptionsSource(
            IReadOnlyList<string> roots,
            string mode,
            string[] preferRegions,
            IReadOnlyList<string> extensions,
            bool removeJunk,
            bool onlyGames,
            bool keepUnknownWhenOnlyGames,
            bool aggressiveJunk,
            bool sortConsole,
            bool enableDat,
            string? datRoot,
            string hashType,
            string? convertFormat,
            bool convertOnly,
            string? trashRoot,
            string conflictPolicy)
        {
            Roots = roots;
            Mode = mode;
            PreferRegions = preferRegions;
            Extensions = extensions;
            RemoveJunk = removeJunk;
            OnlyGames = onlyGames;
            KeepUnknownWhenOnlyGames = keepUnknownWhenOnlyGames;
            AggressiveJunk = aggressiveJunk;
            SortConsole = sortConsole;
            EnableDat = enableDat;
            DatRoot = datRoot;
            HashType = hashType;
            ConvertFormat = convertFormat;
            ConvertOnly = convertOnly;
            TrashRoot = trashRoot;
            ConflictPolicy = conflictPolicy;
        }

        public IReadOnlyList<string> Roots { get; }
        public string Mode { get; }
        public string[] PreferRegions { get; }
        public IReadOnlyList<string> Extensions { get; }
        public bool RemoveJunk { get; }
        public bool OnlyGames { get; }
        public bool KeepUnknownWhenOnlyGames { get; }
        public bool AggressiveJunk { get; }
        public bool SortConsole { get; }
        public bool EnableDat { get; }
        public string? DatRoot { get; }
        public string HashType { get; }
        public string? ConvertFormat { get; }
        public bool ConvertOnly { get; }
        public string? TrashRoot { get; }
        public string ConflictPolicy { get; }
    }
}
