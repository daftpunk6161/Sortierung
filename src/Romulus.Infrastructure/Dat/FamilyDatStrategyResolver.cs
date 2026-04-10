using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// Resolves family-specific DAT policy and applies hashStrategy-aware overrides.
/// </summary>
public sealed class FamilyDatStrategyResolver : IFamilyDatStrategyResolver
{
    private readonly IReadOnlyDictionary<PlatformFamily, IFamilyDatStrategy> _strategies;
    private readonly IFamilyDatStrategy _fallback = new GenericDatStrategy();

    public FamilyDatStrategyResolver()
        : this(new IFamilyDatStrategy[]
        {
            new CartridgeDatStrategy(),
            new DiscDatStrategy(),
            new ArcadeDatStrategy(),
            new ComputerDatStrategy(),
            new HybridDatStrategy(),
            new FolderDatStrategy(),
        })
    {
    }

    public FamilyDatStrategyResolver(IEnumerable<IFamilyDatStrategy> strategies)
    {
        _strategies = strategies.ToDictionary(s => s.Family);
    }

    public FamilyDatPolicy ResolvePolicy(PlatformFamily family, string extension, string? hashStrategy)
    {
        if (!_strategies.TryGetValue(family, out var strategy))
            strategy = _fallback;

        var basePolicy = strategy.GetPolicy(extension, hashStrategy);
        return ApplyHashStrategyOverrides(basePolicy, hashStrategy);
    }

    private static FamilyDatPolicy ApplyHashStrategyOverrides(FamilyDatPolicy basePolicy, string? hashStrategy)
    {
        var strategy = (hashStrategy ?? string.Empty).Trim().ToLowerInvariant();
        return strategy switch
        {
            "headerless-sha1" => basePolicy with
            {
                UseHeaderlessHash = true,
                UseContainerHash = true
            },
            "track-sha1" => basePolicy with
            {
                UseHeaderlessHash = false,
                UseContainerHash = true,
                AllowNameOnlyDatMatch = true
            },
            "container-sha1" => basePolicy with
            {
                UseHeaderlessHash = false,
                UseContainerHash = true
            },
            "set-archive-sha1" => basePolicy with
            {
                PreferArchiveInnerHash = true,
                UseHeaderlessHash = false,
                UseContainerHash = true,
                RequireStrictNameForNameOnly = true
            },
            "folder-signature" => basePolicy with
            {
                PreferArchiveInnerHash = false,
                UseHeaderlessHash = false,
                UseContainerHash = true,
                AllowNameOnlyDatMatch = false,
                RequireStrictNameForNameOnly = false
            },
            _ => basePolicy
        };
    }

    private sealed class GenericDatStrategy : IFamilyDatStrategy
    {
        public PlatformFamily Family => PlatformFamily.Unknown;

        public FamilyDatPolicy GetPolicy(string extension, string? hashStrategy)
        {
            var ext = (extension ?? string.Empty).ToLowerInvariant();
            var allowNameOnly = ext is ".chd" or ".iso" or ".gcm" or ".img" or ".cso" or ".rvz";

            return new FamilyDatPolicy(
                PreferArchiveInnerHash: true,
                UseHeaderlessHash: false,
                UseContainerHash: true,
                AllowNameOnlyDatMatch: allowNameOnly,
                RequireStrictNameForNameOnly: false,
                EnableCrossConsoleLookup: true);
        }
    }
}
