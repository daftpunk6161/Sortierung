using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.State;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Application = System.Windows.Application;

namespace Romulus.UI.Wpf;

public partial class App : Application
{
    /// <summary>Named mutex for single-instance enforcement.</summary>
    private Mutex? _singleInstanceMutex;

    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Single-instance guard: prevent zombie processes from multiple launches.
        // Verhalten bei laufender Instance: bestehendes Fenster in den Vordergrund holen
        // und still beenden. KEINE modale MessageBox - die ueberdeckte sonst Overlays
        // wie den First-Run-Wizard und blockierte den User.
        const string mutexName = "Local\\Romulus_SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            TryActivateExistingInstance();
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
            Shutdown(0);
            return;
        }

        // Register global handlers before resolving UI to catch startup exceptions.
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;

        try
        {
            var services = new ServiceCollection();
            ConfigureServices(services);
            Services = services.BuildServiceProvider();

            var mainWindow = Services.GetRequiredService<MainWindow>();
            mainWindow.Show();
        }
        catch (Exception ex)
        {
            LogFatalException(ex);
            MessageBox.Show(
                $"Der Start von Romulus ist fehlgeschlagen:\n\n{ex.Message}\n\nDetails wurden in crash.log gespeichert.",
                "Romulus – Startfehler",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        // Dispose DI container to release all singleton disposables
        (Services as IDisposable)?.Dispose();

        // Release single-instance mutex so next launch can acquire it
        if (_singleInstanceMutex is not null)
        {
            try { _singleInstanceMutex.ReleaseMutex(); } catch (ApplicationException) { /* not owned */ }
            _singleInstanceMutex.Dispose();
            _singleInstanceMutex = null;
        }

        base.OnExit(e);
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddRomulusCore();

        // Services
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAppState, AppStateStore>();
        services.AddTransient<IDialogService, WpfDialogService>();

        // DAT cache + background prewarm: shared across runs so cached payloads
        // survive multiple Preview/Execute cycles within one process lifetime.
        services.AddSingleton<IDatEntryCache>(_ =>
            new FileSystemDatEntryCache(
                Path.Combine(AppStoragePathResolver.ResolveRoamingAppDirectory(), "dat-cache")));
        services.AddSingleton<DatPrewarmService>(sp =>
        {
            var cache = sp.GetRequiredService<IDatEntryCache>();
            MainViewModel? vm = null;
            void Log(string msg)
            {
                vm ??= sp.GetService<MainViewModel>();
                vm?.AddLog(msg, "INFO");
            }
            return new DatPrewarmService(cache, Log);
        });

        // Feature domain services
        services.AddSingleton<IRunService, RunService>();
        services.AddSingleton<IResultExportService, ResultExportService>();
        services.AddSingleton<FeatureCommandService>();

        // Wave-2 F-06: API process lifecycle
        services.AddSingleton<IProcessLauncher, DefaultProcessLauncher>();
        services.AddSingleton<IApiProcessHost>(sp =>
        {
            var launcher = sp.GetRequiredService<IProcessLauncher>();
            // ViewModel logging is wired up after VM construction; capture lazily.
            MainViewModel? vm = null;
            void Log(string msg, string level)
            {
                vm ??= sp.GetService<MainViewModel>();
                vm?.AddLog(msg, level);
            }
            return new ApiProcessHost(launcher, Log);
        });

        // Child ViewModels (TASK-050: DI composition)
        services.AddSingleton<ShellViewModel>(sp =>
            new ShellViewModel(sp.GetRequiredService<ILocalizationService>()));
        services.AddSingleton<SetupViewModel>();
        services.AddSingleton<ToolsViewModel>();
        services.AddSingleton<RunViewModel>();
        services.AddSingleton<CommandPaletteViewModel>();
        services.AddSingleton<DatAuditViewModel>();
        services.AddSingleton<DatCatalogViewModel>();
        services.AddSingleton<ConversionPreviewViewModel>();

        // ViewModel
        services.AddSingleton<MainViewModel>();

        // Windows
        services.AddSingleton<MainWindow>();
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogFatalException(e.Exception);
        MessageBox.Show(
            $"Ein unerwarteter Fehler ist aufgetreten:\n\n{e.Exception.Message}\n\nDetails wurden in crash.log gespeichert.",
            "Romulus – Fehler",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogFatalException(ex);
    }

    private static void LogFatalException(Exception ex)
    {
        try
        {
            var logDir = AppStoragePathResolver.ResolveRoamingAppDirectory();
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");
            RotateCrashLogIfTooLarge(logPath);
            AtomicFileWriter.AppendText(logPath, $"[{DateTime.UtcNow:O}] {ex}\n\n");
        }
        catch { /* best effort — don't throw during crash handling */ }
    }

    /// <summary>
    /// F-16: Rotate <c>crash.log</c> when it exceeds 1 MB. Keeps the most recent
    /// rotated copy as <c>crash.log.1</c>. Older rotations are discarded so the
    /// crash-log directory stays bounded on machines with chronic startup failures.
    /// </summary>
    internal const long CrashLogMaxBytes = 1L * 1024 * 1024;

    internal static void RotateCrashLogIfTooLarge(string logPath)
    {
        try
        {
            var fi = new FileInfo(logPath);
            if (!fi.Exists || fi.Length < CrashLogMaxBytes)
                return;

            var rotated = logPath + ".1";
            if (File.Exists(rotated))
                File.Delete(rotated);
            File.Move(logPath, rotated);
        }
        catch (Exception rotEx) when (rotEx is IOException or UnauthorizedAccessException)
        {
            // Rotation is best-effort. Failing to rotate must not prevent the next
            // crash entry from being written.
        }
    }

    // ─── Single-Instance: bestehendes Romulus-Fenster aktivieren ───
    private const int SW_RESTORE = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr hWnd);

    private static void TryActivateExistingInstance()
    {
        try
        {
            var current = Process.GetCurrentProcess();
            var existing = Process.GetProcessesByName(current.ProcessName)
                .FirstOrDefault(p => p.Id != current.Id && p.MainWindowHandle != IntPtr.Zero);

            if (existing is null) return;

            var handle = existing.MainWindowHandle;
            if (IsIconic(handle))
                ShowWindow(handle, SW_RESTORE);
            SetForegroundWindow(handle);
        }
        catch { /* best effort - never block exit on activation failure */ }
    }
}
