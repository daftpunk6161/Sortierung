using Avalonia.Controls;
using Romulus.UI.Avalonia.ViewModels;

namespace Romulus.UI.Avalonia;

public partial class MainWindow : Window
{
    public MainWindow()
        : this(new MainWindowViewModel())
    {
    }

    public MainWindow(MainWindowViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
