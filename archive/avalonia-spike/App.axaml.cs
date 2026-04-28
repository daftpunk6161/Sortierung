using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Contracts.Ports;
using Romulus.UI.Avalonia.Runtime;
using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;

namespace Romulus.UI.Avalonia;

public partial class App : Application
{
    private ServiceProvider? _services;
    private SingleInstanceGuard? _singleInstanceGuard;

    public override void Initialize()
        => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        _singleInstanceGuard = SingleInstanceGuard.Acquire("Global\\Romulus_Avalonia_SingleInstance");
        if (!_singleInstanceGuard.IsAcquired)
        {
            _singleInstanceGuard.Dispose();
            _singleInstanceGuard = null;
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime duplicateDesktop)
                duplicateDesktop.Shutdown(0);
            return;
        }

        var services = new ServiceCollection();
        ConfigureServices(services);
        _services = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = _services.GetRequiredService<MainWindow>();
            desktop.Exit += (_, _) => DisposeResources();
        }

        base.OnFrameworkInitializationCompleted();
    }

    private static void ConfigureServices(IServiceCollection services)
    {
        services.AddSingleton<IAvaloniaDialogBackend, SafeDialogBackend>();
        services.AddSingleton<IDialogService, AvaloniaDialogService>();
        services.AddSingleton<IAvaloniaFolderPickerService, AvaloniaStorageFolderPickerService>();
        services.AddSingleton<IAvaloniaFilePickerService, AvaloniaStorageFilePickerService>();
        services.AddSingleton<IAvaloniaThemeHost, AvaloniaApplicationThemeHost>();
        services.AddSingleton<AvaloniaThemeService>();
        services.AddSingleton<ITrayService, NoOpTrayService>();

        services.AddSingleton<StartViewModel>();
        services.AddSingleton<ProgressViewModel>();
        services.AddSingleton<ResultViewModel>();
        services.AddSingleton<MainWindowViewModel>();
        services.AddSingleton<MainWindow>();
    }

    private void DisposeResources()
    {
        _services?.Dispose();
        _services = null;

        _singleInstanceGuard?.Dispose();
        _singleInstanceGuard = null;
    }
}
