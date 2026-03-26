using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// TASK-125: Conversion preview child ViewModel — shows pending conversion items
/// and a summary of the planned conversions before execution.
/// </summary>
public sealed class ConversionPreviewViewModel : ObservableObject
{
    private readonly ILocalizationService _loc;

    public ConversionPreviewViewModel(ILocalizationService? loc = null)
    {
        _loc = loc ?? new LocalizationService();
    }

    public ObservableCollection<ConversionPreviewItem> Items { get; } = [];

    public bool HasItems => Items.Count > 0;

    private string _summaryText = "";
    public string SummaryText
    {
        get => _summaryText;
        set => SetProperty(ref _summaryText, value);
    }

    public void Clear()
    {
        Items.Clear();
        SummaryText = "";
        OnPropertyChanged(nameof(HasItems));
    }

    public void Load(IEnumerable<ConversionPreviewItem> items)
    {
        Items.Clear();
        foreach (var item in items)
            Items.Add(item);
        OnPropertyChanged(nameof(HasItems));
    }
}

public sealed record ConversionPreviewItem(
    string FileName,
    string SourceFormat,
    string TargetFormat,
    string Tool,
    long FileSize);
