using System.Collections;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Profiles;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;

namespace RomCleanup.Tests;

public sealed class WpfProductizationTests : IDisposable
{
    private readonly string _tempRoot;

    public WpfProductizationTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "RomCleanup_WpfProductization_" + Guid.NewGuid().ToString("N"));
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
    }

    [Fact]
    public void ToolsViewModel_ContainsCollectionMergeEntry()
    {
        var vm = new ToolsViewModel(new LocalizationService());

        Assert.Contains(vm.ToolItems, item => item.Key == FeatureCommandKeys.CollectionMerge);
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
        return Path.Combine(repoRoot, "src", "RomCleanup.UI.Wpf", folder, fileName);
    }

    private static string FindRepoRoot(string? callerPath)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "src", "RomCleanup.sln")))
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
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<RomCleanup.Contracts.Models.ConversionReviewEntry> entries) => true;
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
