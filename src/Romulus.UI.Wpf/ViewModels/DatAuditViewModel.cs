using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Safety;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// TASK-051: ViewModel for the read-only DatAudit tab.
/// Displays DatAuditEntry rows with filtering by status and console.
/// </summary>
public sealed partial class DatAuditViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;
    private readonly IDialogService _dialog;
    private readonly object _entriesLock = new();

    public DatAuditViewModel(ILocalizationService? loc = null, IDialogService? dialog = null)
    {
        _loc = loc ?? new LocalizationService();
        _dialog = dialog ?? new WpfDialogService();
        BindingOperations.EnableCollectionSynchronization(Entries, _entriesLock);
        BindingOperations.EnableCollectionSynchronization(ConsoleFilterOptions, _entriesLock);
        EntriesView = CollectionViewSource.GetDefaultView(Entries);
        EntriesView.Filter = ApplyFilter;
    }

    // ═══ DATA ════════════════════════════════════════════════════════════
    public ObservableCollection<DatAuditEntry> Entries { get; } = [];
    public ICollectionView EntriesView { get; }

    // ═══ SUMMARY COUNTERS ═══════════════════════════════════════════════
    private int _haveCount;
    public int HaveCount { get => _haveCount; private set => SetProperty(ref _haveCount, value); }

    private int _haveWrongNameCount;
    public int HaveWrongNameCount { get => _haveWrongNameCount; private set => SetProperty(ref _haveWrongNameCount, value); }

    private int _missCount;
    public int MissCount { get => _missCount; private set => SetProperty(ref _missCount, value); }

    private int _unknownCount;
    public int UnknownCount { get => _unknownCount; private set => SetProperty(ref _unknownCount, value); }

    private int _ambiguousCount;
    public int AmbiguousCount { get => _ambiguousCount; private set => SetProperty(ref _ambiguousCount, value); }

    private int _totalCount;
    public int TotalCount { get => _totalCount; private set => SetProperty(ref _totalCount, value); }

    public bool HasData => TotalCount > 0;

    // ═══ FILTER ═════════════════════════════════════════════════════════
    public ObservableCollection<string> StatusFilterOptions { get; } = ["Alle", "Have", "Wrong Name", "Miss", "Unknown", "Ambiguous"];
    public ObservableCollection<string> ConsoleFilterOptions { get; } = ["Alle"];

    private string _selectedStatusFilter = "Alle";
    public string SelectedStatusFilter
    {
        get => _selectedStatusFilter;
        set
        {
            if (SetProperty(ref _selectedStatusFilter, value))
                EntriesView.Refresh();
        }
    }

    private string _selectedConsoleFilter = "Alle";
    public string SelectedConsoleFilter
    {
        get => _selectedConsoleFilter;
        set
        {
            if (SetProperty(ref _selectedConsoleFilter, value))
                EntriesView.Refresh();
        }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                EntriesView.Refresh();
        }
    }

    // ═══ RENAME ELIGIBILITY ═════════════════════════════════════════════
    public bool HasRenameEligibleEntries => HaveWrongNameCount > 0;

    // ═══ METHODS ════════════════════════════════════════════════════════

    /// <summary>Loads DatAudit result into the ViewModel. Call from UI thread.</summary>
    public void LoadResult(DatAuditResult? result)
    {
        Entries.Clear();
        ConsoleFilterOptions.Clear();
        ConsoleFilterOptions.Add("Alle");

        if (result is null || result.Entries.Count == 0)
        {
            HaveCount = 0;
            HaveWrongNameCount = 0;
            MissCount = 0;
            UnknownCount = 0;
            AmbiguousCount = 0;
            TotalCount = 0;
            OnPropertyChanged(nameof(HasData));
            OnPropertyChanged(nameof(HasRenameEligibleEntries));
            return;
        }

        foreach (var entry in result.Entries)
            Entries.Add(entry);

        HaveCount = result.HaveCount;
        HaveWrongNameCount = result.HaveWrongNameCount;
        MissCount = result.MissCount;
        UnknownCount = result.UnknownCount;
        AmbiguousCount = result.AmbiguousCount;
        TotalCount = result.Entries.Count;

        // Build console filter options from distinct consoles
        var consoles = result.Entries
            .Select(e => e.ConsoleKey)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(c => c, StringComparer.OrdinalIgnoreCase);

        foreach (var console in consoles)
            ConsoleFilterOptions.Add(console);

        SelectedStatusFilter = "Alle";
        SelectedConsoleFilter = "Alle";
        SearchText = "";

        OnPropertyChanged(nameof(HasData));
        OnPropertyChanged(nameof(HasRenameEligibleEntries));
    }

    /// <summary>Exports current (filtered) entries as CSV to the specified path.</summary>
    [RelayCommand]
    private void ExportCsv()
    {
        var path = _dialog.SaveFile(
            "DatAudit CSV exportieren",
            "CSV-Dateien (*.csv)|*.csv|Alle Dateien|*.*",
            "dat-audit.csv");

        if (string.IsNullOrEmpty(path))
            return;

        try
        {
            var safePath = SafetyValidator.EnsureSafeOutputPath(path);
            using var writer = new StreamWriter(safePath, false, System.Text.Encoding.UTF8);
            writer.WriteLine("FilePath,Hash,Status,DatGameName,DatRomFileName,ConsoleKey,Confidence");

            foreach (var entry in EntriesView.Cast<DatAuditEntry>())
            {
                writer.WriteLine(string.Join(",",
                    CsvEscape(entry.FilePath),
                    CsvEscape(entry.Hash),
                    entry.Status,
                    CsvEscape(entry.DatGameName ?? ""),
                    CsvEscape(entry.DatRomFileName ?? ""),
                    CsvEscape(entry.ConsoleKey),
                    entry.Confidence));
            }
        }
        catch (InvalidOperationException ex)
        {
            _dialog.Error($"CSV-Export blockiert:\n\n{ex.Message}", "DatAudit CSV exportieren");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _dialog.Error($"CSV-Export fehlgeschlagen:\n\n{ex.Message}", "DatAudit CSV exportieren");
        }
    }

    // ═══ PRIVATE ════════════════════════════════════════════════════════

    private bool ApplyFilter(object obj)
    {
        if (obj is not DatAuditEntry entry)
            return false;

        // Status filter
        if (_selectedStatusFilter != "Alle")
        {
            var matchStatus = _selectedStatusFilter switch
            {
                "Have" => DatAuditStatus.Have,
                "Wrong Name" => DatAuditStatus.HaveWrongName,
                "Miss" => DatAuditStatus.Miss,
                "Unknown" => DatAuditStatus.Unknown,
                "Ambiguous" => DatAuditStatus.Ambiguous,
                _ => (DatAuditStatus?)null
            };

            if (matchStatus.HasValue && entry.Status != matchStatus.Value)
                return false;
        }

        // Console filter
        if (_selectedConsoleFilter != "Alle"
            && !string.Equals(entry.ConsoleKey, _selectedConsoleFilter, StringComparison.OrdinalIgnoreCase))
            return false;

        // Text search
        if (!string.IsNullOrWhiteSpace(_searchText))
        {
            var needle = _searchText;
            if (!entry.FilePath.Contains(needle, StringComparison.OrdinalIgnoreCase)
                && !(entry.DatGameName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                && !(entry.DatRomFileName?.Contains(needle, StringComparison.OrdinalIgnoreCase) ?? false)
                && !entry.ConsoleKey.Contains(needle, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        return true;
    }

    private static string CsvEscape(string value)
    {
        if (string.IsNullOrEmpty(value))
            return "\"\"";

        // CSV-Injection prevention: prefix formula characters
        var sanitized = value;
        if (sanitized.Length > 0 && sanitized[0] is '=' or '+' or '-' or '@' or '\t' or '\r')
            sanitized = "'" + sanitized;

        if (sanitized.Contains('"') || sanitized.Contains(',') || sanitized.Contains('\n'))
            return "\"" + sanitized.Replace("\"", "\"\"") + "\"";

        return sanitized;
    }
}
