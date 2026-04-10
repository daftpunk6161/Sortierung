using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// GUI-Phase4 Task 4.1: Visual Command Palette overlay (Ctrl+K).
/// Fuzzy search across all registered palette commands.
/// </summary>
public sealed partial class CommandPaletteViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;
    private Action<string>? _executeCommand;

    public CommandPaletteViewModel(ILocalizationService? loc = null, Action<string>? executeCommand = null)
    {
        _loc = loc ?? new LocalizationService();
        _executeCommand = executeCommand;
    }

    /// <summary>Wire external execute callback (e.g., from FeatureCommandService).</summary>
    public void SetExecuteCallback(Action<string> callback) => _executeCommand = callback;

    // ═══ STATE ══════════════════════════════════════════════════════════
    private bool _isOpen;
    public bool IsOpen
    {
        get => _isOpen;
        set
        {
            if (SetProperty(ref _isOpen, value) && value)
            {
                SearchText = "";
                SelectedIndex = 0;
                RefreshResults();
            }
        }
    }

    private string _searchText = "";
    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
                RefreshResults();
        }
    }

    private int _selectedIndex;
    public int SelectedIndex
    {
        get => _selectedIndex;
        set => SetProperty(ref _selectedIndex, value);
    }

    // ═══ RESULTS ════════════════════════════════════════════════════════
    public ObservableCollection<PaletteEntry> Results { get; } = [];

    private void RefreshResults()
    {
        var matches = FeatureService.SearchCommands(SearchText);
        Results.Clear();
        foreach (var (key, name, shortcut, score) in matches)
            Results.Add(new PaletteEntry(key, name, shortcut, score));
        if (Results.Count > 0) SelectedIndex = 0;
    }

    // ═══ COMMANDS ═══════════════════════════════════════════════════════
    [RelayCommand]
    private void Execute()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Results.Count)
        {
            var key = Results[SelectedIndex].Key;
            IsOpen = false;
            _executeCommand?.Invoke(key);
        }
    }

    [RelayCommand]
    private void MoveUp()
    {
        if (SelectedIndex > 0) SelectedIndex--;
    }

    [RelayCommand]
    private void MoveDown()
    {
        if (SelectedIndex < Results.Count - 1) SelectedIndex++;
    }

    [RelayCommand]
    private void Close()
    {
        IsOpen = false;
    }
}

/// <summary>Single entry in the Command Palette results list.</summary>
public sealed record PaletteEntry(string Key, string Name, string Shortcut, int Score);
