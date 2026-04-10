using Romulus.Contracts.Models;
using Romulus.Infrastructure.Deduplication;
using Romulus.Infrastructure.Linking;
using Xunit;

namespace Romulus.Tests;

public class CrossRootDeduplicatorTests
{
    [Fact]
    public void FindDuplicates_SameHash_DifferentRoots()
    {
        var files = new List<CrossRootFile>
        {
            new() { Path = @"C:\Root1\game.zip", Root = @"C:\Root1", Hash = "abc123", Extension = ".zip", SizeBytes = 1000 },
            new() { Path = @"D:\Root2\game.zip", Root = @"D:\Root2", Hash = "abc123", Extension = ".zip", SizeBytes = 1000 }
        };
        var groups = CrossRootDeduplicator.FindDuplicates(files);
        Assert.Single(groups);
        Assert.Equal(2, groups[0].Files.Count);
    }

    [Fact]
    public void FindDuplicates_SameRoot_Excluded()
    {
        var files = new List<CrossRootFile>
        {
            new() { Path = @"C:\Root1\a.zip", Root = @"C:\Root1", Hash = "abc", Extension = ".zip" },
            new() { Path = @"C:\Root1\b.zip", Root = @"C:\Root1", Hash = "abc", Extension = ".zip" }
        };
        var groups = CrossRootDeduplicator.FindDuplicates(files);
        Assert.Empty(groups); // same root
    }

    [Fact]
    public void FindDuplicates_NoDuplicates()
    {
        var files = new List<CrossRootFile>
        {
            new() { Path = @"C:\R1\a.zip", Root = @"C:\R1", Hash = "h1", Extension = ".zip" },
            new() { Path = @"D:\R2\b.zip", Root = @"D:\R2", Hash = "h2", Extension = ".zip" }
        };
        var groups = CrossRootDeduplicator.FindDuplicates(files);
        Assert.Empty(groups);
    }

    [Fact]
    public void FindDuplicates_EmptyHash_Ignored()
    {
        var files = new List<CrossRootFile>
        {
            new() { Path = @"C:\R1\a.zip", Root = @"C:\R1", Hash = "", Extension = ".zip" },
            new() { Path = @"D:\R2\b.zip", Root = @"D:\R2", Hash = "", Extension = ".zip" }
        };
        var groups = CrossRootDeduplicator.FindDuplicates(files);
        Assert.Empty(groups);
    }

    [Fact]
    public void GetMergeAdvice_KeepsBestFormat()
    {
        var group = new CrossRootDuplicateGroup
        {
            Hash = "abc",
            Files = new List<CrossRootFile>
            {
                new() { Path = @"C:\R1\game.zip", Root = @"C:\R1", Hash = "abc", Extension = ".zip", SizeBytes = 500 },
                new() { Path = @"D:\R2\game.chd", Root = @"D:\R2", Hash = "abc", Extension = ".chd", SizeBytes = 400 }
            }
        };
        var advice = CrossRootDeduplicator.GetMergeAdvice(group);
        Assert.Equal(".chd", advice.Keep.Extension); // CHD=850 > ZIP=500
        Assert.Single(advice.Remove);
        Assert.Equal(".zip", advice.Remove[0].Extension);
    }

    [Fact]
    public void GetMergeAdvice_TiebreakBySize()
    {
        var group = new CrossRootDuplicateGroup
        {
            Hash = "x",
            Files = new List<CrossRootFile>
            {
                new() { Path = @"C:\R1\game.iso", Root = @"C:\R1", Hash = "x", Extension = ".iso", SizeBytes = 100 },
                new() { Path = @"D:\R2\game.iso", Root = @"D:\R2", Hash = "x", Extension = ".iso", SizeBytes = 200 }
            }
        };
        var advice = CrossRootDeduplicator.GetMergeAdvice(group);
        Assert.Equal(200, advice.Keep.SizeBytes); // larger wins tiebreak
    }

    [Fact]
    public void GetMergeAdvice_UsesSameWinnerTruthAsDeduplicationEngine()
    {
        var group = new CrossRootDuplicateGroup
        {
            Hash = "samehash",
            Files =
            [
                new CrossRootFile
                {
                    Path = @"C:\R1\Game.chd",
                    Root = @"C:\R1",
                    Hash = "samehash",
                    Extension = ".chd",
                    SizeBytes = 500,
                    FormatScore = 850,
                    Category = FileCategory.Unknown,
                    CompletenessScore = 0,
                    DatMatch = false
                },
                new CrossRootFile
                {
                    Path = @"D:\R2\Game.iso",
                    Root = @"D:\R2",
                    Hash = "samehash",
                    Extension = ".iso",
                    SizeBytes = 400,
                    FormatScore = 700,
                    Category = FileCategory.Game,
                    CompletenessScore = 50,
                    DatMatch = true
                }
            ]
        };

        var advice = CrossRootDeduplicator.GetMergeAdvice(group);

        Assert.Equal(@".iso", advice.Keep.Extension);
        Assert.Single(advice.Remove);
        Assert.Equal(@".chd", advice.Remove[0].Extension);
    }
}

public class HardlinkServiceTests
{
    [Fact]
    public void CreateConfig_SetsValues()
    {
        var config = HardlinkService.CreateConfig(@"C:\Source", @"C:\Target", LinkType.Symlink, LinkGroupBy.Region);
        Assert.Equal(@"C:\Source", config.SourceRoot);
        Assert.Equal(LinkType.Symlink, config.LinkType);
        Assert.Equal(LinkGroupBy.Region, config.GroupBy);
    }

    [Fact]
    public void CreateOperation_PendingByDefault()
    {
        var op = HardlinkService.CreateOperation(@"C:\src\file.zip", @"C:\tgt\file.zip");
        Assert.Equal("Pending", op.Status);
        Assert.Equal(LinkType.Hardlink, op.LinkType);
    }

    [Fact]
    public void BuildPlan_GroupsByConsole()
    {
        var config = HardlinkService.CreateConfig(@"C:\src", @"C:\tgt");
        var files = new List<(string, string?, string?, string?)>
        {
            (@"C:\src\game1.zip", "SNES", null, "EU"),
            (@"C:\src\game2.zip", "NES", null, "US")
        };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Equal(2, plan.Operations.Count);
        Assert.Contains("SNES", plan.Operations[0].TargetPath);
        Assert.Contains("NES", plan.Operations[1].TargetPath);
    }

    [Fact]
    public void BuildPlan_GroupsByRegion()
    {
        var config = HardlinkService.CreateConfig(@"C:\src", @"C:\tgt", groupBy: LinkGroupBy.Region);
        var files = new List<(string, string?, string?, string?)>
        {
            (@"C:\src\game.zip", "SNES", null, "EU")
        };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Contains("EU", plan.Operations[0].TargetPath);
    }

    [Fact]
    public void BuildPlan_HardlinkSaves100Percent()
    {
        var config = HardlinkService.CreateConfig(@"C:\src", @"C:\tgt", LinkType.Hardlink);
        var plan = HardlinkService.BuildPlan(config, new List<(string, string?, string?, string?)>());
        Assert.Equal(0, plan.Savings.FileCount);
    }

    [Fact]
    public void GetStatistics_CountsCorrectly()
    {
        var ops = new List<LinkOperation>
        {
            new() { Status = "Completed" },
            new() { Status = "Pending" },
            new() { Status = "Failed" },
            new() { Status = "Completed" }
        };
        var stats = HardlinkService.GetStatistics(ops);
        Assert.Equal(2, stats.Completed);
        Assert.Equal(1, stats.Pending);
        Assert.Equal(1, stats.Failed);
        Assert.Equal(4, stats.Total);
    }
}
