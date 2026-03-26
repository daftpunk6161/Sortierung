using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Tests.TestFixtures;

/// <summary>
/// Stub IDialogService for headless unit testing of ViewModel logic.
/// </summary>
internal sealed class StubDialogService : IDialogService
{
    public bool ConfirmResult { get; set; }
    public ConfirmResult YesNoCancelResult { get; set; } = Contracts.Ports.ConfirmResult.Yes;
    public string InputBoxResult { get; set; } = "";
    public string? BrowseFolderResult { get; set; }
    public string? BrowseFileResult { get; set; }
    public string? SaveFileResult { get; set; }
    public bool DangerConfirmResult { get; set; }
    public bool ConfirmDatRenameResult { get; set; }

    public List<string> InfoMessages { get; } = [];
    public List<string> ErrorMessages { get; } = [];
    public List<string> TextMessages { get; } = [];

    public string? BrowseFolder(string title = "Ordner auswählen") => BrowseFolderResult;
    public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => BrowseFileResult;
    public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null)
        => SaveFileResult;
    public bool Confirm(string message, string title = "Bestätigung") => ConfirmResult;
    public void Info(string message, string title = "Information") => InfoMessages.Add(message);
    public void Error(string message, string title = "Fehler") => ErrorMessages.Add(message);
    public ConfirmResult YesNoCancel(string message, string title = "Frage") => YesNoCancelResult;
    public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => InputBoxResult;
    public void ShowText(string title, string content) => TextMessages.Add(content);
    public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen")
        => DangerConfirmResult;
    public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => ConfirmDatRenameResult;
}
