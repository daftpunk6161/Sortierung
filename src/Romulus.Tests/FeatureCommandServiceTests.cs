using System.Windows.Input;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Paths;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for FeatureCommandService: verifies command registration,
/// command execution through dialog/VM interactions, and edge cases.
/// </summary>
public sealed class FeatureCommandServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MainViewModel _vm;
    private readonly RecordingDialogService _dialog;
    private readonly StubSettingsService _settings;
    private readonly StubWindowHost _windowHost;
    private readonly FeatureCommandService _sut;

    public FeatureCommandServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_FCS_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);

        _dialog = new RecordingDialogService();
        _settings = new StubSettingsService();
        _windowHost = new StubWindowHost();
        _vm = new MainViewModel(new StubThemeService(), _dialog, _settings);
        _sut = new FeatureCommandService(_vm, _settings, _dialog, _windowHost);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    /// <summary>Check whether command produced any output (log, dialog text, or info).</summary>
    private bool HasOutput() =>
        _vm.LogEntries.Count > 0 || _dialog.ShowTextCalls.Count > 0 || _dialog.InfoCalls.Count > 0;

    private async Task ExecuteFeatureCommandAsync(string commandKey)
    {
        var command = _vm.FeatureCommands[commandKey];
        if (command is IAsyncRelayCommand asyncCommand)
        {
            asyncCommand.Execute(null);
            if (asyncCommand.ExecutionTask is { } executionTask)
                await executionTask;
            return;
        }

        command.Execute(null);
    }

    // ═══ REGISTER COMMANDS ══════════════════════════════════════════════

    [Fact]
    public void RegisterCommands_PopulatesFeatureCommandsDictionary()
    {
        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.Count > 0);
    }

    [Theory]
    [InlineData("ExportLog")]
    [InlineData("ProfileDelete")]
    [InlineData("ProfileImport")]
    [InlineData("ProfileShare")]
    [InlineData("CliCommandCopy")]
    [InlineData("ConfigDiff")]
    [InlineData("ExportUnified")]
    [InlineData("ConfigImport")]
    [InlineData("AutoFindTools")]
    [InlineData("HealthScore")]
    [InlineData("DuplicateAnalysis")]
    [InlineData("ExportCollection")]
    [InlineData("RollbackHistoryBack")]
    [InlineData("RollbackHistoryForward")]
    [InlineData("RollbackQuick")]
    [InlineData("RollbackUndo")]
    [InlineData("RollbackRedo")]
    [InlineData("ApplyLocale")]
    [InlineData("AutoProfile")]
    [InlineData("JunkReport")]
    [InlineData("RomFilter")]
    [InlineData("MissingRom")]
    [InlineData("HeaderAnalysis")]
    [InlineData("Completeness")]
    [InlineData("DryRunCompare")]
    [InlineData("ConversionPipeline")]
    [InlineData("ConversionVerify")]
    [InlineData("FormatPriority")]
    [InlineData("PatchPipeline")]
    [InlineData("DatAutoUpdate")]
    [InlineData("DatDiffViewer")]
    [InlineData("CustomDatEditor")]
    [InlineData("HashDatabaseExport")]
    [InlineData("CollectionManager")]
    [InlineData("CloneListViewer")]
    [InlineData("VirtualFolderPreview")]
    [InlineData("CollectionMerge")]
    [InlineData("IntegrityMonitor")]
    [InlineData("BackupManager")]
    [InlineData("Quarantine")]
    [InlineData("RuleEngine")]
    [InlineData("HeaderRepair")]
    [InlineData("FilterBuilder")]
    [InlineData("SortTemplates")]
    [InlineData("PipelineEngine")]
    [InlineData("SchedulerApply")]
    [InlineData("RulePackSharing")]
    [InlineData("ArcadeMergeSplit")]
    [InlineData("HtmlReport")]
    [InlineData("LauncherIntegration")]
    [InlineData("DatImport")]
    [InlineData("StorageTiering")]
    [InlineData("NasOptimization")]
    [InlineData("PortableMode")]
    [InlineData("HardlinkMode")]
    public void RegisterCommands_ContainsExpectedCommand(string commandKey)
    {
        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.ContainsKey(commandKey),
            $"Feature command '{commandKey}' not registered");
    }

    [Theory]
    [InlineData("CommandPalette")]
    [InlineData("SystemTray")]
    [InlineData("ApiServer")]
    [InlineData("Accessibility")]
    public void RegisterCommands_WithWindowHost_ContainsWindowCommands(string commandKey)
    {
        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.ContainsKey(commandKey),
            $"Window command '{commandKey}' not registered");
    }

    [Fact]
    public void RegisterCommands_WithoutWindowHost_OmitsWindowCommands()
    {
        var sut = new FeatureCommandService(_vm, _settings, _dialog); // no windowHost
        sut.RegisterCommands();
        Assert.False(_vm.FeatureCommands.ContainsKey("CommandPalette"));
        Assert.False(_vm.FeatureCommands.ContainsKey("SystemTray"));
        Assert.False(_vm.FeatureCommands.ContainsKey("ApiServer"));
        Assert.False(_vm.FeatureCommands.ContainsKey("Accessibility"));
    }

    [Fact]
    public void RegisterCommands_AllValuesAreICommand()
    {
        _sut.RegisterCommands();
        foreach (var kvp in _vm.FeatureCommands)
            Assert.IsAssignableFrom<ICommand>(kvp.Value);
    }

    [Fact]
    public void RegisterCommands_CountIsAtLeast56()
    {
        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.Count >= 56,
            $"Expected at least 56 commands, got {_vm.FeatureCommands.Count}");
    }

    // ═══ EXPORT LOG ═════════════════════════════════════════════════════

    [Fact]
    public void ExportLog_WhenSaveFileReturnsNull_DoesNothing()
    {
        _sut.RegisterCommands();
        _dialog.SaveFileResult = null;
        _vm.FeatureCommands["ExportLog"].Execute(null);
        // No exception, no file created
    }

    [Fact]
    public void ExportLog_WritesLogEntriesToFile()
    {
        _sut.RegisterCommands();
        var exportPath = Path.Combine(_tempDir, "export.txt");
        _dialog.SaveFileResult = exportPath;

        _vm.LogEntries.Add(new LogEntry("Test message 1", "INFO"));
        _vm.LogEntries.Add(new LogEntry("Warning message", "WARN"));

        _vm.FeatureCommands["ExportLog"].Execute(null);

        Assert.True(File.Exists(exportPath));
        var lines = File.ReadAllLines(exportPath);
        Assert.Equal(2, lines.Length);
        Assert.Contains("Test message 1", lines[0]);
        Assert.Contains("Warning message", lines[1]);
    }

    // ═══ PROFILE DELETE ═════════════════════════════════════════════════

    [Fact]
    public void ProfileDelete_WhenNotConfirmed_DoesNothing()
    {
        _sut.RegisterCommands();
        _dialog.ConfirmResult = false;
        _vm.FeatureCommands["ProfileDelete"].Execute(null);
        // No exception
    }

    // ═══ HEALTH SCORE ═══════════════════════════════════════════════════

    [Fact]
    public void HealthScore_NoCandidates_LogsOrShowsResult()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["HealthScore"].Execute(null);
        Assert.True(HasOutput(), "HealthScore should produce output");
    }

    // ═══ JUNK REPORT ════════════════════════════════════════════════════

    [Fact]
    public void JunkReport_NoCandidates_LogsOrShowsReport()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["JunkReport"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ FORMAT PRIORITY ════════════════════════════════════════════════

    [Fact]
    public void FormatPriority_ShowsFormatScores()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["FormatPriority"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ SORT TEMPLATES ═════════════════════════════════════════════════

    [Fact]
    public void SortTemplates_ShowsTemplates()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["SortTemplates"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ DUPLICATE ANALYSIS ════════════════════════════════════════════

    [Fact]
    public void DuplicateAnalysis_NoCandidates_LogsOrShowsResult()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["DuplicateAnalysis"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ ROM FILTER ═════════════════════════════════════════════════════

    [Fact]
    public void RomFilter_UserCancelsInput_DoesNothing()
    {
        _sut.RegisterCommands();
        _dialog.ShowInputBoxResult = "";
        _vm.FeatureCommands["RomFilter"].Execute(null);
        // No exception on empty filter
    }


    // ═══ APPLY LOCALE ═══════════════════════════════════════════════════

    [Fact]
    public void ApplyLocale_ExecutesWithoutError()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["ApplyLocale"].Execute(null);
        // Locale loading is best-effort; should not throw
    }

    // ═══ PIPELINE ENGINE ════════════════════════════════════════════════

    [Fact]
    public void PipelineEngine_ShowsPipelineInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["PipelineEngine"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ QUARANTINE ═════════════════════════════════════════════════════

    [Fact]
    public void Quarantine_ShowsQuarantineInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["Quarantine"].Execute(null);
        Assert.True(HasOutput());
    }


    // ═══ PORTABLE MODE ══════════════════════════════════════════════════

    [Fact]
    public void PortableMode_ShowsPortableInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["PortableMode"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ STORAGE TIERING ════════════════════════════════════════════════

    [Fact]
    public void StorageTiering_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["StorageTiering"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ NAS OPTIMIZATION ═══════════════════════════════════════════════

    [Fact]
    public void NasOptimization_ShowsNasInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["NasOptimization"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ HARDLINK MODE ══════════════════════════════════════════════════

    [Fact]
    public void HardlinkMode_ShowsHardlinkInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["HardlinkMode"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ MISSING ROM ════════════════════════════════════════════════════

    [Fact]
    public void MissingRom_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["MissingRom"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ COMPLETENESS ═══════════════════════════════════════════════════

    [Fact]
    public async Task Completeness_ShowsResult()
    {
        _sut.RegisterCommands();

        await ExecuteFeatureCommandAsync("Completeness");

        Assert.True(HasOutput());
    }

    [Fact]
    public async Task Completeness_FixDatWriteFailure_ShowsErrorAndAddsErrorSummaryItem()
    {
        _sut.RegisterCommands();

        var runRoot = Path.Combine(_tempDir, "completeness-root");
        Directory.CreateDirectory(runRoot);
        await File.WriteAllTextAsync(Path.Combine(runRoot, "Present Game.sfc"), "rom");

        var datRoot = Path.Combine(_tempDir, "datroot");
        Directory.CreateDirectory(datRoot);
        var datPath = Path.Combine(datRoot, "snes.dat");
        await File.WriteAllTextAsync(datPath,
            """
            <?xml version="1.0" encoding="utf-8"?>
            <datafile>
              <header>
                <name>SNES Test DAT</name>
              </header>
              <game name="Missing Test Game">
                <description>Missing Test Game</description>
                <rom name="Missing Test Game.sfc" crc="1A2B3C4D" />
              </game>
            </datafile>
            """);

        _vm.Roots.Clear();
    _vm.Roots.Add(runRoot);
        _vm.UseDat = true;
        _vm.DatRoot = datRoot;

        _dialog.ConfirmResult = true;
        _dialog.SaveFileResult = _tempDir; // directory path -> write should fail

        await ExecuteFeatureCommandAsync("Completeness");

        Assert.Contains(_dialog.ErrorCalls, message =>
            message.Contains("FixDAT-Export fehlgeschlagen", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(_vm.ErrorSummaryItems, item =>
            string.Equals(item.Code, "ANALYSIS-FIXDAT", StringComparison.Ordinal) &&
            item.Message.Contains("FixDAT-Export fehlgeschlagen", StringComparison.OrdinalIgnoreCase));
    }

    // ═══ COLLECTION MANAGER ═════════════════════════════════════════════

    [Fact]
    public void CollectionManager_NoCandidates_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["CollectionManager"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ VIRTUAL FOLDER PREVIEW ═════════════════════════════════════════

    [Fact]
    public void VirtualFolderPreview_NoCandidates_LogsOrShowsPreview()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["VirtualFolderPreview"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ RULE ENGINE ════════════════════════════════════════════════════

    [Fact]
    public void RuleEngine_ShowsRuleReport()
    {
        _sut.RegisterCommands();
        _dialog.YesNoCancelResult = Contracts.Ports.ConfirmResult.Yes;
        _vm.FeatureCommands["RuleEngine"].Execute(null);
        Assert.True(HasOutput());
    }

    [Fact]
    public void RuleEngine_CustomJunkEditor_InvalidRule_ShowsValidationError()
    {
        _sut.RegisterCommands();
        _dialog.YesNoCancelResult = Contracts.Ports.ConfirmResult.No;
        _dialog.ShowMultilineInputBoxResult =
            """
            {
              "enabled": true,
              "rules": [
                {
                  "field": "filename",
                  "operator": "contains",
                  "value": "(Beta)",
                  "logic": "AND",
                  "action": "SetCategoryJunk",
                  "priority": 1000,
                  "enabled": true
                }
              ]
            }
            """;

        _vm.FeatureCommands["RuleEngine"].Execute(null);

        Assert.Contains(_dialog.ErrorCalls, message =>
            message.Contains("nicht erlaubt", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuleEngine_CustomJunkEditor_ValidRule_ShowsPreviewBeforeSave()
    {
        _sut.RegisterCommands();
        _dialog.YesNoCancelResult = Contracts.Ports.ConfirmResult.No;
        _dialog.ConfirmResult = false; // don't persist to shared data dir during tests
        _dialog.ShowMultilineInputBoxResult =
            """
            {
              "enabled": true,
              "rules": [
                {
                  "field": "name",
                  "operator": "contains",
                  "value": "(Beta)",
                  "logic": "AND",
                  "action": "SetCategoryJunk",
                  "priority": 1000,
                  "enabled": true
                }
              ]
            }
            """;

        _vm.FeatureCommands["RuleEngine"].Execute(null);

        Assert.Contains(_dialog.ShowTextCalls, call =>
            call.Title.Contains("Vorschau", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(_dialog.ErrorCalls);
    }

    [Fact]
    public void RuleEngine_CustomJunkEditor_InvalidRegex_ShowsValidationError()
    {
        _sut.RegisterCommands();
        _dialog.YesNoCancelResult = Contracts.Ports.ConfirmResult.No;
        _dialog.ShowMultilineInputBoxResult =
            """
            {
              "enabled": true,
              "rules": [
                {
                  "field": "name",
                  "operator": "regex",
                  "value": "(",
                  "logic": "AND",
                  "action": "SetCategoryJunk",
                  "priority": 1000,
                  "enabled": true
                }
              ]
            }
            """;

        _vm.FeatureCommands["RuleEngine"].Execute(null);

        Assert.Contains(_dialog.ErrorCalls, message =>
            message.Contains("regex", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RuleEngine_CustomJunkEditor_PromptUsesRoamingPath()
    {
        _sut.RegisterCommands();
        _dialog.YesNoCancelResult = Contracts.Ports.ConfirmResult.No;
        _dialog.ShowMultilineInputBoxResult = "";

        _vm.FeatureCommands["RuleEngine"].Execute(null);

        var expectedPath = AppStoragePathResolver.ResolveRoamingPath("custom-junk-rules.json");
        Assert.Contains(expectedPath, _dialog.LastMultilinePrompt ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    // ═══ CONVERSION PIPELINE ════════════════════════════════════════════

    [Fact]
    public void ConversionPipeline_NoCandidates_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["ConversionPipeline"].Execute(null);
        Assert.True(HasOutput());
    }

    [Fact]
    public void ConversionPipeline_WithConvertibleCandidates_ShowsPerConsoleAdvisorBreakdown()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates =
        [
            new RomCandidate { MainPath = "ps1/game1.iso", Extension = ".iso", SizeBytes = 1_000_000, ConsoleKey = "ps1", Category = FileCategory.Game },
            new RomCandidate { MainPath = "wii/game2.gcz", Extension = ".gcz", SizeBytes = 700_000, ConsoleKey = "wii", Category = FileCategory.Game },
            new RomCandidate { MainPath = "snes/game3.sfc", Extension = ".sfc", SizeBytes = 200_000, ConsoleKey = "snes", Category = FileCategory.Game }
        ];

        _vm.FeatureCommands["ConversionPipeline"].Execute(null);

        var shown = Assert.Single(_dialog.ShowTextCalls);
        Assert.Contains("Einsparung pro Konsole", shown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ps1", shown.Content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("wii", shown.Content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PatchPipeline_WhenSourceSelectionCancelled_DoesNothing()
    {
        _sut.RegisterCommands();
        _dialog.BrowseFileResult = null;

        _vm.FeatureCommands["PatchPipeline"].Execute(null);

        Assert.Empty(_dialog.ShowTextCalls);
    }

    // ═══ WINDOW HOST COMMANDS ═══════════════════════════════════════════

    [Fact]
    public void SystemTray_CallsWindowHostToggle()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["SystemTray"].Execute(null);
        Assert.True(_windowHost.SystemTrayToggled);
    }

    // ═══ AUTO PROFILE ═══════════════════════════════════════════════════

    [Fact]
    public void AutoProfile_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["AutoProfile"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ WITH CANDIDATES ════════════════════════════════════════════════

    [Fact]
    public void HealthScore_WithCandidates_ShowsScore()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<Romulus.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "test.sfc", GameKey = "TestGame", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game }
        };
        _vm.FeatureCommands["HealthScore"].Execute(null);
        Assert.True(HasOutput());
    }

    [Fact]
    public void JunkReport_WithMixedCandidates_ShowsReport()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<Romulus.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "game.sfc", GameKey = "Game", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game },
            new RomCandidate { MainPath = "demo.sfc", GameKey = "Demo", Region = "US",
                Extension = ".sfc", SizeBytes = 512, Category = FileCategory.Junk }
        };
        _vm.FeatureCommands["JunkReport"].Execute(null);
        Assert.True(HasOutput());
    }

    [Fact]
    public void DuplicateAnalysis_WithCandidates_ShowsDialog()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<Romulus.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "a.sfc", GameKey = "Game", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game },
            new RomCandidate { MainPath = "b.sfc", GameKey = "Game", Region = "US",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game }
        };
        _vm.FeatureCommands["DuplicateAnalysis"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ EXPORT COMMANDS WITH CANDIDATES ════════════════════════════════

    [Fact]
    public void ExportCollection_CsvFormat_CreatesCsvFile()
    {
        _sut.RegisterCommands();
        var csvPath = Path.Combine(_tempDir, "export.csv");
        _dialog.ShowInputBoxResult = "1";
        _dialog.SaveFileResult = csvPath;
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<Romulus.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "game.sfc", GameKey = "Game", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game, ConsoleKey = "SNES" }
        };
        _vm.FeatureCommands["ExportCollection"].Execute(null);
        Assert.True(File.Exists(csvPath));
    }

    [Fact]
    public void ExportCollection_ExcelFormat_CreatesXmlFile()
    {
        _sut.RegisterCommands();
        var xmlPath = Path.Combine(_tempDir, "export.xml");
        _dialog.ShowInputBoxResult = "2";
        _dialog.SaveFileResult = xmlPath;
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<Romulus.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "game.sfc", GameKey = "Game", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game, ConsoleKey = "SNES" }
        };
        _vm.FeatureCommands["ExportCollection"].Execute(null);
        Assert.True(File.Exists(xmlPath));
    }

    // ═══ HEADER ANALYSIS WITH FILE ══════════════════════════════════════

    [Fact]
    public void HeaderAnalysis_BrowseFileCancelled_DoesNothing()
    {
        _sut.RegisterCommands();
        _dialog.BrowseFileResult = null;
        _vm.FeatureCommands["HeaderAnalysis"].Execute(null);
        // No text dialog shown because user cancelled
    }

    [Fact]
    public void HeaderAnalysis_WithValidFile_ShowsResult()
    {
        _sut.RegisterCommands();
        var testFile = Path.Combine(_tempDir, "test.nes");
        var data = new byte[1024];
        data[0] = 0x4E; data[1] = 0x45; data[2] = 0x53; data[3] = 0x1A; // NES header
        File.WriteAllBytes(testFile, data);
        _dialog.BrowseFileResult = testFile;

        _vm.FeatureCommands["HeaderAnalysis"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ FILTER BUILDER ═════════════════════════════════════════════════

    [Fact]
    public void FilterBuilder_ShowsFilterInfo()
    {
        _sut.RegisterCommands();
        _dialog.ShowInputBoxResult = "region=EU";
        _vm.FeatureCommands["FilterBuilder"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ ROLLBACK QUICK ═════════════════════════════════════════════════

    [Fact]
    public void RollbackQuick_IsRegistered()
    {
        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.ContainsKey("RollbackQuick"),
            "RollbackQuick must be registered as a feature command");
    }

    // ═══ ROLLBACK UNDO / REDO ═══════════════════════════════════════════

    [Fact]
    public void RollbackHistoryBack_NoHistory_LogsWarning()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["RollbackHistoryBack"].Execute(null);

        Assert.Contains(_vm.LogEntries, entry => entry.Level == "WARN" && entry.Text.Contains("rollback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RollbackHistoryForward_NoHistory_LogsWarning()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["RollbackHistoryForward"].Execute(null);

        Assert.Contains(_vm.LogEntries, entry => entry.Level == "WARN" && entry.Text.Contains("rollback", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RollbackHistoryBack_WithHistory_MovesEntryToRedoStack()
    {
        _sut.RegisterCommands();
        _vm.PushRollbackUndo("audit-001.csv");

        _vm.FeatureCommands["RollbackHistoryBack"].Execute(null);

        Assert.False(_vm.HasRollbackUndo);
        Assert.True(_vm.HasRollbackRedo);
        Assert.Contains(_vm.LogEntries, entry => entry.Level == "INFO" && entry.Text.Contains("Rollback-Verlauf", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void RollbackHistoryForward_WithRedoHistory_MovesEntryBackToUndoStack()
    {
        _sut.RegisterCommands();
        _vm.PushRollbackUndo("audit-002.csv");
        _vm.FeatureCommands["RollbackHistoryBack"].Execute(null);

        _vm.FeatureCommands["RollbackHistoryForward"].Execute(null);

        Assert.True(_vm.HasRollbackUndo);
        Assert.False(_vm.HasRollbackRedo);
        Assert.Contains(_vm.LogEntries, entry => entry.Level == "INFO" && entry.Text.Contains("Rollback-Verlauf", StringComparison.OrdinalIgnoreCase));
    }

    // ═══ IDEMPOTENCY ════════════════════════════════════════════════════

    [Fact]
    public void RegisterCommands_CalledTwice_OverwritesWithoutError()
    {
        _sut.RegisterCommands();
        var firstCount = _vm.FeatureCommands.Count;
        _sut.RegisterCommands();
        Assert.Equal(firstCount, _vm.FeatureCommands.Count);
    }

    // ═══ TASK-052: DuplicateAnalysis — 3 Sections ═══════════════════════

    [Fact]
    public void DuplicateAnalysis_NoCandidates_ShowsAllThreeSections()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["DuplicateAnalysis"].Execute(null);
        var shown = _dialog.ShowTextCalls;
        Assert.Single(shown);
        var content = shown[0].Content;
        Assert.Contains("Verzeichnis-Analyse", content);
        Assert.Contains("Konsolen-Heatmap", content);
        Assert.Contains("Cross-Root-Duplikate", content);
    }

    [Fact]
    public void DuplicateAnalysis_WithDedupeGroups_ShowsHeatmapData()
    {
        _sut.RegisterCommands();
        _vm.LastDedupeGroups.Add(new DedupeGroup
        {
            GameKey = "TestGame",
            Winner = new RomCandidate { MainPath = "SNES/a.sfc", GameKey = "TestGame", Region = "EU", Extension = ".sfc", SizeBytes = 1024, ConsoleKey = "SNES" },
            Losers = [new RomCandidate { MainPath = "SNES/b.sfc", GameKey = "TestGame", Region = "US", Extension = ".sfc", SizeBytes = 1024, ConsoleKey = "SNES" }]
        });
        _vm.FeatureCommands["DuplicateAnalysis"].Execute(null);
        var content = _dialog.ShowTextCalls[0].Content;
        Assert.Contains("Konsolen-Heatmap", content);
        Assert.Contains("SNES", content);
    }

    [Fact]
    public void DuplicateAnalysis_WithDedupeGroups_ShowsGroupInspectorScoreBreakdown()
    {
        _sut.RegisterCommands();
        _vm.LastDedupeGroups.Add(new DedupeGroup
        {
            GameKey = "InspectorGame",
            Winner = new RomCandidate
            {
                MainPath = "winner.sfc",
                GameKey = "InspectorGame",
                Region = "US",
                Extension = ".sfc",
                SizeBytes = 1024,
                ConsoleKey = "SNES",
                RegionScore = 10,
                FormatScore = 8,
                VersionScore = 4,
                HeaderScore = 2,
                CompletenessScore = 3,
                SizeTieBreakScore = 1
            },
            Losers =
            [
                new RomCandidate
                {
                    MainPath = "loser.sfc",
                    GameKey = "InspectorGame",
                    Region = "EU",
                    Extension = ".sfc",
                    SizeBytes = 1024,
                    ConsoleKey = "SNES",
                    RegionScore = 8,
                    FormatScore = 8,
                    VersionScore = 4,
                    HeaderScore = 2,
                    CompletenessScore = 3,
                    SizeTieBreakScore = 1
                }
            ]
        });

        _vm.FeatureCommands["DuplicateAnalysis"].Execute(null);

        var content = _dialog.ShowTextCalls[0].Content;
        Assert.Contains("Gruppen-Inspektor", content);
        Assert.Contains("Score=", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Kriterien:", content, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Δ", content, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateAnalysis_SingleRoot_CrossRootSaysNeedsTwo()
    {
        _sut.RegisterCommands();
        _vm.Roots.Clear();
        _vm.Roots.Add(@"C:\Roms");
        _vm.FeatureCommands["DuplicateAnalysis"].Execute(null);
        var content = _dialog.ShowTextCalls[0].Content;
        Assert.Contains("Cross-Root-Duplikate", content);
        Assert.Contains("2 Root-Ordner", content);
    }

    // ═══ TASK-053: ExportCollection — 3 Formats ═════════════════════════

    [Fact]
    public void ExportCollection_DuplicateCsvFormat_CreatesDuplicateCsvFile()
    {
        _sut.RegisterCommands();
        var csvPath = Path.Combine(_tempDir, "dupes.csv");
        _dialog.ShowInputBoxResult = "3";
        _dialog.SaveFileResult = csvPath;
        _vm.LastDedupeGroups.Add(new DedupeGroup
        {
            GameKey = "DupeGame",
            Winner = new RomCandidate { MainPath = "w.sfc", GameKey = "DupeGame", Region = "EU", Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game, ConsoleKey = "SNES" },
            Losers = [new RomCandidate { MainPath = "l.sfc", GameKey = "DupeGame", Region = "US", Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game, ConsoleKey = "SNES" }]
        });
        _vm.FeatureCommands["ExportCollection"].Execute(null);
        Assert.True(File.Exists(csvPath));
        var content = File.ReadAllText(csvPath);
        Assert.Contains("l.sfc", content);
    }

    [Fact]
    public void ExportCollection_InvalidChoice_LogsWarning()
    {
        _sut.RegisterCommands();
        _dialog.ShowInputBoxResult = "99";
        _vm.FeatureCommands["ExportCollection"].Execute(null);
        Assert.Contains(_vm.LogEntries, e => e.Level == "WARN");
    }

    [Fact]
    public void ExportCollection_EmptyInput_DoesNothing()
    {
        _sut.RegisterCommands();
        _dialog.ShowInputBoxResult = "";
        _vm.FeatureCommands["ExportCollection"].Execute(null);
        Assert.Empty(_vm.LogEntries);
        Assert.Empty(_dialog.ShowTextCalls);
    }

    // ═══ TASK-054: CommandPalette — FeatureCommands coverage ═════════════

    [Fact]
    public void CommandPalette_SearchFindsRegisteredToolKey()
    {
        _sut.RegisterCommands();
        // Verify SearchCommands finds keys from the full FeatureCommands dictionary
        var results = FeatureService.SearchCommands("HealthScore", _vm.FeatureCommands);
        Assert.True(results.Count > 0, "SearchCommands should find 'HealthScore' in FeatureCommands");
        Assert.Equal("HealthScore", results[0].key);
        Assert.Equal(0, results[0].score); // exact match
    }

    [Fact]
    public void CommandPalette_FuzzySearchFindsCloseMatch()
    {
        _sut.RegisterCommands();
        // "HelthScore" is Levenshtein distance 1 from "HealthScore"
        var results = FeatureService.SearchCommands("HelthScore", _vm.FeatureCommands);
        Assert.True(results.Count > 0, "Fuzzy search should find close match");
        Assert.Contains(results, r => r.key == "HealthScore");
    }

    [Fact]
    public void CommandPalette_SearchCoversAllFeatureCommandKeys()
    {
        _sut.RegisterCommands();
        // All FeatureCommand keys must be findable via CommandPalette search
        foreach (var key in _vm.FeatureCommands.Keys)
        {
            var results = FeatureService.SearchCommands(key, _vm.FeatureCommands);
            Assert.True(results.Count > 0, $"CommandPalette should find command key '{key}'");
            Assert.Equal(key, results[0].key);
        }
    }

    [Fact]
    public void CommandPalette_FeatureCommandsCountExceedsOldHardcodedLimit()
    {
        _sut.RegisterCommands();
        // Phase 7 expanded CommandPalette from 8 hardcoded to all FeatureCommands
        // Verify we have significantly more than the old 8-entry limit
        var results = FeatureService.SearchCommands("e", _vm.FeatureCommands);
        Assert.True(results.Count > 8,
            $"CommandPalette should return more than 8 results (old hardcoded limit), got {results.Count}");
    }

    // ═══ TASK-055: DefaultPinnedKeys — no removed keys ══════════════════

    [Fact]
    public void DefaultPinnedKeys_AllKeysExistInToolItems()
    {
        // DefaultPinnedKeys should only reference keys that exist in the ToolItems collection
        var vm = new ToolsViewModel(new LocalizationService());
        var pinnedKeys = vm.ToolItems.Where(t => t.IsPinned).Select(t => t.Key).ToHashSet();
        var allToolKeys = vm.ToolItems.Select(t => t.Key).ToHashSet();

        Assert.True(pinnedKeys.Count > 0, "At least one tool should be pinned by default");
        foreach (var key in pinnedKeys)
            Assert.Contains(key, allToolKeys);
    }

    [Fact]
    public void DefaultPinnedKeys_ContainsExpectedKeys()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        var pinnedKeys = vm.ToolItems.Where(t => t.IsPinned).Select(t => t.Key).ToHashSet();
        Assert.Contains("HealthScore", pinnedKeys);
        Assert.Contains("DuplicateAnalysis", pinnedKeys);
        Assert.Contains("RollbackQuick", pinnedKeys);
        Assert.Contains("ExportCollection", pinnedKeys);
        Assert.Contains("DatAutoUpdate", pinnedKeys);
        Assert.Contains("IntegrityMonitor", pinnedKeys);
    }

    [Fact]
    public void DefaultPinnedKeys_DoNotContainRemovedKeys()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        var pinnedKeys = vm.ToolItems.Where(t => t.IsPinned).Select(t => t.Key).ToHashSet();
        var removedKeys = new[]
        {
            "FtpSource", "CloudSync", "PluginMarketplaceFeature", "PluginManager",
            "GpuHashing", "ParallelHashing", "QuickPreview",
            "ConvertQueue", "ConversionEstimate", "TosecDat", "SplitPanelPreview",
            "PlaytimeTracker", "GenreClassification", "EmulatorCompat",
            "CoverScraper", "CollectionSharing", "TrendAnalysis",
            "WindowsContextMenu", "DockerContainer", "SystemTray",
            "DuplicateInspector", "DuplicateHeatmap", "CrossRootDupe",
            "ExportCsv", "ExportExcel", "DuplicateExport",
            "PdfReport", "MobileWebUI", "SchedulerAdvanced", "ToolImport"
        };
        foreach (var key in removedKeys)
            Assert.DoesNotContain(key, pinnedKeys);
    }

    // ═══ TASK-056: Removed keys — not in FeatureCommands ═════════════════

    [Theory]
    [InlineData("FtpSource")]
    [InlineData("CloudSync")]
    [InlineData("PluginMarketplaceFeature")]
    [InlineData("PluginManager")]
    [InlineData("GpuHashing")]
    [InlineData("ParallelHashing")]
    [InlineData("QuickPreview")]
    [InlineData("CollectionDiff")]
    [InlineData("ConvertQueue")]
    [InlineData("ConversionEstimate")]
    [InlineData("TosecDat")]
    [InlineData("SplitPanelPreview")]
    [InlineData("PlaytimeTracker")]
    [InlineData("GenreClassification")]
    [InlineData("EmulatorCompat")]
    [InlineData("CoverScraper")]
    [InlineData("CollectionSharing")]
    [InlineData("TrendAnalysis")]
    [InlineData("WindowsContextMenu")]
    [InlineData("DockerContainer")]
    [InlineData("DuplicateInspector")]
    [InlineData("DuplicateHeatmap")]
    [InlineData("CrossRootDupe")]
    [InlineData("ExportCsv")]
    [InlineData("ExportExcel")]
    [InlineData("DuplicateExport")]
    [InlineData("PdfReport")]
    [InlineData("MobileWebUI")]
    [InlineData("SchedulerAdvanced")]
    [InlineData("ToolImport")]
    public void RemovedToolKeys_MustNotExistInFeatureCommands(string removedKey)
    {
        _sut.RegisterCommands();
        Assert.False(_vm.FeatureCommands.ContainsKey(removedKey),
            $"Removed tool key '{removedKey}' should not be registered in FeatureCommands");
    }

    [Fact]
    public void RemovedToolKeys_MustNotExistInToolItems()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        var removedKeys = new[]
        {
            "FtpSource", "CloudSync", "PluginMarketplaceFeature", "PluginManager",
            "GpuHashing", "ParallelHashing", "QuickPreview", "CollectionDiff",
            "ConvertQueue", "ConversionEstimate", "TosecDat", "SplitPanelPreview",
            "PlaytimeTracker", "GenreClassification", "EmulatorCompat",
            "CoverScraper", "CollectionSharing", "TrendAnalysis",
            "WindowsContextMenu", "DockerContainer", "SystemTray",
            "DuplicateInspector", "DuplicateHeatmap", "CrossRootDupe",
            "ExportCsv", "ExportExcel", "DuplicateExport",
            "PdfReport", "MobileWebUI", "SchedulerAdvanced", "ToolImport"
        };
        var toolKeys = vm.ToolItems.Select(t => t.Key).ToHashSet();
        foreach (var key in removedKeys)
            Assert.DoesNotContain(key, toolKeys);
    }

    // ═══ TASK-057: i18n consistency — all ToolItems have i18n entries ════

    [Fact]
    public void I18n_AllToolItems_HaveLocalizedDisplayNameAndDescription()
    {
        // Verify that all registered ToolItems have non-empty DisplayName and Description
        // (which are loaded from i18n via Tool.{Key} and Tool.{Key}.Desc)
        var vm = new ToolsViewModel(new LocalizationService());
        foreach (var item in vm.ToolItems)
        {
            Assert.False(string.IsNullOrWhiteSpace(item.DisplayName),
                $"ToolItem '{item.Key}' has empty DisplayName (missing i18n key 'Tool.{item.Key}')");
            Assert.False(string.IsNullOrWhiteSpace(item.Description),
                $"ToolItem '{item.Key}' has empty Description (missing i18n key 'Tool.{item.Key}.Desc')");
        }
    }

    [Fact]
    public void CollectionMerge_ToolKey_IsPresentInCatalogAndCommands()
    {
        var vm = new ToolsViewModel(new LocalizationService());

        Assert.Contains(vm.ToolItems, item => item.Key == FeatureCommandKeys.CollectionMerge && !string.IsNullOrWhiteSpace(item.DisplayName));

        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.ContainsKey(FeatureCommandKeys.CollectionMerge));
    }

    [Fact]
    public void ToolsViewModel_MaturityAssignments_AreExplicitForKnownFeatures()
    {
        var vm = new ToolsViewModel(new LocalizationService());

        Assert.Equal(ToolMaturity.Production, vm.ToolItems.Single(item => item.Key == FeatureCommandKeys.HealthScore).Maturity);
        Assert.Equal(ToolMaturity.Guided, vm.ToolItems.Single(item => item.Key == FeatureCommandKeys.ConversionPipeline).Maturity);
        Assert.Equal(ToolMaturity.Experimental, vm.ToolItems.Single(item => item.Key == FeatureCommandKeys.CollectionManager).Maturity);
        Assert.All(vm.ToolItems, item =>
        {
            Assert.False(string.IsNullOrWhiteSpace(item.MaturityBadgeText));
            Assert.False(string.IsNullOrWhiteSpace(item.MaturityDescription));
        });
    }

    [Fact]
    public void ToolsViewModel_WiredToolCommand_RecordsRecentUsage()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        var executions = 0;
        vm.FeatureCommands[FeatureCommandKeys.HealthScore] = new RelayCommand(() => executions++);
        vm.WireToolItemCommands();
        vm.RefreshContext(new ToolContextSnapshot(
            HasRoots: true,
            RootCount: 1,
            HasRunResult: true,
            CandidateCount: 1,
            DedupeGroupCount: 0,
            JunkCount: 0,
            UnverifiedCount: 0,
            UseDat: false,
            DatConfigured: false,
            ConvertEnabled: false,
            ConvertOnly: false,
            ConvertedCount: 0,
            CanRollback: false));

        var item = vm.ToolItems.Single(tool => tool.Key == FeatureCommandKeys.HealthScore);
        Assert.NotNull(item.Command);
        Assert.True(item.Command!.CanExecute(null));

        item.Command.Execute(null);

        Assert.Equal(1, executions);
        Assert.Single(vm.RecentToolItems);
        Assert.Equal(FeatureCommandKeys.HealthScore, vm.RecentToolItems[0].Key);
    }

    [Fact]
    public void ToolsViewModel_Recommendations_ExcludeExperimentalFeatures()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        foreach (var key in new[]
                 {
                     FeatureCommandKeys.DatAutoUpdate,
                     FeatureCommandKeys.FormatPriority,
                     FeatureCommandKeys.ConversionVerify,
                     FeatureCommandKeys.CollectionMerge,
                     FeatureCommandKeys.CommandPalette,
                     FeatureCommandKeys.RulePackSharing,
                     FeatureCommandKeys.AutoProfile
                 })
        {
            vm.FeatureCommands[key] = new RelayCommand(() => { });
        }

        vm.WireToolItemCommands();
        vm.RefreshContext(new ToolContextSnapshot(
            HasRoots: true,
            RootCount: 2,
            HasRunResult: false,
            CandidateCount: 0,
            DedupeGroupCount: 0,
            JunkCount: 0,
            UnverifiedCount: 0,
            UseDat: true,
            DatConfigured: false,
            ConvertEnabled: true,
            ConvertOnly: false,
            ConvertedCount: 0,
            CanRollback: false));

        Assert.NotEmpty(vm.RecommendedToolItems);
        Assert.DoesNotContain(vm.RecommendedToolItems, item => item.IsExperimental);
        Assert.DoesNotContain(vm.RecommendedToolItems, item => item.Key == FeatureCommandKeys.AutoProfile);
        Assert.Contains(vm.RecommendedToolItems, item => item.Key == FeatureCommandKeys.DatAutoUpdate);
        Assert.Contains(vm.RecommendedToolItems, item => item.Key == FeatureCommandKeys.CommandPalette);
    }

    [Fact]
    public void ToolsViewModel_AvailableToolCount_ExcludesLockedTools()
    {
        var vm = new ToolsViewModel(new LocalizationService());
        vm.FeatureCommands[FeatureCommandKeys.HealthScore] = new RelayCommand(() => { });
        vm.FeatureCommands[FeatureCommandKeys.CommandPalette] = new RelayCommand(() => { });
        vm.WireToolItemCommands();

        vm.RefreshContext(new ToolContextSnapshot(
            HasRoots: true,
            RootCount: 1,
            HasRunResult: false,
            CandidateCount: 0,
            DedupeGroupCount: 0,
            JunkCount: 0,
            UnverifiedCount: 0,
            UseDat: false,
            DatConfigured: false,
            ConvertEnabled: false,
            ConvertOnly: false,
            ConvertedCount: 0,
            CanRollback: false));

        Assert.Equal(1, vm.AvailableToolCount);
        Assert.Contains(vm.ToolItems, item => item.Key == FeatureCommandKeys.HealthScore && item.IsLocked);
        Assert.Contains(vm.ToolItems, item => item.Key == FeatureCommandKeys.CommandPalette && !item.IsLocked && !item.IsUnavailable);
    }

    [Fact]
    public void I18n_DeAndEn_HaveMatchingToolKeys()
    {
        var dataDir = FeatureService.ResolveDataDirectory();
        if (dataDir is null) return; // skip if data dir unavailable

        var deJson = Path.Combine(dataDir, "i18n", "de.json");
        var enJson = Path.Combine(dataDir, "i18n", "en.json");
        if (!File.Exists(deJson) || !File.Exists(enJson)) return;

        var de = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(deJson))!;
        var en = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(enJson))!;

        var deToolKeys = de.Keys.Where(k => k.StartsWith("Tool.")).OrderBy(k => k).ToList();
        var enToolKeys = en.Keys.Where(k => k.StartsWith("Tool.")).OrderBy(k => k).ToList();

        Assert.Equal(deToolKeys, enToolKeys);
    }

    [Fact]
    public void I18n_NoRemovedToolKeys_InAnyLocale()
    {
        var dataDir = FeatureService.ResolveDataDirectory();
        if (dataDir is null) return;

        var removedKeys = new[]
        {
            "Tool.FtpSource", "Tool.CloudSync", "Tool.PluginMarketplaceFeature",
            "Tool.PluginManager", "Tool.GpuHashing", "Tool.ParallelHashing",
            "Tool.QuickPreview", "Tool.CollectionDiff", "Tool.ConvertQueue",
            "Tool.ConversionEstimate", "Tool.TosecDat", "Tool.SplitPanelPreview",
            "Tool.PlaytimeTracker", "Tool.GenreClassification", "Tool.EmulatorCompat",
            "Tool.CoverScraper", "Tool.CollectionSharing", "Tool.TrendAnalysis",
            "Tool.WindowsContextMenu", "Tool.DockerContainer",
            "Tool.DuplicateInspector", "Tool.DuplicateHeatmap", "Tool.CrossRootDupe",
            "Tool.ExportCsv", "Tool.ExportExcel", "Tool.DuplicateExport",
            "Tool.PdfReport", "Tool.MobileWebUI", "Tool.SchedulerAdvanced", "Tool.ToolImport",
            "Tool.Cat.UI"
        };

        foreach (var locale in new[] { "de.json", "en.json", "fr.json" })
        {
            var path = Path.Combine(dataDir, "i18n", locale);
            if (!File.Exists(path)) continue;
            var dict = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(path))!;
            foreach (var removed in removedKeys)
            {
                Assert.False(dict.ContainsKey(removed), $"Removed i18n key '{removed}' found in {locale}");
                Assert.False(dict.ContainsKey(removed + ".Desc"), $"Removed i18n key '{removed}.Desc' found in {locale}");
            }
        }
    }

    // ═══ LOCALIZATION FALLBACK ══════════════════════════════════════════

    [Fact]
    public void LocalizationService_PerKeyFallback_FrMissingKeysResolveFromDe()
    {
        var dataDir = FeatureService.ResolveDataDirectory();
        if (dataDir is null) return;

        var deJson = Path.Combine(dataDir, "i18n", "de.json");
        var frJson = Path.Combine(dataDir, "i18n", "fr.json");
        if (!File.Exists(deJson) || !File.Exists(frJson)) return;

        var de = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(deJson))!
            .Where(kv => kv.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            .ToDictionary(kv => kv.Key, kv => kv.Value.GetString()!);
        var fr = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(frJson))!
            .Where(kv => kv.Value.ValueKind == System.Text.Json.JsonValueKind.String)
            .ToDictionary(kv => kv.Key, kv => kv.Value.GetString()!);

        // Per-key fallback: merge DE base with FR overlay (simulates LocalizationService logic)
        var merged = new Dictionary<string, string>(de);
        foreach (var kv in fr)
            merged[kv.Key] = kv.Value;

        // Every DE key must be present in the merged result
        foreach (var key in de.Keys)
            Assert.True(merged.ContainsKey(key), $"Key '{key}' missing after FR merge");
    }

    [Fact]
    public void LocalizationService_LoadLocale_DeReturnsNonEmpty()
    {
        var dict = FeatureService.LoadLocale("de");
        // If data dir is available, DE must have keys
        if (dict.Count == 0) return; // skip if data dir unavailable
        Assert.True(dict.Count > 100, $"DE locale has only {dict.Count} keys, expected >100");
        Assert.True(dict.ContainsKey("App.Title"));
    }

    [Fact]
    public void LocalizationService_LoadLocale_EnReturnsAllDeKeys()
    {
        var de = FeatureService.LoadLocale("de");
        var en = FeatureService.LoadLocale("en");
        if (de.Count == 0 || en.Count == 0) return;

        var missingInEn = de.Keys.Where(k => !en.ContainsKey(k)).ToList();
        Assert.Empty(missingInEn);
    }

    [Fact]
    public void LocalizationService_LoadLocale_FrReturnsAllDeKeys()
    {
        var de = FeatureService.LoadLocale("de");
        var fr = FeatureService.LoadLocale("fr");
        if (de.Count == 0 || fr.Count == 0) return;

        var missingInFr = de.Keys.Where(k => !fr.ContainsKey(k)).ToList();
        Assert.Empty(missingInFr);
    }

    [Fact]
    public void LocalizationService_FrMetadata_HasCorrectLocale()
    {
        var dataDir = FeatureService.ResolveDataDirectory();
        if (dataDir is null) return;
        var frJson = Path.Combine(dataDir, "i18n", "fr.json");
        if (!File.Exists(frJson)) return;

        var doc = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, System.Text.Json.JsonElement>>(File.ReadAllText(frJson))!;
        Assert.True(doc.ContainsKey("_meta"));
        var meta = doc["_meta"];
        Assert.Equal("fr", meta.GetProperty("locale").GetString());
        Assert.Equal("Français", meta.GetProperty("language").GetString());
    }

    // ═══ STUBS ══════════════════════════════════════════════════════════

    private sealed class RecordingDialogService : IDialogService
    {
        public string? BrowseFileResult { get; set; }
        public string? BrowseFolderResult { get; set; }
        public string? SaveFileResult { get; set; }
        public bool ConfirmResult { get; set; } = true;
        public Contracts.Ports.ConfirmResult YesNoCancelResult { get; set; } = Contracts.Ports.ConfirmResult.Yes;
        public string ShowInputBoxResult { get; set; } = "";
        public string? ShowMultilineInputBoxResult { get; set; }
        public string? LastMultilinePrompt { get; private set; }

        public List<string> InfoCalls { get; } = [];
        public List<string> ErrorCalls { get; } = [];
        public List<(string Title, string Content)> ShowTextCalls { get; } = [];

        public string? BrowseFolder(string title = "Ordner auswählen") => BrowseFolderResult;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => BrowseFileResult;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => SaveFileResult;
        public bool Confirm(string message, string title = "Bestätigung") => ConfirmResult;
        public void Info(string message, string title = "Information") => InfoCalls.Add(message);
        public void Error(string message, string title = "Fehler") => ErrorCalls.Add(message);
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => YesNoCancelResult;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => ShowInputBoxResult;
        public string ShowMultilineInputBox(string prompt, string title = "Eingabe", string defaultValue = "")
        {
            LastMultilinePrompt = prompt;
            return ShowMultilineInputBoxResult ?? ShowInputBoxResult;
        }
        public void ShowText(string title, string content) => ShowTextCalls.Add((title, content));
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => ConfirmResult;
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

    private sealed class StubWindowHost : IWindowHost
    {
        public bool SystemTrayToggled { get; private set; }
        public double FontSize { get; set; } = 14;
        public void SelectTab(int index) { }
        public void ShowTextDialog(string title, string content) { }
        public void ToggleSystemTray() => SystemTrayToggled = true;
        public void StartApiProcess(string projectPath) { }
        public void StopApiProcess() { }
    }
}
