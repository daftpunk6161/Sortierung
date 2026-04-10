using System.Windows;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Helpers;

/// <summary>
/// Shared drag-and-drop behavior for root folder targets.
/// Centralizes folder validation, busy-state gating, VM updates, and accessibility announcement flow.
/// </summary>
internal static class RootsDragDropHelper
{
    public static readonly DependencyProperty EnabledProperty =
        DependencyProperty.RegisterAttached(
            "Enabled",
            typeof(bool),
            typeof(RootsDragDropHelper),
            new PropertyMetadata(false, OnEnabledChanged));

    public static bool GetEnabled(DependencyObject obj) => (bool)obj.GetValue(EnabledProperty);

    public static void SetEnabled(DependencyObject obj, bool value) => obj.SetValue(EnabledProperty, value);

    private static void OnEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not UIElement element)
            return;

        if ((bool)e.NewValue)
        {
            element.DragEnter += OnDragEnter;
            element.DragOver += OnDragOver;
            element.DragLeave += OnDragLeave;
            element.Drop += OnDrop;
            return;
        }

        element.DragEnter -= OnDragEnter;
        element.DragOver -= OnDragOver;
        element.DragLeave -= OnDragLeave;
        element.Drop -= OnDrop;
    }

    private static void OnDragEnter(object sender, DragEventArgs e) => UpdateDragState(sender, e);

    private static void OnDragOver(object sender, DragEventArgs e) => UpdateDragState(sender, e);

    private static void OnDragLeave(object sender, DragEventArgs e)
    {
        SetDragState(sender, isActive: false);
        e.Handled = true;
    }

    private static void OnDrop(object sender, DragEventArgs e)
    {
        try
        {
            SetDragState(sender, isActive: false);

            if (sender is not FrameworkElement element ||
                element.DataContext is not MainViewModel vm ||
                vm.IsBusy ||
                !TryGetDroppedPaths(e, out var paths))
            {
                e.Effects = DragDropEffects.None;
                return;
            }

            var added = vm.HandleDroppedFolders(paths);
            e.Effects = added > 0 ? DragDropEffects.Link : DragDropEffects.None;
        }
        finally
        {
            e.Handled = true;
        }
    }

    private static void UpdateDragState(object sender, DragEventArgs e)
    {
        var isActive = sender is FrameworkElement element
            && element.DataContext is MainViewModel vm
            && !vm.IsBusy
            && TryGetDroppedPaths(e, out _);

        SetDragState(sender, isActive);
        e.Effects = isActive ? DragDropEffects.Link : DragDropEffects.None;
        e.Handled = true;
    }

    private static void SetDragState(object sender, bool isActive)
    {
        if (sender is FrameworkElement element &&
            element.DataContext is MainViewModel vm)
        {
            vm.SetRootDropTargetActive(isActive);
        }
    }

    private static bool TryGetDroppedPaths(DragEventArgs e, out string[] paths)
    {
        if (e.Data.GetDataPresent(DataFormats.FileDrop) &&
            e.Data.GetData(DataFormats.FileDrop) is string[] droppedPaths &&
            droppedPaths.Length > 0)
        {
            paths = droppedPaths;
            return true;
        }

        paths = [];
        return false;
    }
}
