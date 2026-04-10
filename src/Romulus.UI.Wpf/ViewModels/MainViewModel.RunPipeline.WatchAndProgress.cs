using System.IO;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Paths;
using Romulus.UI.Wpf.Models;
using RunState = Romulus.UI.Wpf.Models.RunState;

namespace Romulus.UI.Wpf.ViewModels;

public sealed partial class MainViewModel
{
    // ═══ WATCH-MODE ════════════════════════════════════════════════════

    /// <summary>GUI-109: Start/stop periodic scheduled runs.</summary>
    public void ApplyScheduler()
    {
        _scheduleService.Stop();
        _scheduleService.IsBusyCheck = () => IsBusy;

        if (SchedulerIntervalMinutes <= 0 || Roots.Count == 0)
            return;

        if (_scheduleService.Start(SchedulerIntervalMinutes))
            AddLog(_loc.Format("Log.SchedulerActive", SchedulerIntervalDisplay), "INFO");
    }

    private void ToggleWatchMode()
        => SetWatchMode(!IsWatchModeActive, showDialog: true);

    private void SetWatchMode(bool enabled, bool showDialog)
    {
        if (Roots.Count == 0)
        {
            AddLog(_loc["Log.WatchModeNoRoots"], "WARN");
            return;
        }

        _watchService.IsBusyCheck = () => IsBusy;

        var count = enabled ? _watchService.Start(Roots) : 0;
        IsWatchModeActive = _watchService.IsActive;
        if (count == 0 || !enabled)
        {
            AddLog(_loc["Log.WatchModeDeactivated"], "INFO");
            if (showDialog)
                _dialog.Info(_loc["Dialog.Watch.Deactivated"], _loc["Dialog.Watch.Title"]);
        }
        else
        {
            AddLog(_loc.Format("Log.WatchModeActivated", count), "INFO");
            if (showDialog)
            {
                _dialog.Info(_loc.Format("Dialog.Watch.Activated", string.Join("\n", Roots)),
                    _loc["Dialog.Watch.Title"]);
            }
        }
    }

    private void OnWatchRunTriggered()
    {
        if (Roots.Count > 0)
        {
            _ = EmitCollectionHealthMonitorHintsAsync();
            AddLog(_loc["Log.WatchTriggered"], "INFO");
            DryRun = true;
            RunCommand.Execute(null);
        }
    }

    private void OnScheduledRunTriggered()
    {
        _syncContext?.Post(_ =>
        {
            if (!IsBusy && Roots.Count > 0)
            {
                _ = EmitCollectionHealthMonitorHintsAsync();
                AddLog(_loc["Log.ScheduledRunStarted"], "INFO");
                DryRun = true;
                RunCommand.Execute(null);
            }
        }, null);
    }

    internal async Task EmitCollectionHealthMonitorHintsAsync(CancellationToken cancellationToken = default)
    {
        if (Roots.Count == 0)
            return;

        var datEnabled = UseDat;
        var datRoot = DatRoot;

        List<(string Level, string Message)> hints;
        try
        {
            hints = await Task.Run(async () =>
            {
                var entries = new List<(string Level, string Message)>();

                try
                {
                    using var collectionIndex = new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath());
                    var insights = await RunHistoryInsightsService.BuildStorageInsightsAsync(collectionIndex, 14, cancellationToken);
                    if (insights.SampleCount > 1)
                    {
                        if (insights.TotalFiles.Delta != 0)
                            entries.Add(("INFO", $"[Health] Dateibestand seit letztem Snapshot: {insights.TotalFiles.Delta:+#;-#;0}."));

                        if (insights.HealthScore.Delta < 0)
                            entries.Add(("WARN", $"[Health] Health-Score gesunken ({insights.HealthScore.Delta:+#;-#;0}). DryRun + DAT-Pruefung empfohlen."));
                        else if (insights.HealthScore.Delta > 0)
                            entries.Add(("INFO", $"[Health] Health-Score verbessert ({insights.HealthScore.Delta:+#;-#;0})."));
                    }
                }
                catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                {
                    entries.Add(("DEBUG", $"[Health] Snapshot-Analyse nicht verfuegbar: {ex.Message}"));
                }

                if (datEnabled && !string.IsNullOrWhiteSpace(datRoot) && Directory.Exists(datRoot))
                {
                    try
                    {
                        var (_, localDatCount, staleDatCount) = DatAnalysisService.BuildDatAutoUpdateReport(datRoot);
                        if (localDatCount == 0)
                        {
                            entries.Add(("WARN", "[Health] Keine lokalen DAT-Dateien gefunden. DAT-Verifizierung ist eingeschraenkt."));
                        }
                        else if (staleDatCount > 0)
                        {
                            entries.Add(("WARN", $"[Health] {staleDatCount} DAT-Datei(en) sind aelter als {DatCatalogStateService.StaleThresholdDays} Tage. DAT-Update empfohlen."));
                        }
                    }
                    catch (Exception ex) when (ex is InvalidOperationException or IOException or UnauthorizedAccessException)
                    {
                        entries.Add(("DEBUG", $"[Health] DAT-Status konnte nicht geprueft werden: {ex.Message}"));
                    }
                }

                return entries;
            }, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        var dispatcher = System.Windows.Application.Current?.Dispatcher;
        if (dispatcher is null)
        {
            foreach (var (level, message) in hints)
                AddLogCore(message, level);
            return;
        }

        if (!dispatcher.CheckAccess())
        {
            await dispatcher.InvokeAsync(() =>
            {
                foreach (var (level, message) in hints)
                    AddLogCore(message, level);
            });
            return;
        }

        foreach (var (level, message) in hints)
            AddLogCore(message, level);
    }

    /// <summary>GUI-115: Dispose watch-mode and scheduler resources — unsubscribe all events.</summary>
    public void CleanupWatchers()
    {
        _watchService.RunTriggered -= OnWatchRunTriggered;
        _watchService.WatcherError -= OnWatcherError;
        _watchService.Dispose();
        _scheduleService.Triggered -= OnScheduledRunTriggered;
        _scheduleService.Dispose();
    }

    private static bool ShouldDispatchProgressMessage(
        string message,
        DateTime nowUtc,
        DateTime lastProgressUpdateUtc,
        string lastProgressPhaseKey)
    {
        if (TrySplitProgressMessage(message, out var phaseKey, out _)
            && !phaseKey.Equals(lastProgressPhaseKey, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return (nowUtc - lastProgressUpdateUtc).TotalMilliseconds >= 100;
    }

    private void ResetRunProgressEstimator()
    {
        _progressPhaseKey = string.Empty;
        _progressPhaseEventCount = 0;
        _progressPhaseStartedUtc = DateTime.MinValue;
        _progressPhaseRanges.Clear();
    }

    private void ConfigureRunProgressPlan(RunOptions options)
    {
        _progressPhaseRanges.Clear();

        const double preflightWidth = 5d;
        const double reportWidth = 5d;
        const double workRangeStart = preflightWidth;
        const double workRangeEnd = 100d - reportWidth;

        _progressPhaseRanges[UiProgressPhase.Preflight] = (0d, preflightWidth);
        _progressPhaseRanges[UiProgressPhase.Report] = (workRangeEnd, 100d);

        var activeWorkPhases = new List<UiProgressPhase>
        {
            UiProgressPhase.Scan
        };

        if (!options.ConvertOnly)
            activeWorkPhases.Add(UiProgressPhase.Dedupe);

        if (!options.ConvertOnly
            && string.Equals(options.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase))
            activeWorkPhases.Add(UiProgressPhase.Move);

        if (!options.ConvertOnly && options.SortConsole)
            activeWorkPhases.Add(UiProgressPhase.Sort);

        var includeConvertPhase = options.ConvertOnly
            || (string.Equals(options.Mode, RunConstants.ModeMove, StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(options.ConvertFormat));
        if (includeConvertPhase)
            activeWorkPhases.Add(UiProgressPhase.Convert);

        var phaseWidth = activeWorkPhases.Count == 0
            ? 0d
            : (workRangeEnd - workRangeStart) / activeWorkPhases.Count;

        var cursor = workRangeStart;
        foreach (var phase in activeWorkPhases)
        {
            var end = cursor + phaseWidth;
            _progressPhaseRanges[phase] = (cursor, end);
            cursor = end;
        }
    }

    private void ApplyProgressMessage(string message)
    {
        var phaseProgress = EstimatePhaseProgress(message);
        if (phaseProgress >= 0)
            Progress = phaseProgress;

        ProgressText = $"{Math.Round(Progress):0}%";
        UpdateCurrentRunStateFromProgress(message);
        UpdatePerfContext(message);
        AddLog(message, "INFO");
    }

    private double EstimatePhaseProgress(string message)
    {
        if (string.IsNullOrEmpty(message) || !message.StartsWith("[", StringComparison.Ordinal))
            return -1;

        var closingBracket = message.IndexOf(']');
        if (closingBracket <= 1)
            return -1;

        var phaseKey = message[..(closingBracket + 1)];
        if (!TryGetPhaseRange(phaseKey, out var rangeStart, out var rangeEnd))
            return -1;

        if (phaseKey.Equals("[Fertig]", StringComparison.OrdinalIgnoreCase))
            return 100;

        if (!phaseKey.Equals(_progressPhaseKey, StringComparison.OrdinalIgnoreCase))
        {
            _progressPhaseKey = phaseKey;
            _progressPhaseEventCount = 0;
            _progressPhaseStartedUtc = DateTime.UtcNow;
        }

        _progressPhaseEventCount++;

        if (TryParseProgressFraction(message, out var fraction))
        {
            var preciseCandidate = rangeStart + ((rangeEnd - rangeStart) * fraction);
            return Math.Min(rangeEnd, Math.Max(Progress, preciseCandidate));
        }

        if (message.Contains("Abgeschlossen", StringComparison.OrdinalIgnoreCase))
            return Math.Max(Progress, rangeEnd);

        var elapsedSeconds = _progressPhaseStartedUtc == DateTime.MinValue
            ? 0
            : (DateTime.UtcNow - _progressPhaseStartedUtc).TotalSeconds;

        // Grow conservatively when no explicit x/y progress is available.
        var eventFactor = Math.Min(1d, _progressPhaseEventCount / 120d);
        var timeFactor = Math.Min(1d, elapsedSeconds / 45d);
        var factor = Math.Max(eventFactor, timeFactor);

        var candidate = rangeStart + ((rangeEnd - rangeStart) * factor);
        return Math.Min(rangeEnd, Math.Max(Progress, candidate));
    }

    private void UpdateCurrentRunStateFromProgress(string message)
    {
        if (CurrentRunState is RunState.Completed or RunState.CompletedDryRun or RunState.Cancelled or RunState.Failed)
            return;

        if (!TrySplitProgressMessage(message, out var phaseKey, out _)
            || !TryMapMessageToPhase(phaseKey, out var phase))
        {
            return;
        }

        var targetState = phase switch
        {
            UiProgressPhase.Preflight => RunState.Preflight,
            UiProgressPhase.Scan => RunState.Scanning,
            UiProgressPhase.Dedupe => RunState.Deduplicating,
            UiProgressPhase.Move => RunState.Moving,
            UiProgressPhase.Sort => RunState.Sorting,
            UiProgressPhase.Convert => RunState.Converting,
            UiProgressPhase.Report => CurrentRunState,
            _ => CurrentRunState
        };

        if (targetState == CurrentRunState)
            return;

        if (RunStateMachine.IsValidTransition(CurrentRunState, targetState))
            CurrentRunState = targetState;
    }

    private void UpdatePerfContext(string message)
    {
        if (!TrySplitProgressMessage(message, out var phaseKey, out var detail))
            return;

        if (!TryMapMessageToPhase(phaseKey, out _))
            return;

        PerfPhase = $"Phase: {ResolvePhaseLabel(phaseKey)}";
        PerfFile = string.IsNullOrWhiteSpace(detail) ? "–" : detail;
    }

    internal static bool TrySplitProgressMessage(string message, out string phase, out string detail)
    {
        phase = string.Empty;
        detail = string.Empty;

        if (string.IsNullOrWhiteSpace(message) || !message.StartsWith("[", StringComparison.Ordinal))
            return false;

        var closingBracket = message.IndexOf(']');
        if (closingBracket <= 1)
            return false;

        phase = message[..(closingBracket + 1)];
        detail = message[(closingBracket + 1)..].Trim();
        return true;
    }

    internal static bool TryParseProgressFraction(string message, out double fraction)
    {
        fraction = 0;
        if (string.IsNullOrWhiteSpace(message))
            return false;

        var slash = message.IndexOf('/');
        if (slash <= 0 || slash >= message.Length - 1)
            return false;

        var leftEnd = slash - 1;
        while (leftEnd >= 0 && !char.IsDigit(message[leftEnd]))
            leftEnd--;

        if (leftEnd < 0)
            return false;

        var leftStart = leftEnd;
        while (leftStart >= 0 && char.IsDigit(message[leftStart]))
            leftStart--;
        leftStart++;

        var rightStart = slash + 1;
        while (rightStart < message.Length && !char.IsDigit(message[rightStart]))
            rightStart++;

        if (rightStart >= message.Length)
            return false;

        var rightEnd = rightStart;
        while (rightEnd < message.Length && char.IsDigit(message[rightEnd]))
            rightEnd++;

        if (!int.TryParse(message[leftStart..(leftEnd + 1)], out var current) ||
            !int.TryParse(message[rightStart..rightEnd], out var total) ||
            total <= 0 ||
            current < 0)
        {
            return false;
        }

        fraction = Math.Clamp((double)current / total, 0d, 1d);
        return true;
    }

    private bool TryGetPhaseRange(string phaseKey, out double start, out double end)
    {
        if (phaseKey.StartsWith("[Fertig]", StringComparison.OrdinalIgnoreCase))
        {
            start = 100d;
            end = 100d;
            return true;
        }

        if (TryMapMessageToPhase(phaseKey, out var phase)
            && _progressPhaseRanges.TryGetValue(phase, out var range))
        {
            start = range.Start;
            end = range.End;
            return true;
        }

        start = 0d;
        end = 0d;
        return false;
    }

    private static bool TryMapMessageToPhase(string phaseKey, out UiProgressPhase phase)
    {
        switch (phaseKey)
        {
            case var _ when phaseKey.StartsWith("[Preflight]", StringComparison.OrdinalIgnoreCase):
                phase = UiProgressPhase.Preflight;
                return true;
            case var _ when phaseKey.StartsWith("[Scan]", StringComparison.OrdinalIgnoreCase)
                          || phaseKey.StartsWith("[Filter]", StringComparison.OrdinalIgnoreCase):
                phase = UiProgressPhase.Scan;
                return true;
            case var _ when phaseKey.StartsWith("[Dedupe]", StringComparison.OrdinalIgnoreCase):
                phase = UiProgressPhase.Dedupe;
                return true;
            case var _ when phaseKey.StartsWith("[Junk]", StringComparison.OrdinalIgnoreCase)
                          || phaseKey.StartsWith("[Move]", StringComparison.OrdinalIgnoreCase):
                phase = UiProgressPhase.Move;
                return true;
            case var _ when phaseKey.StartsWith("[Sort]", StringComparison.OrdinalIgnoreCase):
                phase = UiProgressPhase.Sort;
                return true;
            case var _ when phaseKey.StartsWith("[Convert]", StringComparison.OrdinalIgnoreCase):
                phase = UiProgressPhase.Convert;
                return true;
            case var _ when phaseKey.StartsWith("[Report]", StringComparison.OrdinalIgnoreCase)
                          || phaseKey.StartsWith("[Fertig]", StringComparison.OrdinalIgnoreCase):
                phase = UiProgressPhase.Report;
                return true;
            default:
                phase = default;
                return false;
        }
    }

    private string ResolvePhaseLabel(string phaseKey)
    {
        return TryMapMessageToPhase(phaseKey, out var phase)
            ? phase switch
            {
                UiProgressPhase.Preflight => _loc["Phase.Preflight"],
                UiProgressPhase.Scan => _loc["Phase.Scan"],
                UiProgressPhase.Dedupe => _loc["Phase.Dedupe"],
                UiProgressPhase.Move => _loc["Phase.Move"],
                UiProgressPhase.Sort => _loc["Phase.Sort"],
                UiProgressPhase.Convert => _loc["Phase.Convert"],
                UiProgressPhase.Report => _loc["Phase.Done"],
                _ => phaseKey
            }
            : phaseKey;
    }

    private static int GetConsoleSortFailureCount(object sortResult)
    {
        var type = sortResult.GetType();

        if (type.GetProperty("Failed")?.GetValue(sortResult) is int failed)
            return failed;

        if (type.GetProperty("FailCount")?.GetValue(sortResult) is int failCount)
            return failCount;

        return 0;
    }
}
