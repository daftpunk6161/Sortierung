using System.Windows;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// WPF adapter for <see cref="IDialogService"/> — delegates to the static <see cref="DialogService"/>.
/// </summary>
public sealed class WpfDialogService : IDialogService
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

    internal static string ResolveLocalizedDefault(string? value, string germanDefault, string localizationKey)
    {
        if (string.IsNullOrWhiteSpace(value)
            || string.Equals(value, germanDefault, StringComparison.Ordinal))
        {
            return FeatureService.GetLocalizedString(localizationKey, germanDefault);
        }

        return value;
    }

    public string? BrowseFolder(string title = "")
        => DialogService.BrowseFolder(
            ResolveLocalizedDefault(title, DefaultBrowseFolderTitle, "Dialog.BrowseFolder.FolderTitle"));

    public string? BrowseFile(string title = "", string filter = "")
        => DialogService.BrowseFile(
            ResolveLocalizedDefault(title, DefaultBrowseFileTitle, "Dialog.BrowseFile.Title"),
            ResolveLocalizedDefault(filter, DefaultAllFilesFilter, "Dialog.FileFilter.AllFiles"));

    public string? SaveFile(string title = "", string filter = "", string? defaultFileName = null)
        => DialogService.SaveFile(
            ResolveLocalizedDefault(title, DefaultSaveFileTitle, "Dialog.SaveFile.Title"),
            ResolveLocalizedDefault(filter, DefaultAllFilesFilter, "Dialog.FileFilter.AllFiles"),
            defaultFileName);

    public bool Confirm(string message, string title = "")
        => DialogService.Confirm(
            message,
            ResolveLocalizedDefault(title, DefaultConfirmTitle, "Dialog.Confirm.Title"));

    public void Info(string message, string title = "")
        => DialogService.Info(
            message,
            ResolveLocalizedDefault(title, DefaultInfoTitle, "Dialog.Info.Title"));

    public void Error(string message, string title = "")
        => DialogService.Error(
            message,
            ResolveLocalizedDefault(title, DefaultErrorTitle, "Dialog.Error.GenericTitle"));

    public ConfirmResult YesNoCancel(string message, string title = "")
    {
        var result = DialogService.YesNoCancel(
            message,
            ResolveLocalizedDefault(title, DefaultQuestionTitle, "Dialog.Question.Title"));
        return result switch
        {
            MessageBoxResult.Yes => ConfirmResult.Yes,
            MessageBoxResult.No => ConfirmResult.No,
            _ => ConfirmResult.Cancel,
        };
    }

    public string ShowInputBox(string prompt, string title = "", string defaultValue = "")
        => DialogService.ShowInputBox(
            prompt,
            ResolveLocalizedDefault(title, DefaultInputTitle, "Dialog.Input.Title"),
            defaultValue);

    public string ShowMultilineInputBox(string prompt, string title = "", string defaultValue = "")
        => DialogService.ShowMultilineInputBox(
            prompt,
            ResolveLocalizedDefault(title, DefaultInputTitle, "Dialog.Input.Title"),
            defaultValue);

    public void ShowText(string title, string content)
        => DialogService.ShowText(title, content);

    public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "")
        => DialogService.DangerConfirm(
            title,
            message,
            confirmText,
            ResolveLocalizedDefault(buttonLabel, DefaultConfirmButtonLabel, "Dialog.DangerConfirm.ButtonLabel"));

    public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries)
        => DialogService.ConfirmConversionReview(title, summary, entries);

    public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals)
        => DialogService.ConfirmDatRenamePreview(renameProposals);
}
