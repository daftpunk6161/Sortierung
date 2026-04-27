using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-DAT-12: BIOS detection must not flag legitimate game titles whose names
/// contain BIOS-like substrings ("Boot Camp", "Firmware Update Game", "BIOS Wars",
/// "IPL Demo"). Only the No-Intro / MAME bracketed-tag convention is safe to
/// trust as a heuristic; the structural &lt;isbios&gt;/&lt;isdevice&gt; XML attribute
/// remains the primary classification signal upstream.
/// </summary>
public sealed class BiosHeuristicFalsePositiveTests
{
    [Theory]
    [InlineData("Boot Camp")]
    [InlineData("Firmware Update Game")]
    [InlineData("BIOS Wars")]
    [InlineData("IPL Demo")]
    [InlineData("Sysroom Hero")]
    [InlineData("Bootrom Bandit")]
    [InlineData("Bios Mendel - The Game")]
    public void IsLikelyBiosGameName_DoesNotFlagLegitimateGameTitles(string gameName)
    {
        Assert.False(DatRepositoryAdapter.IsLikelyBiosGameName(gameName, romFileName: null));
    }

    [Theory]
    [InlineData("Atari Lynx (USA) [BIOS]")]
    [InlineData("Sega CD Model 1 BIOS (USA)")] // not bracketed → must NOT match (was over-eager before)
    [InlineData("Console System (BIOS)")]
    [InlineData("Sega Mega-CD (Europe) [BIOS]")]
    [InlineData("Some Cartridge (Firmware)")]
    public void IsLikelyBiosGameName_FlagsBracketedBiosConvention(string gameName)
    {
        // The bracketed convention "[BIOS]" / "(BIOS)" / "[FIRMWARE]" etc. is the
        // No-Intro standard for BIOS dumps. Bare unbracketed mentions are
        // intentionally NOT enough — those must rely on the structural
        // <isbios>/<isdevice>/bios=... attribute upstream.
        var expected = gameName.Contains('[') || gameName.Contains('(');
        // The "Sega CD Model 1 BIOS (USA)" line above intentionally has parens
        // around "USA", not around BIOS, so it must NOT match.
        var hasBracketedBiosTag =
            gameName.Contains("[BIOS]", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("(BIOS)", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("[FIRMWARE]", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("(FIRMWARE)", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("[BOOT ROM]", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("(BOOT ROM)", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("[BOOTROM]", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("(BOOTROM)", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("[SYSROM]", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("(SYSROM)", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("[IPL]", StringComparison.OrdinalIgnoreCase) ||
            gameName.Contains("(IPL)", StringComparison.OrdinalIgnoreCase);

        Assert.Equal(hasBracketedBiosTag, DatRepositoryAdapter.IsLikelyBiosGameName(gameName, romFileName: null));
        _ = expected; // suppress unused warning; kept for documentation
    }

    [Fact]
    public void IsLikelyBiosGameName_FlagsRomFileNameWithBracketedTag()
    {
        Assert.True(DatRepositoryAdapter.IsLikelyBiosGameName(
            gameName: "Generic Title",
            romFileName: "neogeo [BIOS].rom"));
    }
}
