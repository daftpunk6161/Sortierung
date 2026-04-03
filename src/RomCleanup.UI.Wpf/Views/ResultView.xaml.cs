using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using RomCleanup.UI.Wpf.ViewModels;
using ScottPlot;

namespace RomCleanup.UI.Wpf.Views;

public partial class ResultView : UserControl
{
    public ResultView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel vm) return;
        vm.PropertyChanged += OnVmPropertyChanged;
        if (vm.HasRunResult || vm.Run.HasRunData)
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                RefreshCharts(vm);
            });
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(MainViewModel.HasRunResult) or nameof(MainViewModel.LastReportPath)
            && sender is MainViewModel vm
            && (vm.HasRunResult || vm.Run.HasRunData))
        {
            Dispatcher.BeginInvoke(System.Windows.Threading.DispatcherPriority.Loaded, () =>
            {
                RefreshCharts(vm);
            });
        }
    }

    private void RefreshCharts(MainViewModel vm)
    {
        // ── Bar Chart: Before/After (Keep vs Move vs Junk) ──
        chartBeforeAfter.Plot.Clear();
        var totalGames = vm.Run.GamesRaw;
        if (totalGames > 0)
        {
            var dupes = vm.Run.DupesRaw;
            var junk = vm.Run.JunkRaw;
            int kept = totalGames - dupes - junk;

            double[] values = [kept, dupes, junk];
            var bar = chartBeforeAfter.Plot.Add.Bars(values);
            bar.Color = ScottPlot.Color.FromHex("#00d4ff");

            ScottPlot.TickGenerators.NumericManual ticks = new();
            ticks.AddMajor(0, "Keep");
            ticks.AddMajor(1, "Move");
            ticks.AddMajor(2, "Junk");
            chartBeforeAfter.Plot.Axes.Bottom.TickGenerator = ticks;
        }
        StyleChart(chartBeforeAfter);
        chartBeforeAfter.Refresh();
    }

    /// <summary>Apply dark theme style consistent with app theme.</summary>
    private static void StyleChart(ScottPlot.WPF.WpfPlot chart)
    {
        chart.Plot.FigureBackground.Color = ScottPlot.Color.FromHex("#1a1a2e");
        chart.Plot.DataBackground.Color = ScottPlot.Color.FromHex("#16213e");
        chart.Plot.Axes.Color(ScottPlot.Color.FromHex("#d8e1ff"));
    }
}
