using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Family-specific DAT lookup policy used by the enrichment pipeline.
/// Determines which DAT lookup stages are allowed for a platform family.
/// </summary>
public sealed record FamilyDatPolicy(
    bool PreferArchiveInnerHash = true,
    bool UseHeaderlessHash = false,
    bool UseContainerHash = true,
    bool AllowNameOnlyDatMatch = false,
    bool RequireStrictNameForNameOnly = false,
    bool EnableCrossConsoleLookup = true)
{
    public static FamilyDatPolicy Default { get; } = new();
}

/// <summary>
/// Strategy contract for family-specific DAT behavior.
/// </summary>
public interface IFamilyDatStrategy
{
    PlatformFamily Family { get; }

    FamilyDatPolicy GetPolicy(string extension, string? hashStrategy);
}

/// <summary>
/// Resolves the effective DAT policy for a platform family.
/// </summary>
public interface IFamilyDatStrategyResolver
{
    FamilyDatPolicy ResolvePolicy(PlatformFamily family, string extension, string? hashStrategy);
}
