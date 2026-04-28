using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Romulus.UI.Avalonia.Services;

public sealed class AvaloniaStorageFolderPickerService : IAvaloniaFolderPickerService
{
    private const string DefaultBrowseFolderTitle = "Ordner auswählen";
    private readonly IClassicDesktopStyleApplicationLifetime? _desktopLifetime;

    public AvaloniaStorageFolderPickerService(IClassicDesktopStyleApplicationLifetime? desktopLifetime = null)
    {
        _desktopLifetime = desktopLifetime
            ?? Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
    }

    public async Task<string?> BrowseFolderAsync(string title = "")
    {
        var owner = ResolveOwnerWindow();
        if (owner?.StorageProvider is not { CanPickFolder: true } storageProvider)
            return null;

        var pickResult = await storageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions
        {
            AllowMultiple = false,
            Title = string.IsNullOrWhiteSpace(title) ? DefaultBrowseFolderTitle : title
        });

        var selected = pickResult.FirstOrDefault();
        var localPath = selected?.TryGetLocalPath();
        return string.IsNullOrWhiteSpace(localPath) ? null : localPath;
    }

    private Window? ResolveOwnerWindow()
    {
        var mainWindow = _desktopLifetime?.MainWindow;
        if (mainWindow is not null)
            return mainWindow;

        var windows = _desktopLifetime?.Windows;
        if (windows is null || windows.Count == 0)
            return null;

        return windows.FirstOrDefault(static window => window.IsActive)
            ?? windows[0];
    }
}
