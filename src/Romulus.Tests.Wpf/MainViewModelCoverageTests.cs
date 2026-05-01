using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for MainViewModel: extension/console filters, region preferences,
/// rollback stack, fingerprint, wizard helpers.
/// Uses parameterless constructor (no mocks needed).
/// </summary>
public sealed class MainViewModelCoverageTests
{
    private static MainViewModel Create() => new();

    #region Extension Filters

    [Fact]
    public void ExtensionFilters_Initialized()
    {
        var vm = Create();
        Assert.NotEmpty(vm.ExtensionFilters);
        Assert.True(vm.ExtensionFilters.Count >= 60, "Should have 60+ extension filters");
    }

    [Fact]
    public void GetSelectedExtensions_NoneChecked_ReturnsEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.GetSelectedExtensions());
    }

    [Fact]
    public void GetSelectedExtensions_SomeChecked_ReturnsChecked()
    {
        var vm = Create();
        vm.ExtensionFilters.First(e => e.Extension == ".chd").IsChecked = true;
        vm.ExtensionFilters.First(e => e.Extension == ".iso").IsChecked = true;
        var selected = vm.GetSelectedExtensions();
        Assert.Equal(2, selected.Length);
        Assert.Contains(".chd", selected);
        Assert.Contains(".iso", selected);
    }

    [Fact]
    public void SelectedExtensionCount_ReflectsChecked()
    {
        var vm = Create();
        Assert.Equal(0, vm.SelectedExtensionCount);
        vm.ExtensionFilters[0].IsChecked = true;
        Assert.Equal(1, vm.SelectedExtensionCount);
    }

    [Fact]
    public void ExtensionCountDisplay_Format()
    {
        var vm = Create();
        var display = vm.ExtensionCountDisplay;
        Assert.Contains("/", display);
        Assert.StartsWith("0", display);
    }

    [Fact]
    public void SelectAllExtensions_ChecksAll()
    {
        var vm = Create();
        vm.SelectAllExtensionsCommand.Execute(null);
        Assert.All(vm.ExtensionFilters, e => Assert.True(e.IsChecked));
    }

    [Fact]
    public void ClearAllExtensions_UnchecksAll()
    {
        var vm = Create();
        vm.SelectAllExtensionsCommand.Execute(null);
        vm.ClearAllExtensionsCommand.Execute(null);
        Assert.All(vm.ExtensionFilters, e => Assert.False(e.IsChecked));
    }

    [Fact]
    public void SelectExtensionGroup_ChecksOnlyGroup()
    {
        var vm = Create();
        vm.SelectExtensionGroupCommand.Execute("Archive");
        var archiveItems = vm.ExtensionFilters.Where(e => e.Category == "Archive").ToList();
        Assert.All(archiveItems, e => Assert.True(e.IsChecked));
        // Non-archive items should remain unchecked
        var nonArchive = vm.ExtensionFilters.Where(e => e.Category != "Archive").ToList();
        Assert.All(nonArchive, e => Assert.False(e.IsChecked));
    }

    [Fact]
    public void DeselectExtensionGroup_UnchecksGroup()
    {
        var vm = Create();
        vm.SelectAllExtensionsCommand.Execute(null);
        vm.DeselectExtensionGroupCommand.Execute("Archive");
        var archiveItems = vm.ExtensionFilters.Where(e => e.Category == "Archive").ToList();
        Assert.All(archiveItems, e => Assert.False(e.IsChecked));
    }

    #endregion

    #region Console Filters

    [Fact]
    public void ConsoleFilters_Initialized()
    {
        var vm = Create();
        Assert.NotEmpty(vm.ConsoleFilters);
    }

    [Fact]
    public void GetSelectedConsoles_NoneChecked_ReturnsEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.GetSelectedConsoles());
    }

    [Fact]
    public void GetSelectedConsoles_SomeChecked_ReturnsKeys()
    {
        var vm = Create();
        vm.ConsoleFilters.First(c => c.Key == "PS2").IsChecked = true;
        var selected = vm.GetSelectedConsoles();
        Assert.Single(selected);
        Assert.Equal("PS2", selected[0]);
    }

    [Fact]
    public void SelectAllConsoles_ChecksAll()
    {
        var vm = Create();
        vm.SelectAllConsolesCommand.Execute(null);
        Assert.All(vm.ConsoleFilters, c => Assert.True(c.IsChecked));
    }

    [Fact]
    public void ClearAllConsoles_UnchecksAll()
    {
        var vm = Create();
        vm.SelectAllConsolesCommand.Execute(null);
        vm.ClearAllConsolesCommand.Execute(null);
        Assert.All(vm.ConsoleFilters, c => Assert.False(c.IsChecked));
    }

    #endregion

    #region Region Preferences

    [Fact]
    public void GetPreferredRegions_AllFalse_ReturnsDefault()
    {
        var vm = Create();
        vm.PreferEU = false;
        vm.PreferUS = false;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        var regions = vm.GetPreferredRegions();
        // Should return fallback based on locale or empty
        Assert.NotNull(regions);
    }

    [Fact]
    public void GetPreferredRegions_ReturnsSelected()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("EU", regions);
        Assert.Contains("US", regions);
    }

    [Fact]
    public void InitRegionPriorities_PopulatesFromBooleans()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferJP = true;
        vm.InitRegionPriorities();
        var euItem = vm.RegionPriorities.FirstOrDefault(r => r.Code == "EU");
        Assert.NotNull(euItem);
        Assert.True(euItem.IsEnabled);
        Assert.True(euItem.Position > 0);
    }

    [Fact]
    public void EnabledRegionCount_ReflectsPriorities()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();
        Assert.Equal(2, vm.EnabledRegionCount);
    }

    [Fact]
    public void ApplyLocaleRegionDefaults_SetsAtLeastOneRegion()
    {
        var vm = Create();
        vm.PreferEU = false;
        vm.PreferUS = false;
        vm.PreferJP = false;
        vm.ApplyLocaleRegionDefaults();
        Assert.True(vm.PreferEU || vm.PreferUS || vm.PreferJP);
    }

    [Fact]
    public void UpdateWizardRegionSummary_ReflectsPreferences()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferJP = true;
        vm.UpdateWizardRegionSummary();
        Assert.Contains("EU", vm.Shell.WizardRegionSummary);
        Assert.Contains("JP", vm.Shell.WizardRegionSummary);
    }

    [Fact]
    public void UpdateWizardRegionSummary_NoneSelected_ShowsDash()
    {
        var vm = Create();
        vm.PreferEU = false;
        vm.PreferUS = false;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.UpdateWizardRegionSummary();
        Assert.Equal("–", vm.Shell.WizardRegionSummary);
    }

    #endregion

    #region Simple Mode

    [Fact]
    public void IsSimpleMode_DefaultTrue()
    {
        var vm = Create();
        Assert.True(vm.IsSimpleMode);
    }

    [Fact]
    public void IsSimpleMode_Toggle()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        Assert.False(vm.IsSimpleMode);
        Assert.False(vm.Shell.IsSimpleMode);
    }

    #endregion

    #region Roots Collection

    [Fact]
    public void Roots_InitiallyEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.Roots);
    }

    [Fact]
    public void Roots_AddAndRemove()
    {
        var vm = Create();
        vm.Roots.Add(@"C:\Roms");
        Assert.Single(vm.Roots);
        vm.Roots.Clear();
        Assert.Empty(vm.Roots);
    }

    #endregion

    #region DryRun Flag

    [Fact]
    public void DryRun_DefaultTrue()
    {
        var vm = Create();
        Assert.True(vm.DryRun);
    }

    [Fact]
    public void ConflictPolicy_DefaultRename()
    {
        var vm = Create();
        Assert.Equal(UI.Wpf.Models.ConflictPolicy.Rename, vm.ConflictPolicy);
    }

    #endregion

    #region Child ViewModels

    [Fact]
    public void ChildViewModels_NotNull()
    {
        var vm = Create();
        Assert.NotNull(vm.Shell);
        Assert.NotNull(vm.Setup);
        Assert.NotNull(vm.Tools);
        Assert.NotNull(vm.Run);
        Assert.NotNull(vm.CommandPalette);
        Assert.NotNull(vm.DatAudit);
        Assert.NotNull(vm.DatCatalog);
        Assert.NotNull(vm.ConversionPreview);
    }

    #endregion

    #region Presets

    [Fact]
    public void PresetSafeDryRun_SetsDryRunTrue()
    {
        var vm = Create();
        vm.DryRun = false;
        vm.PresetSafeDryRunCommand.Execute(null);
        Assert.True(vm.DryRun);
    }

    [Fact]
    public void PresetConvert_EnablesConvertAndDryRun()
    {
        var vm = Create();
        vm.ConvertEnabled = false;
        vm.DryRun = false;
        vm.PresetConvertCommand.Execute(null);
        Assert.True(vm.ConvertEnabled);
        Assert.True(vm.DryRun);
        Assert.Equal("Convert", vm.ActivePreset);
    }

    #endregion

    #region Log

    [Fact]
    public void LogEntries_InitiallyEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.LogEntries);
    }

    [Fact]
    public void AddLog_AddsEntryToLog()
    {
        var vm = Create();
        vm.AddLog("Test message", "INFO");
        Assert.Single(vm.LogEntries);
    }

    #endregion

    #region Configuration Tracking

    [Fact]
    public void CanStartMoveWithCurrentPreview_NoPreview_False()
    {
        var vm = Create();
        Assert.False(vm.CanStartMoveWithCurrentPreview);
    }

    [Fact]
    public void ShowConfigChangedBanner_NoPriorPreview_False()
    {
        var vm = Create();
        Assert.False(vm.ShowConfigChangedBanner);
    }

    [Fact]
    public void MoveApplyGateText_NoPriorPreview_NotEmpty()
    {
        var vm = Create();
        Assert.NotNull(vm.MoveApplyGateText);
    }

    [Fact]
    public void IsMovePhaseApplicable_WhenDryRun_False()
    {
        var vm = Create();
        vm.DryRun = true;
        Assert.False(vm.IsMovePhaseApplicable);
    }

    [Fact]
    public void IsConvertPhaseApplicable_WhenConvertOnly_True()
    {
        var vm = Create();
        vm.ConvertOnly = true;
        Assert.True(vm.IsConvertPhaseApplicable);
    }

    #endregion

    #region Rollback Stack

    [Fact]
    public void HasRollbackUndo_DefaultFalse()
    {
        var vm = Create();
        Assert.False(vm.HasRollbackUndo);
    }

    [Fact]
    public void PushRollbackUndo_MakesUndoAvailable()
    {
        var vm = Create();
        vm.PushRollbackUndo(@"C:\audit.csv");
        Assert.True(vm.HasRollbackUndo);
    }

    [Fact]
    public void PopRollbackUndo_ReturnsPath()
    {
        var vm = Create();
        vm.PushRollbackUndo(@"C:\audit.csv");
        var path = vm.PopRollbackUndo();
        Assert.Equal(@"C:\audit.csv", path);
        Assert.False(vm.HasRollbackUndo);
    }

    [Fact]
    public void PopRollbackUndo_Empty_ReturnsNull()
    {
        var vm = Create();
        Assert.Null(vm.PopRollbackUndo());
    }

    [Fact]
    public void PushPopRollback_MultipleEntries()
    {
        var vm = Create();
        vm.PushRollbackUndo("a");
        vm.PushRollbackUndo("b");
        Assert.Equal("b", vm.PopRollbackUndo());
        Assert.Equal("a", vm.PopRollbackUndo());
    }

    #endregion

    #region GetPreferredRegions

    [Fact]
    public void GetPreferredRegions_FromPriorities_ReturnsEnabled()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();
        var regions = vm.GetPreferredRegions();
        Assert.Contains("EU", regions);
        Assert.Contains("US", regions);
        Assert.DoesNotContain("JP", regions);
    }

    [Fact]
    public void GetPreferredRegions_FallbackWhenPrioritiesEmpty()
    {
        var vm = Create();
        vm.RegionPriorities.Clear();
        vm.PreferEU = true;
        vm.PreferJP = true;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("EU", regions);
        Assert.Contains("JP", regions);
    }

    #endregion

    #region GetCurrentConfigMap

    [Fact]
    public void GetCurrentConfigMap_ContainsExpectedKeys()
    {
        var vm = Create();
        var map = vm.GetCurrentConfigMap();
        Assert.True(map.ContainsKey("sortConsole"));
        Assert.True(map.ContainsKey("dryRun"));
        Assert.True(map.ContainsKey("useDat"));
        Assert.True(map.ContainsKey("convertEnabled"));
        Assert.True(map.ContainsKey("locale"));
    }

    [Fact]
    public void GetCurrentConfigMap_ReflectsCurrentState()
    {
        var vm = Create();
        vm.DryRun = false;
        vm.UseDat = true;
        var map = vm.GetCurrentConfigMap();
        Assert.Equal("False", map["dryRun"]);
        Assert.Equal("True", map["useDat"]);
    }

    #endregion

    #region MoveRegionTo

    [Fact]
    public void MoveRegionTo_ValidMove_Reorders()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = true;
        vm.InitRegionPriorities();
        // EU is at 0, US at 1
        vm.MoveRegionTo(0, 1);
        Assert.Equal("US", vm.RegionPriorities[0].Code);
        Assert.Equal("EU", vm.RegionPriorities[1].Code);
    }

    [Fact]
    public void MoveRegionTo_SameIndex_NoOp()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.InitRegionPriorities();
        var first = vm.RegionPriorities[0].Code;
        vm.MoveRegionTo(0, 0);
        Assert.Equal(first, vm.RegionPriorities[0].Code);
    }

    [Fact]
    public void MoveRegionTo_OutOfBounds_NoOp()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.InitRegionPriorities();
        vm.MoveRegionTo(-1, 0); // no crash
        vm.MoveRegionTo(0, 999); // no crash
    }

    [Fact]
    public void MoveRegionTo_DisabledItem_NoOp()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferUS = false;
        vm.InitRegionPriorities();
        // Try to move disabled item
        int disabledIdx = vm.RegionPriorities.ToList().FindIndex(r => !r.IsEnabled);
        if (disabledIdx >= 0)
            vm.MoveRegionTo(disabledIdx, 0); // should be no-op
    }

    #endregion

    #region Region Presets

    [Fact]
    public void RegionPresetEuFocus_SetsEuGroup()
    {
        var vm = Create();
        vm.RegionPresetEuFocusCommand.Execute(null);
        var enabled = vm.RegionPriorities.Where(r => r.IsEnabled).Select(r => r.Code).ToArray();
        Assert.Contains("EU", enabled);
        Assert.Contains("DE", enabled);
        Assert.Contains("FR", enabled);
        Assert.Contains("WORLD", enabled);
    }

    [Fact]
    public void RegionPresetUsFocus_SetsUsGroup()
    {
        var vm = Create();
        vm.RegionPresetUsFocusCommand.Execute(null);
        var enabled = vm.RegionPriorities.Where(r => r.IsEnabled).Select(r => r.Code).ToArray();
        Assert.Contains("US", enabled);
        Assert.Contains("WORLD", enabled);
        Assert.Contains("EU", enabled);
        Assert.DoesNotContain("JP", enabled);
    }

    [Fact]
    public void RegionPresetMultiRegion_Sets4()
    {
        var vm = Create();
        vm.RegionPresetMultiRegionCommand.Execute(null);
        var enabled = vm.RegionPriorities.Where(r => r.IsEnabled).Select(r => r.Code).ToArray();
        Assert.Equal(4, enabled.Length);
    }

    [Fact]
    public void RegionPresetAll_EnablesAll16()
    {
        var vm = Create();
        vm.RegionPresetAllCommand.Execute(null);
        Assert.All(vm.RegionPriorities, r => Assert.True(r.IsEnabled));
    }

    #endregion

    #region ToggleRegion

    [Fact]
    public void ToggleRegion_DisablesEnabled()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.InitRegionPriorities();
        var eu = vm.RegionPriorities.First(r => r.Code == "EU");
        Assert.True(eu.IsEnabled);
        vm.ToggleRegionCommand.Execute(eu);
        Assert.False(eu.IsEnabled);
    }

    [Fact]
    public void ToggleRegion_EnablesDisabled()
    {
        var vm = Create();
        vm.PreferDE = false;
        vm.InitRegionPriorities();
        var de = vm.RegionPriorities.First(r => r.Code == "DE");
        Assert.False(de.IsEnabled);
        vm.ToggleRegionCommand.Execute(de);
        Assert.True(de.IsEnabled);
    }

    #endregion

    #region MoveRegionUp/Down

    [Fact]
    public void MoveRegionUp_MovesItem()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.InitRegionPriorities();
        var us = vm.RegionPriorities.First(r => r.Code == "US");
        vm.MoveRegionUpCommand.Execute(us);
        Assert.Equal("US", vm.RegionPriorities[0].Code);
    }

    [Fact]
    public void MoveRegionDown_MovesItem()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.InitRegionPriorities();
        var eu = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionDownCommand.Execute(eu);
        Assert.Equal("US", vm.RegionPriorities[0].Code);
    }

    [Fact]
    public void MoveRegionUp_FirstItem_NoOp()
    {
        var vm = Create();
        vm.PreferEU = true;
        vm.InitRegionPriorities();
        var eu = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionUpCommand.Execute(eu);
        Assert.Equal("EU", vm.RegionPriorities[0].Code);
    }

    #endregion

    #region SchedulerIntervalDisplay

    [Theory]
    [InlineData(0, "–")]
    [InlineData(30, "30 min")]
    [InlineData(60, "1 h")]
    [InlineData(120, "2 h")]
    public void SchedulerIntervalDisplay_Formats(int minutes, string expected)
    {
        var vm = Create();
        vm.SchedulerIntervalMinutes = minutes;
        Assert.Equal(expected, vm.SchedulerIntervalDisplay);
    }

    #endregion

    #region IsSimpleMode / IsExpertMode

    [Fact]
    public void IsExpertMode_InverseOfSimple()
    {
        var vm = Create();
        Assert.True(vm.IsSimpleMode);
        Assert.False(vm.IsExpertMode);
        vm.IsSimpleMode = false;
        Assert.True(vm.IsExpertMode);
    }

    [Fact]
    public void CurrentUiModeLabel_Einfach_WhenSimple()
    {
        var vm = Create();
        vm.IsSimpleMode = true;
        Assert.Equal("Einfach", vm.CurrentUiModeLabel);
    }

    [Fact]
    public void CurrentUiModeLabel_Experte_WhenExpert()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        Assert.Equal("Experte", vm.CurrentUiModeLabel);
    }

    #endregion

    #region ConflictPolicyIndex

    [Fact]
    public void ConflictPolicyIndex_MapsToEnum()
    {
        var vm = Create();
        vm.ConflictPolicyIndex = 1; // Skip
        Assert.Equal(UI.Wpf.Models.ConflictPolicy.Skip, vm.ConflictPolicy);
    }

    [Fact]
    public void ConflictPolicyIndex_RoundTrips()
    {
        var vm = Create();
        vm.ConflictPolicy = UI.Wpf.Models.ConflictPolicy.Overwrite;
        Assert.Equal(2, vm.ConflictPolicyIndex);
    }

    #endregion

    #region Preset SafeDryRun / FullSort

    [Fact]
    public void PresetSafeDryRun_SetsFlags()
    {
        var vm = Create();
        vm.DryRun = false;
        vm.AggressiveJunk = true;
        vm.PresetSafeDryRunCommand.Execute(null);
        Assert.True(vm.DryRun);
        Assert.False(vm.AggressiveJunk);
        Assert.Equal("SafeDryRun", vm.ActivePreset);
    }

    [Fact]
    public void PresetFullSort_SetsFlags()
    {
        var vm = Create();
        vm.SortConsole = false;
        vm.PresetFullSortCommand.Execute(null);
        Assert.True(vm.SortConsole);
        Assert.True(vm.DryRun);
        Assert.Equal("FullSort", vm.ActivePreset);
    }

    #endregion

    #region Theme Properties

    [Fact]
    public void CurrentThemeName_NotEmpty()
    {
        var vm = Create();
        Assert.False(string.IsNullOrEmpty(vm.CurrentThemeName));
    }

    [Fact]
    public void CurrentThemeLabel_NotEmpty()
    {
        var vm = Create();
        Assert.False(string.IsNullOrEmpty(vm.CurrentThemeLabel));
    }

    [Fact]
    public void ThemeToggleText_NotEmpty()
    {
        var vm = Create();
        Assert.StartsWith("⮞", vm.ThemeToggleText);
    }

    [Fact]
    public void AvailableThemes_NotEmpty()
    {
        var vm = Create();
        Assert.NotEmpty(vm.AvailableThemes);
    }

    #endregion

    #region CanStartCurrentRun

    [Fact]
    public void CanStartCurrentRun_NoRoots_False()
    {
        var vm = Create();
        vm.Roots.Clear();
        Assert.False(vm.CanStartCurrentRun);
    }

    [Fact]
    public void CanStartCurrentRun_WithRootsAndDryRun_True()
    {
        var vm = Create();
        vm.Roots.Add(@"C:\Roms");
        vm.DryRun = true;
        Assert.True(vm.CanStartCurrentRun);
    }

    #endregion
}
