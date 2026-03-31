using System.Windows;
using System.Windows.Controls;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.Views;

/// <summary>
/// Legacy monolithic settings view retained for migration/reference.
/// The active shell is composed from dedicated Config/Tools/System views.
/// </summary>
public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
    }

    private void OnBrowseDatFile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { DataContext: DatMapRow row })
            return;

        var path = DialogService.BrowseFile(
            "DAT-Datei auswählen",
            "DAT-Dateien (*.dat;*.xml)|*.dat;*.xml|Alle Dateien|*.*");

        if (path is not null)
            row.DatFile = path;
    }
}
