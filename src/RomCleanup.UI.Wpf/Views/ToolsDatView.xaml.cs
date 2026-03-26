using System.Windows;
using System.Windows.Controls;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;

namespace RomCleanup.UI.Wpf.Views;

public partial class ToolsDatView : UserControl
{
    public ToolsDatView()
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
