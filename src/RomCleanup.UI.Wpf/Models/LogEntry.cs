using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Single log entry for the Protokoll tab.
/// Level drives color via LogLevelToBrushConverter in XAML.
/// </summary>
public sealed record LogEntry(string Text, string Level);

/// <summary>
/// GUI-113: Shared ObservableObject base eliminates INotifyPropertyChanged boilerplate.
/// Bindable file-extension filter checkbox item (UX-004).
/// Category is used for visual grouping in the UI.
/// </summary>
public sealed partial class ExtensionFilterItem : ObservableObject
{
    public required string Extension { get; init; }
    public required string Category { get; init; }
    public required string ToolTip { get; init; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// Bindable console filter checkbox item.
/// Category is used for visual grouping (Sony, Nintendo, Sega, Andere).
/// </summary>
public sealed partial class ConsoleFilterItem : ObservableObject
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }

    [ObservableProperty]
    private bool _isChecked;
}

/// <summary>
/// Bindable tool/feature item for the Werkzeuge tab (RD-004).
/// Category is used for visual grouping and filtering.
/// </summary>
public sealed partial class ToolItem : ObservableObject
{
    public required string Key { get; init; }
    public required string DisplayName { get; init; }
    public required string Category { get; init; }
    public required string Description { get; init; }
    public required string Icon { get; init; }
    public bool RequiresRunResult { get; init; }

    /// <summary>Bound to the templated Button in the Werkzeuge tab. Set after FeatureCommands are registered.</summary>
    public System.Windows.Input.ICommand? Command { get; set; }

    [ObservableProperty]
    private bool _isVisible = true;

    /// <summary>User-pinned to the Quick Access bar.</summary>
    [ObservableProperty]
    private bool _isPinned;

    /// <summary>Timestamp of last use for "Recently Used" section.</summary>
    public DateTime? LastUsedAt { get; set; }

    /// <summary>True when RequiresRunResult is set but no run result is available yet.</summary>
    [ObservableProperty]
    private bool _isLocked;

    /// <summary>P1-004: True for features that are planned but not yet fully implemented.</summary>
    public bool IsPlanned { get; init; }
}

/// <summary>
/// Groups tool items by category for Expander-based display.
/// </summary>
public sealed partial class ToolCategory : ObservableObject
{
    public required string Name { get; init; }
    public required string Icon { get; init; }
    public ObservableCollection<ToolItem> Items { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;
}

/// <summary>
/// Bindable region priority item for the Region Priority Ranker (E2).
/// Position in the collection determines priority order.
/// </summary>
public sealed partial class RegionPriorityItem : ObservableObject
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public required string Group { get; init; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private int _position;
}
