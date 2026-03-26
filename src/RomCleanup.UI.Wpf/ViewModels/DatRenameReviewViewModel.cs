using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RomCleanup.Contracts.Models;

namespace RomCleanup.UI.Wpf.ViewModels;

/// <summary>
/// A-22: ViewModel for the DatRename preview/confirm dialog.
/// Shows rename proposals and allows the user to confirm or cancel.
/// </summary>
public sealed partial class DatRenameReviewViewModel : ObservableObject
{
    private readonly Action<bool?> _close;

    public DatRenameReviewViewModel(
        IReadOnlyList<DatRenameDisplayItem> proposals,
        Action<bool?> close)
    {
        Proposals = new ObservableCollection<DatRenameDisplayItem>(proposals);
        _close = close;
    }

    public string Title => "DAT-Rename Vorschau";

    public string Summary =>
        $"{Proposals.Count} Datei(en) werden gemäß DAT-Kanonname umbenannt.\n" +
        "Überprüfe die Änderungen und bestätige mit dem Button unten.";

    public string CountLabel => $"{Proposals.Count} Umbenennung(en) geplant.";

    public ObservableCollection<DatRenameDisplayItem> Proposals { get; }

    [RelayCommand]
    private void Confirm() => _close(true);

    [RelayCommand]
    private void Cancel() => _close(false);
}

/// <summary>Display item for DatRename preview grid.</summary>
public sealed record DatRenameDisplayItem(
    string CurrentFileName,
    string ProposedFileName,
    string ConsoleKey,
    string? DatGameName,
    int Confidence);
