using Romulus.Contracts.Models;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for DatCatalogViewModel and DatCatalogItemVm:
/// filter logic, counters, computed properties, SelectAll, status display.
/// </summary>
public sealed class DatCatalogViewModelCoverageTests
{
    private static DatCatalogViewModel Create(Func<string>? getDatRoot = null, Action<string, string>? addLog = null)
        => new(getDatRoot: getDatRoot ?? (() => ""), addLog: addLog);

    private static DatCatalogItemVm MakeItem(
        string id = "test",
        string group = "TestGroup",
        string system = "TestSystem",
        string consoleKey = "TST",
        DatInstallStatus status = DatInstallStatus.Missing,
        DatDownloadStrategy strategy = DatDownloadStrategy.Auto) => new()
    {
        Id = id,
        Group = group,
        System = system,
        ConsoleKey = consoleKey,
        Status = status,
        DownloadStrategy = strategy,
        Url = "https://example.com",
        Format = "logiqx"
    };

    #region DatCatalogItemVm - Computed Properties

    [Theory]
    [InlineData(DatInstallStatus.Installed, "Aktuell")]
    [InlineData(DatInstallStatus.Stale, "Veraltet")]
    [InlineData(DatInstallStatus.Missing, "Fehlend")]
    [InlineData(DatInstallStatus.Error, "Fehler")]
    public void StatusDisplay_ReflectsStatus(DatInstallStatus status, string expectedContains)
    {
        var item = MakeItem(status: status);
        Assert.Contains(expectedContains, item.StatusDisplay);
    }

    [Fact]
    public void ActionDisplay_InstalledIsAktuell()
    {
        var item = MakeItem(status: DatInstallStatus.Installed);
        Assert.Equal("Aktuell", item.ActionDisplay);
    }

    [Fact]
    public void ActionDisplay_MissingAutoIsDownload()
    {
        var item = MakeItem(status: DatInstallStatus.Missing, strategy: DatDownloadStrategy.Auto);
        Assert.Equal("Herunterladen", item.ActionDisplay);
    }

    [Fact]
    public void ActionDisplay_PackImport()
    {
        var item = MakeItem(status: DatInstallStatus.Missing, strategy: DatDownloadStrategy.PackImport);
        Assert.Equal("Pack importieren", item.ActionDisplay);
    }

    [Fact]
    public void ActionDisplay_ManualLogin()
    {
        var item = MakeItem(status: DatInstallStatus.Missing, strategy: DatDownloadStrategy.ManualLogin);
        Assert.Contains("Manuell", item.ActionDisplay);
    }

    [Fact]
    public void InstalledDateDisplay_NullShowsDash()
    {
        var item = MakeItem();
        Assert.Equal("—", item.InstalledDateDisplay);
    }

    [Fact]
    public void InstalledDateDisplay_ShowsDate()
    {
        var item = MakeItem();
        item.InstalledDate = new DateTime(2025, 3, 15);
        Assert.Equal("2025-03-15", item.InstalledDateDisplay);
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData(0L, "—")]
    [InlineData(512L, "512 B")]
    [InlineData(2048L, "2.0 KB")]
    [InlineData(2_097_152L, "2.0 MB")]
    public void FileSizeDisplay_FormatsCorrectly(long? size, string expected)
    {
        var item = new DatCatalogItemVm { FileSizeBytes = size };
        Assert.Equal(expected, item.FileSizeDisplay);
    }

    [Fact]
    public void IsSelected_DefaultFalse()
    {
        var item = MakeItem();
        Assert.False(item.IsSelected);
    }

    [Fact]
    public void Status_SetterRaisesPropertyChanged()
    {
        var item = MakeItem(status: DatInstallStatus.Missing);
        var raised = new List<string>();
        item.PropertyChanged += (_, e) => raised.Add(e.PropertyName!);
        item.Status = DatInstallStatus.Installed;
        Assert.Contains("Status", raised);
        Assert.Contains("StatusDisplay", raised);
        Assert.Contains("ActionDisplay", raised);
    }

    #endregion

    #region DatCatalogViewModel - Filter

    [Fact]
    public void Constructor_InitializesDefaults()
    {
        var vm = Create();
        Assert.NotNull(vm.EntriesView);
        Assert.Empty(vm.Entries);
        Assert.False(vm.HasData);
        Assert.Equal(0, vm.TotalCount);
        Assert.Equal("Alle", vm.SelectedGroupFilter);
        Assert.Equal("Alle", vm.SelectedStatusFilter);
    }

    [Fact]
    public void Filter_ByGroupFilter()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "a", group: "Nintendo"));
        vm.Entries.Add(MakeItem(id: "b", group: "Sony"));
        vm.SelectedGroupFilter = "Nintendo";
        var visible = vm.EntriesView.Cast<DatCatalogItemVm>().ToList();
        Assert.Single(visible);
        Assert.Equal("a", visible[0].Id);
    }

    [Fact]
    public void Filter_ByStatusFilter()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "installed", status: DatInstallStatus.Installed));
        vm.Entries.Add(MakeItem(id: "missing", status: DatInstallStatus.Missing));
        vm.SelectedStatusFilter = "Installiert";
        var visible = vm.EntriesView.Cast<DatCatalogItemVm>().ToList();
        Assert.Single(visible);
        Assert.Equal("installed", visible[0].Id);
    }

    [Fact]
    public void Filter_ByStatusFilter_Stale()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "stale", status: DatInstallStatus.Stale));
        vm.Entries.Add(MakeItem(id: "missing", status: DatInstallStatus.Missing));
        vm.SelectedStatusFilter = "Veraltet";
        Assert.Single(vm.EntriesView.Cast<DatCatalogItemVm>().ToList());
    }

    [Fact]
    public void Filter_BySearchText()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "snes", system: "Super Nintendo", consoleKey: "SNES"));
        vm.Entries.Add(MakeItem(id: "ps2", system: "PlayStation 2", consoleKey: "PS2"));
        vm.SearchText = "Nintendo";
        var visible = vm.EntriesView.Cast<DatCatalogItemVm>().ToList();
        Assert.Single(visible);
        Assert.Equal("snes", visible[0].Id);
    }

    [Fact]
    public void Filter_SearchByConsoleKey()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "snes", system: "Super Nintendo", consoleKey: "SNES"));
        vm.Entries.Add(MakeItem(id: "ps2", system: "PlayStation 2", consoleKey: "PS2"));
        vm.SearchText = "PS2";
        Assert.Single(vm.EntriesView.Cast<DatCatalogItemVm>().ToList());
    }

    [Fact]
    public void Filter_CombineGroupAndStatus()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "a", group: "Nintendo", status: DatInstallStatus.Installed));
        vm.Entries.Add(MakeItem(id: "b", group: "Nintendo", status: DatInstallStatus.Missing));
        vm.Entries.Add(MakeItem(id: "c", group: "Sony", status: DatInstallStatus.Installed));
        vm.SelectedGroupFilter = "Nintendo";
        vm.SelectedStatusFilter = "Fehlend";
        var visible = vm.EntriesView.Cast<DatCatalogItemVm>().ToList();
        Assert.Single(visible);
        Assert.Equal("b", visible[0].Id);
    }

    #endregion

    #region SelectAll

    [Fact]
    public void SelectAll_SetsAllVisibleItems()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "a"));
        vm.Entries.Add(MakeItem(id: "b"));
        vm.SelectAll = true;
        Assert.All(vm.Entries, item => Assert.True(item.IsSelected));
    }

    [Fact]
    public void SelectAll_FalseDeselectsAll()
    {
        var vm = Create();
        vm.Entries.Add(MakeItem(id: "a"));
        vm.Entries.Add(MakeItem(id: "b"));
        vm.SelectAll = true;
        vm.SelectAll = false;
        Assert.All(vm.Entries, item => Assert.False(item.IsSelected));
    }

    #endregion

    #region IsBusy / StatusText

    [Fact]
    public void IsBusy_DefaultFalse()
    {
        var vm = Create();
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void StatusText_DefaultEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.StatusText);
    }

    #endregion

    #region GroupFilterOptions / StatusFilterOptions

    [Fact]
    public void GroupFilterOptions_DefaultContainsAlle()
    {
        var vm = Create();
        Assert.Contains("Alle", vm.GroupFilterOptions);
    }

    [Fact]
    public void StatusFilterOptions_ContainsAllStatuses()
    {
        var vm = Create();
        Assert.Contains("Installiert", vm.StatusFilterOptions);
        Assert.Contains("Fehlend", vm.StatusFilterOptions);
        Assert.Contains("Veraltet", vm.StatusFilterOptions);
    }

    #endregion
}
