using System.IO;
using System.Windows;
using RomCleanup.UI.Wpf.ViewModels;
using DragEventArgs = System.Windows.DragEventArgs;

namespace RomCleanup.UI.Wpf.Helpers;

/// <summary>
/// Shared drag-and-drop logic for root folder lists.
/// Centralizes root-folder drag-and-drop for the active configuration views.
/// </summary>
internal static class RootsDragDropHelper
{
    internal static void OnDragEnter(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop)
            ? DragDropEffects.Link
            : DragDropEffects.None;
        e.Handled = true;
    }

    internal static void OnDrop(object sender, DragEventArgs e, MainViewModel? vm)
    {
        if (vm is null || vm.IsBusy) return;
        if (e.Data.GetData(DataFormats.FileDrop) is not string[] paths) return;
        foreach (var path in paths)
        {
            if (Directory.Exists(path) && !vm.Roots.Contains(path))
                vm.Roots.Add(path);
        }
    }
}
