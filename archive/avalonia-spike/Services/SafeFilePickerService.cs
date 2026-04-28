namespace Romulus.UI.Avalonia.Services;

public sealed class SafeFilePickerService : IAvaloniaFilePickerService
{
    public Task<string?> BrowseFileAsync(string title = "", string filter = "")
        => Task.FromResult<string?>(null);

    public Task<string?> SaveFileAsync(string title = "", string filter = "", string? defaultFileName = null)
        => Task.FromResult<string?>(null);
}
