namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Wave-2 F-06: lifecycle abstraction for the detached REST-API process previously
/// managed inline in MainWindow.xaml.cs. Centralises start/stop semantics, tracking
/// lease management, and post-start browser launch so MainWindow stays a thin host
/// and the lifecycle becomes unit-testable.
/// </summary>
public interface IApiProcessHost
{
    /// <summary>
    /// Starts the API process from the given project path. Any existing process is
    /// terminated first. Returns true when the new process handle is alive.
    /// </summary>
    bool Start(string projectPath);

    /// <summary>
    /// Best-effort termination of the currently tracked process.
    /// </summary>
    void Stop();
}
