using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT policy for Redump disc families.
/// Prefers container/track hashes and allows conservative name-only fallback for disc image formats.
/// </summary>
public sealed class DiscDatStrategy : IFamilyDatStrategy
{
    public PlatformFamily Family => PlatformFamily.RedumpDisc;

    public FamilyDatPolicy GetPolicy(string extension, string? hashStrategy)
    {
        var ext = (extension ?? string.Empty).ToLowerInvariant();
        var allowNameOnly = ext is ".chd" or ".iso" or ".gcm" or ".img" or ".cso" or ".rvz" or ".bin";

        return new FamilyDatPolicy(
            PreferArchiveInnerHash: true,
            UseHeaderlessHash: false,
            UseContainerHash: true,
            AllowNameOnlyDatMatch: allowNameOnly,
            RequireStrictNameForNameOnly: false,
            EnableCrossConsoleLookup: true);
    }
}
