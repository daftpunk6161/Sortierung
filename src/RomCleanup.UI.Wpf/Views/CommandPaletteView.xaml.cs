using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Views;

public partial class CommandPaletteView : UserControl
{
    public CommandPaletteView()
    {
        InitializeComponent();
        IsVisibleChanged += OnVisibleChanged;
    }

    private void OnVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is true)
            Dispatcher.BeginInvoke(() => SearchBox.Focus(), System.Windows.Threading.DispatcherPriority.Input);
    }

    private void OnBackdropClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CommandPalette.IsOpen = false;
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        switch (e.Key)
        {
            case Key.Down:
                vm.CommandPalette.MoveDownCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Up:
                vm.CommandPalette.MoveUpCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Enter:
                vm.CommandPalette.ExecuteCommand.Execute(null);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.CommandPalette.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private void OnResultDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.CommandPalette.ExecuteCommand.Execute(null);
    }
}
