using System.Windows;
using System.Windows.Input;

namespace Romulus.UI.Wpf;

public partial class DangerConfirmDialog : Window
{
    private string _confirmText = "";

    private DangerConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// GUI-054: Danger-Confirm dialog. User must type confirmText to enable the confirm button.
    /// Returns true only if the user confirmed.
    /// F-03: hint and default button label are localized via FeatureService.
    /// </summary>
    public static bool Show(string title, string message, string confirmText, string buttonLabel = "", Window? owner = null)
    {
        // Guard: don't set Owner if the window is closing or not visible
        if (owner is not null && (!owner.IsLoaded || !owner.IsVisible))
            owner = null;

        var dlg = new DangerConfirmDialog();
        if (owner is not null)
            dlg.Owner = owner;
        else
            dlg.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        dlg._confirmText = confirmText;
        dlg.txtTitle.Text = title;
        dlg.txtMessage.Text = message;

        var hintFormat = Services.FeatureService.GetLocalizedString(
            "Dialog.DangerConfirm.HintFormat",
            "Gib \"{0}\" ein um fortzufahren:");
        dlg.txtConfirmHint.Text = string.Format(System.Globalization.CultureInfo.CurrentCulture, hintFormat, confirmText);

        var resolvedButtonLabel = string.IsNullOrWhiteSpace(buttonLabel)
            ? Services.FeatureService.GetLocalizedString("Dialog.DangerConfirm.DefaultButtonLabel", "Bestätigen")
            : buttonLabel;
        dlg.txtBtnLabel.Text = resolvedButtonLabel;

        return dlg.ShowDialog() == true;
    }

    private void OnConfirmTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        btnConfirm.IsEnabled = string.Equals(txtConfirmInput.Text.Trim(), _confirmText, System.StringComparison.Ordinal);
    }

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
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
