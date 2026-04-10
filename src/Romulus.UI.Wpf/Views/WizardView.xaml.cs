using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Views;

public partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // GUI-095: Focus management on wizard step change (now on Shell)
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is MainViewModel oldVm)
            oldVm.Shell.PropertyChanged -= OnShellPropertyChanged;
        if (e.NewValue is MainViewModel newVm)
            newVm.Shell.PropertyChanged += OnShellPropertyChanged;
    }

    private void OnShellPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.WizardStep))
            Dispatcher.BeginInvoke(() => MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
    }
}
