using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.UI.Avalonia.Services;

public interface IAvaloniaDialogBackend
{
    string? BrowseFolder(string title);

    string? BrowseFile(string title, string filter);

    string? SaveFile(string title, string filter, string? defaultFileName);

    bool Confirm(string message, string title);

    void Info(string message, string title);

    void Error(string message, string title);

    ConfirmResult YesNoCancel(string message, string title);

    string ShowInputBox(string prompt, string title, string defaultValue);

    string ShowMultilineInputBox(string prompt, string title, string defaultValue);

    void ShowText(string title, string content);

    bool DangerConfirm(string title, string message, string confirmText, string buttonLabel);

    bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries);

    bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals);
}
