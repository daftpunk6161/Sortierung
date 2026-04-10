using System.ComponentModel;

namespace Romulus.UI.Wpf.Models;

/// <summary>
/// GUI-028: Bindable region preference item replacing 16 individual bool properties.
/// Supports drag-reordering via Priority and IsActive toggling.
/// </summary>
public sealed class RegionItem : INotifyPropertyChanged
{
    public required string Code { get; init; }
    public required string DisplayName { get; init; }
    public string FlagEmoji { get; init; } = "";

    private bool _isActive;
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsActive)));
        }
    }

    private int _priority;
    /// <summary>Lower = higher priority. Used for drag-reordering in Region-Ranking UI.</summary>
    public int Priority
    {
        get => _priority;
        set
        {
            if (_priority == value) return;
            _priority = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Priority)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;
}
