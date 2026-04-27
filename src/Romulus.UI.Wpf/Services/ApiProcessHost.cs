using System.Diagnostics;
using System.Windows;
using Romulus.Infrastructure.Tools;

namespace Romulus.UI.Wpf.Services;

/// <summary>
/// Wave-2 F-06: production implementation of <see cref="IApiProcessHost"/>.
/// Encapsulates dotnet run + ExternalProcessGuard tracking + delayed browser
/// launch. All UI-thread coupling is reduced to a single dispatcher hop for the
/// post-start browser launch (timed delay).
///
/// Kept process-launch primitives behind <see cref="IProcessLauncher"/> so the
/// host's lifecycle is exercisable without spawning real processes.
/// </summary>
public sealed class ApiProcessHost : IApiProcessHost, IDisposable
{
    private readonly IProcessLauncher _launcher;
    private readonly Action<string, string> _log;
    private readonly TimeSpan _browserLaunchDelay;
    private readonly string _healthUrl;

    private Process? _process;
    private IDisposable? _trackingLease;

    public ApiProcessHost(IProcessLauncher launcher, Action<string, string> log)
        : this(launcher, log, TimeSpan.FromSeconds(2), "http://127.0.0.1:5000/health")
    {
    }

    internal ApiProcessHost(IProcessLauncher launcher, Action<string, string> log, TimeSpan browserLaunchDelay, string healthUrl)
    {
        _launcher = launcher ?? throw new ArgumentNullException(nameof(launcher));
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _browserLaunchDelay = browserLaunchDelay;
        _healthUrl = healthUrl;
    }

    public bool Start(string projectPath)
    {
        if (string.IsNullOrWhiteSpace(projectPath))
        {
            _log("REST API Start fehlgeschlagen: kein Projektpfad.", "WARN");
            return false;
        }

        Stop();

        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            Arguments = $"run --project \"{projectPath}\"",
            UseShellExecute = false,
            CreateNoWindow = false,
        };

        _process = _launcher.Start(psi);
        if (_process is null)
        {
            _log("REST API Start fehlgeschlagen: Prozess konnte nicht gestartet werden.", "WARN");
            return false;
        }

        _trackingLease = ExternalProcessGuard.Track(_process, "api-process", msg => _log(msg, "WARN"));
        _log("REST API gestartet: http://127.0.0.1:5000", "INFO");

        _ = Task.Delay(_browserLaunchDelay).ContinueWith(_ =>
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                _launcher.OpenBrowser(_healthUrl);
                return;
            }
            dispatcher.InvokeAsync(() => _launcher.OpenBrowser(_healthUrl));
        }, TaskScheduler.Default);

        return true;
    }

    public void Stop()
    {
        var proc = _process;
        _process = null;
        _trackingLease?.Dispose();
        _trackingLease = null;
        if (proc is null) return;

        try
        {
            if (!proc.HasExited)
            {
                proc.Kill(entireProcessTree: true);
                if (!proc.WaitForExit(5000))
                    _log("API process did not exit within 5 s after kill", "WARN");
            }
        }
        catch (InvalidOperationException) { /* already exited */ }
        catch (System.ComponentModel.Win32Exception ex) { _log($"API process kill failed: {ex.Message}", "WARN"); }

        try { proc.Dispose(); }
        catch (InvalidOperationException) { /* already disposed */ }
    }

    public void Dispose() => Stop();
}

/// <summary>
/// Seam for unit-testing <see cref="ApiProcessHost"/> without spawning real processes.
/// </summary>
public interface IProcessLauncher
{
    Process? Start(ProcessStartInfo info);
    void OpenBrowser(string url);
}

/// <summary>
/// Default production launcher backed by <see cref="Process.Start(ProcessStartInfo)"/>.
/// </summary>
public sealed class DefaultProcessLauncher : IProcessLauncher
{
    public Process? Start(ProcessStartInfo info) => Process.Start(info);

    public void OpenBrowser(string url)
    {
        try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); }
        catch { /* browser launch failed */ }
    }
}
