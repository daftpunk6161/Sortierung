using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure;
using RomCleanup.Infrastructure.State;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Application = System.Windows.Application;

namespace RomCleanup.UI.Wpf;

public partial class App : Application
{
    public IServiceProvider Services { get; private set; } = null!;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

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

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddRomCleanupCore();

        // Services
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IAppState, AppStateStore>();
        services.AddTransient<IDialogService, WpfDialogService>();

        // Feature domain services (GUI-034 to GUI-040)
        services.AddSingleton<IHealthAnalyzer, HealthAnalyzer>();
        services.AddSingleton<ICollectionService, CollectionService>();
        services.AddSingleton<IConversionEstimator, ConversionEstimator>();
        services.AddSingleton<IExportService, ExportService>();
        services.AddSingleton<IDatManagementService, DatManagementService>();
        services.AddSingleton<IHeaderService, HeaderSecurityService>();
        services.AddSingleton<IWorkflowService, WorkflowService>();
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
            "RomCleanup – Fehler",
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
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                RomCleanup.Contracts.AppIdentity.AppFolderName);
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "crash.log");
            File.AppendAllText(logPath, $"[{DateTime.UtcNow:O}] {ex}\n\n");
        }
        catch { /* best effort — don't throw during crash handling */ }
    }
}
