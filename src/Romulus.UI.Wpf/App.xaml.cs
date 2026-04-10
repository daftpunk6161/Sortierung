using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure;
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

        // Single-instance guard: prevent zombie processes from multiple launches
        const string mutexName = "Global\\Romulus_SingleInstance";
        _singleInstanceMutex = new Mutex(true, mutexName, out bool createdNew);
        if (!createdNew)
        {
            MessageBox.Show(
                "Romulus läuft bereits (ggf. im System-Tray).",
                "Romulus",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
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

        // Feature domain services
        services.AddSingleton<IRunService, RunService>();

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
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] {ex}\n\n");
        }
        catch { /* best effort — don't throw during crash handling */ }
    }
}
