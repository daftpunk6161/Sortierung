using System.Windows;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// WPF adapter for <see cref="IDialogService"/> — delegates to the static <see cref="DialogService"/>.
/// </summary>
public sealed class WpfDialogService : IDialogService
{
    public string? BrowseFolder(string title = "Ordner auswählen")
        => DialogService.BrowseFolder(title);

    public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*")
        => DialogService.BrowseFile(title, filter);

    public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null)
        => DialogService.SaveFile(title, filter, defaultFileName);

    public bool Confirm(string message, string title = "Bestätigung")
        => DialogService.Confirm(message, title);

    public void Info(string message, string title = "Information")
        => DialogService.Info(message, title);

    public void Error(string message, string title = "Fehler")
        => DialogService.Error(message, title);

    public ConfirmResult YesNoCancel(string message, string title = "Frage")
    {
        var result = DialogService.YesNoCancel(message, title);
        return result switch
        {
            MessageBoxResult.Yes => ConfirmResult.Yes,
            MessageBoxResult.No => ConfirmResult.No,
            _ => ConfirmResult.Cancel,
        };
    }

    public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "")
        => DialogService.ShowInputBox(prompt, title, defaultValue);

    public void ShowText(string title, string content)
        => DialogService.ShowText(title, content);

    public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen")
        => DialogService.DangerConfirm(title, message, confirmText, buttonLabel);

    public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals)
        => DialogService.ConfirmDatRenamePreview(renameProposals);
}
