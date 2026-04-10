namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Abstraction over Window-level operations that cannot be accessed from ViewModels.
/// Injected into FeatureCommandService so handlers can be moved out of code-behind.
/// </summary>
public interface IWindowHost
{
    /// <summary>Get or set the Window font size (for Accessibility handler).</summary>
    double FontSize { get; set; }

    /// <summary>Navigate to a screen by index (for CommandPalette → "settings").</summary>
    void SelectTab(int index);

    /// <summary>Show a modal text dialog (ResultDialog wrapper).</summary>
    void ShowTextDialog(string title, string content);

    /// <summary>Toggle the system tray icon on/off.</summary>
    void ToggleSystemTray();

    /// <summary>Start a detached API process and open browser after delay.</summary>
    void StartApiProcess(string projectPath);

    /// <summary>Kill the currently running API process.</summary>
    void StopApiProcess();
}
