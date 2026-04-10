using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Romulus.Contracts.Models;

namespace Romulus.UI.Wpf.ViewModels;

public sealed partial class ConversionReviewViewModel : ObservableObject
{
    private readonly Action<bool?> _close;

    public ConversionReviewViewModel(
        string title,
        string summary,
        IReadOnlyList<ConversionReviewEntry> entries,
        Action<bool?> close)
    {
        Title = title;
        Summary = summary;
        Entries = new ObservableCollection<ConversionReviewEntry>(entries);
        _close = close;
    }

    public string Title { get; }

    public string Summary { get; }

    public ObservableCollection<ConversionReviewEntry> Entries { get; }

    public string CountLabel => $"{Entries.Count} Datei(en) benötigen manuelle Bestätigung.";

    [RelayCommand]
    private void Confirm()
    {
        _close(true);
    }

    [RelayCommand]
    private void Cancel()
    {
        _close(false);
    }
}
