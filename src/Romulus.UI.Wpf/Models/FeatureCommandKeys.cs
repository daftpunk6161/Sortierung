namespace Romulus.UI.Wpf.Models;

/// <summary>
/// GUI-032: Typed constant keys for FeatureCommands dictionary.
/// Replaces magic strings with compile-time checked constants.
/// </summary>
public static class FeatureCommandKeys
{
    // ── Analyse & Berichte ──
    public const string HealthScore = "HealthScore";
    public const string DuplicateAnalysis = "DuplicateAnalysis";
    public const string JunkReport = "JunkReport";
    public const string RomFilter = "RomFilter";
    public const string MissingRom = "MissingRom";
    public const string HeaderAnalysis = "HeaderAnalysis";
    public const string Completeness = "Completeness";
    public const string DryRunCompare = "DryRunCompare";

    // ── Konvertierung & Hashing ──
    public const string ConversionPipeline = "ConversionPipeline";
    public const string ConversionVerify = "ConversionVerify";
    public const string FormatPriority = "FormatPriority";
    public const string PatchPipeline = "PatchPipeline";

    // ── DAT & Verifizierung ──
    public const string DatAutoUpdate = "DatAutoUpdate";
    public const string DatDiffViewer = "DatDiffViewer";
    public const string CustomDatEditor = "CustomDatEditor";
    public const string HashDatabaseExport = "HashDatabaseExport";

    // ── Sammlungsverwaltung ──
    public const string CollectionManager = "CollectionManager";
    public const string CloneListViewer = "CloneListViewer";
    public const string VirtualFolderPreview = "VirtualFolderPreview";
    public const string CollectionMerge = "CollectionMerge";

    // ── Sicherheit & Integrität ──
    public const string IntegrityMonitor = "IntegrityMonitor";
    public const string BackupManager = "BackupManager";
    public const string Quarantine = "Quarantine";
    public const string RuleEngine = "RuleEngine";
    public const string HeaderRepair = "HeaderRepair";
    public const string RollbackQuick = "RollbackQuick";
    public const string RollbackHistoryBack = "RollbackHistoryBack";
    public const string RollbackHistoryForward = "RollbackHistoryForward";
    public const string RollbackUndo = "RollbackUndo";
    public const string RollbackRedo = "RollbackRedo";

    // ── Workflow & Automatisierung ──
    public const string CommandPalette = "CommandPalette";
    public const string FilterBuilder = "FilterBuilder";
    public const string SortTemplates = "SortTemplates";
    public const string PipelineEngine = "PipelineEngine";
    public const string RulePackSharing = "RulePackSharing";
    public const string ArcadeMergeSplit = "ArcadeMergeSplit";
    public const string AutoProfile = "AutoProfile";

    // ── Export & Integration ──
    public const string HtmlReport = "HtmlReport";
    public const string LauncherIntegration = "LauncherIntegration";
    public const string DatImport = "DatImport";
    public const string ExportCollection = "ExportCollection";

    // ── Infrastruktur ──
    public const string StorageTiering = "StorageTiering";
    public const string NasOptimization = "NasOptimization";
    public const string PortableMode = "PortableMode";
    public const string ApiServer = "ApiServer";
    public const string HardlinkMode = "HardlinkMode";

    // ── UI & Erscheinungsbild ──
    public const string Accessibility = "Accessibility";

    // ── Funktionale Buttons (Settings/Config) ──
    public const string ExportLog = "ExportLog";
    public const string ProfileSave = "ProfileSave";
    public const string ProfileLoad = "ProfileLoad";
    public const string ProfileDelete = "ProfileDelete";
    public const string ProfileImport = "ProfileImport";
    public const string ProfileShare = "ProfileShare";
    public const string CliCommandCopy = "CliCommandCopy";
    public const string ConfigDiff = "ConfigDiff";
    public const string ExportUnified = "ExportUnified";
    public const string ConfigImport = "ConfigImport";
    public const string AutoFindTools = "AutoFindTools";
    public const string ApplyLocale = "ApplyLocale";
    public const string SchedulerApply = "SchedulerApply";

    // ── Fenster-gebundene Commands (benötigen WindowHost) ──
    public const string SystemTray = "SystemTray";
}
