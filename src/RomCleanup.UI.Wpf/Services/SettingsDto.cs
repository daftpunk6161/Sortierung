using RomCleanup.Contracts;
using RomCleanup.UI.Wpf.Models;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>RF-010: Settings DTO — decouples persistence from ViewModel.</summary>
public sealed record SettingsDto
{
    // General
    public string LogLevel { get; init; } = "Info";
    public bool AggressiveJunk { get; init; }
    public bool AliasKeying { get; init; }
    public string[] PreferredRegions { get; init; } = RunConstants.DefaultPreferRegions;

    // Tool paths
    public string ToolChdman { get; init; } = "";
    public string ToolDolphin { get; init; } = "";
    public string Tool7z { get; init; } = "";
    public string ToolPsxtract { get; init; } = "";
    public string ToolCiso { get; init; } = "";

    // DAT
    public bool UseDat { get; init; }
    public bool EnableDatAudit { get; init; } = true;
    public bool EnableDatRename { get; init; }
    public string DatRoot { get; init; } = "";
    public string DatHashType { get; init; } = "SHA1";
    public bool DatFallback { get; init; } = true;
    public DatMappingEntry[] DatMappings { get; init; } = [];

    // Paths
    public string TrashRoot { get; init; } = "";
    public string AuditRoot { get; init; } = "";
    public string Ps3DupesRoot { get; init; } = "";
    public string? LastAuditPath { get; init; }

    // Roots
    public string[] Roots { get; init; } = [];

    // UI
    public bool SortConsole { get; init; }
    public bool RemoveJunk { get; init; } = true;
    public bool OnlyGames { get; init; }
    public bool KeepUnknownWhenOnlyGames { get; init; } = true;
    public bool DryRun { get; init; } = true;
    public bool ConvertEnabled { get; init; }
    public bool ConfirmMove { get; init; } = true;
    public ConflictPolicy ConflictPolicy { get; init; } = ConflictPolicy.Rename;
    public string Theme { get; init; } = "Dark";
}

/// <summary>Serializable DAT mapping entry (console key → DAT file path).</summary>
public sealed record DatMappingEntry(string Console, string DatFile);
