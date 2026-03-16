using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Views;

public partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    // GUI-095: Focus management on wizard step change
    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.OldValue is INotifyPropertyChanged oldVm)
            oldVm.PropertyChanged -= OnVmPropertyChanged;
        if (e.NewValue is INotifyPropertyChanged newVm)
            newVm.PropertyChanged += OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.WizardStep))
            Dispatcher.BeginInvoke(() => MoveFocus(new TraversalRequest(FocusNavigationDirection.Next)));
    }
}
