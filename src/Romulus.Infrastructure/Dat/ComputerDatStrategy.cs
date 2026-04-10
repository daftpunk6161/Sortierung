using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT policy for TOSEC-like computer platforms.
/// Uses strict name fallback because many sets rely on naming conventions.
/// </summary>
public sealed class ComputerDatStrategy : IFamilyDatStrategy
{
    public PlatformFamily Family => PlatformFamily.ComputerTOSEC;

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
