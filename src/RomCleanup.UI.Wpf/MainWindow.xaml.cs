using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Threading;
using RomCleanup.Contracts.Ports;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf;

public partial class MainWindow : Window, IWindowHost
{
    private readonly MainViewModel _vm;
    private readonly ISettingsService _settings;
    private readonly DispatcherTimer _settingsTimer;
    private Task? _activeRunTask;
    // System tray service
    private TrayService? _trayService;

    // Detached API process from Mobile Web UI
    private Process? _apiProcess;
    // Guard against recursive OnClosing calls
    private bool _isClosing;

    public MainWindow(MainViewModel vm, ISettingsService settings, IDialogService dialog)
    {
        _vm = vm;
        _settings = settings;
        DataContext = _vm;

        InitializeComponent();

        // Periodic settings save every 5 minutes (P3-BUG-051 / UX-07)
        _settingsTimer = new DispatcherTimer { Interval = TimeSpan.FromMinutes(5) };
        _settingsTimer.Tick += OnSettingsTimerTick;
        _settingsTimer.Start();

        Loaded += OnLoaded;
        Closing += OnClosing;

        // Wire orchestration events
        _vm.RunRequested += OnRunRequested;

        // Feature commands (registered into VM.FeatureCommands, bound in XAML)
        var featureCommands = new FeatureCommandService(_vm, _settings, dialog, this);
        featureCommands.RegisterCommands();
        _vm.WireToolItemCommands();
    }

    // ═══ LIFECYCLE ══════════════════════════════════════════════════════

    private void OnSettingsTimerTick(object? sender, EventArgs e) => _vm.SaveSettings();

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        _vm.LoadInitialSettings();
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Guard against recursive calls when Close() is called from within OnClosing
        if (_isClosing) return;

        // P0-VULN-B1: Prevent window close if operation is running
        if (_vm.IsBusy)
        {
            var confirmed = DialogService.Confirm(
                "Ein Lauf ist aktiv. Abbrechen und beenden?",
                "Lauf aktiv");

            if (!confirmed)
            {
                e.Cancel = true;
                return;
            }

            // User chose to close — cancel the operation and wait for it to finish
            _vm.CancelCommand.Execute(null);
            // V2-WPF-M01: Set _isClosing immediately to prevent race on multiple close clicks
            _isClosing = true;
            e.Cancel = true; // Cancel this close; re-close after task completes

            var runTask = _activeRunTask;
            if (runTask is not null)
            {
                try { await runTask; } catch { /* already handled in RunCoreAsync */ }
            }

            _vm.SaveSettings();
            CleanupResources();
            _isClosing = true;
            Close(); // Re-trigger close now that task is done
            return;
        }

        _vm.SaveSettings();
        CleanupResources();
    }

    /// <summary>Release all resources — called from both OnClosing paths (normal + busy-cancel).</summary>
    private void CleanupResources()
    {
        // Stop periodic save timer
        _settingsTimer.Stop();
        // V2-WPF-H02: Unsubscribe tick to prevent stale callbacks
        _settingsTimer.Tick -= OnSettingsTimerTick;

        // Unsubscribe VM events to prevent leaks
        _vm.RunRequested -= OnRunRequested;

        // System tray
        _trayService?.Dispose();
        _trayService = null;

        // Kill detached API process if running
        try { if (_apiProcess is { HasExited: false }) _apiProcess.Kill(entireProcessTree: true); } catch { }
        try { _apiProcess?.Dispose(); } catch { }
        _apiProcess = null;

        // Dispose file watchers (owned by VM)
        _vm.CleanupWatchers();
    }

    // ═══ DRAG & DROP ════════════════════════════════════════════════════
    // Moved to Views/SortView.xaml.cs

    // ═══ RUN ORCHESTRATION ══════════════════════════════════════════════

    private async void OnRunRequested(object? sender, EventArgs e)
    {
        _activeRunTask = ExecuteAndRefreshAsync();
        try { await _activeRunTask; }
        catch (Exception ex)
        {
            // V2-THR-H01: Prevent unhandled exception from crashing the app
            _vm.AddLog($"Pipeline-Fehler: {ex.Message}", "ERROR");
        }
        finally { _activeRunTask = null; }
    }

    private async Task ExecuteAndRefreshAsync()
    {
        await _vm.ExecuteRunAsync();
        if (_vm.CurrentRunState is RunState.Completed or RunState.CompletedDryRun)
        {
            // P1-003: Auto-switch to Ergebnis tab after run completion
            tabMain.SelectedIndex = tabMain.Items.Count - 1;
            resultView.RefreshReportPreview();
        }
    }

    // ═══ WORKFLOW & AUTOMATISIERUNG ═════════════════════════════════════

    // ═══ IWindowHost IMPLEMENTATION ═════════════════════════════════════

    double IWindowHost.FontSize
    {
        get => FontSize;
        set => FontSize = value;
    }

    void IWindowHost.SelectTab(int index) => tabMain.SelectedIndex = index;

    void IWindowHost.ShowTextDialog(string title, string content) =>
        ResultDialog.ShowText(title, content, this);

    void IWindowHost.ToggleSystemTray()
    {
        _trayService ??= new TrayService(this, _vm);
        _trayService.Toggle();
    }

    void IWindowHost.StartApiProcess(string projectPath)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
        };
        try { if (_apiProcess is { HasExited: false }) _apiProcess.Kill(entireProcessTree: true); } catch { }
        try { _apiProcess?.Dispose(); } catch { }
        _apiProcess = Process.Start(psi);
        _vm.AddLog("REST API gestartet: http://127.0.0.1:5000", "INFO");
        _ = Task.Delay(2000).ContinueWith(_ =>
        {
            var d = Application.Current?.Dispatcher;
            if (d is null) return;
            d.InvokeAsync(() =>
            {
                try { Process.Start(new ProcessStartInfo("http://127.0.0.1:5000/health") { UseShellExecute = true }); }
                catch { /* browser launch failed */ }
            });
        });
    }

    void IWindowHost.StopApiProcess()
    {
        try { if (_apiProcess is { HasExited: false }) _apiProcess.Kill(entireProcessTree: true); } catch { }
        try { _apiProcess?.Dispose(); } catch { }
        _apiProcess = null;
    }


}
