using System.Windows;
using Microsoft.Win32;

namespace RomCleanup.UI.Wpf.Services;

/// <summary>
/// Dialog helper service: folder pickers, file pickers, confirmation dialogs.
/// Port of WpfHost.ps1 dialog functions and WpfSlice.AdvancedFeatures.ps1 Show-WpfTextInputDialog.
/// </summary>
public static class DialogService
{
    /// <summary>Show a folder browser dialog and return the selected path, or null.</summary>
    public static string? BrowseFolder(string title = "Ordner auswählen", Window? owner = null)
    {
        var dialog = new OpenFolderDialog
        {
            Title = title,
            Multiselect = false
        };
        return dialog.ShowDialog(owner) == true ? dialog.FolderName : null;
    }

    /// <summary>Show a file open dialog and return the selected path, or null.</summary>
    public static string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*", Window? owner = null)
    {
        var dialog = new OpenFileDialog
        {
            Title = title,
            Filter = filter
        };
        return dialog.ShowDialog(owner) == true ? dialog.FileName : null;
    }

    /// <summary>Show a confirmation dialog. Returns true if user confirmed.</summary>
    public static bool Confirm(string message, string title = "Bestätigung", Window? owner = null)
    {
        var result = MessageBox.Show(
            owner ?? Application.Current.MainWindow,
            message, title,
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);
        return result == MessageBoxResult.Yes;
    }

    /// <summary>Show an info message.</summary>
    public static void Info(string message, string title = "Information", Window? owner = null)
    {
        MessageBox.Show(
            owner ?? Application.Current.MainWindow,
            message, title,
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    /// <summary>Show an error message.</summary>
    public static void Error(string message, string title = "Fehler", Window? owner = null)
    {
        MessageBox.Show(
            owner ?? Application.Current.MainWindow,
            message, title,
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
