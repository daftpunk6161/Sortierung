using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT policy for folder-based platforms.
/// File-level lookup remains conservative until explicit folder-signature support is available.
/// </summary>
public sealed class FolderDatStrategy : IFamilyDatStrategy
{
    public PlatformFamily Family => PlatformFamily.FolderBased;

    public FamilyDatPolicy GetPolicy(string extension, string? hashStrategy)
    {
        return new FamilyDatPolicy(
            PreferArchiveInnerHash: false,
            UseHeaderlessHash: false,
            UseContainerHash: true,
            AllowNameOnlyDatMatch: false,
            RequireStrictNameForNameOnly: false,
            EnableCrossConsoleLookup: true);
    }
}
