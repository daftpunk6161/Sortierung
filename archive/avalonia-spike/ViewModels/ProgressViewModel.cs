using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace Romulus.UI.Avalonia.ViewModels;

public sealed class ProgressViewModel : ObservableObject
{
    private double _progress;
    private string _progressText = "0%";
    private string _busyHint = "Bereit";
    private string _currentPhase = "Idle";
    private bool _isRunning;

    public double Progress
    {
        get => _progress;
        set
        {
            var clamped = Math.Clamp(value, 0d, 100d);
            if (!SetProperty(ref _progress, clamped))
                return;

            ProgressText = $"{clamped:0}%";
        }
    }

    public string ProgressText
    {
        get => _progressText;
        private set => SetProperty(ref _progressText, value);
    }

    public string BusyHint
    {
        get => _busyHint;
        private set => SetProperty(ref _busyHint, value);
    }

    public string CurrentPhase
    {
        get => _currentPhase;
        private set => SetProperty(ref _currentPhase, value);
    }

    public bool IsRunning
    {
        get => _isRunning;
        private set => SetProperty(ref _isRunning, value);
    }

    public ObservableCollection<string> LiveLog { get; } = [];

    public void BeginPreviewRun(int rootCount)
    {
        Reset();
        IsRunning = true;
        BusyHint = $"Preview läuft mit {rootCount} Root(s)";
        CurrentPhase = "Preflight";
        AppendLog("[Preflight] Konfiguration validiert");
    }

    public void UpdateProgress(double progress, string phase, string message)
    {
        Progress = progress;
        CurrentPhase = phase;
        AppendLog($"[{phase}] {message}");
    }

    public void Complete(string summaryText)
    {
        IsRunning = false;
        BusyHint = "Lauf abgeschlossen";
        CurrentPhase = "Done";
        Progress = 100d;
        AppendLog($"[Done] {summaryText}");
    }

    public void Cancel()
    {
        IsRunning = false;
        BusyHint = "Abbruch angefordert";
        CurrentPhase = "Cancelled";
        AppendLog("[Cancelled] Benutzerabbruch");
    }

    public void Reset()
    {
        Progress = 0d;
        BusyHint = "Bereit";
        CurrentPhase = "Idle";
        IsRunning = false;
        LiveLog.Clear();
    }

    private void AppendLog(string line)
        => LiveLog.Add(line);
}
