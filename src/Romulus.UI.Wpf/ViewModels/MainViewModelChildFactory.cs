using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.Services;

namespace Romulus.UI.Wpf.ViewModels;

internal static class MainViewModelChildFactory
{
    public static ShellViewModel CreateShell(ILocalizationService localizationService, Action? commandRequery)
        => new(localizationService, commandRequery);

    public static SetupViewModel CreateSetup(
        IThemeService themeService,
        IDialogService dialogService,
        ISettingsService settingsService,
        ILocalizationService localizationService)
        => new(themeService, dialogService, settingsService, localizationService);

    public static ToolsViewModel CreateTools(ILocalizationService localizationService)
        => new(localizationService);

    public static RunViewModel CreateRun()
        => new();

    public static CommandPaletteViewModel CreateCommandPalette(ILocalizationService localizationService)
        => new(localizationService);

    public static DatAuditViewModel CreateDatAudit(ILocalizationService localizationService, IDialogService dialogService)
        => new(localizationService, dialogService);

    public static DatCatalogViewModel CreateDatCatalog(
        ILocalizationService localizationService,
        IDialogService dialogService,
        Func<string> getDatRoot,
        Action<string, string> addLog)
        => new(localizationService, dialogService, getDatRoot, addLog);

    public static ConversionPreviewViewModel CreateConversionPreview(ILocalizationService localizationService)
        => new(localizationService);
}
