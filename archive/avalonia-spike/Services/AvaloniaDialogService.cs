using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.UI.Avalonia.Services;

public sealed class AvaloniaDialogService : IDialogService
{
    private const string DefaultBrowseFolderTitle = "Ordner auswählen";
    private const string DefaultBrowseFileTitle = "Datei auswählen";
    private const string DefaultSaveFileTitle = "Speichern unter";
    private const string DefaultAllFilesFilter = "Alle Dateien|*.*";
    private const string DefaultConfirmTitle = "Bestätigung";
    private const string DefaultInfoTitle = "Information";
    private const string DefaultErrorTitle = "Fehler";
    private const string DefaultQuestionTitle = "Frage";
    private const string DefaultInputTitle = "Eingabe";
    private const string DefaultConfirmButtonLabel = "Bestätigen";

    private readonly IAvaloniaDialogBackend _backend;

    public AvaloniaDialogService(IAvaloniaDialogBackend? backend = null)
    {
        _backend = backend ?? new SafeDialogBackend();
    }

    public string? BrowseFolder(string title = "")
        => _backend.BrowseFolder(ResolveOrDefault(title, DefaultBrowseFolderTitle));

    public string? BrowseFile(string title = "", string filter = "")
        => _backend.BrowseFile(
            ResolveOrDefault(title, DefaultBrowseFileTitle),
            ResolveOrDefault(filter, DefaultAllFilesFilter));

    public string? SaveFile(string title = "", string filter = "", string? defaultFileName = null)
        => _backend.SaveFile(
            ResolveOrDefault(title, DefaultSaveFileTitle),
            ResolveOrDefault(filter, DefaultAllFilesFilter),
            defaultFileName);

    public bool Confirm(string message, string title = "")
        => _backend.Confirm(message, ResolveOrDefault(title, DefaultConfirmTitle));

    public void Info(string message, string title = "")
        => _backend.Info(message, ResolveOrDefault(title, DefaultInfoTitle));

    public void Error(string message, string title = "")
        => _backend.Error(message, ResolveOrDefault(title, DefaultErrorTitle));

    public ConfirmResult YesNoCancel(string message, string title = "")
        => _backend.YesNoCancel(message, ResolveOrDefault(title, DefaultQuestionTitle));

    public string ShowInputBox(string prompt, string title = "", string defaultValue = "")
        => _backend.ShowInputBox(prompt, ResolveOrDefault(title, DefaultInputTitle), defaultValue);

    public string ShowMultilineInputBox(string prompt, string title = "", string defaultValue = "")
        => _backend.ShowMultilineInputBox(prompt, ResolveOrDefault(title, DefaultInputTitle), defaultValue);

    public void ShowText(string title, string content)
        => _backend.ShowText(title, content);

    public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "")
        => _backend.DangerConfirm(
            title,
            message,
            confirmText,
            ResolveOrDefault(buttonLabel, DefaultConfirmButtonLabel));

    public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries)
        => _backend.ConfirmConversionReview(title, summary, entries);

    public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals)
        => _backend.ConfirmDatRenamePreview(renameProposals);

    private static string ResolveOrDefault(string value, string fallback)
        => string.IsNullOrWhiteSpace(value) ? fallback : value;
}
