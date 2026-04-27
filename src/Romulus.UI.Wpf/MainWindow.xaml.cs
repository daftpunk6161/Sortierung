using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Tools;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;

namespace Romulus.UI.Wpf;

public partial class MainWindow : Window, IWindowHost
{
    private const double ContextPanelMinWindowWidth = 1240;
    private const double ContextPanelDefaultWidth = 320;
    private const double NavCompactBreakpoint = 1180;

    private readonly MainViewModel _vm;
    private readonly ISettingsService _settings;
    private readonly IDialogService _dialog;
    private readonly FeatureCommandService _featureCommands;
    private readonly IApiProcessHost _apiProcessHost;
    private readonly System.Threading.Timer _settingsTimer;
    private Task? _activeRunTask;
    // System tray service
    private TrayService? _trayService;

    // Guard against recursive OnClosing calls
    private bool _isClosing;
    // Explicit app-exit intent (e.g. tray 'Beenden') should bypass minimize-to-tray interception.
    private bool _forceExitRequested;

    public MainWindow(MainViewModel vm, ISettingsService settings, IDialogService dialog, FeatureCommandService featureCommands, IApiProcessHost apiProcessHost)
    {
        _vm = vm;
        _settings = settings;
        _dialog = dialog;
        _featureCommands = featureCommands;
        _apiProcessHost = apiProcessHost;
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

        // React to ContextWing toggle from ViewModel (Shell layer)
        _vm.Shell.PropertyChanged += OnShellPropertyChanged;

        // Wire orchestration events
        _vm.RunRequested += OnRunRequested;

        // Feature commands (registered into VM.FeatureCommands, bound in XAML)
        _featureCommands.AttachWindowHost(this);
        _featureCommands.RegisterCommands();
        _vm.WireToolItemCommands();
        _vm.Tools.LoadConversionRegistry();
        _vm.NotifyFeatureCommandsReady();

        // Wire Command Palette execute callback to FeatureCommandService
        _vm.CommandPalette.SetExecuteCallback(_featureCommands.ExecuteCommand);
    }

    // ═══ LIFECYCLE ══════════════════════════════════════════════════════

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        try
        {
            _vm.LoadInitialSettings();
            UpdateContextPanelWidth();
            _vm.Shell.IsCompactNav = ActualWidth < NavCompactBreakpoint;
            if (_vm.ShowFirstRunWizardOnStartup && _vm.Roots.Count == 0)
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
        if (_vm.MinimizeToTray && !_vm.IsBusy && !_forceExitRequested)
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
            var confirmed = _dialog.Confirm(
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
        _vm.Shell.PropertyChanged -= OnShellPropertyChanged;
        Loaded -= OnLoaded;
        SizeChanged -= OnWindowSizeChanged;
        Closing -= OnClosing;

        // System tray
        _trayService?.Dispose();
        _trayService = null;

        // Kill detached API process if running
        _apiProcessHost.Stop();

        // Final safety net: terminate any remaining tracked child processes.
        ExternalProcessGuard.KillAllTrackedProcesses("app-shutdown", msg => _vm.AddLog(msg, "WARN"));

        // GUI-115: Dispose VM-owned resources (watchers, timers, event subscriptions)
        _vm.Dispose();

        // Force application exit so no zombie .NET Host processes remain
        Application.Current?.Shutdown();
    }

    private void OnShellPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ShellViewModel.ShowContextWing))
            UpdateContextPanelWidth();
    }

    // ═══ DRAG & DROP ════════════════════════════════════════════════════
    // Root drag-and-drop lives in Helpers/RootsDragDropHelper.cs

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
        _trayService?.UpdateTooltip("Romulus");

        if (_vm.CurrentRunState is RunState.Completed or RunState.CompletedDryRun)
        {
            // ResultView contains the integrated report preview and refreshes on load.
        }
    }

    /// <summary>
    /// Request a real application exit from non-window UI (e.g. tray menu).
    /// This bypasses minimize-to-tray interception in OnClosing.
    /// </summary>
    public void RequestApplicationExit()
    {
        _forceExitRequested = true;
        Close();
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
        // Wave-2 F-06: lifecycle delegated to IApiProcessHost.
        _apiProcessHost.Start(projectPath);
    }

    void IWindowHost.StopApiProcess()
    {
        _apiProcessHost.Stop();
    }

}
