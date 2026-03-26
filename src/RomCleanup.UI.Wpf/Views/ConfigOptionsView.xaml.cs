using System.IO;
using System.Windows;
using System.Windows.Controls;
using RomCleanup.UI.Wpf.ViewModels;
using DragEventArgs = System.Windows.DragEventArgs;

namespace RomCleanup.UI.Wpf.Views;

public partial class ConfigOptionsView : UserControl
{
    public ConfigOptionsView()
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
}
