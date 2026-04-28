using Romulus.Contracts.Ports;
using Romulus.UI.Avalonia.Services;
using Romulus.UI.Avalonia.ViewModels;
using Xunit;

namespace Romulus.Tests;

public sealed class AvaloniaPhaseCAdapterTests
{
    [Fact]
    public void DialogService_WithSafeBackend_ReturnsDeterministicFallbacks()
    {
        var service = new AvaloniaDialogService(new SafeDialogBackend());

        Assert.Null(service.BrowseFolder());
        Assert.Null(service.BrowseFile());
        Assert.Null(service.SaveFile());
        Assert.False(service.Confirm("confirm"));
        Assert.Equal(ConfirmResult.Cancel, service.YesNoCancel("question"));
        Assert.Equal("seed", service.ShowInputBox("prompt", defaultValue: "seed"));
    }

    [Fact]
    public void ThemeService_Toggle_CyclesSystemDarkLight()
    {
        var host = new TestThemeHost();
        var service = new AvaloniaThemeService(host);

        Assert.Equal(AvaloniaThemeKind.System, service.Current);

        service.Toggle();
        Assert.Equal(AvaloniaThemeKind.Dark, service.Current);

        service.Toggle();
        Assert.Equal(AvaloniaThemeKind.Light, service.Current);

        service.Toggle();
        Assert.Equal(AvaloniaThemeKind.System, service.Current);
    }

    [Fact]
    public void NoOpTrayService_IsSafeAndUnsupported()
    {
        using var service = new NoOpTrayService();

        Assert.False(service.IsSupported);
        Assert.False(service.IsActive);

        service.Toggle();
        service.UpdateTooltip("Romulus");
        service.ShowNotification("title", "message");

        Assert.False(service.IsActive);
    }

    [Fact]
    public void MainWindowViewModel_ToggleThemeCommand_UpdatesThemeLabel()
    {
        var host = new TestThemeHost();
        var themeService = new AvaloniaThemeService(host);
        using var trayService = new NoOpTrayService();
        var vm = new MainWindowViewModel(themeService: themeService, trayService: trayService);

        var initial = vm.ThemeLabel;

        vm.ToggleThemeCommand.Execute(null);

        Assert.NotEqual(initial, vm.ThemeLabel);
    }

    private sealed class TestThemeHost : IAvaloniaThemeHost
    {
        public AvaloniaThemeKind CurrentTheme { get; set; } = AvaloniaThemeKind.System;
    }
}
