using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.ViewModels;
using DragEventArgs = System.Windows.DragEventArgs;

namespace RomCleanup.UI.Wpf.Views;

public partial class SortView : UserControl
{
    public SortView()
    {
        InitializeComponent();
        listRoots.DragEnter += OnRootsDragEnter;
        listRoots.Drop += OnRootsDrop;
    }

    private static void OnRootsDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Link
            : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnRootsDrop(object sender, DragEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        if (vm.IsBusy) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
        {
            if (Directory.Exists(path) && !vm.Roots.Contains(path))
                vm.Roots.Add(path);
        }
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
