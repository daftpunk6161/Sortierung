namespace RomCleanup.Contracts.Ports;

/// <summary>
/// Port interface for user-facing dialogs (file pickers, confirmations, input boxes).
/// WPF adapter in UI.Wpf/Services/WpfDialogService.cs.
/// </summary>
public interface IDialogService
{
    string? BrowseFolder(string title = "Ordner auswählen");
    string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*");
    string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null);
    bool Confirm(string message, string title = "Bestätigung");
    void Info(string message, string title = "Information");
    void Error(string message, string title = "Fehler");
    /// <summary>Returns Yes / No / Cancel as <see cref="ConfirmResult"/>.</summary>
    ConfirmResult YesNoCancel(string message, string title = "Frage");
    /// <summary>Themed input box. Returns user input, or empty string if cancelled.</summary>
    string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "");
    /// <summary>Show a large text result dialog with Copy/Export buttons.</summary>
    void ShowText(string title, string content);
    /// <summary>GUI-054: Danger-Confirm dialog requiring typed confirmation. Returns true only if confirmed.</summary>
    bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen");
    /// <summary>A-22: DatRename preview/confirm dialog. Returns true only when user confirms the rename proposals.</summary>
    bool ConfirmDatRenamePreview(IReadOnlyList<Models.DatAuditEntry> renameProposals);
}

/// <summary>Platform-neutral replacement for <c>System.Windows.MessageBoxResult</c>.</summary>
public enum ConfirmResult { Yes, No, Cancel }
