using CommunityToolkit.Mvvm.ComponentModel;

namespace Romulus.UI.Wpf.Models;

/// <summary>
/// T-W4-REVIEW-INBOX: row shown in one of the three Inbox lanes
/// (Safe/Review/Blocked). Selection is bindable so XAML checkboxes can
/// drive bulk-actions in the view-model.
/// </summary>
public sealed partial class ReviewInboxRow : ObservableObject
{
    public string FileName { get; }
    public string ConsoleKey { get; }
    public string GameKey { get; }
    public string SortDecision { get; }
    public string MatchLevel { get; }
    public string Reason { get; }

    [ObservableProperty]
    private bool _isSelected;

    public ReviewInboxRow(
        string fileName,
        string consoleKey,
        string gameKey,
        string sortDecision,
        string matchLevel,
        string reason)
    {
        FileName = fileName;
        ConsoleKey = consoleKey;
        GameKey = gameKey;
        SortDecision = sortDecision;
        MatchLevel = matchLevel;
        Reason = reason;
    }
}
