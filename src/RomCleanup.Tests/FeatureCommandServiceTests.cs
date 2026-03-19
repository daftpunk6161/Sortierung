using System.Windows.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;

namespace RomCleanup.Tests;

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
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_FCS_" + Guid.NewGuid().ToString("N"));
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
    [InlineData("ConfigDiff")]
    [InlineData("ExportUnified")]
    [InlineData("ConfigImport")]
    [InlineData("AutoFindTools")]
    [InlineData("HealthScore")]
    [InlineData("CollectionDiff")]
    [InlineData("DuplicateInspector")]
    [InlineData("DuplicateExport")]
    [InlineData("ExportCsv")]
    [InlineData("ExportExcel")]
    [InlineData("RollbackHistoryBack")]
    [InlineData("RollbackHistoryForward")]
    [InlineData("ApplyLocale")]
    [InlineData("PluginManager")]
    [InlineData("AutoProfile")]
    [InlineData("ConversionEstimate")]
    [InlineData("JunkReport")]
    [InlineData("RomFilter")]
    [InlineData("DuplicateHeatmap")]
    [InlineData("MissingRom")]
    [InlineData("CrossRootDupe")]
    [InlineData("HeaderAnalysis")]
    [InlineData("Completeness")]
    [InlineData("DryRunCompare")]
    [InlineData("TrendAnalysis")]
    [InlineData("EmulatorCompat")]
    [InlineData("ConversionPipeline")]
    [InlineData("NKitConvert")]
    [InlineData("ConvertQueue")]
    [InlineData("ConversionVerify")]
    [InlineData("FormatPriority")]
    [InlineData("ParallelHashing")]
    [InlineData("GpuHashing")]
    [InlineData("DatAutoUpdate")]
    [InlineData("DatDiffViewer")]
    [InlineData("TosecDat")]
    [InlineData("CustomDatEditor")]
    [InlineData("HashDatabaseExport")]
    [InlineData("CollectionManager")]
    [InlineData("CloneListViewer")]
    [InlineData("CoverScraper")]
    [InlineData("GenreClassification")]
    [InlineData("PlaytimeTracker")]
    [InlineData("CollectionSharing")]
    [InlineData("VirtualFolderPreview")]
    [InlineData("IntegrityMonitor")]
    [InlineData("BackupManager")]
    [InlineData("Quarantine")]
    [InlineData("RuleEngine")]
    [InlineData("PatchEngine")]
    [InlineData("HeaderRepair")]
    [InlineData("SplitPanelPreview")]
    [InlineData("FilterBuilder")]
    [InlineData("SortTemplates")]
    [InlineData("PipelineStatus")]
    [InlineData("SchedulerAdvanced")]
    [InlineData("RulePackSharing")]
    [InlineData("ArcadeMergeSplit")]
    [InlineData("PdfReport")]
    [InlineData("LauncherIntegration")]
    [InlineData("ToolImport")]
    [InlineData("StorageTiering")]
    [InlineData("NasOptimization")]
    [InlineData("FtpSource")]
    [InlineData("CloudSync")]
    [InlineData("PluginMarketplaceFeature")]
    [InlineData("PortableMode")]
    [InlineData("DockerContainer")]
    [InlineData("WindowsContextMenu")]
    [InlineData("HardlinkMode")]
    [InlineData("MultiInstanceSync")]
    public void RegisterCommands_ContainsExpectedCommand(string commandKey)
    {
        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.ContainsKey(commandKey),
            $"Feature command '{commandKey}' not registered");
    }

    [Theory]
    [InlineData("CommandPalette")]
    [InlineData("SystemTray")]
    [InlineData("MobileWebUI")]
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
        Assert.False(_vm.FeatureCommands.ContainsKey("MobileWebUI"));
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
    public void RegisterCommands_CountIsAtLeast60()
    {
        _sut.RegisterCommands();
        Assert.True(_vm.FeatureCommands.Count >= 60,
            $"Expected at least 60 commands, got {_vm.FeatureCommands.Count}");
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

    // ═══ CONVERSION ESTIMATE ════════════════════════════════════════════

    [Fact]
    public void ConversionEstimate_NoCandidates_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["ConversionEstimate"].Execute(null);
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

    // ═══ DUPLICATE HEATMAP ══════════════════════════════════════════════

    [Fact]
    public void DuplicateHeatmap_NoCandidates_LogsOrShowsResult()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["DuplicateHeatmap"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ EMULATOR COMPAT ════════════════════════════════════════════════

    [Fact]
    public void EmulatorCompat_NoCandidates_LogsOrShowsResult()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["EmulatorCompat"].Execute(null);
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

    // ═══ PLUGIN MANAGER ═════════════════════════════════════════════════

    [Fact]
    public void PluginManager_ShowsPluginInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["PluginManager"].Execute(null);
        Assert.True(HasOutput());
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
    public void PipelineStatus_ShowsPipelineInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["PipelineStatus"].Execute(null);
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

    // ═══ CLOUD SYNC ═════════════════════════════════════════════════════

    [Fact]
    public void CloudSync_ShowsCloudInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["CloudSync"].Execute(null);
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

    // ═══ DOCKER CONTAINER ═══════════════════════════════════════════════

    [Fact]
    public void DockerContainer_ShowsDockerInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["DockerContainer"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ SPLIT PANEL PREVIEW ════════════════════════════════════════════

    [Fact]
    public void SplitPanelPreview_NoCandidates_LogsOrShowsPreview()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["SplitPanelPreview"].Execute(null);
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

    // ═══ CROSS ROOT DUPE ════════════════════════════════════════════════

    [Fact]
    public void CrossRootDupe_NoCandidates_LogsOrShowsResult()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["CrossRootDupe"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ COMPLETENESS ═══════════════════════════════════════════════════

    [Fact]
    public void Completeness_ShowsResult()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["Completeness"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ COLLECTION MANAGER ═════════════════════════════════════════════

    [Fact]
    public void CollectionManager_NoCandidates_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["CollectionManager"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ GENRE CLASSIFICATION ═══════════════════════════════════════════

    [Fact]
    public void GenreClassification_NoCandidates_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["GenreClassification"].Execute(null);
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
        _vm.FeatureCommands["RuleEngine"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ CONVERSION PIPELINE ════════════════════════════════════════════

    [Fact]
    public void ConversionPipeline_NoCandidates_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["ConversionPipeline"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ CONVERT QUEUE ══════════════════════════════════════════════════

    [Fact]
    public void ConvertQueue_NoCandidates_LogsOrShowsInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["ConvertQueue"].Execute(null);
        Assert.True(HasOutput());
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

    // ═══ MULTI INSTANCE SYNC ════════════════════════════════════════════

    [Fact]
    public void MultiInstanceSync_ShowsSyncInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["MultiInstanceSync"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ WITH CANDIDATES ════════════════════════════════════════════════

    [Fact]
    public void HealthScore_WithCandidates_ShowsScore()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
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
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
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
    public void DuplicateHeatmap_WithCandidates_ShowsHeatmap()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "a.sfc", GameKey = "Game", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game },
            new RomCandidate { MainPath = "b.sfc", GameKey = "Game", Region = "US",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game }
        };
        _vm.FeatureCommands["DuplicateHeatmap"].Execute(null);
        Assert.True(HasOutput());
    }

    [Fact]
    public void ConversionEstimate_WithCandidates_ShowsEstimate()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "game.iso", GameKey = "Game", Region = "EU",
                Extension = ".iso", SizeBytes = 700_000_000, Category = FileCategory.Game }
        };
        _vm.FeatureCommands["ConversionEstimate"].Execute(null);
        Assert.True(HasOutput());
    }

    [Fact]
    public void GenreClassification_WithCandidates_ShowsGenres()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "Super Mario World.sfc", GameKey = "Super Mario World",
                Region = "US", Extension = ".sfc", SizeBytes = 2048, Category = FileCategory.Game },
            new RomCandidate { MainPath = "Zelda.sfc", GameKey = "The Legend of Zelda",
                Region = "EU", Extension = ".sfc", SizeBytes = 2048, Category = FileCategory.Game }
        };
        _vm.FeatureCommands["GenreClassification"].Execute(null);
        Assert.True(HasOutput());
    }

    [Fact]
    public void EmulatorCompat_WithCandidates_ShowsCompat()
    {
        _sut.RegisterCommands();
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "game.sfc", GameKey = "Game", Region = "EU", ConsoleKey = "SNES",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game }
        };
        _vm.FeatureCommands["EmulatorCompat"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ EXPORT COMMANDS WITH CANDIDATES ════════════════════════════════

    [Fact]
    public void ExportCsv_WithCandidatesAndSavePath_CreatesCsvFile()
    {
        _sut.RegisterCommands();
        var csvPath = Path.Combine(_tempDir, "export.csv");
        _dialog.SaveFileResult = csvPath;
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "game.sfc", GameKey = "Game", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game, ConsoleKey = "SNES" }
        };
        _vm.FeatureCommands["ExportCsv"].Execute(null);
        Assert.True(File.Exists(csvPath));
    }

    [Fact]
    public void ExportExcel_WithCandidatesAndSavePath_CreatesXmlFile()
    {
        _sut.RegisterCommands();
        var xmlPath = Path.Combine(_tempDir, "export.xml");
        _dialog.SaveFileResult = xmlPath;
        _vm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCleanup.Contracts.Models.RomCandidate>
        {
            new RomCandidate { MainPath = "game.sfc", GameKey = "Game", Region = "EU",
                Extension = ".sfc", SizeBytes = 1024, Category = FileCategory.Game, ConsoleKey = "SNES" }
        };
        _vm.FeatureCommands["ExportExcel"].Execute(null);
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

    // ═══ SCHEDULER ADVANCED ═════════════════════════════════════════════

    [Fact]
    public void SchedulerAdvanced_ShowsSchedulerInfo()
    {
        _sut.RegisterCommands();
        _dialog.ShowInputBoxResult = "* * * * *";
        _vm.FeatureCommands["SchedulerAdvanced"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ GPU HASHING ════════════════════════════════════════════════════

    [Fact]
    public void GpuHashing_ShowsGpuInfo()
    {
        _sut.RegisterCommands();
        _vm.FeatureCommands["GpuHashing"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ PARALLEL HASHING ═══════════════════════════════════════════════

    [Fact]
    public void ParallelHashing_ShowsThreadInfo()
    {
        _sut.RegisterCommands();
        _dialog.ShowInputBoxResult = "4";
        _vm.FeatureCommands["ParallelHashing"].Execute(null);
        Assert.True(HasOutput());
    }

    // ═══ WINDOWS CONTEXT MENU ═══════════════════════════════════════════

    [Fact]
    public void WindowsContextMenu_WithSavePath_CreatesFile()
    {
        _sut.RegisterCommands();
        var regPath = Path.Combine(_tempDir, "context-menu.reg");
        _dialog.SaveFileResult = regPath;
        _vm.FeatureCommands["WindowsContextMenu"].Execute(null);
        Assert.True(File.Exists(regPath));
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

    // ═══ STUBS ══════════════════════════════════════════════════════════

    private sealed class RecordingDialogService : IDialogService
    {
        public string? BrowseFileResult { get; set; }
        public string? BrowseFolderResult { get; set; }
        public string? SaveFileResult { get; set; }
        public bool ConfirmResult { get; set; } = true;
        public string ShowInputBoxResult { get; set; } = "";

        public List<string> InfoCalls { get; } = [];
        public List<(string Title, string Content)> ShowTextCalls { get; } = [];

        public string? BrowseFolder(string title = "Ordner auswählen") => BrowseFolderResult;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => BrowseFileResult;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => SaveFileResult;
        public bool Confirm(string message, string title = "Bestätigung") => ConfirmResult;
        public void Info(string message, string title = "Information") => InfoCalls.Add(message);
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => Contracts.Ports.ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => ShowInputBoxResult;
        public void ShowText(string title, string content) => ShowTextCalls.Add((title, content));
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
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
