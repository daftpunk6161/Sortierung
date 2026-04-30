using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.UI.Wpf.ViewModels;

/// <summary>
/// T-W5-BEFORE-AFTER-SIMULATOR pass 4 — WPF projection over
/// <see cref="IBeforeAfterSimulator"/>. Drives the two-list before/after
/// view: <see cref="BeforeItems"/> mirrors the current library state,
/// <see cref="AfterItems"/> shows what survives the planned run
/// (Remove entries are filtered out — that is the only diff projection).
/// Summary numbers are taken verbatim from the simulator result; the VM
/// MUST NEVER recompute them locally so we keep "eine fachliche Wahrheit"
/// across CLI / GUI / Reports.
/// </summary>
public sealed class SimulatorViewModel : ObservableObject
{
    private readonly IBeforeAfterSimulator _simulator;
    private BeforeAfterSummary _summary = new(0, 0, 0, 0, 0, 0, 0L);
    private bool _hasResult;
    private string? _lastError;

    public SimulatorViewModel(IBeforeAfterSimulator simulator)
    {
        ArgumentNullException.ThrowIfNull(simulator);
        _simulator = simulator;
    }

    /// <summary>Current library snapshot — every source path the simulator saw.</summary>
    public ObservableCollection<BeforeAfterEntry> BeforeItems { get; } = [];

    /// <summary>Projected after-state — every entry that survives the plan
    /// (Keep, Convert, Rename). Remove entries are filtered out.</summary>
    public ObservableCollection<BeforeAfterEntry> AfterItems { get; } = [];

    public BeforeAfterSummary Summary
    {
        get => _summary;
        private set => SetProperty(ref _summary, value);
    }

    public bool HasResult
    {
        get => _hasResult;
        private set => SetProperty(ref _hasResult, value);
    }

    public string? LastError
    {
        get => _lastError;
        private set => SetProperty(ref _lastError, value);
    }

    /// <summary>
    /// Runs the simulator with the supplied <paramref name="options"/> and
    /// republishes the projection. Options are forwarded verbatim — the
    /// simulator owns the <c>ForceDryRun</c> chokepoint, the VM does NOT
    /// pre-mutate them.
    /// </summary>
    public void Refresh(RunOptions options, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        BeforeAfterSimulationResult result;
        try
        {
            result = _simulator.Simulate(options, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            LastError = ex.Message;
            BeforeItems.Clear();
            AfterItems.Clear();
            Summary = new BeforeAfterSummary(0, 0, 0, 0, 0, 0, 0L);
            HasResult = false;
            return;
        }

        LastError = null;
        BeforeItems.Clear();
        AfterItems.Clear();
        foreach (var item in result.Items)
        {
            BeforeItems.Add(item);
            if (item.Action != BeforeAfterAction.Remove)
                AfterItems.Add(item);
        }
        Summary = result.Summary;
        HasResult = true;
    }

    public void Reset()
    {
        BeforeItems.Clear();
        AfterItems.Clear();
        Summary = new BeforeAfterSummary(0, 0, 0, 0, 0, 0, 0L);
        HasResult = false;
        LastError = null;
    }
}
