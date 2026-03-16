namespace RomCleanup.UI.Wpf.Models;

/// <summary>GUI-061: Navigation sidebar item.</summary>
public sealed class NavigationItem
{
    public required string Key { get; init; }
    public required string Icon { get; init; }
    public required string LabelKey { get; init; }
    public int Index { get; init; }
}
