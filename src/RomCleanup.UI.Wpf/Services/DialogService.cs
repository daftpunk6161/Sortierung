using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;

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
    /// Themed input dialog replacing Microsoft.VisualBasic.Interaction.InputBox.
    /// Returns user input, or empty string if cancelled.
    /// </summary>
    public static string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "", Window? owner = null)
    {
        return InvokeOnUiThread(() =>
        {
            var previousFocus = Keyboard.FocusedElement;
            var dlg = new Window
            {
                Title = title,
                Width = 420,
                Height = 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = owner ?? GetMainWindow(),
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Background = Application.Current.TryFindResource("BrushBackground") as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.White,
            };

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock
            {
                Text = prompt,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 8),
                Foreground = Application.Current.TryFindResource("BrushTextPrimary") as System.Windows.Media.Brush
                    ?? System.Windows.Media.Brushes.Black,
            };
            Grid.SetRow(label, 0);

            var textBox = new TextBox
            {
                Text = defaultValue,
                Margin = new Thickness(0, 0, 0, 12),
                Padding = new Thickness(6, 4, 6, 4),
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
            };
            Grid.SetRow(textBox, 1);

            var buttonPanel = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            var btnOk = new Button { Content = "OK", Width = 80, Margin = new Thickness(0, 0, 8, 0), IsDefault = true };
            var btnCancel = new Button { Content = "Abbrechen", Width = 80, IsCancel = true };
            buttonPanel.Children.Add(btnOk);
            buttonPanel.Children.Add(btnCancel);
            Grid.SetRow(buttonPanel, 2);

            string result = "";
            btnOk.Click += (_, _) => { result = textBox.Text; dlg.DialogResult = true; };
            btnCancel.Click += (_, _) => { dlg.DialogResult = false; };

            grid.Children.Add(label);
            grid.Children.Add(textBox);
            grid.Children.Add(buttonPanel);
            dlg.Content = grid;

            textBox.SelectAll();
            textBox.Focus();

            dlg.ShowDialog();
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
        try { return Application.Current?.MainWindow; }
        catch { return null; }
    }
}
