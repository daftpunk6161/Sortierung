using System.Collections;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using Microsoft.Win32;

namespace Romulus.UI.Wpf;

/// <summary>
/// Reusable result dialog for displaying feature output.
/// Supports plain text view and tabular data (DataGrid) with Copy and Export.
/// Replaces the ad-hoc ShowTextDialog pattern (UX-005/RD-008).
/// </summary>
public partial class ResultDialog : Window
{
    private string _plainText = "";

    public ResultDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show the dialog with plain text content.</summary>
    public static void ShowText(string title, string content, Window? owner = null)
    {
        var dlg = new ResultDialog
        {
            Owner = owner ?? Application.Current?.MainWindow,
        };
        dlg.txtTitle.Text = title;
        dlg.Title = title;
        dlg._plainText = content;
        dlg.txtContent.Text = content;
        dlg.tabText.Visibility = Visibility.Visible;
        dlg.tabTable.Visibility = Visibility.Collapsed;
        dlg.tabContent.SelectedItem = dlg.tabText;
        // Hide tab headers when only one view
        dlg.tabContent.SetValue(TabControl.TabStripPlacementProperty, Dock.Bottom);
        dlg.ShowDialog();
    }

    /// <summary>Show the dialog with tabular data (auto-generates DataGrid columns).</summary>
    public static void ShowTable(string title, IEnumerable data, string? fallbackText = null, Window? owner = null)
    {
        var dlg = new ResultDialog
        {
            Owner = owner ?? Application.Current?.MainWindow,
        };
        dlg.txtTitle.Text = title;
        dlg.Title = title;
        dlg.gridContent.ItemsSource = data;
        dlg.tabTable.Visibility = Visibility.Visible;
        dlg.tabText.Visibility = Visibility.Collapsed;
        dlg.tabContent.SelectedItem = dlg.tabTable;
        dlg._plainText = fallbackText ?? "";
        dlg.tabContent.SetValue(TabControl.TabStripPlacementProperty, Dock.Bottom);
        dlg.ShowDialog();
    }

    /// <summary>Show the dialog with both text and table views.</summary>
    public static void ShowBoth(string title, string textContent, IEnumerable tableData, Window? owner = null)
    {
        var dlg = new ResultDialog
        {
            Owner = owner ?? Application.Current?.MainWindow,
        };
        dlg.txtTitle.Text = title;
        dlg.Title = title;
        dlg._plainText = textContent;
        dlg.txtContent.Text = textContent;
        dlg.gridContent.ItemsSource = tableData;
        dlg.tabText.Visibility = Visibility.Visible;
        dlg.tabTable.Visibility = Visibility.Visible;
        dlg.tabContent.SelectedItem = dlg.tabTable;
        dlg.ShowDialog();
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        var text = GetCurrentText();
        if (!string.IsNullOrEmpty(text))
        {
            Clipboard.SetText(text);
        }
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Ergebnis exportieren",
            Filter = "Textdatei|*.txt|CSV-Datei|*.csv|Alle Dateien|*.*",
            FileName = SanitizeFileName(txtTitle.Text) + ".txt"
        };

        if (dialog.ShowDialog(this) == true)
        {
            var text = GetCurrentText();
            File.WriteAllText(dialog.FileName, text);
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void OnClose(object sender, ExecutedRoutedEventArgs e)
    {
        Close();
    }

    private string GetCurrentText()
    {
        if (tabContent.SelectedItem == tabTable && gridContent.ItemsSource is not null)
        {
            return DataGridToText();
        }
        return _plainText;
    }

    private string DataGridToText()
    {
        var sb = new System.Text.StringBuilder();
        // Header row from columns
        var columns = gridContent.Columns;
        foreach (var col in columns)
        {
            if (sb.Length > 0) sb.Append('\t');
            sb.Append(col.Header?.ToString() ?? "");
        }
        sb.AppendLine();

        // Data rows
        foreach (var item in gridContent.ItemsSource)
        {
            var first = true;
            foreach (var col in columns)
            {
                if (!first) sb.Append('\t');
                first = false;
                if (col is System.Windows.Controls.DataGridBoundColumn boundCol
                    && boundCol.Binding is System.Windows.Data.Binding binding
                    && binding.Path?.Path is string path)
                {
                    var prop = item.GetType().GetProperty(path);
                    sb.Append(prop?.GetValue(item)?.ToString() ?? "");
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    private static string SanitizeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new char[name.Length];
        for (int i = 0; i < name.Length; i++)
        {
            sanitized[i] = Array.IndexOf(invalid, name[i]) >= 0 ? '_' : name[i];
        }
        return new string(sanitized);
    }
}
