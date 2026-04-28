namespace Romulus.UI.Avalonia.Services;

public interface IAvaloniaFilePickerService
{
    Task<string?> BrowseFileAsync(string title = "", string filter = "");

    Task<string?> SaveFileAsync(string title = "", string filter = "", string? defaultFileName = null);
}
