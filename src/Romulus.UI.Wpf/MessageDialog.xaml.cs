using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace Romulus.UI.Wpf;

/// <summary>
/// Themed message dialog replacing raw MessageBox.Show (TASK-095).
/// Uses DynamicResource brushes so it adapts to Dark/Light themes.
/// </summary>
public partial class MessageDialog : Window
{
    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private MessageDialog()
    {
        InitializeComponent();
    }

    /// <summary>Show a themed message dialog.</summary>
    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string title,
        MessageBoxButton buttons,
        MessageBoxImage image)
    {
        var dlg = new MessageDialog
        {
            Title = title,
            Owner = owner,
        };

        dlg.txtMessage.Text = message;
        dlg.txtIcon.Text = image switch
        {
            MessageBoxImage.Question => "\uE897",     // Help/question icon
            MessageBoxImage.Warning => "\uE7BA",      // Warning icon
            MessageBoxImage.Error => "\uEA39",        // Error icon
            MessageBoxImage.Information => "\uE946",   // Info icon
            _ => "\uE946"
        };

        dlg.txtIcon.Foreground = image switch
        {
            MessageBoxImage.Error => Application.Current.TryFindResource("BrushDanger") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Red,
            MessageBoxImage.Warning => Application.Current.TryFindResource("BrushWarning") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.Orange,
            _ => Application.Current.TryFindResource("BrushAccentCyan") as System.Windows.Media.Brush
                ?? System.Windows.Media.Brushes.DeepSkyBlue,
        };

        dlg.AddButtons(buttons);
        dlg.ShowDialog();
        return dlg.Result;
    }

    private void AddButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("OK", MessageBoxResult.OK, isDefault: true);
                break;
            case MessageBoxButton.OKCancel:
                AddButton("OK", MessageBoxResult.OK, isDefault: true);
                AddButton("Abbrechen", MessageBoxResult.Cancel, isCancel: true);
                break;
            case MessageBoxButton.YesNo:
                AddButton("Ja", MessageBoxResult.Yes, isDefault: true);
                AddButton("Nein", MessageBoxResult.No, isCancel: true);
                break;
            case MessageBoxButton.YesNoCancel:
                AddButton("Ja", MessageBoxResult.Yes, isDefault: true);
                AddButton("Nein", MessageBoxResult.No);
                AddButton("Abbrechen", MessageBoxResult.Cancel, isCancel: true);
                break;
        }
    }

    private void AddButton(string content, MessageBoxResult result, bool isDefault = false, bool isCancel = false)
    {
        var btn = new Button
        {
            Content = content,
            MinWidth = 80,
            Padding = new Thickness(12, 6, 12, 6),
            Margin = new Thickness(8, 0, 0, 0),
            IsDefault = isDefault,
            IsCancel = isCancel,
        };
        btn.Click += (_, _) =>
        {
            Result = result;
            DialogResult = result != MessageBoxResult.Cancel;
        };
        panelButtons.Children.Add(btn);
    }

    private void OnCloseCommand(object sender, ExecutedRoutedEventArgs e)
    {
        Result = MessageBoxResult.Cancel;
        DialogResult = false;
    }
}
