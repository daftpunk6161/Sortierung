using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace RomCleanup.UI.Wpf.Views;

public partial class StartView : UserControl
{
    public StartView()
    {
        InitializeComponent();
    }

    // ═══ GUI-065/069: Hero Drop-Zone with visual feedback ═══════════════

    private void OnHeroDrop(object sender, DragEventArgs e)
    {
        ResetDropVisual();
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            if (DataContext is not ViewModels.MainViewModel vm) return;
            int added = vm.AddDroppedFolders(paths);
            // GUI-093: Screen-reader announcement
            if (added > 0)
            {
                dropAnnouncement.Text = $"{added} Ordner hinzugefügt";
                dropAnnouncement.Visibility = Visibility.Visible;
            }
        }
    }

    private void OnHeroDragEnter(object sender, DragEventArgs e)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            heroDropZone.BorderBrush = (Brush)FindResource("BrushAccentCyan");
            heroDropZone.BorderThickness = new Thickness(3);
        }
    }

    private void OnHeroDragLeave(object sender, DragEventArgs e)
    {
        ResetDropVisual();
    }

    private void ResetDropVisual()
    {
        heroDropZone.BorderBrush = (Brush)FindResource("BrushBorder");
        heroDropZone.BorderThickness = new Thickness(2);
    }
}
