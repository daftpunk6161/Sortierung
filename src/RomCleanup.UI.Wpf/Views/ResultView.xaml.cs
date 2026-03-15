using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RomCleanup.UI.Wpf.ViewModels;
using ScottPlot;

namespace RomCleanup.UI.Wpf.Views;

public partial class ResultView : UserControl
{
    private readonly DispatcherTimer _logScrollTimer;
    private System.Collections.Specialized.NotifyCollectionChangedEventHandler? _logScrollHandler;

    public ResultView()
    {
        InitializeComponent();

        _logScrollTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
        _logScrollTimer.Tick += (_, _) =>
        {
            _logScrollTimer.Stop();
            if (listLog.Items.Count > 0)
                listLog.ScrollIntoView(listLog.Items[^1]);
        };

        btnRefreshReportPreview.Click += (_, _) => RefreshReportPreview();
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

        // GUI-104/106: Refresh charts when run result becomes available
        vm.PropertyChanged += OnVmPropertyChanged;

        if (!string.IsNullOrEmpty(vm.LastReportPath) && File.Exists(vm.LastReportPath))
            RefreshReportPreview();

        if (vm.HasRunResult)
            RefreshCharts(vm);
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _logScrollTimer.Stop();
        if (DataContext is MainViewModel vm)
        {
            if (_logScrollHandler is not null)
                vm.LogEntries.CollectionChanged -= _logScrollHandler;
            vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _logScrollHandler = null;
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
        var items = vm.ConsoleDistribution;
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
            var pie = chartConsolePie.Plot.Add.Pie(slices);
            pie.DonutFraction = 0.4;
            chartConsolePie.Plot.ShowLegend();
        }
        StyleChart(chartConsolePie);
        chartConsolePie.Refresh();

        // ── Bar Chart: Before/After (Keep vs Move vs Junk) ──
        chartBeforeAfter.Plot.Clear();
        if (int.TryParse(vm.DashGames, out var totalGames) && totalGames > 0)
        {
            int.TryParse(vm.DashDupes, out var dupes);
            int.TryParse(vm.DashJunk, out var junk);
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

    /// <summary>Load the last report into the WebView2 preview and update error summary.</summary>
    public async void RefreshReportPreview()
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrEmpty(vm.LastReportPath) || !File.Exists(vm.LastReportPath))
        {
            vm.ErrorSummaryItems.Clear();
            vm.ErrorSummaryItems.Add(new Models.UiError("GUI-NOREPORT", "Kein Report vorhanden.", Models.UiErrorSeverity.Info));
            await EnsureWebView2Initialized(vm);
            webReportPreview.NavigateToString(
                "<html><body style='background:#1a1a2e;color:#888;font-family:Consolas;padding:16px'>" +
                "<p>Kein Report vorhanden. Erst einen Lauf starten.</p></body></html>");
            return;
        }

        try
        {
            var fullPath = Path.GetFullPath(vm.LastReportPath);
            await EnsureWebView2Initialized(vm);
            webReportPreview.Source = new Uri(fullPath);
            vm.PopulateErrorSummary();
            vm.AddLog($"Report-Vorschau geladen: {Path.GetFileName(fullPath)}", "INFO");
        }
        catch (Exception ex)
        {
            vm.ErrorSummaryItems.Clear();
            vm.ErrorSummaryItems.Add(new Models.UiError("GUI-REPORTERR", ex.Message, Models.UiErrorSeverity.Error));
            vm.AddLog($"Report-Vorschau fehlgeschlagen: {ex.Message}", "ERROR");
        }
    }

    private async Task EnsureWebView2Initialized(MainViewModel vm)
    {
        if (webReportPreview.CoreWebView2 is not null) return;
        try
        {
            await webReportPreview.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            vm.AddLog($"WebView2-Runtime nicht verf\u00fcgbar: {ex.Message}", "ERROR");
            webReportPreview.Visibility = Visibility.Collapsed;
            // V2-WPF-H03: Guard against duplicate fallback TextBlocks
            if (webReportPreview.Parent is Panel panel && !panel.Children.OfType<TextBlock>().Any(tb => tb.Name == "webView2Fallback"))
            {
                panel.Children.Remove(webReportPreview);
                var fallback = new TextBlock
                {
                    Text = "WebView2-Runtime nicht installiert.\nBericht kann über 'Bericht öffnen' im Browser angezeigt werden.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("BrushWarning"),
                    FontSize = 12,
                    Margin = new Thickness(8),
                    Name = "webView2Fallback",
                    VerticalAlignment = System.Windows.VerticalAlignment.Center
                };
                panel.Children.Add(fallback);
            }
        }
    }
}
