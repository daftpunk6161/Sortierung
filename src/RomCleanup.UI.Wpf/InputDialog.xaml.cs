using System.Windows;
using System.Windows.Input;

namespace RomCleanup.UI.Wpf;

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
