namespace Romulus.Contracts.Models;

// ═══ DAT CATALOG STATE (persisted in AppData) ═══════════════════════════

/// <summary>
/// Tracks the local install state of every DAT file.
/// Persisted as JSON in <c>%APPDATA%\Romulus\dat-catalog-state.json</c>.
/// </summary>
public sealed class DatCatalogState
{
    public Dictionary<string, DatLocalInfo> Entries { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public DateTime? LastFullScan { get; set; }

    /// <summary>User-added catalog entries (persisted alongside built-in catalog).</summary>
    public List<DatUserEntry> UserEntries { get; set; } = [];

    /// <summary>Built-in catalog IDs the user has explicitly removed/hidden.</summary>
    public HashSet<string> RemovedBuiltinIds { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>
/// A user-defined DAT catalog entry (not part of the built-in dat-catalog.json).
/// </summary>
public sealed class DatUserEntry
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "Benutzerdefiniert";
    public string System { get; set; } = "";
    public string Url { get; set; } = "";
    public string Format { get; set; } = "raw-dat";
    public string ConsoleKey { get; set; } = "";
}

/// <summary>
/// Per-DAT local info tracked in the state file.
/// </summary>
public sealed class DatLocalInfo
{
    public DateTime InstalledDate { get; set; }
    public string FileSha256 { get; set; } = "";
    public long FileSizeBytes { get; set; }
    public string LocalPath { get; set; } = "";
}

// ═══ DAT CATALOG STATUS (computed, for display) ═════════════════════════

/// <summary>Install status of a DAT entry relative to the local file system.</summary>
public enum DatInstallStatus { Missing, Installed, Stale, Error }

/// <summary>Download strategy determined by catalog format and group.</summary>
public enum DatDownloadStrategy { Auto, PackImport, ManualLogin }

/// <summary>
/// Merged view of a catalog entry + its local status, used for UI display.
/// Immutable record — computed by <c>DatCatalogStateService.BuildCatalogStatus</c>.
/// </summary>
public sealed record DatCatalogStatusEntry
{
    public string Id { get; init; } = "";
    public string Group { get; init; } = "";
    public string System { get; init; } = "";
    public string ConsoleKey { get; init; } = "";
    public string Url { get; init; } = "";
    public string Format { get; init; } = "";
    public DatInstallStatus Status { get; init; }
    public DatDownloadStrategy DownloadStrategy { get; init; }
    public DateTime? InstalledDate { get; init; }
    public string? LocalPath { get; init; }
    public long? FileSizeBytes { get; init; }
}
