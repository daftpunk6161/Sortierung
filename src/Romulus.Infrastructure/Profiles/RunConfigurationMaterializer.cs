using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Infrastructure.Profiles;

public sealed class RunConfigurationMaterializer
{
    private readonly RunConfigurationResolver _resolver;
    private readonly IRunOptionsFactory _runOptionsFactory;

    public RunConfigurationMaterializer(
        RunConfigurationResolver resolver,
        IRunOptionsFactory? runOptionsFactory = null)
    {
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _runOptionsFactory = runOptionsFactory ?? new RunOptionsFactory();
    }

    public async ValueTask<MaterializedRunConfiguration> MaterializeAsync(
        RunConfigurationDraft draft,
        RunConfigurationExplicitness explicitness,
        RomulusSettings settings,
        RunConfigurationDraft? baselineDraft = null,
        string? profileFilePath = null,
        string? auditPath = null,
        string? reportPath = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentNullException.ThrowIfNull(explicitness);
        ArgumentNullException.ThrowIfNull(settings);

        var resolved = await _resolver.ResolveAsync(draft, explicitness, profileFilePath, ct).ConfigureAwait(false);
        ValidateResolvedDraft(resolved.Draft);

        var effectiveDraft = BuildEffectiveDraft(resolved.Draft, baselineDraft, settings);
        var options = _runOptionsFactory.Create(new EffectiveRunOptionsSource(effectiveDraft), auditPath, reportPath);

        return new MaterializedRunConfiguration(
            effectiveDraft,
            resolved.Workflow,
            resolved.Profile,
            resolved.EffectiveProfileId,
            options);
    }

    private static void ValidateResolvedDraft(RunConfigurationDraft draft)
    {
        var validationSource = new RunProfileSettings
        {
            PreferRegions = draft.PreferRegions,
            Extensions = draft.Extensions,
            RemoveJunk = draft.RemoveJunk,
            OnlyGames = draft.OnlyGames,
            KeepUnknownWhenOnlyGames = draft.KeepUnknownWhenOnlyGames,
            AggressiveJunk = draft.AggressiveJunk,
            SortConsole = draft.SortConsole,
            EnableDat = draft.EnableDat,
            EnableDatAudit = draft.EnableDatAudit,
            EnableDatRename = draft.EnableDatRename,
            DatRoot = draft.DatRoot,
            HashType = draft.HashType,
            ConvertFormat = draft.ConvertFormat,
            ConvertOnly = draft.ConvertOnly,
            ApproveReviews = draft.ApproveReviews,
            ApproveConversionReview = draft.ApproveConversionReview,
            ConflictPolicy = draft.ConflictPolicy,
            TrashRoot = draft.TrashRoot,
            Mode = draft.Mode
        };

        var errors = RunProfileValidator.ValidateSettings(validationSource);
        if (errors.Count > 0)
            throw new InvalidOperationException(string.Join(" ", errors));
    }

    private static RunConfigurationDraft BuildEffectiveDraft(
        RunConfigurationDraft resolvedDraft,
        RunConfigurationDraft? baselineDraft,
        RomulusSettings settings)
    {
        var preferredRegions = resolvedDraft.PreferRegions is { Length: > 0 }
            ? NormalizeRegions(resolvedDraft.PreferRegions)
            : baselineDraft?.PreferRegions is { Length: > 0 }
                ? NormalizeRegions(baselineDraft.PreferRegions)
            : settings.General.PreferredRegions.Count > 0
                ? NormalizeRegions(settings.General.PreferredRegions)
                : RunConstants.DefaultPreferRegions;

        var extensions = resolvedDraft.Extensions is { Length: > 0 }
            ? NormalizeExtensions(resolvedDraft.Extensions)
            : baselineDraft?.Extensions is { Length: > 0 }
                ? NormalizeExtensions(baselineDraft.Extensions)
            : ResolveSettingsExtensions(settings);

        return resolvedDraft with
        {
            Mode = NormalizeMode(resolvedDraft.Mode ?? baselineDraft?.Mode ?? settings.General.Mode),
            PreferRegions = preferredRegions,
            Extensions = extensions,
            RemoveJunk = resolvedDraft.RemoveJunk ?? baselineDraft?.RemoveJunk ?? true,
            OnlyGames = resolvedDraft.OnlyGames ?? baselineDraft?.OnlyGames ?? false,
            KeepUnknownWhenOnlyGames = resolvedDraft.KeepUnknownWhenOnlyGames ?? baselineDraft?.KeepUnknownWhenOnlyGames ?? true,
            AggressiveJunk = resolvedDraft.AggressiveJunk ?? baselineDraft?.AggressiveJunk ?? settings.General.AggressiveJunk,
            SortConsole = resolvedDraft.SortConsole ?? baselineDraft?.SortConsole ?? false,
            EnableDat = resolvedDraft.EnableDat ?? baselineDraft?.EnableDat ?? settings.Dat.UseDat,
            EnableDatAudit = resolvedDraft.EnableDatAudit ?? baselineDraft?.EnableDatAudit ?? false,
            EnableDatRename = resolvedDraft.EnableDatRename ?? baselineDraft?.EnableDatRename ?? false,
            DatRoot = NormalizeOptionalPath(resolvedDraft.DatRoot ?? baselineDraft?.DatRoot ?? settings.Dat.DatRoot),
            HashType = NormalizeHashType(resolvedDraft.HashType ?? baselineDraft?.HashType ?? settings.Dat.HashType),
            ConvertFormat = NormalizeConvertFormat(resolvedDraft.ConvertFormat ?? baselineDraft?.ConvertFormat),
            ConvertOnly = resolvedDraft.ConvertOnly ?? baselineDraft?.ConvertOnly ?? false,
            ApproveReviews = resolvedDraft.ApproveReviews ?? baselineDraft?.ApproveReviews ?? false,
            ApproveConversionReview = resolvedDraft.ApproveConversionReview ?? baselineDraft?.ApproveConversionReview ?? false,
            ConflictPolicy = NormalizeConflictPolicy(resolvedDraft.ConflictPolicy ?? baselineDraft?.ConflictPolicy ?? RunConstants.DefaultConflictPolicy),
            TrashRoot = NormalizeOptionalPath(resolvedDraft.TrashRoot ?? baselineDraft?.TrashRoot)
        };
    }

    private static string[] ResolveSettingsExtensions(RomulusSettings settings)
    {
        if (string.IsNullOrWhiteSpace(settings.General.Extensions))
            return NormalizeExtensions(RunOptions.DefaultExtensions);

        return NormalizeExtensions(
            settings.General.Extensions.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    }

    private static string[] NormalizeRegions(IEnumerable<string> regions)
    {
        var normalized = regions
            .Where(static region => !string.IsNullOrWhiteSpace(region))
            .Select(static region => region.Trim().ToUpperInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? RunConstants.DefaultPreferRegions : normalized;
    }

    private static string[] NormalizeExtensions(IEnumerable<string> extensions)
    {
        var normalized = extensions
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .Select(static extension => extension.Trim())
            .Select(static extension => extension.StartsWith('.') ? extension : "." + extension)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static extension => extension, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return normalized.Length == 0 ? RunOptions.DefaultExtensions : normalized;
    }

    private static string NormalizeMode(string? mode)
        => string.Equals(mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase)
            ? RunConstants.ModeMove
            : RunConstants.ModeDryRun;

    private static string NormalizeHashType(string? hashType)
        => string.IsNullOrWhiteSpace(hashType)
            ? RunConstants.DefaultHashType
            : hashType.Trim().ToUpperInvariant();

    private static string? NormalizeConvertFormat(string? convertFormat)
        => string.IsNullOrWhiteSpace(convertFormat)
            ? null
            : convertFormat.Trim().ToLowerInvariant();

    private static string NormalizeConflictPolicy(string? conflictPolicy)
    {
        if (string.IsNullOrWhiteSpace(conflictPolicy))
            return RunConstants.DefaultConflictPolicy;

        foreach (var validPolicy in RunConstants.ValidConflictPolicies)
        {
            if (string.Equals(validPolicy, conflictPolicy.Trim(), StringComparison.OrdinalIgnoreCase))
                return validPolicy;
        }

        throw new InvalidOperationException(
            $"Invalid conflictPolicy '{conflictPolicy}'. Valid values: {string.Join(", ", RunConstants.ValidConflictPolicies.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))}.");
    }

    private static string? NormalizeOptionalPath(string? path)
        => string.IsNullOrWhiteSpace(path) ? null : path.Trim();

    private sealed class EffectiveRunOptionsSource : IRunOptionsSource
    {
        public EffectiveRunOptionsSource(RunConfigurationDraft effectiveDraft)
        {
            Roots = effectiveDraft.Roots;
            Mode = effectiveDraft.Mode ?? RunConstants.ModeDryRun;
            PreferRegions = effectiveDraft.PreferRegions ?? RunConstants.DefaultPreferRegions;
            Extensions = effectiveDraft.Extensions ?? RunOptions.DefaultExtensions;
            RemoveJunk = effectiveDraft.RemoveJunk ?? true;
            OnlyGames = effectiveDraft.OnlyGames ?? false;
            KeepUnknownWhenOnlyGames = effectiveDraft.KeepUnknownWhenOnlyGames ?? true;
            AggressiveJunk = effectiveDraft.AggressiveJunk ?? false;
            SortConsole = effectiveDraft.SortConsole ?? false;
            EnableDat = effectiveDraft.EnableDat ?? false;
            EnableDatAudit = effectiveDraft.EnableDatAudit ?? false;
            EnableDatRename = effectiveDraft.EnableDatRename ?? false;
            DatRoot = effectiveDraft.DatRoot;
            HashType = effectiveDraft.HashType ?? RunConstants.DefaultHashType;
            ConvertFormat = effectiveDraft.ConvertFormat;
            ConvertOnly = effectiveDraft.ConvertOnly ?? false;
            ApproveReviews = effectiveDraft.ApproveReviews ?? false;
            ApproveConversionReview = effectiveDraft.ApproveConversionReview ?? false;
            TrashRoot = effectiveDraft.TrashRoot;
            ConflictPolicy = effectiveDraft.ConflictPolicy ?? RunConstants.DefaultConflictPolicy;
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
        public bool EnableDatAudit { get; }
        public bool EnableDatRename { get; }
        public string? DatRoot { get; }
        public string HashType { get; }
        public string? ConvertFormat { get; }
        public bool ConvertOnly { get; }
        public bool ApproveReviews { get; }
        public bool ApproveConversionReview { get; }
        public string? TrashRoot { get; }
        public string ConflictPolicy { get; }
    }
}
