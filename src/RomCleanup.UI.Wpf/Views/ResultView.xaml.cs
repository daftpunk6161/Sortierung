using System.ComponentModel;
using System.Linq;
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
        if (vm.HasRunResult)
            RefreshCharts(vm);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
            vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.HasRunResult) && sender is MainViewModel vm && vm.HasRunResult)
            RefreshCharts(vm);
    }

    /// <summary>GUI-104/106: Populate ScottPlot charts with run result data.</summary>
    private void RefreshCharts(MainViewModel vm)
    {
        // ── Pie Chart: Console Distribution ──
        var items = vm.Run.ConsoleDistribution;
        chartConsolePie.Plot.Clear();
        if (items.Count > 0)
        {
            var slices = new List<PieSlice>();
            var palette = new ScottPlot.Palettes.Category10();
            for (int i = 0; i < items.Count; i++)
            {
                slices.Add(new PieSlice
                {
                    Value = items[i].FileCount,
                    Label = items[i].DisplayName,
                    FillColor = palette.GetColor(i),
                });
            }
            double total = slices.Sum(s => s.Value);
            foreach (var s in slices)
            {
                s.LabelFontSize = 15;
                s.LabelFontColor = ScottPlot.Colors.White;
                if (total > 0 && s.Value / total < 0.03)
                    s.Label = string.Empty;
            }

            var pie = chartConsolePie.Plot.Add.Pie(slices);
            pie.DonutFraction = 0.4;
            pie.SliceLabelDistance = 1.35;

            var legend = chartConsolePie.Plot.ShowLegend();
            legend.FontSize = 13;
            legend.FontColor = ScottPlot.Colors.White;
        }
        StyleChart(chartConsolePie);
        chartConsolePie.Plot.Axes.Frameless();
        chartConsolePie.Plot.HideGrid();
        chartConsolePie.Refresh();

        // ── Bar Chart: Before/After (Keep vs Move vs Junk) ──
        chartBeforeAfter.Plot.Clear();
        if (int.TryParse(vm.Run.DashGames, out var totalGames) && totalGames > 0)
        {
            int.TryParse(vm.Run.DashDupes, out var dupes);
            int.TryParse(vm.Run.DashJunk, out var junk);
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
        chart.Plot.Axes.Color(ScottPlot.Color.FromHex("#888888"));
    }
}
