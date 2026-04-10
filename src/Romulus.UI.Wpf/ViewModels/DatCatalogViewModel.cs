using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Dat;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// ViewModel for the DAT Catalog management view.
/// Displays all ~300 catalog entries with install status, group filter, and batch actions.
/// </summary>
public sealed partial class DatCatalogViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialog;
    private readonly object _entriesLock = new();
    private readonly Func<string> _getDatRoot;
    private readonly Action<string, string> _addLog;

    public DatCatalogViewModel(
        ILocalizationService? loc = null,
        IDialogService? dialog = null,
        Func<string>? getDatRoot = null,
        Action<string, string>? addLog = null)
    {
        _loc = loc ?? new LocalizationService();
        _dialog = dialog ?? new WpfDialogService();
        _getDatRoot = getDatRoot ?? (() => "");
        _addLog = addLog ?? ((_, _) => { });

        BindingOperations.EnableCollectionSynchronization(Entries, _entriesLock);
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = ApplyFilter;
    }

    // ═══ DATA ════════════════════════════════════════════════════════════
    public ObservableCollection<DatCatalogItemVm> Entries { get; } = [];
    public ICollectionView EntriesView { get; }

    // ═══ SUMMARY COUNTERS ═══════════════════════════════════════════════
    private int _totalCount;
    public int TotalCount { get => _totalCount; private set => SetProperty(ref _totalCount, value); }

    private int _installedCount;
    public int InstalledCount { get => _installedCount; private set => SetProperty(ref _installedCount, value); }

    private int _missingCount;
    public int MissingCount { get => _missingCount; private set => SetProperty(ref _missingCount, value); }

    private int _staleCount;
    public int StaleCount { get => _staleCount; private set => SetProperty(ref _staleCount, value); }

    private int _autoCount;
    public int AutoCount { get => _autoCount; private set => SetProperty(ref _autoCount, value); }

    public bool HasData => TotalCount > 0;

    private bool _isBusy;
    public bool IsBusy { get => _isBusy; set => SetProperty(ref _isBusy, value); }

    private string _statusText = "";
    public string StatusText { get => _statusText; set => SetProperty(ref _statusText, value); }

    // ═══ FILTER ═════════════════════════════════════════════════════════
    public ObservableCollection<string> GroupFilterOptions { get; } = ["Alle"];
    public ObservableCollection<string> StatusFilterOptions { get; } = ["Alle", "Installiert", "Fehlend", "Veraltet"];

    private string _selectedGroupFilter = "Alle";
    public string SelectedGroupFilter
    {
        get => _selectedGroupFilter;
        set { if (SetProperty(ref _selectedGroupFilter, value)) EntriesView.Refresh(); }
    }

    private string _selectedStatusFilter = "Alle";
    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set { if (SetProperty(ref _selectedStatusFilter, value)) EntriesView.Refresh(); }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value)) EntriesView.Refresh(); }
    }

    private bool _selectAll;
    public bool SelectAll
    {
        get => _selectAll;
        set
        {
            if (SetProperty(ref _selectAll, value))
            {
                foreach (var item in EntriesView.Cast<DatCatalogItemVm>())
                    item.IsSelected = value;
            }
        }
    }

    // ═══ STATE ══════════════════════════════════════════════════════════
    private DatCatalogState _state = new();
    private List<DatCatalogEntry> _builtinCatalog = [];
    private List<DatCatalogEntry> _catalog = [];
    private string _statePath = DatCatalogStateService.GetDefaultStatePath();

    // ═══ COMMANDS ═══════════════════════════════════════════════════════

    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsBusy) return;
        IsBusy = true;
        StatusText = "Katalog wird geladen…";

        try
        {
            await Task.Run(() =>
            {
                var dataDir = FeatureService.ResolveDataDirectory()
                    ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
                var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
                _builtinCatalog = DatSourceService.LoadCatalog(catalogPath);
                _state = DatCatalogStateService.LoadState(_statePath);
                _catalog = DatCatalogStateService.MergeCatalogs(_builtinCatalog, _state);

                var datRoot = _getDatRoot();
                _state = DatCatalogStateService.FullScan(_catalog, datRoot, _state);
                DatCatalogStateService.SaveState(_statePath, _state);
            });

            RebuildEntries();
            StatusText = $"{TotalCount} DATs im Katalog · {InstalledCount} installiert · {MissingCount} fehlend · {StaleCount} veraltet";
            _addLog($"DAT-Katalog: {TotalCount} Einträge, {InstalledCount} installiert, {MissingCount} fehlend, {StaleCount} veraltet", "INFO");
        }
        catch (Exception ex)
        {
            StatusText = $"Fehler: {ex.Message}";
            _addLog($"DAT-Katalog Fehler: {ex.Message}", "ERROR");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task UpdateAutoAsync()
    {
        if (IsBusy) return;

        var autoEntries = Entries.Where(e =>
            e.DownloadStrategy == DatDownloadStrategy.Auto
            && e.Status is DatInstallStatus.Missing or DatInstallStatus.Stale).ToList();

        if (autoEntries.Count == 0)
        {
            _dialog.Info("Alle auto-downloadbaren DATs sind aktuell.", "DAT-Katalog");
            return;
        }

        if (!_dialog.Confirm(
            $"{autoEntries.Count} DATs automatisch herunterladen?\n\n" +
            string.Join("\n", autoEntries.GroupBy(e => e.Group).Select(g => $"  {g.Key}: {g.Count()}")),
            "Auto-Download"))
            return;

        IsBusy = true;
        var datRoot = _getDatRoot();
        if (string.IsNullOrWhiteSpace(datRoot) || !Directory.Exists(datRoot))
        {
            _dialog.Info("DAT-Root nicht konfiguriert oder existiert nicht.", "DAT-Katalog");
            IsBusy = false;
            return;
        }

        int success = 0, failed = 0;
        try
        {
            using var datService = new DatSourceService(datRoot);
            for (int i = 0; i < autoEntries.Count; i++)
            {
                var entry = autoEntries[i];
                StatusText = $"[{i + 1}/{autoEntries.Count}] {entry.System}…";
                _addLog($"  [{i + 1}/{autoEntries.Count}] {entry.Id}…", "DEBUG");

                try
                {
                    var result = await datService.DownloadDatByFormatAsync(
                        entry.Url, entry.Id + ".dat", entry.Format);
                    if (result is not null)
                    {
                        success++;
                        var fi = new FileInfo(result);
                        DatCatalogStateService.UpdateStateAfterDownload(
                            _state, entry.Id, result, fi.Length);
                        entry.Status = DatInstallStatus.Installed;
                        entry.InstalledDate = DateTime.UtcNow;
                        entry.LocalPath = result;
                        // Clean up .bak after successful download
                        try { var bak = result + ".bak"; if (File.Exists(bak)) File.Delete(bak); }
                        catch (IOException) { }
                        _addLog($"  ✓ {entry.Id}", "INFO");
                    }
                    else
                    {
                        failed++;
                        _addLog($"  ✗ {entry.Id}: Download fehlgeschlagen", "WARN");
                    }
                }
                catch (InvalidOperationException ex) when (ex.Message.Contains("HTML"))
                {
                    failed++;
                    _addLog($"  ✗ {entry.Id}: Login-Seite erhalten", "WARN");
                }
                catch (Exception ex)
                {
                    failed++;
                    _addLog($"  ✗ {entry.Id}: {ex.Message}", "WARN");
                }
            }

            await Task.Run(() => DatCatalogStateService.SaveState(_statePath, _state));
            RefreshCounters();
            StatusText = $"Download: {success} erfolgreich, {failed} fehlgeschlagen";
            _addLog($"DAT-Download: {success} erfolgreich, {failed} fehlgeschlagen", success > 0 ? "INFO" : "WARN");
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task ImportDatsAsync()
    {
        await ImportPackAsync("DAT-Ordner auswählen (enthält .dat/.xml Dateien)", formatFilter: null, groupFilter: null);
    }

    [RelayCommand]
    private void AddDat()
    {
        var system = _dialog.ShowInputBox("System-Name (z.B. 'Sony - PlayStation 2'):", "DAT hinzufügen");
        if (string.IsNullOrWhiteSpace(system)) return;

        var url = _dialog.ShowInputBox("URL (optional, für Auto-Download):", "DAT hinzufügen");
        var consoleKey = _dialog.ShowInputBox("Konsolen-Key (z.B. PS2):", "DAT hinzufügen");
        if (string.IsNullOrWhiteSpace(consoleKey)) return;

        var id = "user-" + consoleKey.ToLowerInvariant().Replace(' ', '-');

        // Prevent duplicate IDs
        if (_state.UserEntries.Any(u => string.Equals(u.Id, id, StringComparison.OrdinalIgnoreCase))
            || _catalog.Any(c => string.Equals(c.Id, id, StringComparison.OrdinalIgnoreCase)))
        {
            _dialog.Info($"Ein Eintrag mit ID '{id}' existiert bereits.", "DAT hinzufügen");
            return;
        }

        var format = string.IsNullOrWhiteSpace(url) ? "raw-dat" : "raw-dat";
        _state.UserEntries.Add(new DatUserEntry
        {
            Id = id,
            System = system.Trim(),
            ConsoleKey = consoleKey.Trim().ToUpperInvariant(),
            Url = url?.Trim() ?? "",
            Format = format,
            Group = "Benutzerdefiniert"
        });

        _catalog = DatCatalogStateService.MergeCatalogs(_builtinCatalog, _state);
        DatCatalogStateService.SaveState(_statePath, _state);
        RebuildEntries();
        _addLog($"DAT hinzugefügt: {system} ({id})", "INFO");
        StatusText = $"DAT '{system}' hinzugefügt";
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        var selected = Entries.Where(e => e.IsSelected).ToList();
        if (selected.Count == 0)
        {
            _dialog.Info("Keine Einträge ausgewählt.", "DAT entfernen");
            return;
        }

        if (!_dialog.Confirm($"{selected.Count} DAT-Einträge aus dem Katalog entfernen?", "DAT entfernen"))
            return;

        foreach (var item in selected)
        {
            // User entry → delete from UserEntries
            var userEntry = _state.UserEntries.FirstOrDefault(u =>
                string.Equals(u.Id, item.Id, StringComparison.OrdinalIgnoreCase));
            if (userEntry is not null)
            {
                _state.UserEntries.Remove(userEntry);
            }
            else
            {
                // Built-in entry → add to RemovedBuiltinIds
                _state.RemovedBuiltinIds.Add(item.Id);
            }
        }

        _catalog = DatCatalogStateService.MergeCatalogs(_builtinCatalog, _state);
        DatCatalogStateService.SaveState(_statePath, _state);
        RebuildEntries();
        _addLog($"{selected.Count} DAT-Einträge entfernt", "INFO");
        StatusText = $"{selected.Count} Einträge entfernt";
    }

    [RelayCommand]
    private async Task DownloadSelectedAsync()
    {
        if (IsBusy) return;

        var selected = Entries.Where(e => e.IsSelected && e.DownloadStrategy == DatDownloadStrategy.Auto
            && e.Status is DatInstallStatus.Missing or DatInstallStatus.Stale).ToList();

        if (selected.Count == 0)
        {
            _dialog.Info("Keine auto-downloadbaren DATs ausgewählt.", "DAT-Katalog");
            return;
        }

        if (!_dialog.Confirm($"{selected.Count} ausgewählte DATs herunterladen?", "Download"))
            return;

        IsBusy = true;
        var datRoot = _getDatRoot();
        int success = 0, failed = 0;

        try
        {
            using var datService = new DatSourceService(datRoot);
            for (int i = 0; i < selected.Count; i++)
            {
                var entry = selected[i];
                StatusText = $"[{i + 1}/{selected.Count}] {entry.System}…";

                try
                {
                    var result = await datService.DownloadDatByFormatAsync(
                        entry.Url, entry.Id + ".dat", entry.Format);
                    if (result is not null)
                    {
                        success++;
                        var fi = new FileInfo(result);
                        DatCatalogStateService.UpdateStateAfterDownload(
                            _state, entry.Id, result, fi.Length);
                        entry.Status = DatInstallStatus.Installed;
                        entry.InstalledDate = DateTime.UtcNow;
                        entry.LocalPath = result;
                        entry.IsSelected = false;
                        // Clean up .bak after successful download
                        try { var bak = result + ".bak"; if (File.Exists(bak)) File.Delete(bak); }
                        catch (IOException) { }
                    }
                    else { failed++; }
                }
                catch { failed++; }
            }

            await Task.Run(() => DatCatalogStateService.SaveState(_statePath, _state));
            RefreshCounters();
            StatusText = $"Download: {success} erfolgreich, {failed} fehlgeschlagen";
            _addLog($"Selektiver Download: {success} erfolgreich, {failed} fehlgeschlagen", "INFO");
        }
        finally
        {
            IsBusy = false;
        }
    }

    // ═══ INTERNAL ═══════════════════════════════════════════════════════

    private async Task ImportPackAsync(string dialogTitle, string? formatFilter, string? groupFilter = null)
    {
        if (IsBusy) return;

        var packDir = _dialog.BrowseFolder(dialogTitle);
        if (string.IsNullOrWhiteSpace(packDir) || !Directory.Exists(packDir))
            return;

        IsBusy = true;
        StatusText = "Importiere DATs…";
        var datRoot = _getDatRoot();

        try
        {
            int imported = 0;
            await Task.Run(() =>
            {
                using var datService = new DatSourceService(datRoot);

                // Filter catalog entries by group/format if requested
                var filteredCatalog = _catalog.AsEnumerable();
                if (formatFilter is not null)
                    filteredCatalog = filteredCatalog.Where(e =>
                        string.Equals(e.Format, formatFilter, StringComparison.OrdinalIgnoreCase));
                if (groupFilter is not null)
                    filteredCatalog = filteredCatalog.Where(e =>
                        string.Equals(e.Group, groupFilter, StringComparison.OrdinalIgnoreCase));

                imported = datService.ImportLocalDatPacks(packDir, filteredCatalog.ToList());

                // Rescan after import
                _state = DatCatalogStateService.FullScan(_catalog, datRoot, _state);
                DatCatalogStateService.SaveState(_statePath, _state);
            });

            RebuildEntries();
            StatusText = $"Import: {imported} DATs importiert";
            _addLog($"DAT-Import: {imported} DATs aus {packDir}", imported > 0 ? "INFO" : "WARN");
        }
        catch (Exception ex)
        {
            StatusText = $"Import-Fehler: {ex.Message}";
            _addLog($"DAT-Import Fehler: {ex.Message}", "ERROR");
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RebuildEntries()
    {
        var datRoot = _getDatRoot();
        var statusList = DatCatalogStateService.BuildCatalogStatus(_catalog, datRoot, _state);

        // Rebuild group filter options — preserve user selection
        var previousGroup = _selectedGroupFilter;
        var previousStatus = _selectedStatusFilter;
        var groups = statusList.Select(e => e.Group).Distinct().OrderBy(g => g).ToList();
        GroupFilterOptions.Clear();
        GroupFilterOptions.Add("Alle");
        foreach (var g in groups) GroupFilterOptions.Add(g);
        // Restore selection (or fall back to "Alle" if the previous group no longer exists)
        SelectedGroupFilter = GroupFilterOptions.Contains(previousGroup) ? previousGroup : "Alle";
        SelectedStatusFilter = StatusFilterOptions.Contains(previousStatus) ? previousStatus : "Alle";

        // Determine which IDs are user entries for display
        var userIds = new HashSet<string>(
            _state.UserEntries.Select(u => u.Id), StringComparer.OrdinalIgnoreCase);

        // Rebuild entries
        Entries.Clear();
        foreach (var s in statusList.OrderBy(e => e.Group).ThenBy(e => e.System))
        {
            Entries.Add(new DatCatalogItemVm
            {
                Id = s.Id,
                Group = s.Group,
                System = s.System,
                ConsoleKey = s.ConsoleKey,
                Url = s.Url,
                Format = s.Format,
                Status = s.Status,
                DownloadStrategy = s.DownloadStrategy,
                InstalledDate = s.InstalledDate,
                LocalPath = s.LocalPath,
                FileSizeBytes = s.FileSizeBytes,
                IsUserEntry = userIds.Contains(s.Id)
            });
        }

        RefreshCounters();
        EntriesView.Refresh();
    }

    private void RefreshCounters()
    {
        TotalCount = Entries.Count;
        InstalledCount = Entries.Count(e => e.Status == DatInstallStatus.Installed);
        MissingCount = Entries.Count(e => e.Status == DatInstallStatus.Missing);
        StaleCount = Entries.Count(e => e.Status == DatInstallStatus.Stale);
        AutoCount = Entries.Count(e => e.DownloadStrategy == DatDownloadStrategy.Auto);
        OnPropertyChanged(nameof(HasData));
    }

    private bool ApplyFilter(object obj)
    {
        if (obj is not DatCatalogItemVm item) return false;

        // Group filter
        if (!string.Equals(_selectedGroupFilter, "Alle", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(item.Group, _selectedGroupFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        // Status filter
        if (!string.Equals(_selectedStatusFilter, "Alle", StringComparison.OrdinalIgnoreCase))
        {
            var statusMatch = _selectedStatusFilter switch
            {
                "Installiert" => item.Status == DatInstallStatus.Installed,
                "Fehlend" => item.Status == DatInstallStatus.Missing,
                "Veraltet" => item.Status == DatInstallStatus.Stale,
                _ => true
            };
            if (!statusMatch) return false;
        }

        // Text search
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            return item.System.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || item.Id.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || item.ConsoleKey.Contains(_searchText, StringComparison.OrdinalIgnoreCase)
                || item.Group.Contains(_searchText, StringComparison.OrdinalIgnoreCase);
        }

        return true;
    }
}

/// <summary>
/// Per-row ViewModel for a single DAT catalog entry. Mutable for UI binding updates.
/// </summary>
public sealed class DatCatalogItemVm : ObservableObject
{
    public string Id { get; init; } = "";
    public string Group { get; init; } = "";
    public string System { get; init; } = "";
    public string ConsoleKey { get; init; } = "";
    public string Url { get; init; } = "";
    public string Format { get; init; } = "";
    public bool IsUserEntry { get; init; }

    private DatInstallStatus _status;
    public DatInstallStatus Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusDisplay));
                OnPropertyChanged(nameof(ActionDisplay));
            }
        }
    }

    public DatDownloadStrategy DownloadStrategy { get; init; }

    private DateTime? _installedDate;
    public DateTime? InstalledDate
    {
        get => _installedDate;
        set => SetProperty(ref _installedDate, value);
    }

    private string? _localPath;
    public string? LocalPath
    {
        get => _localPath;
        set => SetProperty(ref _localPath, value);
    }

    public long? FileSizeBytes { get; init; }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => SetProperty(ref _isSelected, value);
    }

    public string StatusDisplay => Status switch
    {
        DatInstallStatus.Installed => "✓ Aktuell",
        DatInstallStatus.Stale => "⟳ Veraltet",
        DatInstallStatus.Missing => "✗ Fehlend",
        DatInstallStatus.Error => "⚠ Fehler",
        _ => "?"
    };

    public string ActionDisplay => (Status, DownloadStrategy) switch
    {
        (DatInstallStatus.Installed, _) => "Aktuell",
        (_, DatDownloadStrategy.Auto) => "Herunterladen",
        (_, DatDownloadStrategy.PackImport) => "Pack importieren",
        (_, DatDownloadStrategy.ManualLogin) => "Manuell (redump.org)",
        _ => ""
    };

    public string InstalledDateDisplay => InstalledDate?.ToString("yyyy-MM-dd") ?? "—";

    public string FileSizeDisplay => FileSizeBytes switch
    {
        null or 0 => "—",
        < 1024 => $"{FileSizeBytes} B",
        < 1024 * 1024 => $"{FileSizeBytes / 1024.0:F1} KB",
        _ => $"{FileSizeBytes / (1024.0 * 1024.0):F1} MB"
    };
}
