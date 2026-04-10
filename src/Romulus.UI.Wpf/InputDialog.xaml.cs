using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Romulus.UI.Wpf;

public partial class InputDialog : Window
{
    public string Result { get; private set; } = "";

    private InputDialog()
    {
        InitializeComponent();
    }

    public static string Show(string prompt, string title = "Eingabe", string defaultValue = "", Window? owner = null)
    {
        var dlg = new InputDialog
        {
            Title = title,
            Owner = owner
        };
        dlg.txtPrompt.Text = prompt;
        dlg.txtInput.Text = defaultValue;
        dlg.txtInput.SelectAll();

        return dlg.ShowDialog() == true ? dlg.Result : "";
    }

    public static string ShowMultiline(string prompt, string title = "Eingabe", string defaultValue = "", Window? owner = null)
    {
        var dlg = new InputDialog
        {
            Title = title,
            Owner = owner,
            Width = 820,
            Height = 580,
            MinWidth = 540,
            MinHeight = 360,
            ResizeMode = ResizeMode.CanResize
        };

        dlg.txtPrompt.Text = prompt;
        dlg.txtInput.Text = defaultValue;
        dlg.txtInput.AcceptsReturn = true;
        dlg.txtInput.AcceptsTab = true;
        dlg.txtInput.TextWrapping = TextWrapping.NoWrap;
        dlg.txtInput.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        dlg.txtInput.HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        dlg.txtInput.MinHeight = 280;
        dlg.txtInput.Select(0, 0);

        return dlg.ShowDialog() == true ? dlg.Result : "";
    }

    private void OnOk(object sender, RoutedEventArgs e)
    {
        Result = txtInput.Text;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
    }

    private void OnCloseCommand(object sender, ExecutedRoutedEventArgs e)
    {
        DialogResult = false;
    }
}
