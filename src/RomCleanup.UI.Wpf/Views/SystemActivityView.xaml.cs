using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf.Views;

public partial class SystemActivityView : UserControl
{
    private readonly DispatcherTimer _logScrollTimer;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _logScrollHandler;

    public SystemActivityView()
    {
        InitializeComponent();

        _logScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _logScrollTimer.Tick += (_, _) =>
        {
            _logScrollTimer.Stop();
            if (listLog.Items.Count > 0)
                listLog.ScrollIntoView(listLog.Items[^1]);
        };

        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;

        _logScrollHandler = (_, _) =>
        {
            if (!_logScrollTimer.IsEnabled)
                _logScrollTimer.Start();
        };
        vm.LogEntries.CollectionChanged += _logScrollHandler;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _logScrollTimer.Stop();
        if (DataContext is MainViewModel vm && _logScrollHandler is not null)
            vm.LogEntries.CollectionChanged -= _logScrollHandler;
        _logScrollHandler = null;
    }
}
