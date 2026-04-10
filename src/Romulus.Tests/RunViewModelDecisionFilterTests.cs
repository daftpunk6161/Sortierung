using System.Linq;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class RunViewModelDecisionFilterTests
{
    [Fact]
    public void DedupeGroupItemsView_FiltersByGameKeyAndFileNames()
    {
        var vm = new RunViewModel();
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "super-mario",
            Winner = new DedupeEntryItem
            {
                FileName = "Super Mario.sfc",
                Region = "EU",
                RegionScore = 10,
                FormatScore = 20,
                VersionScore = 30,
                DecisionClass = "Keep",
                EvidenceTier = "Strong",
                PrimaryMatchKind = "Hash",
                PlatformFamily = "Console",
                IsWinner = true
            },
            Losers =
            [
                new DedupeEntryItem
                {
                    FileName = "Super Mario (USA).sfc",
                    Region = "US",
                    RegionScore = 5,
                    FormatScore = 20,
                    VersionScore = 30,
                    DecisionClass = "Drop",
                    EvidenceTier = "Strong",
                    PrimaryMatchKind = "Hash",
                    PlatformFamily = "Console",
                    IsWinner = false
                }
            ]
        });
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "zelda",
            Winner = new DedupeEntryItem
            {
                FileName = "Zelda.sfc",
                Region = "EU",
                RegionScore = 10,
                FormatScore = 20,
                VersionScore = 30,
                DecisionClass = "Keep",
                EvidenceTier = "Strong",
                PrimaryMatchKind = "Hash",
                PlatformFamily = "Console",
                IsWinner = true
            },
            Losers = []
        });

        vm.DecisionSearchText = "mario";

        var filtered = vm.DedupeGroupItemsView.Cast<DedupeGroupItem>().ToArray();
        Assert.Single(filtered);
        Assert.Equal("super-mario", filtered[0].GameKey);

        vm.DecisionSearchText = "zelda.sfc";

        filtered = vm.DedupeGroupItemsView.Cast<DedupeGroupItem>().ToArray();
        Assert.Single(filtered);
        Assert.Equal("zelda", filtered[0].GameKey);

        vm.DecisionSearchText = "USA";

        filtered = vm.DedupeGroupItemsView.Cast<DedupeGroupItem>().ToArray();
        Assert.Single(filtered);
        Assert.Equal("super-mario", filtered[0].GameKey);
    }

    [Fact]
    public void DedupeGroupItemsView_EmptySearch_ShowsAllGroups()
    {
        var vm = new RunViewModel();
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "alpha",
            Winner = new DedupeEntryItem
            {
                FileName = "alpha.rom",
                Region = "EU",
                RegionScore = 1,
                FormatScore = 2,
                VersionScore = 3,
                DecisionClass = "Keep",
                EvidenceTier = "Strong",
                PrimaryMatchKind = "Hash",
                PlatformFamily = "Console",
                IsWinner = true
            },
            Losers = []
        });
        vm.DedupeGroupItems.Add(new DedupeGroupItem
        {
            GameKey = "beta",
            Winner = new DedupeEntryItem
            {
                FileName = "beta.rom",
                Region = "US",
                RegionScore = 1,
                FormatScore = 2,
                VersionScore = 3,
                DecisionClass = "Keep",
                EvidenceTier = "Strong",
                PrimaryMatchKind = "Hash",
                PlatformFamily = "Console",
                IsWinner = true
            },
            Losers = []
        });

        vm.DecisionSearchText = "alpha";
        Assert.Single(vm.DedupeGroupItemsView.Cast<DedupeGroupItem>());

        vm.DecisionSearchText = "";
        Assert.Equal(2, vm.DedupeGroupItemsView.Cast<DedupeGroupItem>().Count());
    }
}
