using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Platform.Storage;

namespace Romulus.UI.Avalonia.Services;

public sealed class AvaloniaStorageFilePickerService : IAvaloniaFilePickerService
{
    private const string DefaultBrowseFileTitle = "Datei auswählen";
    private const string DefaultSaveFileTitle = "Speichern unter";
    private readonly IClassicDesktopStyleApplicationLifetime? _desktopLifetime;

    public AvaloniaStorageFilePickerService(IClassicDesktopStyleApplicationLifetime? desktopLifetime = null)
    {
        _desktopLifetime = desktopLifetime
            ?? Application.Current?.ApplicationLifetime as IClassicDesktopStyleApplicationLifetime;
    }

    public async Task<string?> BrowseFileAsync(string title = "", string filter = "")
    {
        var owner = ResolveOwnerWindow();
        if (owner?.StorageProvider is not { CanOpen: true } storageProvider)
            return null;

        var result = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            AllowMultiple = false,
            Title = string.IsNullOrWhiteSpace(title) ? DefaultBrowseFileTitle : title,
            FileTypeFilter = BuildFileTypeFilter(filter)
        });

        var selected = result.FirstOrDefault();
        var localPath = selected?.TryGetLocalPath();
        return string.IsNullOrWhiteSpace(localPath) ? null : localPath;
    }

    public async Task<string?> SaveFileAsync(string title = "", string filter = "", string? defaultFileName = null)
    {
        var owner = ResolveOwnerWindow();
        if (owner?.StorageProvider is not { CanSave: true } storageProvider)
            return null;

        var result = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = string.IsNullOrWhiteSpace(title) ? DefaultSaveFileTitle : title,
            SuggestedFileName = string.IsNullOrWhiteSpace(defaultFileName) ? string.Empty : defaultFileName,
            FileTypeChoices = BuildFileTypeFilter(filter)
        });

        var localPath = result?.TryGetLocalPath();
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

    private static IReadOnlyList<FilePickerFileType>? BuildFileTypeFilter(string filter)
    {
        if (string.IsNullOrWhiteSpace(filter))
            return null;

        var tokens = filter.Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 2)
            return null;

        var fileTypes = new List<FilePickerFileType>();
        for (var i = 0; i + 1 < tokens.Length; i += 2)
        {
            var label = tokens[i];
            var patternToken = tokens[i + 1];
            if (string.IsNullOrWhiteSpace(patternToken))
                continue;

            var patterns = patternToken
                .Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
                .Where(static pattern => !string.IsNullOrWhiteSpace(pattern))
                .ToList();

            if (patterns.Count == 0)
                continue;

            var fileType = new FilePickerFileType(string.IsNullOrWhiteSpace(label) ? patternToken : label)
            {
                Patterns = patterns
            };

            fileTypes.Add(fileType);
        }

        return fileTypes.Count == 0 ? null : fileTypes;
    }
}
