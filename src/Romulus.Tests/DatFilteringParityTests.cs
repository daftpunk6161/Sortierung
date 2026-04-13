using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F6: DAT-Filterung Parity Test — verifies that the canonical filter logic for excluding
/// nointro-pack entries from URL-based downloads uses the shared RunConstants.FormatNoIntroPack
/// constant, and that the DatCatalogStateService download-strategy determination is consistent.
/// </summary>
public sealed class DatFilteringParityTests
{
    /// <summary>
    /// The canonical filter predicate used by CLI and API to select URL-downloadable entries.
    /// Both entry points use: e => !string.IsNullOrWhiteSpace(e.Url)
    ///   &amp;&amp; !string.Equals(e.Format, RunConstants.FormatNoIntroPack, OrdinalIgnoreCase)
    /// This test verifies that predicate against DatCatalogStateService.DetermineDownloadStrategy().
    /// </summary>
    [Fact]
    public void NoIntroPack_ExcludedFromDownload_ByCanonicalFilter()
    {
        var entry = new DatCatalogEntry
        {
            Id = "nointro-nes",
            Group = "No-Intro",
            System = "Nintendo - NES",
            ConsoleKey = "NES",
            Format = RunConstants.FormatNoIntroPack,
            Url = ""
        };

        // Canonical filter: has URL and is not nointro-pack
        bool passesFilter = !string.IsNullOrWhiteSpace(entry.Url)
            && !string.Equals(entry.Format, RunConstants.FormatNoIntroPack, StringComparison.OrdinalIgnoreCase);

        Assert.False(passesFilter, "nointro-pack entry should be excluded from download filter");

        // DatCatalogStateService should agree: strategy is PackImport
        var strategy = DatCatalogStateService.DetermineDownloadStrategy(entry);
        Assert.Equal(DatDownloadStrategy.PackImport, strategy);
    }

    [Fact]
    public void NoIntroPack_CaseInsensitive_ExcludedFromDownload()
    {
        var variants = new[] { "nointro-pack", "NOINTRO-PACK", "NoIntro-Pack" };
        foreach (var format in variants)
        {
            var entry = new DatCatalogEntry
            {
                Id = "test",
                Group = "No-Intro",
                System = "Test",
                ConsoleKey = "TST",
                Format = format,
                Url = ""
            };

            bool passesFilter = !string.IsNullOrWhiteSpace(entry.Url)
                && !string.Equals(entry.Format, RunConstants.FormatNoIntroPack, StringComparison.OrdinalIgnoreCase);

            Assert.False(passesFilter, $"Format '{format}' should be excluded from download filter");
            Assert.Equal(DatDownloadStrategy.PackImport, DatCatalogStateService.DetermineDownloadStrategy(entry));
        }
    }

    [Fact]
    public void AutoDownloadable_Entry_PassesFilter_AndStrategyIsAuto()
    {
        var entry = new DatCatalogEntry
        {
            Id = "nointro-gba",
            Group = "No-Intro",
            System = "Nintendo - GBA",
            ConsoleKey = "GBA",
            Format = "zip-dat",
            Url = "https://example.com/gba.zip"
        };

        bool passesFilter = !string.IsNullOrWhiteSpace(entry.Url)
            && !string.Equals(entry.Format, RunConstants.FormatNoIntroPack, StringComparison.OrdinalIgnoreCase);

        Assert.True(passesFilter, "zip-dat entry with URL should pass download filter");
        Assert.Equal(DatDownloadStrategy.Auto, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    [Fact]
    public void Redump_Entry_PassesFilter_ButStrategyIsManualLogin()
    {
        var entry = new DatCatalogEntry
        {
            Id = "redump-psx",
            Group = "Redump",
            System = "Sony - PlayStation",
            ConsoleKey = "PSX",
            Format = "zip-dat",
            Url = "https://example.com/psx.zip"
        };

        // Canonical filter passes (has URL, not nointro-pack)
        bool passesFilter = !string.IsNullOrWhiteSpace(entry.Url)
            && !string.Equals(entry.Format, RunConstants.FormatNoIntroPack, StringComparison.OrdinalIgnoreCase);

        Assert.True(passesFilter, "Redump entry with URL passes the basic download filter");

        // But DatCatalogStateService knows Redump requires manual login
        Assert.Equal(DatDownloadStrategy.ManualLogin, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    [Fact]
    public void FormatNoIntroPack_Constant_MatchesExpectedValue()
    {
        Assert.Equal("nointro-pack", RunConstants.FormatNoIntroPack);
    }
}
