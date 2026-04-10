using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT policy for No-Intro-like cartridge platforms.
/// Prefers headerless hashing while keeping container fallback for compatibility.
/// </summary>
public sealed class CartridgeDatStrategy : IFamilyDatStrategy
{
    public PlatformFamily Family => PlatformFamily.NoIntroCartridge;

    public FamilyDatPolicy GetPolicy(string extension, string? hashStrategy)
    {
        var strategy = (hashStrategy ?? string.Empty).Trim().ToLowerInvariant();

        var useHeaderless = strategy switch
        {
            "container-sha1" => false,
            "track-sha1" => false,
            "set-archive-sha1" => false,
            _ => true // default/headerless-sha1
        };

        return new FamilyDatPolicy(
            PreferArchiveInnerHash: true,
            UseHeaderlessHash: useHeaderless,
            UseContainerHash: true,
            AllowNameOnlyDatMatch: false,
            RequireStrictNameForNameOnly: false,
            EnableCrossConsoleLookup: true);
    }
}
