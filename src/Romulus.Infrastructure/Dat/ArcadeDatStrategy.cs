using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT policy for arcade families.
/// Prefers set/archive hashes and allows strict name fallback for set-style naming.
/// </summary>
public sealed class ArcadeDatStrategy : IFamilyDatStrategy
{
    public PlatformFamily Family => PlatformFamily.Arcade;

    public FamilyDatPolicy GetPolicy(string extension, string? hashStrategy)
    {
        return new FamilyDatPolicy(
            PreferArchiveInnerHash: true,
            UseHeaderlessHash: false,
            UseContainerHash: true,
            AllowNameOnlyDatMatch: true,
            RequireStrictNameForNameOnly: true,
            EnableCrossConsoleLookup: true);
    }
}
