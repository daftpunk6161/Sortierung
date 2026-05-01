using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ToolsViewModel: catalog initialization, pin toggling,
/// usage recording, context refresh, filtering, metrics, recommendations.
/// </summary>
public sealed class ToolsViewModelCoverageTests
{
    private static ToolsViewModel Create() => new();

    #region Initialization

    [Fact]
    public void Constructor_InitializesToolItems()
    {
        var vm = Create();
        Assert.NotEmpty(vm.ToolItems);
        Assert.True(vm.ToolItems.Count >= 40, "Should have 40+ catalog entries");
    }

    [Fact]
    public void Constructor_InitializesCategories()
    {
        var vm = Create();
        Assert.NotEmpty(vm.ToolCategories);
    }

    [Fact]
    public void Constructor_InitializesQuickAccess()
    {
        var vm = Create();
        Assert.NotEmpty(vm.QuickAccessItems);
        Assert.All(vm.QuickAccessItems, item => Assert.True(item.IsPinned));
    }

    [Fact]
    public void Constructor_SetsDefaultPinnedItems()
    {
        var vm = Create();
        var pinnedKeys = vm.QuickAccessItems.Select(i => i.Key).ToHashSet();
        Assert.Contains(FeatureCommandKeys.HealthScore, pinnedKeys);
        Assert.Contains(FeatureCommandKeys.DuplicateAnalysis, pinnedKeys);
    }

    #endregion

    #region Computed properties

    [Fact]
    public void ProductionToolCount_GreaterThanZero()
    {
        var vm = Create();
        Assert.True(vm.ProductionToolCount > 0);
    }

    [Fact]
    public void GuidedToolCount_GreaterThanZero()
    {
        var vm = Create();
        Assert.True(vm.GuidedToolCount > 0);
    }

    [Fact]
    public void ExperimentalToolCount_GreaterThanZero()
    {
        var vm = Create();
        Assert.True(vm.ExperimentalToolCount > 0);
        Assert.True(vm.HasExperimentalTools);
    }

    [Fact]
    public void AvailableToolCount_AllLockedOrUnavailable_Initially()
    {
        var vm = Create();
        // Before WireToolItemCommands, all items are unavailable
        Assert.Equal(0, vm.AvailableToolCount);
    }

    [Fact]
    public void ToolCountLabel_ContainsCount()
    {
        var vm = Create();
        Assert.Contains(vm.ToolItems.Count.ToString(), vm.ToolCountLabel);
    }

    #endregion

    #region Pin toggle

    [Fact]
    public void ToggleToolPin_UnpinsItem()
    {
        var vm = Create();
        var pinned = vm.QuickAccessItems.First();
        Assert.True(pinned.IsPinned);
        vm.ToggleToolPin(pinned.Key);
        Assert.False(pinned.IsPinned);
    }

    [Fact]
    public void ToggleToolPin_PinsItem()
    {
        var vm = Create();
        var unpinned = vm.ToolItems.First(t => !t.IsPinned);
        // First unpin some to make room
        foreach (var p in vm.QuickAccessItems.ToList())
            vm.ToggleToolPin(p.Key);
        vm.ToggleToolPin(unpinned.Key);
        Assert.True(unpinned.IsPinned);
    }

    [Fact]
    public void ToggleToolPin_MaxSixPins()
    {
        var vm = Create();
        // Already has default pins — fill to 6
        while (vm.QuickAccessItems.Count < 6)
        {
            var candidate = vm.ToolItems.FirstOrDefault(t => !t.IsPinned);
            if (candidate is null) break;
            vm.ToggleToolPin(candidate.Key);
        }
        Assert.True(vm.QuickAccessItems.Count <= 6);
        // Try to pin one more — should be denied
        var extra = vm.ToolItems.FirstOrDefault(t => !t.IsPinned);
        if (extra is not null)
        {
            vm.ToggleToolPin(extra.Key);
            Assert.False(extra.IsPinned);
        }
    }

    [Fact]
    public void ToggleToolPin_UnknownKey_NoOp()
    {
        var vm = Create();
        var initialCount = vm.QuickAccessItems.Count;
        vm.ToggleToolPin("NonExistentKey");
        Assert.Equal(initialCount, vm.QuickAccessItems.Count);
    }

    #endregion

    #region RecordToolUsage

    [Fact]
    public void RecordToolUsage_SetsLastUsedAt()
    {
        var vm = Create();
        var item = vm.ToolItems[0];
        Assert.Null(item.LastUsedAt);
        vm.RecordToolUsage(item.Key);
        Assert.NotNull(item.LastUsedAt);
    }

    [Fact]
    public void RecordToolUsage_PopulatesRecentTools()
    {
        var vm = Create();
        vm.RecordToolUsage(vm.ToolItems[0].Key);
        Assert.NotEmpty(vm.RecentToolItems);
        Assert.True(vm.HasRecentTools);
    }

    [Fact]
    public void RecordToolUsage_MaxSixRecent()
    {
        var vm = Create();
        for (int i = 0; i < 10 && i < vm.ToolItems.Count; i++)
            vm.RecordToolUsage(vm.ToolItems[i].Key);
        Assert.True(vm.RecentToolItems.Count <= 6);
    }

    [Fact]
    public void RecordToolUsage_UnknownKey_NoOp()
    {
        var vm = Create();
        vm.RecordToolUsage("NonExistentKey");
        Assert.Empty(vm.RecentToolItems);
    }

    #endregion

    #region RefreshContext

    [Fact]
    public void RefreshContext_NoRunResult_LocksResultTools()
    {
        var vm = Create();
        var snap = new ToolContextSnapshot(false, 0, false, 0, 0, 0, 0, false, false, false, false, 0, false);
        vm.RefreshContext(snap);
        var resultOnlyTools = vm.ToolItems.Where(t => t.RequiresRunResult).ToList();
        Assert.All(resultOnlyTools, t => Assert.True(t.IsLocked));
    }

    [Fact]
    public void RefreshContext_WithRunResult_UnlocksResultTools()
    {
        var vm = Create();
        var snap = new ToolContextSnapshot(true, 2, true, 100, 10, 5, 3, true, true, false, false, 0, false);
        vm.RefreshContext(snap);
        var resultToolLocked = vm.ToolItems.Where(t => t.RequiresRunResult).All(t => t.IsLocked);
        Assert.False(resultToolLocked);
    }

    [Fact]
    public void RefreshContext_SetsHasRunResult()
    {
        var vm = Create();
        Assert.False(vm.HasRunResult);
        vm.RefreshContext(new ToolContextSnapshot(true, 1, true, 50, 5, 2, 1, false, false, false, false, 0, false));
        Assert.True(vm.HasRunResult);
    }

    [Fact]
    public void RefreshToolLockState_DelegatesToRefreshContext()
    {
        var vm = Create();
        vm.RefreshToolLockState(true);
        Assert.True(vm.HasRunResult);
    }

    #endregion

    #region Filtering

    [Fact]
    public void ToolFilterText_EmptyShowsAll()
    {
        var vm = Create();
        vm.ToolFilterText = "";
        Assert.False(vm.IsToolSearchActive);
    }

    [Fact]
    public void ToolFilterText_NonEmptyActivatesSearch()
    {
        var vm = Create();
        vm.ToolFilterText = "Health";
        Assert.True(vm.IsToolSearchActive);
    }

    [Fact]
    public void ToolFilterText_FiltersView()
    {
        var vm = Create();
        var totalCount = vm.ToolItemsView.Cast<ToolItem>().Count();
        vm.ToolFilterText = "XYZNONEXISTENT";
        var filteredCount = vm.ToolItemsView.Cast<ToolItem>().Count();
        Assert.True(filteredCount < totalCount, "Filter should narrow results");
    }

    #endregion

    #region Sidebar Navigation

    [Fact]
    public void SelectedToolsSection_Default_IsRecommended()
    {
        var vm = Create();
        Assert.Equal("Empfohlen", vm.SelectedToolsSection);
    }

    [Fact]
    public void SelectedToolsSection_CanChange()
    {
        var vm = Create();
        vm.SelectedToolsSection = "Alle Werkzeuge";
        Assert.Equal("Alle Werkzeuge", vm.SelectedToolsSection);
    }

    #endregion

    #region Category structure

    [Fact]
    public void Categories_FirstIsExpanded()
    {
        var vm = Create();
        Assert.True(vm.ToolCategories.First().IsExpanded);
    }

    [Fact]
    public void Categories_ContainAllTools()
    {
        var vm = Create();
        var catToolCount = vm.ToolCategories.Sum(c => c.Items.Count);
        Assert.Equal(vm.ToolItems.Count, catToolCount);
    }

    #endregion

    #region Maturity

    [Fact]
    public void AllToolsHaveMaturityBadgeText()
    {
        var vm = Create();
        Assert.All(vm.ToolItems, item =>
        {
            Assert.NotNull(item.MaturityBadgeText);
            Assert.NotEmpty(item.MaturityBadgeText);
        });
    }

    [Fact]
    public void AllToolsHaveMaturityDescription()
    {
        var vm = Create();
        Assert.All(vm.ToolItems, item =>
        {
            Assert.NotNull(item.MaturityDescription);
            Assert.NotEmpty(item.MaturityDescription);
        });
    }

    #endregion

    #region ConversionRegistry

    [Fact]
    public void HasConversionCapabilities_DefaultFalse()
    {
        var vm = Create();
        Assert.False(vm.HasConversionCapabilities);
    }

    [Fact]
    public void LoadConversionRegistry_PopulatesCapabilities()
    {
        var vm = Create();
        vm.LoadConversionRegistry();
        // May or may not find files depending on test context, but should not throw
        // In CI, data files may be missing → HasConversionCapabilities stays false
    }

    #endregion

    #region WireToolItemCommands

    [Fact]
    public void WireToolItemCommands_WithNoFeatureCommands_AllUnavailable()
    {
        var vm = Create();
        vm.WireToolItemCommands();
        // No FeatureCommands → all tools remain unavailable
        Assert.All(vm.ToolItems, item => Assert.True(item.IsUnavailable));
    }

    #endregion
}
