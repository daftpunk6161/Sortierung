namespace Romulus.UI.Avalonia.Services;

public interface ITrayService : IDisposable
{
    bool IsSupported { get; }

    bool IsActive { get; }

    void Toggle();

    void UpdateTooltip(string tooltip);

    void ShowNotification(string title, string message);
}
