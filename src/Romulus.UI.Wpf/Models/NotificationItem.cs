namespace Romulus.UI.Wpf.Models;

/// <summary>GUI-055: Toast/snackbar notification item.</summary>
public sealed class NotificationItem
{
    public required string Message { get; init; }
    /// <summary>Success, Warning, or Error.</summary>
    public required string Type { get; init; }
    /// <summary>Auto-dismiss in ms. 0 = sticky.</summary>
    public int AutoCloseMs { get; init; }
}
