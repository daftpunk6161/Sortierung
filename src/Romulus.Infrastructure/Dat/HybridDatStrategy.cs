using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT policy for hybrid platforms (.wbfs/.rvz/.pbp and similar container formats).
/// Conservatively disables name-only fallback to reduce false positives.
/// </summary>
public sealed class HybridDatStrategy : IFamilyDatStrategy
{
    public PlatformFamily Family => PlatformFamily.Hybrid;

    public FamilyDatPolicy GetPolicy(string extension, string? hashStrategy)
    {
        return new FamilyDatPolicy(
            PreferArchiveInnerHash: true,
            UseHeaderlessHash: false,
            UseContainerHash: true,
            AllowNameOnlyDatMatch: false,
            RequireStrictNameForNameOnly: false,
            EnableCrossConsoleLookup: true);
    }
}
