using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RomCleanup.UI.Wpf.Helpers;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Views;

/// <summary>
/// Legacy migration view retained for tests and reference.
/// The active application shell does not navigate here.
/// </summary>
public partial class SortView : UserControl
{
    public SortView()
    {
        InitializeComponent();
        listRoots.DragEnter += RootsDragDropHelper.OnDragEnter;
        listRoots.Drop += (s, e) => RootsDragDropHelper.OnDrop(s, e, DataContext as MainViewModel);
    }

    // ═══ TASK-117: Region Ranker Drag & Drop ════════════════════════════
    private RegionPriorityItem? _draggedRegion;

    private void OnRegionDragStart(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox listBox) return;
        if (e.OriginalSource is not FrameworkElement fe) return;
        if (fe.DataContext is not RegionPriorityItem item) return;
        if (!item.IsEnabled) return;

        _draggedRegion = item;
        DragDrop.DoDragDrop(listBox, item, DragDropEffects.Move);
        _draggedRegion = null;
    }

    private void OnRegionDragOver(object sender, DragEventArgs e)
    {
        e.Effects = _draggedRegion is not null ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRegionDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (_draggedRegion is null) return;

        // Find drop target
        if (e.OriginalSource is not FrameworkElement fe) return;
        var target = FindRegionItem(fe);
        if (target is null || !target.IsEnabled) return;

        int fromIdx = vm.RegionPriorities.IndexOf(_draggedRegion);
        int toIdx = vm.RegionPriorities.IndexOf(target);
        if (fromIdx >= 0 && toIdx >= 0)
            vm.MoveRegionTo(fromIdx, toIdx);
    }

    private static RegionPriorityItem? FindRegionItem(FrameworkElement fe)
    {
        DependencyObject? current = fe;
        while (current is not null)
        {
            if (current is FrameworkElement fwe && fwe.DataContext is RegionPriorityItem item)
                return item;
            current = System.Windows.Media.VisualTreeHelper.GetParent(current);
        }
        return null;
    }
}
