using System.Collections;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
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
    public void MainViewModel_SelectedWorkflowScenario_AppliesSharedWorkflowDefaults()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);
        vm.UseDat = false;
        vm.EnableDatAudit = false;
        vm.SortConsole = false;
        vm.RemoveJunk = false;
        vm.DryRun = false;

        vm.SelectedWorkflowScenarioId = WorkflowScenarioIds.FullAudit;

        Assert.Equal(WorkflowScenarioIds.FullAudit, vm.SelectedWorkflowScenarioId);
        Assert.True(vm.UseDat);
        Assert.True(vm.EnableDatAudit);
        Assert.True(vm.SortConsole);
        Assert.True(vm.RemoveJunk);
        Assert.True(vm.DryRun);
        Assert.Equal("default", vm.SelectedRunProfileId);
    }

    [Fact]
    public void MainViewModel_SelectedRunProfile_AppliesSharedProfileDefaults()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);
        vm.UseDat = true;
        vm.SortConsole = true;
        vm.RemoveJunk = true;

        vm.SelectedRunProfileId = "quick-scan";

        Assert.Equal("quick-scan", vm.SelectedRunProfileId);
        Assert.False(vm.UseDat);
        Assert.False(vm.SortConsole);
        Assert.False(vm.RemoveJunk);
        Assert.True(vm.DryRun);
    }

    [Fact]
    public void MainViewModel_Explicitness_TracksOnlyUserOverridesAfterSelection()
    {
        var vm = CreateViewModel();
        vm.Roots.Add(_tempRoot);

        vm.SelectedWorkflowScenarioId = WorkflowScenarioIds.FullAudit;

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
    public void FeatureCommandService_ProfileSave_AndLoad_ReuseSharedProfileModel()
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

        var sut = new FeatureCommandService(vm, new StubSettingsService(), dialog);
        sut.RegisterCommands();

        vm.FeatureCommands[FeatureCommandKeys.ProfileSave].Execute(null);

        Assert.Equal("Space-Saver-Custom", vm.SelectedRunProfileId);
        Assert.Contains(vm.AvailableRunProfiles, item => item.Id == "Space-Saver-Custom");
        Assert.Contains(vm.LogEntries, entry => entry.Text.Contains("Profil gespeichert", StringComparison.OrdinalIgnoreCase));

        vm.SortConsole = false;
        vm.UseDat = false;
        vm.EnableDatAudit = false;
        vm.RemoveJunk = false;
        vm.RestoreRunConfigurationSelection(null, "Space-Saver-Custom");

        vm.FeatureCommands[FeatureCommandKeys.ProfileLoad].Execute(null);

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
        Assert.DoesNotContain("ConverterParameter=Conversion", subTabBarXaml);
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

        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "")
            => _inputQueue.Count > 0 ? _inputQueue.Dequeue() : defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }

    private sealed class StubSettingsService : ISettingsService
    {
        public string? LastAuditPath => null;
        public string LastTheme => "Dark";
        public SettingsDto? Load() => new();
        public void LoadInto(MainViewModel vm) { }
        public bool SaveFrom(MainViewModel vm, string? lastAuditPath = null) => true;
    }

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
        public void ApplyTheme(AppTheme theme) { }
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
    }
}
