namespace Romulus.UI.Avalonia.Services;

public sealed class NoOpTrayService : ITrayService
{
    public bool IsSupported => false;

    public bool IsActive => false;

    public void Toggle()
    {
    }

    public void UpdateTooltip(string tooltip)
    {
    }

    public void ShowNotification(string title, string message)
    {
    }

    public void Dispose()
    {
    }
}
