using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.UI.Avalonia.Services;

public sealed class SafeDialogBackend : IAvaloniaDialogBackend
{
    public string? BrowseFolder(string title) => null;

    public string? BrowseFile(string title, string filter) => null;

    public string? SaveFile(string title, string filter, string? defaultFileName) => null;

    public bool Confirm(string message, string title) => false;

    public void Info(string message, string title)
    {
    }

    public void Error(string message, string title)
    {
    }

    public ConfirmResult YesNoCancel(string message, string title) => ConfirmResult.Cancel;

    public string ShowInputBox(string prompt, string title, string defaultValue) => defaultValue;

    public string ShowMultilineInputBox(string prompt, string title, string defaultValue) => defaultValue;

    public void ShowText(string title, string content)
    {
    }

    public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel) => false;

    public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries) => false;

    public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => false;
}
