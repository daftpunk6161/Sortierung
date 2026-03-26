using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using RomCleanup.Contracts.Models;
using RomCleanup.UI.Wpf.ViewModels;
using RomCleanup.UI.Wpf.Views;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Dialog helper service: folder pickers, file pickers, confirmation dialogs.
/// Port of WpfHost.ps1 dialog functions and WpfSlice.AdvancedFeatures.ps1 Show-WpfTextInputDialog.
/// All methods are thread-safe: calls from background threads are marshalled to the UI dispatcher.
/// </summary>
public static class DialogService
{
    /// <summary>Show a folder browser dialog and return the selected path, or null.</summary>
    public static string? BrowseFolder(string title = "Ordner auswählen", Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var dialog = new OpenFolderDialog
            {
                Title = title,
                Multiselect = false
            };
            var effectiveOwner = owner ?? GetMainWindow();
            return dialog.ShowDialog(effectiveOwner) == true ? dialog.FolderName : null;
        });
    }

    /// <summary>Show a file open dialog and return the selected path, or null.</summary>
    public static string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*", Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var dialog = new OpenFileDialog
            {
                Title = title,
                Filter = filter
            };
            var effectiveOwner = owner ?? GetMainWindow();
            return dialog.ShowDialog(effectiveOwner) == true ? dialog.FileName : null;
        });
    }

    /// <summary>Show a file save dialog and return the selected path, or null.</summary>
    public static string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null, Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var dialog = new SaveFileDialog
            {
                Title = title,
                Filter = filter,
                FileName = defaultFileName ?? ""
            };
            var effectiveOwner = owner ?? GetMainWindow();
            return dialog.ShowDialog(effectiveOwner) == true ? dialog.FileName : null;
        });
    }

    /// <summary>Show a themed confirmation dialog. Returns true if user confirmed.</summary>
    public static bool Confirm(string message, string title = "Bestätigung", Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var effectiveOwner = owner ?? GetMainWindow();
            var result = MessageDialog.Show(
                effectiveOwner,
                message, title,
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            (previousFocus as UIElement)?.Focus();
            return result == MessageBoxResult.Yes;
        });
    }

    /// <summary>Show a themed info message.</summary>
    public static void Info(string message, string title = "Information", Window? owner = null)
    {
        InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var effectiveOwner = owner ?? GetMainWindow();
            MessageDialog.Show(
                effectiveOwner,
                message, title,
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            (previousFocus as UIElement)?.Focus();
            return true; // dummy return for InvokeOnUiThread<T>
        });
    }

    /// <summary>Show a themed error message.</summary>
    public static void Error(string message, string title = "Fehler", Window? owner = null)
    {
        InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var effectiveOwner = owner ?? GetMainWindow();
            MessageDialog.Show(
                effectiveOwner,
                message, title,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            (previousFocus as UIElement)?.Focus();
            return true; // dummy return for InvokeOnUiThread<T>
        });
    }

    /// <summary>Show a themed Yes/No/Cancel question dialog. Returns the MessageBoxResult.</summary>
    public static MessageBoxResult YesNoCancel(string message, string title = "Frage", Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var effectiveOwner = owner ?? GetMainWindow();
            var result = MessageDialog.Show(
                effectiveOwner,
                message, title,
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Question);
            (previousFocus as UIElement)?.Focus();
            return result;
        });
    }

    /// <summary>
    /// GUI-053: Themed XAML-based input dialog replacing programmatic Window construction.
    /// Returns user input, or empty string if cancelled.
    /// </summary>
    public static string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "", Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var result = InputDialog.Show(prompt, title, defaultValue, owner ?? GetMainWindow());
            (previousFocus as UIElement)?.Focus();
            return result;
        });
    }

    /// <summary>
    /// GUI-054: Danger-Confirm dialog requiring typed confirmation text.
    /// Returns true only if user typed the confirmation and clicked confirm.
    /// </summary>
    public static bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen", Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var result = DangerConfirmDialog.Show(title, message, confirmText, buttonLabel, owner ?? GetMainWindow());
            (previousFocus as UIElement)?.Focus();
            return result;
        });
    }

    /// <summary>
    /// Shows a modal conversion review dialog for risky/manual conversion plans.
    /// Returns true only when explicitly confirmed.
    /// </summary>
    public static bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries, Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var dialog = new ConversionReviewDialog
            {
                Owner = owner ?? GetMainWindow()
            };

            var vm = new ConversionReviewViewModel(title, summary, entries, result => dialog.DialogResult = result);
            dialog.DataContext = vm;

            var result = dialog.ShowDialog() == true;
            (previousFocus as UIElement)?.Focus();
            return result;
        });
    }

    /// <summary>
    /// A-22: Shows a modal DatRename preview/confirm dialog.
    /// Returns true only when the user explicitly confirms the rename proposals.
    /// </summary>
    public static bool ConfirmDatRenamePreview(IReadOnlyList<RomCleanup.Contracts.Models.DatAuditEntry> renameProposals, Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var displayItems = renameProposals
                .Select(e => new DatRenameDisplayItem(
                    System.IO.Path.GetFileName(e.FilePath),
                    e.DatRomFileName!,
                    e.ConsoleKey,
                    e.DatGameName,
                    e.Confidence))
                .ToList();

            var dialog = new DatRenameReviewDialog
            {
                Owner = owner ?? GetMainWindow()
            };

            var vm = new DatRenameReviewViewModel(displayItems, result => dialog.DialogResult = result);
            dialog.DataContext = vm;

            var result = dialog.ShowDialog() == true;
            (previousFocus as UIElement)?.Focus();
            return result;
        });
    }

    /// <summary>Show a themed ResultDialog with text content (Copy/Export buttons).</summary>
    public static void ShowText(string title, string content)
    {
        InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            ResultDialog.ShowText(title, content, GetMainWindow());
            (previousFocus as UIElement)?.Focus();
            return true;
        });
    }

    /// <summary>
    /// Marshal a function call to the WPF UI thread if the current thread is not the dispatcher thread.
    /// If already on the UI thread, executes directly.
    /// </summary>
    private static T InvokeOnUiThread<T>(Func<T> action)
    {
        var app = Application.Current;
        if (app is null)
            return action();

        var dispatcher = app.Dispatcher;
        if (dispatcher.CheckAccess())
            return action();

        return dispatcher.Invoke(action);
    }

    /// <summary>
    /// Safely retrieve the main window, returning null if not available.
    /// Must be called on the UI thread.
    /// </summary>
    private static Window? GetMainWindow()
    {
        try
        {
            var w = Application.Current?.MainWindow;
            if (w is not null && w.IsLoaded && w.IsVisible)
                return w;
            return null;
        }
        catch { return null; }
    }
}
