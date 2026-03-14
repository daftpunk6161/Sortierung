using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using RomCleanup.UI.Wpf.ViewModels;

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

        if (!string.IsNullOrEmpty(vm.LastReportPath) && File.Exists(vm.LastReportPath))
            RefreshReportPreview();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _logScrollTimer.Stop();
        if (_logScrollHandler is not null && DataContext is MainViewModel vm)
            vm.LogEntries.CollectionChanged -= _logScrollHandler;
    }

    /// <summary>Load the last report into the WebView2 preview and update error summary.</summary>
    public async void RefreshReportPreview()
    {
        if (DataContext is not MainViewModel vm) return;

        if (string.IsNullOrEmpty(vm.LastReportPath) || !File.Exists(vm.LastReportPath))
        {
            vm.ErrorSummaryItems.Clear();
            vm.ErrorSummaryItems.Add("Kein Report vorhanden.");
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
            vm.ErrorSummaryItems.Add($"Fehler: {ex.Message}");
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
            if (webReportPreview.Parent is Panel panel)
            {
                var fallback = new TextBlock
                {
                    Text = "WebView2-Runtime nicht installiert.\nBericht kann über 'Bericht öffnen' im Browser angezeigt werden.",
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = (Brush)FindResource("BrushWarning"),
                    FontSize = 12,
                    Margin = new Thickness(8),
                    VerticalAlignment = VerticalAlignment.Center
                };
                panel.Children.Add(fallback);
            }
        }
    }
}
