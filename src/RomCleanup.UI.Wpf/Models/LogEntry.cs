using System.Collections.ObjectModel;
using System.ComponentModel;

namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Single log entry for the Protokoll tab.
/// Level drives color via LogLevelToBrushConverter in XAML.
/// </summary>
public sealed record LogEntry(string Text, string Level);

/// <summary>
/// Bindable file-extension filter checkbox item (UX-004).
/// Category is used for visual grouping in the UI.
/// </summary>
public sealed class ExtensionFilterItem : INotifyPropertyChanged
{
    public required string Extension { get; init; }
    public required string Category { get; init; }
    public required string ToolTip { get; init; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Bindable console filter checkbox item.
/// Category is used for visual grouping (Sony, Nintendo, Sega, Andere).
/// </summary>
public sealed class ConsoleFilterItem : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }

    private bool _isChecked;
    public bool IsChecked
    {
        get => _isChecked;
        set
        {
            if (_isChecked == value) return;
            _isChecked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsChecked)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Bindable tool/feature item for the Werkzeuge tab (RD-004).
/// Category is used for visual grouping and filtering.
/// </summary>
public sealed class ToolItem : INotifyPropertyChanged
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public bool RequiresRunResult { get; init; }

    /// <summary>Bound to the templated Button in the Werkzeuge tab. Set after FeatureCommands are registered.</summary>
    public System.Windows.Input.ICommand? Command { get; set; }

    private bool _isVisible = true;
    public bool IsVisible
    {
        get => _isVisible;
        set
        {
            if (_isVisible == value) return;
            _isVisible = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsVisible)));
        }
    }

    private bool _isPinned;
    /// <summary>User-pinned to the Quick Access bar.</summary>
    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (_isPinned == value) return;
            _isPinned = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsPinned)));
        }
    }

    /// <summary>Timestamp of last use for "Recently Used" section.</summary>
    public DateTime? LastUsedAt { get; set; }

    private bool _isLocked;
    /// <summary>True when RequiresRunResult is set but no run result is available yet.</summary>
    public bool IsLocked
    {
        get => _isLocked;
        set
        {
            if (_isLocked == value) return;
            _isLocked = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsLocked)));
        }
    }

    /// <summary>P1-004: True for features that are planned but not yet fully implemented.</summary>
    public bool IsPlanned { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>
/// Groups tool items by category for Expander-based display.
/// </summary>
public sealed class ToolCategory : INotifyPropertyChanged
{
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public ObservableCollection<ToolItem> Items { get; } = [];

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            if (_isExpanded == value) return;
            _isExpanded = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsExpanded)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
