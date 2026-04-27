using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Views;

public partial class WizardView : UserControl
{
    public WizardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        IsVisibleChanged += OnIsVisibleChanged;
    }

    private bool _bindingsReattached;

    /// <summary>
    /// WPF defers binding activation when an element is parsed inside a Collapsed
    /// parent (Shell.ShowFirstRunWizard starts false → MainWindow renders WizardView
    /// with Visibility=Collapsed). When the wizard later becomes visible those
    /// bindings stay BindingStatus.Unattached and never produce values.
    /// We force activation by clearing and re-setting every binding the first time
    /// the wizard becomes visible.
    /// </summary>
    private void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_bindingsReattached || e.NewValue is not true)
            return;
        _bindingsReattached = true;
        Dispatcher.BeginInvoke(new Action(() => ReattachBindings(this)),
            System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private static void ReattachBindings(DependencyObject root)
    {
        if (root is FrameworkElement fe)
            ReattachBindingsOn(fe);
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
            ReattachBindings(VisualTreeHelper.GetChild(root, i));
    }

    /// <summary>
    /// Walks every locally-set DependencyProperty on the element. Any property whose
    /// local value is an unattached binding expression (single, multi, or priority)
    /// is cleared and re-bound so WPF picks up the now-available DataContext.
    ///
    /// Wave-2 F-09: replaces the previous curated DependencyProperty whitelist with a
    /// reflective scan. The whitelist missed bindings on properties such as
    /// ToggleButton.IsChecked, Selector.SelectedValue, ItemsControl.ItemsSource,
    /// MultiBinding-bound TextBlock.Text, etc. The reflective scan covers any future
    /// XAML additions without requiring this list to be maintained.
    /// </summary>
    private static void ReattachBindingsOn(FrameworkElement fe)
    {
        // Snapshot first because we mutate bindings during the walk.
        var staleBindings = new List<(DependencyProperty Dp, BindingBase Binding)>();
        var enumerator = fe.GetLocalValueEnumerator();
        while (enumerator.MoveNext())
        {
            var entry = enumerator.Current;
            if (BindingOperations.GetBindingBase(fe, entry.Property) is not { } binding)
                continue;

            var expr = BindingOperations.GetBindingExpressionBase(fe, entry.Property);
            if (expr is null || expr.Status == BindingStatus.Active)
                continue;

            staleBindings.Add((entry.Property, binding));
        }

        foreach (var (dp, binding) in staleBindings)
        {
            BindingOperations.ClearBinding(fe, dp);
            BindingOperations.SetBinding(fe, dp, binding);
        }

        // Style DataTriggers (e.g. WizardStepXPanelStyle) are evaluated against
        // the styled element's DataContext at apply-time. When the wizard was
        // parsed inside a Collapsed parent these triggers also failed to evaluate.
        // Re-apply the Style to force WPF to re-run all triggers.
        if (fe.Style is { } style)
        {
            fe.Style = null;
            fe.Style = style;
        }
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
