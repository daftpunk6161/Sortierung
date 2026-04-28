namespace Romulus.UI.Avalonia.Services;

public interface IAvaloniaFolderPickerService
{
    Task<string?> BrowseFolderAsync(string title = "");
}
