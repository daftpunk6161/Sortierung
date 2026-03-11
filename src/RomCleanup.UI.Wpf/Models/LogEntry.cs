namespace RomCleanup.UI.Wpf.Models;

/// <summary>
/// Single log entry for the Protokoll tab.
/// Level drives color via LogLevelToBrushConverter in XAML.
/// </summary>
public sealed record LogEntry(string Text, string Level);
