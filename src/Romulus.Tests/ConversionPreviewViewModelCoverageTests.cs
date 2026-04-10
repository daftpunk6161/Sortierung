using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ConversionPreviewViewModel: Load, Clear, HasItems, SummaryText.
/// </summary>
public sealed class ConversionPreviewViewModelCoverageTests
{
    private static ConversionPreviewViewModel Create() => new();

    [Fact]
    public void Items_InitiallyEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.Items);
        Assert.False(vm.HasItems);
    }

    [Fact]
    public void Load_PopulatesItems()
    {
        var vm = Create();
        var items = new[]
        {
            new ConversionPreviewItem("game.iso", ".iso", ".chd", "chdman", 1024 * 1024),
            new ConversionPreviewItem("sonic.bin", ".bin", ".chd", "chdman", 512 * 1024),
        };
        vm.Load(items);
        Assert.Equal(2, vm.Items.Count);
        Assert.True(vm.HasItems);
    }

    [Fact]
    public void Load_SetsCorrectItemProperties()
    {
        var vm = Create();
        var item = new ConversionPreviewItem("game.iso", ".iso", ".chd", "chdman", 2048);
        vm.Load([item]);
        Assert.Equal("game.iso", vm.Items[0].FileName);
        Assert.Equal(".iso", vm.Items[0].SourceFormat);
        Assert.Equal(".chd", vm.Items[0].TargetFormat);
        Assert.Equal("chdman", vm.Items[0].Tool);
        Assert.Equal(2048, vm.Items[0].FileSize);
    }

    [Fact]
    public void Clear_EmptiesItems()
    {
        var vm = Create();
        vm.Load([new ConversionPreviewItem("a.iso", ".iso", ".chd", "chdman", 100)]);
        Assert.True(vm.HasItems);
        vm.Clear();
        Assert.False(vm.HasItems);
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void SummaryText_DefaultEmpty()
    {
        var vm = Create();
        Assert.Equal("", vm.SummaryText);
    }

    [Fact]
    public void SummaryText_CanBeSet()
    {
        var vm = Create();
        vm.SummaryText = "3 Dateien, ~150 MB";
        Assert.Equal("3 Dateien, ~150 MB", vm.SummaryText);
    }

    [Fact]
    public void Load_EmptyList_NothingHappens()
    {
        var vm = Create();
        vm.Load([]);
        Assert.False(vm.HasItems);
    }

    [Fact]
    public void Load_ReplacesExistingItems()
    {
        var vm = Create();
        vm.Load([new ConversionPreviewItem("a.iso", ".iso", ".chd", "chdman", 100)]);
        Assert.Single(vm.Items);
        vm.Load([new ConversionPreviewItem("b.iso", ".iso", ".chd", "chdman", 200), new ConversionPreviewItem("c.iso", ".iso", ".chd", "chdman", 300)]);
        Assert.Equal(2, vm.Items.Count);
    }
}
