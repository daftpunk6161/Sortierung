using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Tests.TestFixtures;

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
    public List<string> ConfirmCalls { get; } = [];
    public List<string> DangerConfirmCalls { get; } = [];

    /// <summary>
    /// Sequenced responses for <see cref="DangerConfirm"/>. When non-empty, each
    /// call dequeues the next bool. When exhausted (or empty from the start),
    /// falls back to <see cref="DangerConfirmResult"/>. Lets tests model the
    /// chained Move-token + Lossy-token gate flow without per-test stub classes.
    /// </summary>
    public Queue<bool> DangerConfirmResponses { get; } = new();

    /// <summary>
    /// Full payload captured for every <see cref="DangerConfirm"/> call so tests
    /// can assert that, e.g., the Lossy gate passes the PendingLossyToken as
    /// confirmText (so the user must type that exact token, not "MOVE").
    /// </summary>
    public List<(string Title, string Message, string ConfirmText, string ButtonLabel)> DangerConfirmInvocations { get; } = [];

    public string? BrowseFolder(string title = "Ordner auswählen") => BrowseFolderResult;
    public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => BrowseFileResult;
    public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null)
        => SaveFileResult;
    public bool Confirm(string message, string title = "Bestätigung")
    {
        ConfirmCalls.Add(title);
        return ConfirmResult;
    }
    public void Info(string message, string title = "Information") => InfoMessages.Add(message);
    public void Error(string message, string title = "Fehler") => ErrorMessages.Add(message);
    public ConfirmResult YesNoCancel(string message, string title = "Frage") => YesNoCancelResult;
    public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => InputBoxResult;
    public void ShowText(string title, string content) => TextMessages.Add(content);
    public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen")
    {
        DangerConfirmCalls.Add(title);
        DangerConfirmInvocations.Add((title, message, confirmText, buttonLabel));
        if (DangerConfirmResponses.Count > 0)
            return DangerConfirmResponses.Dequeue();
        return DangerConfirmResult;
    }
    public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries)
        => ConfirmResult;
    public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => ConfirmDatRenameResult;
}
