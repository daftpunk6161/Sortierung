using System.Text.Json;
using Romulus.Contracts.Models;
using Romulus.Core.GameKeys;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// V2-TEST-H01: 50+ new WPF tests covering Commands, Validation, Services,
/// ViewModel state transitions, Preset commands, Dashboard, Settings roundtrip.
/// </summary>
public sealed class WpfNewTests : IDisposable
{
    private readonly string _tempDir;

    public WpfNewTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"wpf_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══ MainViewModel — Validation (INotifyDataErrorInfo) ══════════════

    [Fact]
    public void Validation_EmptyToolPath_NoError()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = "";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Validation_ValidToolPath_NoError()
    {
        var vm = new MainViewModel();
        var exePath = Path.Combine(_tempDir, "chdman.exe");
        File.WriteAllText(exePath, "dummy");
        vm.ToolChdman = exePath;
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Validation_InvalidToolPath_SetsError()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = @"C:\nonexistent\fake_tool.exe";
        Assert.True(vm.HasErrors);
        var errors = vm.GetErrors(nameof(vm.ToolChdman));
        Assert.NotNull(errors);
    }

    [Fact]
    public void Validation_ClearInvalidPath_ClearsError()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = @"C:\nonexistent\fake.exe";
        Assert.True(vm.HasErrors);
        vm.ToolChdman = "";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Validation_ValidDirectory_NoError()
    {
        var vm = new MainViewModel();
        vm.DatRoot = _tempDir;
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void Validation_InvalidDirectory_SetsError()
    {
        var vm = new MainViewModel();
        vm.DatRoot = @"C:\nonexistent_dir_12345";
        Assert.True(vm.HasErrors);
    }

    [Fact]
    public void Validation_MultipleErrors_AllTracked()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = @"C:\fake\chdman.exe";
        vm.ToolDolphin = @"C:\fake\dolphintool.exe";
        Assert.True(vm.HasErrors);
        var err1 = vm.GetErrors(nameof(vm.ToolChdman));
        var err2 = vm.GetErrors(nameof(vm.ToolDolphin));
        Assert.NotNull(err1);
        Assert.NotNull(err2);
    }

    [Fact]
    public void Validation_ErrorsChanged_EventFires()
    {
        var vm = new MainViewModel();
        var fired = false;
        vm.ErrorsChanged += (_, _) => fired = true;
        vm.ToolChdman = @"C:\nonexistent\tool.exe";
        Assert.True(fired);
    }

    [Fact]
    public void Validation_GetErrors_UnknownProperty_ReturnsEmpty()
    {
        var vm = new MainViewModel();
        var errors = vm.GetErrors("NonExistentProperty");
        Assert.NotNull(errors);
        Assert.Empty(errors.Cast<object>());
    }

    // ═══ MainViewModel — State Machine IsValidTransition ════════════════

    [Theory]
    [InlineData(RunState.Idle, RunState.Preflight, true)]
    [InlineData(RunState.Preflight, RunState.Scanning, true)]
    [InlineData(RunState.Scanning, RunState.Deduplicating, true)]
    [InlineData(RunState.Deduplicating, RunState.Sorting, true)]
    [InlineData(RunState.Sorting, RunState.Moving, true)]
    [InlineData(RunState.Moving, RunState.Converting, true)]
    public void IsValidTransition_ForwardSteps_Valid(RunState from, RunState to, bool expected)
    {
        Assert.Equal(expected, MainViewModel.IsValidTransition(from, to));
    }

    [Theory]
    [InlineData(RunState.Scanning, RunState.Completed)]
    [InlineData(RunState.Deduplicating, RunState.Failed)]
    [InlineData(RunState.Moving, RunState.Cancelled)]
    [InlineData(RunState.Converting, RunState.CompletedDryRun)]
    public void IsValidTransition_AnyActiveToTerminal_Valid(RunState from, RunState to)
    {
        Assert.True(MainViewModel.IsValidTransition(from, to));
    }

    [Theory]
    [InlineData(RunState.Completed, RunState.Idle)]
    [InlineData(RunState.CompletedDryRun, RunState.Preflight)]
    [InlineData(RunState.Failed, RunState.Idle)]
    [InlineData(RunState.Cancelled, RunState.Preflight)]
    public void IsValidTransition_TerminalToReset_Valid(RunState from, RunState to)
    {
        Assert.True(MainViewModel.IsValidTransition(from, to));
    }

    [Theory]
    [InlineData(RunState.Idle, RunState.Scanning)]
    [InlineData(RunState.Idle, RunState.Completed)]
    [InlineData(RunState.Completed, RunState.Scanning)]
    public void IsValidTransition_InvalidJumps_False(RunState from, RunState to)
    {
        Assert.False(MainViewModel.IsValidTransition(from, to));
    }

    [Fact]
    public void IsValidTransition_SameState_Valid()
    {
        foreach (var state in Enum.GetValues<RunState>())
            Assert.True(MainViewModel.IsValidTransition(state, state));
    }

    [Theory]
    [InlineData(RunState.Scanning, RunState.Moving)]
    [InlineData(RunState.Deduplicating, RunState.Converting)]
    public void IsValidTransition_SkipPhases_Valid(RunState from, RunState to)
    {
        Assert.True(MainViewModel.IsValidTransition(from, to));
    }

    // ═══ MainViewModel — State Properties ═══════════════════════════════

    [Fact]
    public void IsBusy_DefaultIdle_False()
    {
        var vm = new MainViewModel();
        Assert.False(vm.IsBusy);
        Assert.True(vm.IsIdle);
    }

    [Fact]
    public void IsBusy_Preflight_True()
    {
        var vm = new MainViewModel();
        vm.CurrentRunState = RunState.Preflight;
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsIdle);
    }

    [Fact]
    public void ShowStartMoveButton_AfterCompletedDryRun_True()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");
        Assert.True(vm.ShowStartMoveButton);
    }

    [Fact]
    public void ShowStartMoveButton_Idle_False()
    {
        var vm = new MainViewModel();
        Assert.False(vm.ShowStartMoveButton);
    }

    [Fact]
    public void HasRunResult_AfterCompleted_True()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Completed);
        Assert.True(vm.HasRunResult);
    }

    [Fact]
    public void HasRunResult_AfterFailed_False()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Failed);
        Assert.False(vm.HasRunResult);
    }

    // ═══ MainViewModel — Rollback Stack ═════════════════════════════════

    [Fact]
    public void RollbackStack_Push_EnablesUndo()
    {
        var vm = new MainViewModel();
        Assert.False(vm.HasRollbackUndo);
        vm.PushRollbackUndo("audit.csv");
        Assert.True(vm.HasRollbackUndo);
    }

    [Fact]
    public void RollbackStack_Pop_ReturnsPath()
    {
        var vm = new MainViewModel();
        vm.PushRollbackUndo("audit_001.csv");
        var popped = vm.PopRollbackUndo();
        Assert.Equal("audit_001.csv", popped);
        Assert.False(vm.HasRollbackUndo);
    }

    [Fact]
    public void RollbackStack_PopUndo_EnablesRedo()
    {
        var vm = new MainViewModel();
        vm.PushRollbackUndo("audit.csv");
        vm.PopRollbackUndo();
        Assert.True(vm.HasRollbackRedo);
    }

    [Fact]
    public void RollbackStack_PopRedo_ReturnsPath()
    {
        var vm = new MainViewModel();
        vm.PushRollbackUndo("audit.csv");
        vm.PopRollbackUndo();
        var redone = vm.PopRollbackRedo();
        Assert.Equal("audit.csv", redone);
    }

    [Fact]
    public void RollbackStack_BoundedAt50()
    {
        var vm = new MainViewModel();
        for (int i = 0; i < 60; i++)
            vm.PushRollbackUndo($"audit_{i}.csv");

        // Count how many we can pop
        int count = 0;
        while (vm.HasRollbackUndo)
        {
            vm.PopRollbackUndo();
            count++;
        }
        Assert.True(count <= 50, $"Expected at most 50 entries, got {count}");
    }

    [Fact]
    public void RollbackStack_PushClearsRedo()
    {
        var vm = new MainViewModel();
        vm.PushRollbackUndo("a.csv");
        vm.PopRollbackUndo();
        Assert.True(vm.HasRollbackRedo);
        vm.PushRollbackUndo("b.csv");
        Assert.False(vm.HasRollbackRedo);
    }

    // ═══ MainViewModel — Preset Commands ════════════════════════════════

    [Fact]
    public void PresetSafeDryRun_SetsDryRunTrue()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.PresetSafeDryRunCommand.Execute(null);
        Assert.True(vm.DryRun);
        Assert.False(vm.ConvertEnabled);
        Assert.False(vm.AggressiveJunk);
    }

    [Fact]
    public void PresetFullSort_SetsSortConsoleTrue()
    {
        var vm = new MainViewModel();
        vm.SortConsole = false;
        vm.PresetFullSortCommand.Execute(null);
        Assert.True(vm.SortConsole);
        Assert.True(vm.DryRun);
    }

    [Fact]
    public void PresetConvert_SetsConvertTrue()
    {
        var vm = new MainViewModel();
        vm.ConvertEnabled = false;
        vm.PresetConvertCommand.Execute(null);
        Assert.True(vm.ConvertEnabled);
        Assert.True(vm.DryRun);
    }

    // ═══ MainViewModel — GetPreferredRegions ════════════════════════════

    [Fact]
    public void GetPreferredRegions_SimpleMode_Index0_Europa()
    {
        var vm = new MainViewModel();
        vm.IsSimpleMode = true;
        vm.PreferEU = true;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("EU", regions);
    }

    [Fact]
    public void GetPreferredRegions_SimpleMode_Index1_NorthAmerica()
    {
        var vm = new MainViewModel();
        vm.IsSimpleMode = true;
        vm.PreferUS = true;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("US", regions);
    }

    [Fact]
    public void GetPreferredRegions_SimpleMode_Index2_Japan()
    {
        var vm = new MainViewModel();
        vm.IsSimpleMode = true;
        vm.PreferJP = true;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("JP", regions);
    }

    [Fact]
    public void GetPreferredRegions_SimpleMode_Index3_Worldwide()
    {
        var vm = new MainViewModel();
        vm.IsSimpleMode = true;
        vm.PreferWORLD = true;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("WORLD", regions);
    }

    [Fact]
    public void GetPreferredRegions_ExpertMode_SelectiveFlags()
    {
        var vm = new MainViewModel();
        vm.IsSimpleMode = false;
        vm.PreferEU = false;
        vm.PreferUS = false;
        vm.PreferJP = true;
        vm.PreferWORLD = false;
        vm.PreferDE = true;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("JP", regions);
        Assert.Contains("DE", regions);
        Assert.DoesNotContain("EU", regions);
        Assert.DoesNotContain("US", regions);
    }

    [Fact]
    public void GetPreferredRegions_ExpertMode_NoFlags_Empty()
    {
        var vm = new MainViewModel();
        vm.IsSimpleMode = false;
        // Clear all region flags
        vm.PreferEU = false;
        vm.PreferUS = false;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.PreferDE = false;
        vm.PreferFR = false;
        vm.PreferIT = false;
        vm.PreferES = false;
        vm.PreferAU = false;
        vm.PreferASIA = false;
        vm.PreferKR = false;
        vm.PreferCN = false;
        vm.PreferBR = false;
        vm.PreferNL = false;
        vm.PreferSE = false;
        vm.PreferSCAN = false;
        var regions = vm.GetPreferredRegions();
        Assert.Empty(regions);
    }

    // ═══ MainViewModel — GetCurrentConfigMap ════════════════════════════

    [Fact]
    public void GetCurrentConfigMap_ContainsExpectedKeys()
    {
        var vm = new MainViewModel();
        var map = vm.GetCurrentConfigMap();
        Assert.Contains("sortConsole", map.Keys);
        Assert.Contains("dryRun", map.Keys);
        Assert.Contains("aggressiveJunk", map.Keys);
        Assert.Contains("useDat", map.Keys);
        Assert.Contains("convertEnabled", map.Keys);
    }

    [Fact]
    public void GetCurrentConfigMap_ReflectsCurrentState()
    {
        var vm = new MainViewModel();
        vm.SortConsole = true;
        vm.DryRun = false;
        var map = vm.GetCurrentConfigMap();
        Assert.Equal("True", map["sortConsole"]);
        Assert.Equal("False", map["dryRun"]);
    }

    // ═══ MainViewModel — GameKey Preview ════════════════════════════════

    [Fact]
    public void GameKeyPreview_NormalizesInput()
    {
        var vm = new MainViewModel();
        vm.GameKeyPreviewInput = "Super Mario Bros (USA) (Rev 1)";
        vm.GameKeyPreviewCommand.Execute(null);
        Assert.False(string.IsNullOrEmpty(vm.GameKeyPreviewOutput));
        Assert.Equal(GameKeyNormalizer.Normalize("Super Mario Bros (USA) (Rev 1)"),
            vm.GameKeyPreviewOutput);
    }

    [Fact]
    public void GameKeyPreview_EmptyInput_CannotExecute()
    {
        var vm = new MainViewModel();
        vm.GameKeyPreviewInput = "";
        Assert.False(vm.GameKeyPreviewCommand.CanExecute(null));
    }

    // ═══ MainViewModel — CompleteRun ════════════════════════════════════

    [Fact]
    public void CompleteRun_DryRunSuccess_TransitionsToCompletedDryRun()
    {
        var vm = new MainViewModel();
        vm.DryRun = true;
        SetRunStateViaValidPath(vm, RunState.Scanning);
        vm.CompleteRun(true);
        Assert.Equal(RunState.CompletedDryRun, vm.CurrentRunState);
    }

    [Fact]
    public void CompleteRun_MoveSuccess_TransitionsToCompleted()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        SetRunStateViaValidPath(vm, RunState.Moving);
        vm.CompleteRun(true);
        Assert.Equal(RunState.Completed, vm.CurrentRunState);
    }

    [Fact]
    public void CompleteRun_Failure_TransitionsToFailed()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Scanning);
        vm.CompleteRun(false);
        Assert.Equal(RunState.Failed, vm.CurrentRunState);
    }

    [Fact]
    public void CompleteRun_WithReportPath_SetsLastReportPath()
    {
        var vm = new MainViewModel();
        vm.DryRun = true;
        SetRunStateViaValidPath(vm, RunState.Scanning);
        vm.CompleteRun(true, "/path/to/report.html");
        Assert.Equal("/path/to/report.html", vm.LastReportPath);
    }

    [Fact]
    public void CompleteRun_WithoutReportPath_ClearsLastReportPath()
    {
        var vm = new MainViewModel();
        vm.DryRun = true;
        SetRunStateViaValidPath(vm, RunState.Scanning);
        vm.CompleteRun(true, "/path/to/report.html");

        SetRunStateViaValidPath(vm, RunState.Scanning);
        vm.CompleteRun(true);

        Assert.Equal(string.Empty, vm.LastReportPath);
    }

    [Fact]
    public void OpenReportCommand_UnsafeExtension_IsBlockedAndLogged()
    {
        var vm = new MainViewModel();
        var tempPath = Path.Combine(Path.GetTempPath(), $"unsafe_report_{Guid.NewGuid():N}.bat");
        File.WriteAllText(tempPath, "@echo off");

        try
        {
            vm.LastReportPath = tempPath;

            Assert.True(vm.OpenReportCommand.CanExecute(null));
            vm.OpenReportCommand.Execute(null);

            Assert.Contains(vm.LogEntries, entry =>
                entry.Text.Contains("blockiert", StringComparison.OrdinalIgnoreCase)
                && entry.Level == "WARN");
        }
        finally
        {
            if (File.Exists(tempPath))
                File.Delete(tempPath);
        }
    }

    // ═══ MainViewModel — CTS Cancellation ═══════════════════════════════

    [Fact]
    public void CreateRunCancellation_ReturnsValidToken()
    {
        var vm = new MainViewModel();
        var token = vm.CreateRunCancellation();
        Assert.False(token.IsCancellationRequested);
    }

    [Fact]
    public void CreateRunCancellation_MultipleCalls_DisposePrevious()
    {
        var vm = new MainViewModel();
        var token1 = vm.CreateRunCancellation();
        var token2 = vm.CreateRunCancellation();
        // token1 should now be from a disposed CTS
        Assert.False(token2.IsCancellationRequested);
    }

    // ═══ MainViewModel — AddLog ═════════════════════════════════════════

    [Fact]
    public void AddLog_AppendsToLogEntries()
    {
        var vm = new MainViewModel();
        var initialCount = vm.LogEntries.Count;
        vm.AddLog("Test message", "INFO");
        Assert.Equal(initialCount + 1, vm.LogEntries.Count);
    }

    [Fact]
    public void AddLog_CapAt10000()
    {
        var vm = new MainViewModel();
        // Add 10010 entries (should cap at 10000)
        for (int i = 0; i < 10_010; i++)
            vm.AddLog($"msg{i}", "INFO");
        Assert.True(vm.LogEntries.Count <= 10_000);
    }

    // ═══ MainViewModel — Collections ════════════════════════════════════

    [Fact]
    public void ExtensionFilters_Initialized()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.ExtensionFilters);
        Assert.True(vm.ExtensionFilters.Count >= 14, "At least 14 extension filters expected");
    }

    [Fact]
    public void ConsoleFilters_Initialized()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.ConsoleFilters);
        Assert.True(vm.ConsoleFilters.Count >= 20, "At least 20 console filters expected");
    }

    [Fact]
    public void Roots_InitiallyEmpty()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.Roots);
        Assert.Empty(vm.Roots);
    }

    [Fact]
    public void ErrorSummaryItems_InitiallyEmpty()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.ErrorSummaryItems);
        Assert.Empty(vm.ErrorSummaryItems);
    }

    // ═══ MainViewModel — Settings Properties Roundtrip ══════════════════

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SettingsRoundtrip_SortConsole(bool value)
    {
        var vm = new MainViewModel();
        vm.SortConsole = value;
        Assert.Equal(value, vm.SortConsole);
    }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("MD5")]
    public void SettingsRoundtrip_DatHashType(string value)
    {
        var vm = new MainViewModel();
        vm.DatHashType = value;
        Assert.Equal(value, vm.DatHashType);
    }

    [Theory]
    [InlineData("de")]
    [InlineData("en")]
    [InlineData("fr")]
    public void SettingsRoundtrip_Locale(string value)
    {
        var vm = new MainViewModel();
        vm.Locale = value;
        Assert.Equal(value, vm.Locale);
    }

    [Theory]
    [InlineData(ConflictPolicy.Rename)]
    [InlineData(ConflictPolicy.Skip)]
    [InlineData(ConflictPolicy.Overwrite)]
    public void SettingsRoundtrip_ConflictPolicy(ConflictPolicy policy)
    {
        var vm = new MainViewModel();
        vm.ConflictPolicyIndex = (int)policy;
        Assert.Equal((int)policy, vm.ConflictPolicyIndex);
    }

    // ═══ MainViewModel — Mode Toggle ════════════════════════════════════

    [Fact]
    public void IsSimpleMode_Default_True()
    {
        var vm = new MainViewModel();
        Assert.True(vm.IsSimpleMode);
    }

    [Fact]
    public void IsSimpleMode_Toggle_UpdatesExpertMode()
    {
        var vm = new MainViewModel();
        vm.IsSimpleMode = false;
        Assert.True(vm.IsExpertMode);
        vm.IsSimpleMode = true;
        Assert.False(vm.IsExpertMode);
    }

    [Fact]
    public void IsSimpleMode_Toggle_SynchronizesShellModeAndLabel()
    {
        var vm = new MainViewModel();

        vm.IsSimpleMode = false;
        Assert.False(vm.Shell.IsSimpleMode);
        Assert.Equal("Experte", vm.CurrentUiModeLabel);

        vm.IsSimpleMode = true;
        Assert.True(vm.Shell.IsSimpleMode);
        Assert.Equal("Einfach", vm.CurrentUiModeLabel);
    }

    [Fact]
    public void ShellViewModel_RetiredToolSubTab_FallsBackToFeatureCatalog()
    {
        var shell = new ShellViewModel(new LocalizationService())
        {
            IsSimpleMode = false
        };

        shell.SelectedNavTag = "Tools";
        shell.SelectedSubTab = "GameKeyLab";

        Assert.Equal("Tools", shell.SelectedNavTag);
        Assert.Equal("Features", shell.SelectedSubTab);
        Assert.True(shell.ShowToolsNav);
    }

    [Fact]
    public void ShellViewModel_EnableSimpleMode_CoercesHiddenLibrarySubTabToResults()
    {
        var shell = new ShellViewModel(new LocalizationService())
        {
            IsSimpleMode = false,
            SelectedNavTag = "Library",
            SelectedSubTab = "Decisions"
        };

        shell.IsSimpleMode = true;

        Assert.Equal("Library", shell.SelectedNavTag);
        Assert.Equal("Results", shell.SelectedSubTab);
        Assert.False(shell.ShowLibraryDecisionsTab);
        Assert.True(shell.ShowLibrarySafetyTab);
    }

    [Fact]
    public void ShellViewModel_RetiredLibraryReportSubTab_FallsBackToResults()
    {
        var shell = new ShellViewModel(new LocalizationService())
        {
            IsSimpleMode = false,
            SelectedNavTag = "Library"
        };

        shell.SelectedSubTab = "Report";

        Assert.Equal("Library", shell.SelectedNavTag);
        Assert.Equal("Results", shell.SelectedSubTab);
        Assert.False(shell.ShowLibraryReportTab);
    }

    // ═══ SettingsDto — Record Defaults ══════════════════════════════════

    [Fact]
    public void SettingsDto_Defaults_AreCorrect()
    {
        var dto = new SettingsDto();
        Assert.Equal("Info", dto.LogLevel);
        Assert.False(dto.AggressiveJunk);
        Assert.False(dto.AliasKeying);
        Assert.Equal(new[] { "EU", "US", "JP", "WORLD" }, dto.PreferredRegions);
        Assert.Equal("SHA1", dto.DatHashType);
        Assert.True(dto.DatFallback);
        Assert.True(dto.DryRun);
        Assert.True(dto.ConfirmMove);
        Assert.Equal(ConflictPolicy.Rename, dto.ConflictPolicy);
        Assert.Equal("Dark", dto.Theme);
    }

    [Fact]
    public void SettingsDto_WithExpression_CreatesModifiedCopy()
    {
        var original = new SettingsDto();
        var modified = original with { AggressiveJunk = true, DatHashType = "SHA256" };
        Assert.False(original.AggressiveJunk);
        Assert.True(modified.AggressiveJunk);
        Assert.Equal("SHA1", original.DatHashType);
        Assert.Equal("SHA256", modified.DatHashType);
    }

    // ═══ SettingsService — ApplyToViewModel ═════════════════════════════

    [Fact]
    public void ApplyToViewModel_MapsAllProperties()
    {
        var vm = new MainViewModel();
        var dto = new SettingsDto
        {
            AggressiveJunk = true,
            AliasKeying = true,
            LogLevel = "Debug",
            DatHashType = "SHA256",
            UseDat = true,
            SortConsole = true
        };
        SettingsService.ApplyToViewModel(vm, dto);
        Assert.True(vm.AggressiveJunk);
        Assert.True(vm.AliasKeying);
        Assert.Equal("Debug", vm.LogLevel);
        Assert.Equal("SHA256", vm.DatHashType);
        Assert.True(vm.UseDat);
        Assert.True(vm.SortConsole);
    }

    [Fact]
    public void ApplyToViewModel_RegionFlags_Scattered()
    {
        var vm = new MainViewModel();
        var dto = new SettingsDto { PreferredRegions = new[] { "JP", "DE" } };
        SettingsService.ApplyToViewModel(vm, dto);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferDE);
        Assert.False(vm.PreferEU);
        Assert.False(vm.PreferUS);
    }

    [Fact]
    public void ApplyToViewModel_Roots_Populated()
    {
        var vm = new MainViewModel();
        var dto = new SettingsDto { Roots = new[] { @"D:\ROMs", @"E:\Games" } };
        SettingsService.ApplyToViewModel(vm, dto);
        Assert.Equal(2, vm.Roots.Count);
        Assert.Contains(@"D:\ROMs", vm.Roots);
        Assert.Contains(@"E:\Games", vm.Roots);
    }

    // ═══ ProfileService — Export/Import/LoadFlat ════════════════════════

    [Fact]
    public void ProfileService_Export_WritesJson()
    {
        var config = new Dictionary<string, string>
        {
            ["sortConsole"] = "True",
            ["dryRun"] = "True",
            ["locale"] = "de"
        };
        var path = Path.Combine(_tempDir, "profile.json");
        ProfileService.Export(path, config);
        Assert.True(File.Exists(path));
        var content = File.ReadAllText(path);
        Assert.Contains("sortConsole", content);
        Assert.Contains("True", content);
    }

    [Fact]
    public void ProfileService_Import_InvalidJson_Throws()
    {
        var sourcePath = Path.Combine(_tempDir, "invalid.json");
        File.WriteAllText(sourcePath, "not json");
        Assert.ThrowsAny<JsonException>(() => ProfileService.Import(sourcePath));
    }

    [Fact]
    public void ProfileService_Import_ArrayJson_ThrowsInvalidOperation()
    {
        var sourcePath = Path.Combine(_tempDir, "array.json");
        File.WriteAllText(sourcePath, "[1, 2, 3]");
        Assert.Throws<InvalidOperationException>(() => ProfileService.Import(sourcePath));
    }

    // ═══ RunService — GetSiblingDirectory ═══════════════════════════════

    [Fact]
    public void RunService_GetSiblingDirectory_ReturnsParallelFolder()
    {
        var root = Path.Combine(_tempDir, "Roms");
        Directory.CreateDirectory(root);
        var sibling = new RunService().GetSiblingDirectory(root, "audit-logs");
        Assert.Contains("audit-logs", sibling);
    }

    [Fact]
    public void RunService_GetSiblingDirectory_DriveRoot_ReturnsSubfolder()
    {
        // For drive roots like C:\, sibling becomes a subfolder
        var sibling = new RunService().GetSiblingDirectory(@"C:\", "reports");
        Assert.Contains("reports", sibling);
    }

    // ═══ Helper ═════════════════════════════════════════════════════════

    private static void SetRunStateViaValidPath(MainViewModel vm, RunState target)
    {
        if (target == RunState.Idle) return;
        vm.CurrentRunState = RunState.Preflight;
        if (target == RunState.Preflight) return;
        if (target is RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled)
        {
            vm.CurrentRunState = target;
            return;
        }
        vm.CurrentRunState = RunState.Scanning;
        if (target == RunState.Scanning) return;
        vm.CurrentRunState = RunState.Deduplicating;
        if (target == RunState.Deduplicating) return;
        vm.CurrentRunState = RunState.Sorting;
        if (target == RunState.Sorting) return;
        vm.CurrentRunState = RunState.Moving;
        if (target == RunState.Moving) return;
        vm.CurrentRunState = RunState.Converting;
    }
}
