namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// GUI-032: Typed constant keys for FeatureCommands dictionary.
/// Replaces magic strings with compile-time checked constants.
/// </summary>
public static class FeatureCommandKeys
{
    // ── Analyse & Berichte ──
    public const string QuickPreview = "QuickPreview";
    public const string HealthScore = "HealthScore";
    public const string CollectionDiff = "CollectionDiff";
    public const string DuplicateInspector = "DuplicateInspector";
    public const string ConversionEstimate = "ConversionEstimate";
    public const string JunkReport = "JunkReport";
    public const string RomFilter = "RomFilter";
    public const string DuplicateHeatmap = "DuplicateHeatmap";
    public const string MissingRom = "MissingRom";
    public const string CrossRootDupe = "CrossRootDupe";
    public const string HeaderAnalysis = "HeaderAnalysis";
    public const string Completeness = "Completeness";
    public const string DryRunCompare = "DryRunCompare";
    public const string TrendAnalysis = "TrendAnalysis";
    public const string EmulatorCompat = "EmulatorCompat";

    // ── Konvertierung & Hashing ──
    public const string ConversionPipeline = "ConversionPipeline";
    public const string NKitConvert = "NKitConvert";
    public const string ConvertQueue = "ConvertQueue";
    public const string ConversionVerify = "ConversionVerify";
    public const string FormatPriority = "FormatPriority";
    public const string ParallelHashing = "ParallelHashing";
    public const string GpuHashing = "GpuHashing";

    // ── DAT & Verifizierung ──
    public const string DatAutoUpdate = "DatAutoUpdate";
    public const string DatDiffViewer = "DatDiffViewer";
    public const string TosecDat = "TosecDat";
    public const string CustomDatEditor = "CustomDatEditor";
    public const string HashDatabaseExport = "HashDatabaseExport";

    // ── Sammlungsverwaltung ──
    public const string CollectionManager = "CollectionManager";
    public const string CloneListViewer = "CloneListViewer";
    public const string CoverScraper = "CoverScraper";
    public const string GenreClassification = "GenreClassification";
    public const string PlaytimeTracker = "PlaytimeTracker";
    public const string CollectionSharing = "CollectionSharing";
    public const string VirtualFolderPreview = "VirtualFolderPreview";

    // ── Sicherheit & Integrität ──
    public const string IntegrityMonitor = "IntegrityMonitor";
    public const string BackupManager = "BackupManager";
    public const string Quarantine = "Quarantine";
    public const string RuleEngine = "RuleEngine";
    public const string PatchEngine = "PatchEngine";
    public const string HeaderRepair = "HeaderRepair";
    public const string RollbackQuick = "RollbackQuick";
    public const string RollbackUndo = "RollbackUndo";
    public const string RollbackRedo = "RollbackRedo";

    // ── Workflow & Automatisierung ──
    public const string CommandPalette = "CommandPalette";
    public const string SplitPanelPreview = "SplitPanelPreview";
    public const string FilterBuilder = "FilterBuilder";
    public const string SortTemplates = "SortTemplates";
    public const string PipelineEngine = "PipelineEngine";
    public const string SystemTray = "SystemTray";
    public const string SchedulerAdvanced = "SchedulerAdvanced";
    public const string RulePackSharing = "RulePackSharing";
    public const string ArcadeMergeSplit = "ArcadeMergeSplit";
    public const string AutoProfile = "AutoProfile";

    // ── Export & Integration ──
    public const string PdfReport = "PdfReport";
    public const string LauncherIntegration = "LauncherIntegration";
    public const string ToolImport = "ToolImport";
    public const string DuplicateExport = "DuplicateExport";
    public const string ExportCsv = "ExportCsv";
    public const string ExportExcel = "ExportExcel";

    // ── Infrastruktur ──
    public const string StorageTiering = "StorageTiering";
    public const string NasOptimization = "NasOptimization";
    public const string FtpSource = "FtpSource";
    public const string CloudSync = "CloudSync";
    public const string PluginMarketplace = "PluginMarketplaceFeature";
    public const string PluginManager = "PluginManager";
    public const string PortableMode = "PortableMode";
    public const string DockerContainer = "DockerContainer";
    public const string MobileWebUI = "MobileWebUI";
    public const string WindowsContextMenu = "WindowsContextMenu";
    public const string HardlinkMode = "HardlinkMode";
    public const string MultiInstanceSync = "MultiInstanceSync";

    // ── UI & Erscheinungsbild ──
    public const string Accessibility = "Accessibility";
    public const string ThemeEngine = "ThemeEngine";

    // ── Funktionale Buttons (Settings/Config) ──
    public const string ExportLog = "ExportLog";
    public const string ProfileDelete = "ProfileDelete";
    public const string ProfileImport = "ProfileImport";
    public const string ConfigDiff = "ConfigDiff";
    public const string ExportUnified = "ExportUnified";
    public const string ConfigImport = "ConfigImport";
    public const string AutoFindTools = "AutoFindTools";
    public const string ApplyLocale = "ApplyLocale";
}
