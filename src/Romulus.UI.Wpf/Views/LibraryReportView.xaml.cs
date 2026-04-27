using System.IO;
using System.Windows;
using System.Windows.Controls;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf.Views;

public partial class LibraryReportView : UserControl
{
    private bool _webViewFallbackActivated;

    public LibraryReportView()
    {
        InitializeComponent();

        btnRefreshReportPreview.Click += async (_, _) => await RefreshReportPreviewAsync();
        Loaded += OnLoaded;
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainViewModel vm && vm.Run.HasRunData)
        {
            await RefreshReportPreviewAsync();
        }
    }

    internal static bool TryNormalizeReportPath(string? value, out string normalizedPath)
        => MainViewModel.TryNormalizeReportPath(value, out normalizedPath);

    public async Task RefreshReportPreviewAsync()
    {
        if (DataContext is not MainViewModel vm) return;

        var preview = vm.BuildReportPreviewResult();

        await EnsureWebView2Initialized(vm);
        if (webReportPreview.CoreWebView2 is null)
            return;

        if (!string.IsNullOrWhiteSpace(preview.ReportFilePath))
        {
            webReportPreview.Source = new Uri(preview.ReportFilePath);
            return;
        }

        webReportPreview.NavigateToString(preview.InlineHtml ?? "<html><body></body></html>");
    }

    private async Task EnsureWebView2Initialized(MainViewModel vm)
    {
        if (_webViewFallbackActivated || webReportPreview.CoreWebView2 is not null) return;

        try
        {
            await webReportPreview.EnsureCoreWebView2Async();
        }
        catch (Exception ex)
        {
            var reason = ex.GetType().Name.Contains("RuntimeNotFound", StringComparison.OrdinalIgnoreCase)
                ? "WebView2-Runtime nicht verfügbar."
                : "Report-Vorschau konnte nicht initialisiert werden.";

            vm.AddLog($"{reason} ({ex.GetType().Name})", "WARN");
            ActivateWebViewFallback();
        }
    }

    /// <summary>
    /// Wave-2 F-08: pure presentation toggle. The fallback TextBlock now lives in XAML
    /// (<see cref="webView2Fallback"/>); we only swap visibilities and dispose the
    /// failed WebView2 host. Previous code-behind allocated the TextBlock from C#,
    /// which mixed view construction with state handling.
    /// </summary>
    private void ActivateWebViewFallback()
    {
        if (_webViewFallbackActivated)
            return;

        _webViewFallbackActivated = true;
        webReportPreview.Visibility = Visibility.Collapsed;
        webView2Fallback.Visibility = Visibility.Visible;

        if (webReportPreview is IDisposable disposable)
        {
            try
            {
                disposable.Dispose();
            }
            catch
            {
                // best effort: fallback mode remains active even if disposal fails
            }
        }
    }
}
