namespace Romulus.UI.Wpf.Models;

/// <summary>
/// GUI-043: Structured UI error record with code, severity, and fix hint.
/// Replaces raw string error messages in ErrorSummaryItems.
/// </summary>
public sealed record UiError(
    string Code,
    string Message,
    UiErrorSeverity Severity,
    string? FixHint = null)
{
    /// <summary>Short display text for ListBox / error summary.</summary>
    public string DisplayText => Severity switch
    {
        UiErrorSeverity.Info => $"[INFO] {Message}",
        UiErrorSeverity.Warning => $"[WARN] {Message}",
        UiErrorSeverity.Error => $"[ERROR] {Message}",
        UiErrorSeverity.Blocked => $"[BLOCKED] {Message}",
        _ => Message
    };

    public override string ToString() => DisplayText;
}

public enum UiErrorSeverity
{
    Info,
    Warning,
    Error,
    Blocked
}
