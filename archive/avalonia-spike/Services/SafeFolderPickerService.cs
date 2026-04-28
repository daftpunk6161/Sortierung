namespace Romulus.UI.Avalonia.Services;

public sealed class SafeFolderPickerService : IAvaloniaFolderPickerService
{
    public Task<string?> BrowseFolderAsync(string title = "")
        => Task.FromResult<string?>(null);
}
