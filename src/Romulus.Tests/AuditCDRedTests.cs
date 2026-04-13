using System.Text.RegularExpressions;
using Romulus.Contracts.Models;
using Romulus.Core.Conversion;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AuditCDRedTests
{
    [Fact]
    public void C01_MainViewModel_UsesChildViewModels_ForMajorDomains()
    {
        var vm = new MainViewModel();

        Assert.NotNull(vm.Setup);
        Assert.NotNull(vm.Run);
        Assert.NotNull(vm.Tools);
        Assert.NotNull(vm.CommandPalette);
    }

    [Fact]
    public void C02_RunConfigurationMap_MustAvoidHardcodedKeyMatrix()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs");

        Assert.DoesNotContain("[\"workflowScenarioId\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"profileId\"]", source, StringComparison.Ordinal);
        Assert.DoesNotContain("[\"mode\"]", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C03_SettingsSync_UsesSharedMirrorStateModel()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs");

        Assert.Contains("SetupSyncMirrorState", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C04_RunPipeline_DeclinePaths_UseSharedResetHelper()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs");

        Assert.Contains("ResetDeclinedRunPromptState", source, StringComparison.Ordinal);
        Assert.DoesNotContain("ConvertOnly = false;\r\n            BusyHint = \"\";\r\n            CurrentRunState = RunState.Idle;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C05_FormatScorer_IsDataDrivenViaProfileFactory()
    {
        var profileSource = ReadSource("src/Romulus.Infrastructure/Orchestration/FormatScoringProfile.cs");

        Assert.Contains("RegisterScoreFactory", profileSource, StringComparison.Ordinal);
        Assert.Contains("format-scores.json", profileSource, StringComparison.Ordinal);
    }

    [Fact]
    public void C06_RunOrchestrator_UsesDedicatedPhasePlanExecutor()
    {
        var source = ReadSource("src/Romulus.Infrastructure/Orchestration/RunOrchestrator.cs");

        Assert.Contains("IPhasePlanExecutor", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private void ExecutePhasePlan(", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C07_RunConfigurationSelectionSuppression_UsesScopeHelper()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.Productization.cs");

        Assert.Contains("EnterRunConfigurationSelectionSuppressionScope", source, StringComparison.Ordinal);
        Assert.DoesNotContain("_suppressRunConfigurationSelectionApply = true;", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C08_ApplyMaterializedRunConfiguration_InvalidConflictPolicy_RollsBackState()
    {
        var vm = new MainViewModel();
        var previousDryRun = vm.DryRun;
        var previousRemoveJunk = vm.RemoveJunk;
        var previousConflictPolicy = vm.ConflictPolicy;

        var materialized = new MaterializedRunConfiguration(
            EffectiveDraft: new RunConfigurationDraft
            {
                Mode = "Move",
                RemoveJunk = !previousRemoveJunk,
                ConflictPolicy = "not-a-valid-policy"
            },
            Workflow: null,
            Profile: null,
            EffectiveProfileId: null,
            Options: new Romulus.Contracts.Models.RunOptions());

        Assert.Throws<InvalidOperationException>(() => vm.ApplyMaterializedRunConfiguration(materialized));
        Assert.Equal(previousDryRun, vm.DryRun);
        Assert.Equal(previousRemoveJunk, vm.RemoveJunk);
        Assert.Equal(previousConflictPolicy, vm.ConflictPolicy);
    }

    [Fact]
    public void C09_MainWindow_Cleanup_DisposesMainViewModel()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/MainWindow.xaml.cs");

        Assert.Contains("_vm.Dispose();", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C10_ScheduleAutoSave_ReusesSingleTimerInstance()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs");
        var scheduleMatch = Regex.Match(
            source,
            @"private void ScheduleAutoSave\(\)[\s\S]*?\n    }",
            RegexOptions.Singleline);

        Assert.True(scheduleMatch.Success, "ScheduleAutoSave method not found.");
        var scheduleBody = scheduleMatch.Value;

        Assert.DoesNotContain("_autoSaveTimer?.Dispose();", scheduleBody, StringComparison.Ordinal);
        Assert.Contains("_autoSaveTimer ??=", scheduleBody, StringComparison.Ordinal);
        Assert.Contains("_autoSaveTimer.Change", scheduleBody, StringComparison.Ordinal);
    }

    [Fact]
    public void C11_ConversionGraph_DepthLimit_IsConfigurableAndWarned()
    {
        var source = ReadSource("src/Romulus.Core/Conversion/ConversionGraph.cs");

        Assert.Contains("RegisterMaxSearchDepth", source, StringComparison.Ordinal);
        Assert.Contains("Trace.TraceWarning", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C12_GameKeyNormalizer_RegistrationPrecedence_IsGuarded()
    {
        var source = ReadSource("src/Romulus.Core/GameKeys/GameKeyNormalizer.cs");

        Assert.Contains("registration precedence", source, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Trace.TraceWarning", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C13_RegionPreferenceAccess_MustUseDictionaryMapping()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.Settings.cs");

        Assert.Contains("RegionPreferenceReaders", source, StringComparison.Ordinal);
        Assert.Contains("RegionPreferenceWriters", source, StringComparison.Ordinal);
        Assert.DoesNotContain("private bool GetRegionBool(string code) => code switch", source, StringComparison.Ordinal);
        Assert.DoesNotContain("switch (code)", source, StringComparison.Ordinal);
    }

    [Fact]
    public void C14_MainViewModel_NavigationCommands_MustUseNamedTagConstants()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.cs");

        Assert.Contains("NavTagConfig", source, StringComparison.Ordinal);
        Assert.Contains("NavTagLibrary", source, StringComparison.Ordinal);
        Assert.Contains("NavTagTools", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.NavigateTo(\"Config\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.NavigateTo(\"Library\")", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Shell.NavigateTo(\"Tools\")", source, StringComparison.Ordinal);
    }

    [Fact]
    public void D01_RunConfigurationExplicitness_IncludesWorkflowAndProfileFlags()
    {
        var source = ReadSource("src/Romulus.Contracts/Models/RunConfigurationModels.cs");

        Assert.Matches(new Regex(@"public bool WorkflowScenarioId \{ get; init; \}"), source);
        Assert.Matches(new Regex(@"public bool ProfileId \{ get; init; \}"), source);
    }

    [Fact]
    public void D02_CliParser_UsesSharedExtensionNormalizer()
    {
        var source = ReadSource("src/Romulus.CLI/CliArgsParser.cs");

        Assert.Contains("VersionHelper.NormalizeExtensionList", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Extensions must start with '.'", source, StringComparison.Ordinal);
    }

    [Fact]
    public void D03_ConversionReviewEntries_AreDelegatedToRunService()
    {
        var source = ReadSource("src/Romulus.UI.Wpf/ViewModels/MainViewModel.RunPipeline.cs");

        Assert.DoesNotContain("RunEnvironmentBuilder.Build", source, StringComparison.Ordinal);
        Assert.Contains("_runService.BuildConversionReviewEntries", source, StringComparison.Ordinal);
    }

    [Fact]
    public void D04_VersionScorer_RegexTimeoutCatch_LogsWarning()
    {
        var source = ReadSource("src/Romulus.Core/Scoring/VersionScorer.cs");

        Assert.Contains("catch (RegexMatchTimeoutException)", source, StringComparison.Ordinal);
        Assert.Contains("Trace.TraceWarning", source, StringComparison.Ordinal);
    }

    [Fact]
    public void D05_GameKeyNormalizer_IterationCap_UsesTraceWarning()
    {
        var source = ReadSource("src/Romulus.Core/GameKeys/GameKeyNormalizer.cs");

        Assert.Contains("Trace.TraceWarning", source, StringComparison.Ordinal);
        Assert.DoesNotContain("Trace.WriteLine($\"[GameKeyNormalizer] DOS metadata strip hit iteration cap", source, StringComparison.Ordinal);
    }

    private static string ReadSource(string relativePath)
    {
        var root = FindRepositoryRoot();
        var fullPath = Path.Combine(root, relativePath.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(fullPath);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not resolve repository root from test context.");
    }
}
