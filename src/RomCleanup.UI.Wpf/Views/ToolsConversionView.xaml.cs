using System.Windows;
using System.Windows.Controls;

namespace RomCleanup.UI.Wpf.Views;

public partial class ToolsConversionView : UserControl
{
    public ToolsConversionView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is ViewModels.MainViewModel vm)
            vm.Tools.LoadConversionRegistry();
    }
}
