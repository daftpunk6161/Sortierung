using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for ShellViewModel:
/// Navigation (5-area shell, sub-tabs, history), Simple/Expert mode coercion,
/// wizard flow (3 steps), overlay toggles, workspace projections, compact mode.
/// </summary>
public sealed class ShellViewModelCoverageTests
{
    private static ShellViewModel Create(Action? requery = null)
        => new(new LocalizationService(), requery);

    #region Navigation — 5-area shell

    [Theory]
    [InlineData(0, "MissionControl")]
    [InlineData(1, "Library")]
    [InlineData(2, "Config")]
    [InlineData(3, "Tools")]
    [InlineData(4, "System")]
    public void SelectedNavIndex_MapsToCorrectTag(int index, string expectedTag)
    {
        var vm = Create();
        vm.SelectedNavIndex = index;
        Assert.Equal(expectedTag, vm.SelectedNavTag);
    }

    [Theory]
    [InlineData("MissionControl", 0)]
    [InlineData("Library", 1)]
    [InlineData("Config", 2)]
    [InlineData("Tools", 3)]
    [InlineData("System", 4)]
    public void SelectedNavTag_SetsCorrectIndex(string tag, int expectedIndex)
    {
        var vm = Create();
        vm.SelectedNavTag = tag;
        Assert.Equal(expectedIndex, vm.SelectedNavIndex);
    }

    [Theory]
    [InlineData("Start", "MissionControl")]
    [InlineData("Analyse", "Library")]
    [InlineData("Setup", "Config")]
    [InlineData("Log", "System")]
    public void SelectedNavTag_NormalizesLegacyAliases(string alias, string expectedTag)
    {
        var vm = Create();
        vm.SelectedNavTag = alias;
        Assert.Equal(expectedTag, vm.SelectedNavTag);
    }

    [Fact]
    public void SelectedNavTag_UnknownTag_FallsBackToMissionControl()
    {
        var vm = Create();
        vm.SelectedNavTag = "NonExistent";
        Assert.Equal("MissionControl", vm.SelectedNavTag);
        Assert.Equal(0, vm.SelectedNavIndex);
    }

    [Fact]
    public void SelectedNavIndex_OutOfRange_CoercesTo0()
    {
        var vm = Create();
        vm.SelectedNavIndex = 99;
        Assert.Equal(0, vm.SelectedNavIndex);
    }

    [Fact]
    public void SelectedNavIndex_Negative_CoercesTo0()
    {
        var vm = Create();
        vm.SelectedNavIndex = -1;
        Assert.Equal(0, vm.SelectedNavIndex);
    }

    #endregion

    #region Sub-Tab Navigation

    [Theory]
    [InlineData("MissionControl", "Dashboard")]
    [InlineData("Library", "Results")]
    [InlineData("Config", "Regions")]
    [InlineData("Tools", "Features")]
    public void DefaultSubTab_MatchesNavArea(string navTag, string expectedSubTab)
    {
        var vm = Create();
        vm.SelectedNavTag = navTag;
        Assert.Equal(expectedSubTab, vm.SelectedSubTab);
    }

    [Fact]
    public void System_DefaultSubTab_SimpleMode_IsAppearance()
    {
        var vm = Create();
        vm.IsSimpleMode = true;
        vm.SelectedNavTag = "System";
        Assert.Equal("Appearance", vm.SelectedSubTab);
    }

    [Fact]
    public void System_DefaultSubTab_ExpertMode_IsActivityLog()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "System";
        Assert.Equal("ActivityLog", vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_InvalidForArea_FallsBackToDefault()
    {
        var vm = Create();
        vm.SelectedNavTag = "MissionControl";
        vm.SelectedSubTab = "Profiles";
        Assert.Equal("Dashboard", vm.SelectedSubTab);
    }

    [Theory]
    [InlineData("Library", "Results")]
    [InlineData("Library", "Safety")]
    public void SubTab_SimpleMode_AllowsBasicLibraryTabs(string nav, string subTab)
    {
        var vm = Create();
        vm.IsSimpleMode = true;
        vm.SelectedNavTag = nav;
        vm.SelectedSubTab = subTab;
        Assert.Equal(subTab, vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_SimpleMode_RejectsDecisionsTab()
    {
        var vm = Create();
        vm.IsSimpleMode = true;
        vm.SelectedNavTag = "Library";
        vm.SelectedSubTab = "Decisions";
        Assert.Equal("Results", vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_ExpertMode_AllowsDecisionsTab()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "Library";
        vm.SelectedSubTab = "Decisions";
        Assert.Equal("Decisions", vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_ExpertMode_AllowsDatAudit()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "Library";
        vm.SelectedSubTab = "DatAudit";
        Assert.Equal("DatAudit", vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_ExpertMode_AllowsConfigProfiles()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "Config";
        vm.SelectedSubTab = "Profiles";
        Assert.Equal("Profiles", vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_SimpleMode_RejectsConfigProfiles()
    {
        var vm = Create();
        vm.IsSimpleMode = true;
        vm.SelectedNavTag = "Config";
        vm.SelectedSubTab = "Profiles";
        Assert.Equal("Regions", vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_System_SimpleMode_RejectsActivityLog()
    {
        var vm = Create();
        vm.IsSimpleMode = true;
        vm.SelectedNavTag = "System";
        vm.SelectedSubTab = "ActivityLog";
        Assert.Equal("Appearance", vm.SelectedSubTab);
    }

    [Fact]
    public void SubTab_System_ExpertMode_AllowsActivityLog()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "System";
        vm.SelectedSubTab = "ActivityLog";
        Assert.Equal("ActivityLog", vm.SelectedSubTab);
    }

    #endregion

    #region Simple / Expert Mode

    [Fact]
    public void IsSimpleMode_DefaultsTrue()
    {
        var vm = Create();
        Assert.True(vm.IsSimpleMode);
        Assert.False(vm.IsExpertMode);
    }

    [Fact]
    public void IsExpertMode_InverseOfSimpleMode()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        Assert.True(vm.IsExpertMode);
        vm.IsSimpleMode = true;
        Assert.False(vm.IsExpertMode);
    }

    [Fact]
    public void ModeSwitch_CoercesHiddenSubTab_Library()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "Library";
        vm.SelectedSubTab = "Decisions";
        Assert.Equal("Decisions", vm.SelectedSubTab);

        vm.IsSimpleMode = true;
        Assert.Equal("Results", vm.SelectedSubTab);
    }

    [Fact]
    public void ModeSwitch_CoercesHiddenSubTab_Config()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "Config";
        vm.SelectedSubTab = "Profiles";
        Assert.Equal("Profiles", vm.SelectedSubTab);

        vm.IsSimpleMode = true;
        Assert.Equal("Regions", vm.SelectedSubTab);
    }

    [Fact]
    public void ModeSwitch_CoercesHiddenSubTab_System()
    {
        var vm = Create();
        vm.IsSimpleMode = false;
        vm.SelectedNavTag = "System";
        vm.SelectedSubTab = "ActivityLog";

        vm.IsSimpleMode = true;
        Assert.Equal("Appearance", vm.SelectedSubTab);
    }

    [Fact]
    public void TabVisibility_DependsOnMode()
    {
        var vm = Create();
        vm.IsSimpleMode = true;
        Assert.False(vm.ShowLibraryDecisionsTab);
        Assert.False(vm.ShowLibraryDatAuditTab);
        Assert.False(vm.ShowConfigProfilesTab);
        Assert.False(vm.ShowSystemActivityLogTab);

        vm.IsSimpleMode = false;
        Assert.True(vm.ShowLibraryDecisionsTab);
        Assert.True(vm.ShowLibraryDatAuditTab);
        Assert.True(vm.ShowConfigProfilesTab);
        Assert.True(vm.ShowSystemActivityLogTab);
    }

    [Fact]
    public void AlwaysVisibleTabs_AreNotModeDependent()
    {
        var vm = Create();
        Assert.True(vm.ShowMissionControlNav);
        Assert.True(vm.ShowLibraryNav);
        Assert.True(vm.ShowConfigNav);
        Assert.True(vm.ShowToolsNav);
        Assert.True(vm.ShowSystemNav);
        Assert.True(vm.ShowMissionDashboardTab);
        Assert.True(vm.ShowMissionRecentRunsTab);
        Assert.True(vm.ShowLibraryResultsTab);
        Assert.True(vm.ShowLibrarySafetyTab);
        Assert.True(vm.ShowConfigRegionsTab);
        Assert.True(vm.ShowConfigOptionsTab);
        Assert.True(vm.ShowToolsExternalToolsTab);
        Assert.True(vm.ShowToolsFeaturesTab);
        Assert.True(vm.ShowToolsDatManagementTab);
        Assert.True(vm.ShowSystemAppearanceTab);
        Assert.True(vm.ShowSystemAboutTab);
    }

    [Fact]
    public void DisabledTabs_AreAlwaysHidden()
    {
        var vm = Create();
        Assert.False(vm.ShowMissionQuickStartTab);
        Assert.False(vm.ShowLibraryReportTab);
        Assert.False(vm.ShowConfigFilteringTab);
        Assert.False(vm.ShowToolsConversionTab);
        Assert.False(vm.ShowToolsGameKeyLabTab);
    }

    [Fact]
    public void ModeSwitch_FiresPropertyChanged()
    {
        var vm = Create();
        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        vm.IsSimpleMode = false;

        Assert.Contains("IsSimpleMode", changedProps);
        Assert.Contains("IsExpertMode", changedProps);
        Assert.Contains("ShowLibraryDecisionsTab", changedProps);
        Assert.Contains("ShowConfigProfilesTab", changedProps);
        Assert.Contains("ShowSystemActivityLogTab", changedProps);
    }

    #endregion

    #region Navigation History

    [Fact]
    public void NavigateTo_PushesHistoryAndClearsForward()
    {
        var vm = Create();
        Assert.False(vm.CanNavBack);
        Assert.False(vm.CanNavForward);

        vm.NavigateTo("Library");
        Assert.True(vm.CanNavBack);
        Assert.Equal(1, vm.SelectedNavIndex);
    }

    [Fact]
    public void NavGoBack_RestoresPreviousIndex()
    {
        var vm = Create();
        vm.NavigateTo("Library");
        vm.NavigateTo("Config");
        Assert.Equal(2, vm.SelectedNavIndex);

        vm.NavGoBack();
        Assert.Equal(1, vm.SelectedNavIndex);
        Assert.True(vm.CanNavBack);
        Assert.True(vm.CanNavForward);
    }

    [Fact]
    public void NavGoForward_RestoresForwardIndex()
    {
        var vm = Create();
        vm.NavigateTo("Library");
        vm.NavigateTo("Config");
        vm.NavGoBack();
        Assert.Equal(1, vm.SelectedNavIndex);

        vm.NavGoForward();
        Assert.Equal(2, vm.SelectedNavIndex);
        Assert.False(vm.CanNavForward);
    }

    [Fact]
    public void NavGoBack_EmptyStack_NoOp()
    {
        var vm = Create();
        vm.NavGoBack();
        Assert.Equal(0, vm.SelectedNavIndex);
    }

    [Fact]
    public void NavGoForward_EmptyStack_NoOp()
    {
        var vm = Create();
        vm.NavGoForward();
        Assert.Equal(0, vm.SelectedNavIndex);
    }

    [Fact]
    public void NavigateTo_SameIndex_DoesNotPushHistory()
    {
        var vm = Create();
        vm.NavigateTo("MissionControl");
        Assert.False(vm.CanNavBack);
    }

    [Fact]
    public void NavigateTo_NewAfterBack_ClearsForwardStack()
    {
        var vm = Create();
        vm.NavigateTo("Library");
        vm.NavigateTo("Config");
        vm.NavGoBack();
        Assert.True(vm.CanNavForward);

        vm.NavigateTo("Tools");
        Assert.False(vm.CanNavForward);
    }

    [Fact]
    public void NavigateTo_LegacyAlias_ResolvesCorrectly()
    {
        var vm = Create();
        vm.NavigateTo("Analyse");
        Assert.Equal("Library", vm.SelectedNavTag);
        Assert.Equal(1, vm.SelectedNavIndex);
    }

    [Fact]
    public void NavHistory_MultipleBackForward_Sequence()
    {
        var vm = Create();
        vm.NavigateTo("Library");
        vm.NavigateTo("Config");
        vm.NavigateTo("Tools");
        vm.NavigateTo("System");

        vm.NavGoBack();
        Assert.Equal(3, vm.SelectedNavIndex); // Tools

        vm.NavGoBack();
        Assert.Equal(2, vm.SelectedNavIndex); // Config

        vm.NavGoForward();
        Assert.Equal(3, vm.SelectedNavIndex); // Tools

        vm.NavGoForward();
        Assert.Equal(4, vm.SelectedNavIndex); // System
    }

    #endregion

    #region Wizard

    [Fact]
    public void Wizard_DefaultState()
    {
        var vm = Create();
        Assert.False(vm.ShowFirstRunWizard);
        Assert.Equal(0, vm.WizardStep);
        Assert.True(vm.WizardStepIs0);
        Assert.False(vm.WizardStepIs1);
        Assert.False(vm.WizardStepIs2);
    }

    [Fact]
    public void WizardNext_AdvancesStep()
    {
        var vm = Create();
        vm.ShowFirstRunWizard = true;

        vm.WizardNextCommand.Execute(null);
        Assert.Equal(1, vm.WizardStep);
        Assert.False(vm.WizardStepIs0);
        Assert.True(vm.WizardStepIs1);
        Assert.False(vm.WizardStepIs2);

        vm.WizardNextCommand.Execute(null);
        Assert.Equal(2, vm.WizardStep);
        Assert.False(vm.WizardStepIs0);
        Assert.False(vm.WizardStepIs1);
        Assert.True(vm.WizardStepIs2);
    }

    [Fact]
    public void WizardNext_AtStep2_FinishesWizard()
    {
        var vm = Create();
        vm.ShowFirstRunWizard = true;
        vm.WizardNextCommand.Execute(null);
        vm.WizardNextCommand.Execute(null);
        Assert.Equal(2, vm.WizardStep);

        vm.WizardNextCommand.Execute(null);
        Assert.False(vm.ShowFirstRunWizard);
        Assert.Equal(0, vm.WizardStep);
    }

    [Fact]
    public void WizardBack_DecreasesStep()
    {
        var vm = Create();
        vm.ShowFirstRunWizard = true;
        vm.WizardNextCommand.Execute(null);
        vm.WizardNextCommand.Execute(null);
        Assert.Equal(2, vm.WizardStep);

        vm.WizardBackCommand.Execute(null);
        Assert.Equal(1, vm.WizardStep);

        vm.WizardBackCommand.Execute(null);
        Assert.Equal(0, vm.WizardStep);
    }

    [Fact]
    public void WizardBack_AtStep0_CannotExecute()
    {
        var vm = Create();
        Assert.False(vm.WizardBackCommand.CanExecute(null));
    }

    [Fact]
    public void WizardSkip_ClosesWizardAndResetsStep()
    {
        var vm = Create();
        vm.ShowFirstRunWizard = true;
        vm.WizardNextCommand.Execute(null);
        Assert.Equal(1, vm.WizardStep);

        vm.WizardSkipCommand.Execute(null);
        Assert.False(vm.ShowFirstRunWizard);
        Assert.Equal(0, vm.WizardStep);
    }

    [Fact]
    public void WizardRegionSummary_SetAndGet()
    {
        var vm = Create();
        Assert.Equal("–", vm.WizardRegionSummary);

        vm.WizardRegionSummary = "EU, US, JP";
        Assert.Equal("EU, US, JP", vm.WizardRegionSummary);
    }

    [Fact]
    public void WizardNextLabel_DependsOnStep()
    {
        var vm = Create();
        var labelStep0 = vm.WizardNextLabel;

        vm.WizardNextCommand.Execute(null);
        var labelStep1 = vm.WizardNextLabel;

        vm.WizardNextCommand.Execute(null);
        var labelFinish = vm.WizardNextLabel;

        Assert.Equal(labelStep0, labelStep1);
        Assert.NotEqual(labelStep0, labelFinish);
    }

    #endregion

    #region Overlays

    [Fact]
    public void ContextWing_Toggle()
    {
        var vm = Create();
        Assert.False(vm.ShowContextWing);
        Assert.Equal("Inspector einblenden", vm.ContextToggleLabel);

        vm.ToggleContextWingCommand.Execute(null);
        Assert.True(vm.ShowContextWing);
        Assert.Equal("Inspector ausblenden", vm.ContextToggleLabel);

        vm.ToggleContextWingCommand.Execute(null);
        Assert.False(vm.ShowContextWing);
    }

    [Fact]
    public void ShortcutSheet_Toggle()
    {
        var vm = Create();
        Assert.False(vm.ShowShortcutSheet);

        vm.ToggleShortcutSheetCommand.Execute(null);
        Assert.True(vm.ShowShortcutSheet);

        vm.ToggleShortcutSheetCommand.Execute(null);
        Assert.False(vm.ShowShortcutSheet);
    }

    [Fact]
    public void DetailDrawer_Toggle()
    {
        var vm = Create();
        Assert.False(vm.ShowDetailDrawer);

        vm.ToggleDetailDrawerCommand.Execute(null);
        Assert.True(vm.ShowDetailDrawer);

        vm.ToggleDetailDrawerCommand.Execute(null);
        Assert.False(vm.ShowDetailDrawer);
    }

    [Fact]
    public void ShowMoveInlineConfirm_TriggersCommandRequery()
    {
        var requeriedCount = 0;
        var vm = Create(() => requeriedCount++);

        vm.ShowMoveInlineConfirm = true;
        Assert.True(vm.ShowMoveInlineConfirm);
        Assert.Equal(1, requeriedCount);

        vm.ShowMoveInlineConfirm = false;
        Assert.Equal(2, requeriedCount);
    }

    [Fact]
    public void CompactNav_GetSet()
    {
        var vm = Create();
        Assert.False(vm.IsCompactNav);

        vm.IsCompactNav = true;
        Assert.True(vm.IsCompactNav);
    }

    #endregion

    #region Workspace Projections

    [Theory]
    [InlineData("MissionControl", "Mission Control")]
    [InlineData("Library", "Library")]
    [InlineData("Config", "Konfiguration")]
    [InlineData("Tools", "Werkzeugkatalog")]
    [InlineData("System", "System")]
    public void CurrentWorkspaceTitle_MatchesNavArea(string tag, string expectedTitle)
    {
        var vm = Create();
        vm.SelectedNavTag = tag;
        Assert.Equal(expectedTitle, vm.CurrentWorkspaceTitle);
    }

    [Theory]
    [InlineData("Dashboard", "Dashboard")]
    [InlineData("Results", "Ergebnisse")]
    [InlineData("Decisions", "Entscheidungen")]
    [InlineData("Safety", "Safety Review")]
    [InlineData("DatAudit", "DAT Audit")]
    [InlineData("Regions", "Regionen")]
    [InlineData("Options", "Optionen")]
    [InlineData("Profiles", "Profile")]
    [InlineData("ExternalTools", "Externe Tools")]
    [InlineData("Features", "Werkzeuge & Features")]
    [InlineData("DatManagement", "DAT-Verwaltung")]
    [InlineData("ActivityLog", "Aktivitaet")]
    [InlineData("Appearance", "Darstellung")]
    [InlineData("About", "Info")]
    public void CurrentWorkspaceSection_MatchesSubTab(string subTab, string expectedSection)
    {
        var vm = Create();
        vm.IsSimpleMode = false;

        // Navigate to area that allows this sub-tab
        if (subTab is "Dashboard" or "RecentRuns")
            vm.SelectedNavTag = "MissionControl";
        else if (subTab is "Results" or "Decisions" or "Safety" or "DatAudit")
            vm.SelectedNavTag = "Library";
        else if (subTab is "Regions" or "Options" or "Profiles")
            vm.SelectedNavTag = "Config";
        else if (subTab is "Features" or "ExternalTools" or "DatManagement")
            vm.SelectedNavTag = "Tools";
        else if (subTab is "ActivityLog" or "Appearance" or "About")
            vm.SelectedNavTag = "System";

        vm.SelectedSubTab = subTab;
        Assert.Equal(expectedSection, vm.CurrentWorkspaceSection);
    }

    [Fact]
    public void CurrentWorkspaceBreadcrumb_CombinesTitleAndSection()
    {
        var vm = Create();
        vm.SelectedNavTag = "Library";
        Assert.Equal("Library / Ergebnisse", vm.CurrentWorkspaceBreadcrumb);
    }

    [Fact]
    public void WorkspaceProjection_UpdatesOnNavChange()
    {
        var vm = Create();
        var changedProps = new List<string>();
        vm.PropertyChanged += (_, e) => changedProps.Add(e.PropertyName!);

        vm.SelectedNavTag = "Config";

        Assert.Contains("CurrentWorkspaceTitle", changedProps);
        Assert.Contains("CurrentWorkspaceSection", changedProps);
        Assert.Contains("CurrentWorkspaceBreadcrumb", changedProps);
    }

    #endregion

    #region Notifications (synchronous parts only)

    [Fact]
    public void Notifications_DefaultsEmpty()
    {
        var vm = Create();
        Assert.Empty(vm.Notifications);
    }

    [Fact]
    public void DismissNotification_RemovesItem()
    {
        var vm = Create();
        var item = new Romulus.UI.Wpf.Models.NotificationItem
        {
            Message = "test",
            Type = "Success",
            AutoCloseMs = 0
        };
        vm.Notifications.Add(item);
        Assert.Single(vm.Notifications);

        vm.DismissNotification(item);
        Assert.Empty(vm.Notifications);
    }

    #endregion
}
