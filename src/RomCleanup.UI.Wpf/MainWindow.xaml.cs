using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using RomCleanup.Contracts.Ports;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.ViewModels;

namespace RomCleanup.UI.Wpf;

public partial class MainWindow : Window, IWindowHost
{
    private const double ContextPanelMinWindowWidth = 1200;
    private const double ContextPanelDefaultWidth = 280;
    private const double NavCompactBreakpoint = 960;

    private readonly MainViewModel _vm;
    private readonly ISettingsService _settings;
    private readonly System.Threading.Timer _settingsTimer;
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
        ApplyRuntimeWindowIcon();

        // GUI-088: Periodic settings save every 5 minutes on background timer (not UI-thread)
        _settingsTimer = new System.Threading.Timer(
            _ => Dispatcher.BeginInvoke(_vm.SaveSettings),
            null,
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(5));

        Loaded += OnLoaded;
        SizeChanged += OnWindowSizeChanged;
        Closing += OnClosing;

        // React to ContextWing toggle from ViewModel
        _vm.PropertyChanged += OnVmPropertyChanged;
        _vm.Shell.PropertyChanged += OnShellPropertyChanged;

        // Wire orchestration events
        _vm.RunRequested += OnRunRequested;

        // Feature commands (registered into VM.FeatureCommands, bound in XAML)
        var featureCommands = new FeatureCommandService(_vm, _settings, dialog, this);
        featureCommands.RegisterCommands();
        _vm.WireToolItemCommands();
        _vm.NotifyFeatureCommandsReady();

        // Wire Command Palette execute callback to FeatureCommandService
        _vm.CommandPalette.SetExecuteCallback(featureCommands.ExecuteCommand);
    }

    // ═══ LIFECYCLE ══════════════════════════════════════════════════════

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.LoadInitialSettings();
            UpdateContextPanelWidth();
            if (_vm.Roots.Count == 0)
            {
                _vm.ApplyLocaleRegionDefaults();
                _vm.Shell.ShowFirstRunWizard = true;
                _vm.Shell.WizardStep = 0;
            }
        }
        catch (Exception ex)
        {
            _vm.AddLog($"Startup-Fehler: {ex.Message}", "ERROR");
            MessageBox.Show(
                $"Ein Startfehler wurde abgefangen:\n\n{ex.Message}\n\nDie Anwendung bleibt geöffnet, aber einige Einstellungen wurden nicht geladen.",
                "Romulus – Startwarnung",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
        }
    }

    private void OnWindowSizeChanged(object sender, SizeChangedEventArgs e)
    {
        UpdateContextPanelWidth();
        _vm.Shell.IsCompactNav = ActualWidth < NavCompactBreakpoint;
    }

    private void UpdateContextPanelWidth()
    {
        // Context Wing can be toggled off by the user (Ctrl+I)
        var showWing = _vm.Shell.ShowContextWing && ActualWidth >= ContextPanelMinWindowWidth;
        var targetWidth = showWing ? ContextPanelDefaultWidth : 0d;

        if (contextColumn.Width.GridUnitType != GridUnitType.Pixel ||
            Math.Abs(contextColumn.Width.Value - targetWidth) > 0.1)
        {
            contextColumn.Width = new GridLength(targetWidth, GridUnitType.Pixel);
        }
    }

    private void ApplyRuntimeWindowIcon()
    {
        try
        {
            var logo = new Views.RomulusLogoMark
            {
                Width = 64,
                Height = 64
            };

            logo.Measure(new Size(64, 64));
            logo.Arrange(new Rect(0, 0, 64, 64));
            logo.UpdateLayout();

            var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(logo);
            Icon = rtb;
        }
        catch
        {
            // Keep static app icon fallback if runtime icon rendering fails.
        }
    }

    private async void OnClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Guard against recursive calls when Close() is called from within OnClosing
        if (_isClosing) return;

        // GUI-110: MinimizeToTray — hide instead of close (unless busy-cancel path)
        if (_vm.MinimizeToTray && !_vm.IsBusy)
        {
            e.Cancel = true;
            _vm.SaveSettings();
            _trayService ??= new TrayService(this, _vm);
            Hide();
            _trayService.ShowBalloonTip(_vm.Loc["App.Title"], _vm.Loc["Tray.Minimized"]);
            return;
        }

        // P0-VULN-B1: Prevent window close if operation is running
        if (_vm.IsBusy)
        {
            var confirmed = DialogService.Confirm(
                _vm.Loc["App.RunActiveConfirm"],
                _vm.Loc["App.RunActiveTitle"]);

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
        // Stop periodic save timer first to prevent concurrent saves
        _settingsTimer.Dispose();

        // F-04 FIX: Synchronous final save to ensure settings are persisted before exit
        try { _vm.SaveSettings(); } catch { /* best effort */ }

        // GUI-115: Unsubscribe all VM events to prevent leaks
        _vm.RunRequested -= OnRunRequested;
        _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm.Shell.PropertyChanged -= OnShellPropertyChanged;
        Loaded -= OnLoaded;
        SizeChanged -= OnWindowSizeChanged;
        Closing -= OnClosing;

        // System tray
        _trayService?.Dispose();
        _trayService = null;

        // Kill detached API process if running
        SafeKillApiProcess();

        // GUI-115: Dispose file watchers (owned by VM) — includes WatchService event unsubscription
        _vm.CleanupWatchers();
    }

    // ═══ GUI-101: Shortcut overlay dismiss on background click ══════════
    private void OnShortcutOverlayClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (e.OriginalSource == sender)
            _vm.Shell.ShowShortcutSheet = false;
    }

    private void OnVmPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        // ShowContextWing now lives on Shell — handled in OnShellPropertyChanged
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ShowContextWing))
            UpdateContextPanelWidth();
    }

    // ═══ DRAG & DROP ════════════════════════════════════════════════════
    // Moved to Views/SortView.xaml.cs

    // ═══ RUN ORCHESTRATION ══════════════════════════════════════════════

    private async void OnRunRequested(object? sender, EventArgs e)
    {
        // Keep code-behind focused on view-only concerns (tray + preview refresh).
        _activeRunTask = ExecuteRunFromViewModelAsync();
        try { await _activeRunTask; }
        catch (Exception ex)
        {
            // V2-THR-H01: Prevent unhandled exception from crashing the app
            _vm.AddLog(_vm.Loc.Format("App.PipelineError", ex.Message), "ERROR");
        }
        finally { _activeRunTask = null; }
    }

    private async Task ExecuteRunFromViewModelAsync()
    {
        // GUI-111: Update tray tooltip during run
        _trayService?.UpdateTooltip(string.Format(_vm.Loc["Tray.RunProgress"], _vm.DryRun ? "DryRun" : "Move"));

        await _vm.ExecuteRunAsync();

        // GUI-112: Tray balloon on run completion (when minimized/hidden)
        if (_trayService is not null && (WindowState == WindowState.Minimized || !IsVisible))
        {
            var msg = string.Format(_vm.Loc["Tray.RunComplete"], _vm.Run.DashGames, _vm.Run.DashDupes, _vm.Run.DashJunk);
            _trayService.ShowBalloonTip(_vm.Loc["App.Title"], msg);
        }
        _trayService?.UpdateTooltip("RomCleanup");

        if (_vm.CurrentRunState is RunState.Completed or RunState.CompletedDryRun)
        {
            // Report preview auto-refreshes via LibraryReportView.OnLoaded
        }
    }

    // ═══ WORKFLOW & AUTOMATISIERUNG ═════════════════════════════════════

    // ═══ IWindowHost IMPLEMENTATION ═════════════════════════════════════

    double IWindowHost.FontSize
    {
        get => FontSize;
        set => FontSize = value;
    }

    void IWindowHost.SelectTab(int index) => _vm.Shell.SelectedNavIndex = index;

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
        SafeKillApiProcess();
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
        SafeKillApiProcess();
    }

    /// <summary>
    /// Safely kill and dispose the detached API process.
    /// Logs failures instead of swallowing them silently to prevent invisible zombie processes.
    /// </summary>
    private void SafeKillApiProcess()
    {
        try { if (_apiProcess is { HasExited: false }) _apiProcess.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { /* process already exited between check and kill */ }
        catch (System.ComponentModel.Win32Exception ex) { _vm.AddLog($"API process kill failed: {ex.Message}", "WARN"); }
        try { _apiProcess?.Dispose(); }
        catch (InvalidOperationException) { /* already disposed */ }
        _apiProcess = null;
    }

}
