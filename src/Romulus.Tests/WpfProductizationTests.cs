using System.Collections;
using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
using Romulus.Tests.TestFixtures;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class WpfProductizationTests : IDisposable
{
    private readonly string _tempRoot;

    public WpfProductizationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_WpfProductization_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }

    [Fact]
    public void MainViewModel_InitializesWorkflowAndProfileCatalogs_FromSharedServices()
    {
        var vm = CreateViewModel();

        Assert.Contains(vm.AvailableWorkflows, item => item.Id == WorkflowScenarioIds.FullAudit);
        Assert.Contains(vm.AvailableWorkflows, item => item.Id == WorkflowScenarioIds.QuickClean);
        Assert.Contains(vm.AvailableRunProfiles, item => item.Id == "default");
        Assert.Contains(vm.AvailableRunProfiles, item => item.Id == "quick-scan");
    }

    [Fact]
    public async Task MainViewModel_SelectedWorkflowScenario_AppliesSharedWorkflowDefaults()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);
        vm.UseDat = false;
        vm.EnableDatAudit = false;
        vm.SortConsole = false;
        vm.RemoveJunk = false;
        vm.DryRun = false;

        vm.SelectedWorkflowScenarioId = WorkflowScenarioIds.FullAudit;
    await vm.ApplySelectedRunConfigurationAsync();

        Assert.Equal(WorkflowScenarioIds.FullAudit, vm.SelectedWorkflowScenarioId);
        Assert.True(vm.UseDat);
        Assert.True(vm.EnableDatAudit);
        Assert.True(vm.SortConsole);
        Assert.True(vm.RemoveJunk);
        Assert.True(vm.DryRun);
        Assert.Equal("default", vm.SelectedRunProfileId);
    }

    [Fact]
    public async Task MainViewModel_SelectedRunProfile_AppliesSharedProfileDefaults()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);
        vm.UseDat = true;
        vm.SortConsole = true;
        vm.RemoveJunk = true;

        vm.SelectedRunProfileId = "quick-scan";
    await vm.ApplySelectedRunConfigurationAsync();

        Assert.Equal("quick-scan", vm.SelectedRunProfileId);
        Assert.True(vm.UseDat);
        Assert.True(vm.EnableDatAudit);
        Assert.False(vm.SortConsole);
        Assert.False(vm.RemoveJunk);
        Assert.True(vm.DryRun);
    }

    [Fact]
    public async Task MainViewModel_SelectedDefaultProfile_PreservesDatAuditToggleWhenNotSpecified()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);
        vm.UseDat = true;
        vm.EnableDatAudit = true;
        vm.EnableDatRename = false;

        vm.SelectedRunProfileId = "default";
        await vm.ApplySelectedRunConfigurationAsync();

        Assert.Equal("default", vm.SelectedRunProfileId);
        Assert.True(vm.UseDat);
        Assert.True(vm.EnableDatAudit);
    }

    [Fact]
    public async Task MainViewModel_Explicitness_TracksOnlyUserOverridesAfterSelection()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);

        vm.SelectedWorkflowScenarioId = WorkflowScenarioIds.FullAudit;
        await vm.ApplySelectedRunConfigurationAsync();

        var baselineExplicitness = vm.BuildCurrentRunConfigurationExplicitness();
        Assert.False(baselineExplicitness.RemoveJunk);
        Assert.False(baselineExplicitness.SortConsole);

        vm.RemoveJunk = !vm.RemoveJunk;

        var changedExplicitness = vm.BuildCurrentRunConfigurationExplicitness();
        Assert.True(changedExplicitness.RemoveJunk);
        Assert.False(changedExplicitness.SortConsole);
    }

    [Fact]
    public void MainViewModel_ApproveConversionReview_RoundTripsThroughRunConfigurationDraft()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);
        vm.ApproveConversionReview = true;

        var draft = vm.BuildCurrentRunConfigurationDraft(includeSelections: false);

        Assert.True(draft.ApproveConversionReview);
    }

    [Fact]
    public async Task FeatureCommandService_ProfileSave_AndLoad_ReuseSharedProfileModel()
    {
        var dialog = new RecordingDialogService();
        dialog.EnqueueInput("Space Saver Custom");
        dialog.EnqueueInput("Shared test profile");

        var vm = CreateViewModel(dialog);
        vm.Roots.Add(_tempRoot);
        vm.SortConsole = true;
        vm.UseDat = true;
        vm.EnableDatAudit = true;
        vm.RemoveJunk = true;

        var fileSystem = new FileSystemAdapter();
        var auditStore = new AuditCsvStore(fileSystem, _ => { }, Path.Combine(_tempRoot, "audit-signing.key"));
        var sut = new FeatureCommandService(vm, new StubSettingsService(), dialog, fileSystem, auditStore);
        sut.RegisterCommands();

        await ExecuteCommandAsync(vm.FeatureCommands[FeatureCommandKeys.ProfileSave]);
        await vm.RefreshRunConfigurationCatalogsAsync();

        Assert.Equal("Space-Saver-Custom", vm.SelectedRunProfileId);

        vm.SortConsole = false;
        vm.UseDat = false;
        vm.EnableDatAudit = false;
        vm.RemoveJunk = false;
        vm.RestoreRunConfigurationSelection(null, "Space-Saver-Custom");

        await ExecuteCommandAsync(vm.FeatureCommands[FeatureCommandKeys.ProfileLoad]);

        Assert.True(vm.SortConsole);
        Assert.True(vm.UseDat);
        Assert.True(vm.EnableDatAudit);
        Assert.True(vm.RemoveJunk);
    }

    [Fact]
    public void SettingsService_ApplyToViewModel_RestoresWorkflowAndProfileSelections()
    {
        var vm = CreateViewModel();
        var dto = new SettingsDto
        {
            SelectedWorkflowScenarioId = WorkflowScenarioIds.FullAudit,
            SelectedRunProfileId = "quick-scan"
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.Equal(WorkflowScenarioIds.FullAudit, vm.SelectedWorkflowScenarioId);
        Assert.Equal("quick-scan", vm.SelectedRunProfileId);
        Assert.Contains("Full Audit", vm.RunConfigurationSelectionSummary, StringComparison.Ordinal);
        Assert.Contains("Quick Scan", vm.RunConfigurationSelectionSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void ProductizationViews_BindWorkflowProfileSelectors_AndWizardGate()
    {
        var configProfilesXaml = File.ReadAllText(FindUiFile("Views", "ConfigProfilesView.xaml"));
        var configOptionsXaml = File.ReadAllText(FindUiFile("Views", "ConfigOptionsView.xaml"));
        var wizardXaml = File.ReadAllText(FindUiFile("Views", "WizardView.xaml"));

        Assert.Contains("SelectedWorkflowScenarioId", configProfilesXaml);
        Assert.Contains("SelectedRunProfileId", configProfilesXaml);
        Assert.Contains("RunConfigurationSelectionSummary", configProfilesXaml);

        Assert.Contains("SelectedWorkflowScenarioId", configOptionsXaml);
        Assert.Contains("SelectedRunProfileId", configOptionsXaml);

        Assert.Contains("RunConfigurationSelectionSummary", wizardXaml);
        Assert.Contains("CanAdvanceWizard", wizardXaml);
        Assert.Contains("WizardAnalysisInProgress", wizardXaml);
        Assert.Contains("WizardAnalysisSummary", wizardXaml);
        Assert.Contains("WizardRecommendationSummary", wizardXaml);
    }

    [Fact]
    public async Task MainViewModel_AnalyzeWizardSetupAsync_PopulatesSummaryAndRecommendation()
    {
        var vm = CreateViewModel();
        vm.RestoreRunConfigurationSelection(null, null);

        var root = Path.Combine(_tempRoot, "wizard-analyze");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "disc-title.iso"), new byte[4096]);
        File.WriteAllBytes(Path.Combine(root, "cart-title.sfc"), new byte[2048]);
        File.WriteAllText(Path.Combine(root, "ignore.txt"), "not-a-rom");

        vm.Roots.Add(root);

        await vm.AnalyzeWizardSetupAsync();
        await vm.ApplySelectedRunConfigurationAsync();

        Assert.False(vm.WizardAnalysisInProgress);
        Assert.True(vm.WizardHasAnalysis);
        Assert.Contains("Erkannt: 2 Datei(en)", vm.WizardAnalysisSummary, StringComparison.Ordinal);
        Assert.Contains("Empfohlen:", vm.WizardRecommendationSummary, StringComparison.Ordinal);
        Assert.Equal(WorkflowScenarioIds.NewCollectionSetup, vm.SelectedWorkflowScenarioId);
    }

    [Fact]
    public async Task MainViewModel_WhenRootsChange_InvalidatesWizardAnalysis()
    {
        var vm = CreateViewModel();
        var rootA = Path.Combine(_tempRoot, "wizard-root-a");
        var rootB = Path.Combine(_tempRoot, "wizard-root-b");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);
        File.WriteAllBytes(Path.Combine(rootA, "sample.iso"), new byte[2048]);

        vm.Roots.Add(rootA);
        await vm.AnalyzeWizardSetupAsync();

        Assert.True(vm.WizardHasAnalysis);
        Assert.False(string.IsNullOrWhiteSpace(vm.WizardRecommendationSummary));

        vm.Roots.Add(rootB);

        Assert.False(vm.WizardHasAnalysis);
        Assert.Equal(string.Empty, vm.WizardAnalysisSummary);
        Assert.Equal(string.Empty, vm.WizardRecommendationSummary);
    }

    [Fact]
    public async Task MainViewModel_EmitCollectionHealthMonitorHintsAsync_ReportsStaleDatWarning()
    {
        var vm = CreateViewModel();
        var root = Path.Combine(_tempRoot, "health-root");
        var datRoot = Path.Combine(_tempRoot, "health-dat");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(datRoot);

        var staleDat = Path.Combine(datRoot, "stale.dat");
        File.WriteAllText(staleDat, "<datafile></datafile>");
        File.SetLastWriteTime(staleDat, DateTime.Now.AddDays(-(DatCatalogStateService.StaleThresholdDays + 10)));

        vm.Roots.Add(root);
        vm.UseDat = true;
        vm.DatRoot = datRoot;

        await vm.EmitCollectionHealthMonitorHintsAsync();

        Assert.Contains(vm.LogEntries, entry =>
            entry.Level == "WARN" &&
            entry.Text.Contains($"DAT-Datei(en) sind aelter als {DatCatalogStateService.StaleThresholdDays} Tage", StringComparison.Ordinal));
    }

    [Fact]
    public void ToolsViewModel_ContainsCollectionMergeEntry()
    {
        var vm = new ToolsViewModel(new LocalizationService());

        Assert.Contains(vm.ToolItems, item => item.Key == FeatureCommandKeys.CollectionMerge);
    }

    [Fact]
    public void ToolSurfaces_Xaml_ExposeRecommendationsPinsAndCommandSearch()
    {
        var toolsViewXaml = File.ReadAllText(FindUiFile("Views", "ToolsView.xaml"));
        var startViewXaml = File.ReadAllText(FindUiFile("Views", "StartView.xaml"));
        var contextPanelXaml = File.ReadAllText(FindUiFile("Views", "ContextPanel.xaml"));
        var commandBarXaml = File.ReadAllText(FindUiFile("Views", "CommandBar.xaml"));

        Assert.Contains("RecommendedToolItems", toolsViewXaml);
        Assert.Contains("ToggleToolPinCommand", toolsViewXaml);
        Assert.Contains("MaturityBadgeText", toolsViewXaml);
        Assert.Contains("RecommendedToolItems", startViewXaml);
        Assert.Contains("RecommendedToolItems", contextPanelXaml);
        Assert.Contains("ToggleCommandPaletteCommand", commandBarXaml);
    }

    [Fact]
    public void ConfigOptionsView_ExposesPrimaryDatControls_InMainSetupFlow()
    {
        var configOptionsXaml = File.ReadAllText(FindUiFile("Views", "ConfigOptionsView.xaml"));

        Assert.Contains("IsChecked=\"{Binding UseDat}\"", configOptionsXaml);
        Assert.Contains("Text=\"{Binding Loc[Settings.SectionDat]}\"", configOptionsXaml);
        Assert.Contains("IsChecked=\"{Binding EnableDatAudit}\"", configOptionsXaml);
        Assert.Contains("IsChecked=\"{Binding EnableDatRename}\"", configOptionsXaml);
        Assert.Contains("Text=\"{Binding DatRoot", configOptionsXaml);
    }

    [Fact]
    public void NavigationAndViews_ConsolidateRetiredSubTabsIntoPrimarySurfaces()
    {
        var startViewXaml = File.ReadAllText(FindUiFile("Views", "StartView.xaml"));
        var subTabBarXaml = File.ReadAllText(FindUiFile("Views", "SubTabBar.xaml"));
        var mainWindowXaml = File.ReadAllText(FindUiFile("", "MainWindow.xaml"));
        var configOptionsXaml = File.ReadAllText(FindUiFile("Views", "ConfigOptionsView.xaml"));
        var resultViewXaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));
        var toolsViewXaml = File.ReadAllText(FindUiFile("Views", "ToolsView.xaml"));

        Assert.DoesNotContain("ConverterParameter=QuickStart", subTabBarXaml);
        Assert.DoesNotContain("ConverterParameter=Filtering", subTabBarXaml);
        Assert.DoesNotContain("ConverterParameter=Report", subTabBarXaml);
        Assert.Contains("ConverterParameter=DatManagement", subTabBarXaml);
        Assert.Contains("ConverterParameter=Conversion", subTabBarXaml);
        Assert.DoesNotContain("ConverterParameter=GameKeyLab", subTabBarXaml);
        Assert.DoesNotContain("ConfigFiltersView", mainWindowXaml);
        Assert.DoesNotContain("LibraryReportView", mainWindowXaml);
        Assert.DoesNotContain("ToolsDatView", mainWindowXaml);
        Assert.DoesNotContain("ToolsConversionView", mainWindowXaml);
        Assert.DoesNotContain("ToolsGameKeyLabView", mainWindowXaml);
        Assert.Contains("ConverterParameter=Dashboard", startViewXaml);
        Assert.Contains("ConsoleFiltersView", configOptionsXaml);
        Assert.Contains("ExtensionFiltersView", configOptionsXaml);
        Assert.Contains("DatMappings", configOptionsXaml);
        Assert.Contains("GameKeyPreviewInput", configOptionsXaml);
        Assert.Contains("OpenReportCommand", resultViewXaml);
        Assert.Contains("ConversionCapabilities", toolsViewXaml);
    }

    [Fact]
    public void MainViewModel_DashboardPrimaryAction_SwitchesToConvertOnly_ForConvertPreset()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);

        vm.PresetConvertCommand.Execute(null);

        Assert.Same(vm.ConvertOnlyCommand, vm.DashboardPrimaryCommand);
        Assert.Equal(vm.Loc["Start.ConvertOnlyButton"], vm.DashboardPrimaryActionText);
        Assert.Equal("\uE8AB", vm.DashboardPrimaryActionIcon);
        Assert.Equal(vm.Loc["Start.ConvertOnlyTip"], vm.DashboardPrimaryActionHintText);
    }

    [Fact]
    public void StartView_BindsDashboardPrimaryAction_AndSubTabsUseUnderlineChrome()
    {
        var startViewXaml = File.ReadAllText(FindUiFile("Views", "StartView.xaml"));
        var controlTemplatesXaml = File.ReadAllText(FindUiFile("Themes", "_ControlTemplates.xaml"));
        var subTabBarXaml = File.ReadAllText(FindUiFile("Views", "SubTabBar.xaml"));

        Assert.Contains("Command=\"{Binding DashboardPrimaryCommand}\"", startViewXaml);
        Assert.Contains("Text=\"{Binding DashboardPrimaryActionText}\"", startViewXaml);
        Assert.Contains("Text=\"{Binding DashboardPrimaryActionHintText}\"", startViewXaml);
        Assert.Contains("x:Key=\"SubTabPill\"", controlTemplatesXaml);
        Assert.Contains("BorderThickness\" Value=\"0,0,0,2\"", controlTemplatesXaml);
        Assert.DoesNotContain("Style x:Key=\"SubTabPill\" TargetType=\"RadioButton\" BasedOn=\"{StaticResource SidebarNavItem}\"", controlTemplatesXaml, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.CurrentWorkspaceTitle", subTabBarXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void ProgressView_WrapsCancelHint_ToAvoidClipping()
    {
        var progressViewXaml = File.ReadAllText(FindUiFile("Views", "ProgressView.xaml"));

        Assert.Contains("Text=\"{Binding Loc[Progress.CancelHint]}\"", progressViewXaml);
        Assert.Contains("TextWrapping=\"Wrap\"", progressViewXaml);
        Assert.Contains("TextAlignment=\"Center\"", progressViewXaml);
    }

    [Fact]
    public void Phase2_MissionControl_ContainsEmbeddedSetupTabs_AndConfigNavIsRetired()
    {
        var subTabBarXaml = File.ReadAllText(FindUiFile("Views", "SubTabBar.xaml"));
        var navigationRailXaml = File.ReadAllText(FindUiFile("Views", "NavigationRail.xaml"));

        Assert.Contains("Shell.ShowMissionRegionsTab", subTabBarXaml);
        Assert.Contains("Shell.ShowMissionOptionsTab", subTabBarXaml);
        Assert.Contains("Shell.ShowMissionProfilesTab", subTabBarXaml);
        Assert.DoesNotContain("Shell.ShowConfigNav", navigationRailXaml);
    }

    [Fact]
    public void Phase2_SystemActivityLog_IsServedViaDetailDrawer_NotSystemSubTabView()
    {
        var mainWindowXaml = File.ReadAllText(FindUiFile("", "MainWindow.xaml"));

        Assert.Contains("Log-Stream", mainWindowXaml);
        Assert.DoesNotContain("<views:SystemActivityView", mainWindowXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase2_ResultView_SecondaryKpis_AreGroupedInExpandableSection()
    {
        var resultViewXaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        Assert.Contains("Weitere Kennzahlen", resultViewXaml);
        Assert.Contains("<Expander", resultViewXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase3_PipelineArea_IsPresentInNavigationSubTabsAndMainContent()
    {
        var navigationRailXaml = File.ReadAllText(FindUiFile("Views", "NavigationRail.xaml"));
        var subTabBarXaml = File.ReadAllText(FindUiFile("Views", "SubTabBar.xaml"));
        var mainWindowXaml = File.ReadAllText(FindUiFile("", "MainWindow.xaml"));

        Assert.Contains("Shell.ShowPipelineNav", navigationRailXaml);
        Assert.Contains("ConverterParameter=Pipeline", navigationRailXaml);
        Assert.Contains("ConverterParameter=Pipeline", subTabBarXaml);
        Assert.Contains("ConverterParameter=Conversion", subTabBarXaml);
        Assert.Contains("ConverterParameter=Sorting", subTabBarXaml);
        Assert.Contains("ConverterParameter=Batch", subTabBarXaml);
        Assert.Contains("<views:PipelineWorkbenchView", mainWindowXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase3_AppearanceView_ExposesReducedMotionAndDensityControls()
    {
        var appearanceXaml = File.ReadAllText(FindUiFile("Views", "SystemAppearanceView.xaml"));

        Assert.Contains("EnableReducedMotion", appearanceXaml);
        Assert.Contains("SelectedDensityMode", appearanceXaml);
        Assert.Contains("AvailableDensityModes", appearanceXaml);
    }

    [Fact]
    public void Phase3_SettingsPersistence_ContainsMotionDensityAndWizardStartupFlags()
    {
        var settingsDto = File.ReadAllText(FindUiFile("Services", "SettingsDto.cs"));
        var settingsService = File.ReadAllText(FindUiFile("Services", "SettingsService.cs"));

        Assert.Contains("ReduceMotion", settingsDto);
        Assert.Contains("DensityMode", settingsDto);
        Assert.Contains("ShowFirstRunWizardOnStartup", settingsDto);

        Assert.Contains("reduceMotion", settingsService, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("densityMode", settingsService, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("showFirstRunWizardOnStartup", settingsService, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Phase3_DialogWindows_UseSharedBaseStyleAndSeveritySignal()
    {
        var messageDialogXaml = File.ReadAllText(FindUiFile("", "MessageDialog.xaml"));
        var inputDialogXaml = File.ReadAllText(FindUiFile("", "InputDialog.xaml"));
        var resultDialogXaml = File.ReadAllText(FindUiFile("", "ResultDialog.xaml"));
        var dangerDialogXaml = File.ReadAllText(FindUiFile("", "DangerConfirmDialog.xaml"));

        Assert.Contains("BaseDialogWindowStyle", messageDialogXaml);
        Assert.Contains("BaseDialogWindowStyle", inputDialogXaml);
        Assert.Contains("BaseDialogWindowStyle", resultDialogXaml);
        Assert.Contains("BaseDialogWindowStyle", dangerDialogXaml);

        Assert.Contains("DialogSeverity", messageDialogXaml);
        Assert.Contains("DialogSeverity", dangerDialogXaml);
    }

    [Fact]
    public void Phase3_ThemeCatalog_IncludesCleanDaylightAndStarkContrastNaming()
    {
        var themeService = File.ReadAllText(FindUiFile("Services", "ThemeService.cs"));
        var settingsVm = File.ReadAllText(FindUiFile("ViewModels", "MainViewModel.Settings.cs"));

        Assert.Contains("CleanDaylight", themeService);
        Assert.Contains("Stark Contrast", settingsVm);
    }

    [Fact]
    public void Phase4_GlowEffects_AreConsistentAcrossThemes()
    {
        var synthwaveTheme = File.ReadAllText(FindUiFile("Themes", "SynthwaveDark.xaml"));
        var arcadeTheme = File.ReadAllText(FindUiFile("Themes", "ArcadeNeon.xaml"));

        Assert.DoesNotContain("Style TargetType=\"ProgressBar\"", synthwaveTheme, StringComparison.Ordinal);
        Assert.DoesNotContain("Style TargetType=\"ProgressBar\"", arcadeTheme, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase4_AnimationTimings_UseTokenizedDurations()
    {
        var controlTemplates = File.ReadAllText(FindUiFile("Themes", "_ControlTemplates.xaml"));

        Assert.Contains("Duration=\"{StaticResource TimingNormal}\"", controlTemplates, StringComparison.Ordinal);
        Assert.Contains("Duration=\"{StaticResource TimingFast}\"", controlTemplates, StringComparison.Ordinal);
        Assert.DoesNotContain("Duration=\"0:0:0.18\"", controlTemplates, StringComparison.Ordinal);
        Assert.DoesNotContain("Duration=\"0:0:0.12\"", controlTemplates, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase4_AppearanceView_HasThemePreviewAndOnboardingTourEntry()
    {
        var appearanceXaml = File.ReadAllText(FindUiFile("Views", "SystemAppearanceView.xaml"));
        var shellVm = File.ReadAllText(FindUiFile("ViewModels", "ShellViewModel.cs"));

        Assert.Contains("Theme Preview", appearanceXaml, StringComparison.Ordinal);
        Assert.Contains("Shell.StartOnboardingTourCommand", appearanceXaml, StringComparison.Ordinal);
        Assert.Contains("StartOnboardingTourCommand", shellVm, StringComparison.Ordinal);
    }

    [Fact]
    public void Phase4_ResultView_ConsoleDistributionChart_IsVisuallyEnhanced()
    {
        var resultViewXaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        Assert.Contains("ConsoleDistributionTrack", resultViewXaml, StringComparison.Ordinal);
        Assert.Contains("ConsoleDistributionBarFill", resultViewXaml, StringComparison.Ordinal);
        Assert.Contains("StringFormat={}{0:P0}", resultViewXaml, StringComparison.Ordinal);
    }

    // ═══ Open Items (Sections 10–11 of UI_UX_REDESIGN_PROPOSAL.md) ══════

    [Fact]
    public void OpenItem_DensitySpacing_ResourceDictionariesExist()
    {
        // Density modes must have dedicated spacing resource dictionaries
        var compactPath = FindUiFile("Themes", "_DensityCompact.xaml");
        var comfortablePath = FindUiFile("Themes", "_DensityComfortable.xaml");

        Assert.True(File.Exists(compactPath), "_DensityCompact.xaml must exist");
        Assert.True(File.Exists(comfortablePath), "_DensityComfortable.xaml must exist");

        var compact = File.ReadAllText(compactPath);
        var comfortable = File.ReadAllText(comfortablePath);

        // Compact must scale spacing down (×0.75 → PaddingCard should be 12)
        Assert.Contains("PaddingCard", compact, StringComparison.Ordinal);
        Assert.Contains("PaddingSection", compact, StringComparison.Ordinal);

        // Comfortable must scale spacing up (×1.25 → PaddingCard should be 20)
        Assert.Contains("PaddingCard", comfortable, StringComparison.Ordinal);
        Assert.Contains("PaddingSection", comfortable, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenItem_DensitySpacing_ViewModelExposesDensityScale()
    {
        var settingsVm = File.ReadAllText(FindUiFile("ViewModels", "MainViewModel.Settings.cs"));

        // ViewModel must expose a DensitySpacingScale property or call density dictionary swap
        Assert.Contains("DensitySpacingScale", settingsVm, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenItem_DensitySpacing_ThemeServiceCanApplyDensity()
    {
        var themeService = File.ReadAllText(FindUiFile("Services", "ThemeService.cs"));

        // ThemeService must have method to swap density dictionaries
        Assert.Contains("ApplyDensity", themeService, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenItem_LiveRegions_ResultViewHasLiveSettings()
    {
        var resultViewXaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        // Run summary and KPI values must be announced to screen readers
        Assert.Contains("AutomationProperties.LiveSetting", resultViewXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenItem_LiveRegions_ContextPanelHasLiveSettings()
    {
        var contextPanelXaml = File.ReadAllText(FindUiFile("Views", "ContextPanel.xaml"));

        Assert.Contains("AutomationProperties.LiveSetting", contextPanelXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenItem_ToolsView_HasViewModeToggle()
    {
        var toolsViewXaml = File.ReadAllText(FindUiFile("Views", "ToolsView.xaml"));

        // ToolsView must offer Grid/List view mode toggle
        Assert.Contains("ToolViewMode", toolsViewXaml, StringComparison.Ordinal);
    }

    [Fact]
    public void OpenItem_ToolsView_HasListViewTemplate()
    {
        var toolsViewXaml = File.ReadAllText(FindUiFile("Views", "ToolsView.xaml"));

        // ToolsView must have a compact list template for List mode
        Assert.Contains("ToolListTemplate", toolsViewXaml, StringComparison.Ordinal);
    }

    private static async Task ExecuteCommandAsync(ICommand command)
    {
        command.Execute(null);
        if (command is IAsyncRelayCommand asyncCommand && asyncCommand.ExecutionTask is not null)
            await asyncCommand.ExecutionTask;
    }

    private static MainViewModel CreateViewModel(RecordingDialogService? dialog = null)
    {
        dialog ??= new RecordingDialogService();
        var dataDir = FeatureService.ResolveDataDirectory() ?? RunEnvironmentBuilder.ResolveDataDir();
        var profileService = new RunProfileService(new InMemoryRunProfileStore(), dataDir);
        var materializer = new RunConfigurationMaterializer(new RunConfigurationResolver(profileService), new RunOptionsFactory());
        return new MainViewModel(
            new StubThemeService(),
            dialog,
            new StubSettingsService(),
            runProfileService: profileService,
            runConfigurationMaterializer: materializer);
    }

    private static string FindUiFile(string folder, string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var repoRoot = FindRepoRoot(callerPath);
        return Path.Combine(repoRoot, "src", "Romulus.UI.Wpf", folder, fileName);
    }

    private static string FindRepoRoot(string? callerPath)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "src", "Romulus.sln")))
                return dir;

            dir = Path.GetDirectoryName(dir);
        }

        return Directory.GetCurrentDirectory();
    }

    private sealed class InMemoryRunProfileStore : IRunProfileStore
    {
        private readonly Dictionary<string, RunProfileDocument> _profiles = new(StringComparer.OrdinalIgnoreCase);

        public ValueTask<IReadOnlyList<RunProfileDocument>> ListAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<RunProfileDocument>>(
                _profiles.Values
                    .OrderBy(static item => item.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        public ValueTask<RunProfileDocument?> TryGetAsync(string id, CancellationToken ct = default)
        {
            _profiles.TryGetValue(id, out var profile);
            return ValueTask.FromResult(profile);
        }

        public ValueTask UpsertAsync(RunProfileDocument profile, CancellationToken ct = default)
        {
            _profiles[profile.Id] = profile;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default)
            => ValueTask.FromResult(_profiles.Remove(id));
    }

    private sealed class RecordingDialogService : IDialogService
    {
        private readonly Queue<string> _inputQueue = new();

        public void EnqueueInput(string value) => _inputQueue.Enqueue(value);

        public string? BrowseFolder(string title = "Ordner auswaehlen") => null;
        public string? BrowseFile(string title = "Datei auswaehlen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestaetigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "")
            => _inputQueue.Count > 0 ? _inputQueue.Dequeue() : defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestaetigen") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }

}
