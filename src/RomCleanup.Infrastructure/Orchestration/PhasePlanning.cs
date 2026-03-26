using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.Orchestration;

public interface IPhaseStep
{
    string Name { get; }
    PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken);
}

public interface IPhasePlanBuilder
{
    IReadOnlyList<IPhaseStep> Build(RunOptions options, StandardPhaseStepActions actions);
}

public sealed class PhaseStepResult
{
    public required string Status { get; init; }
    public required int ItemCount { get; init; }
    public object? TypedResult { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();

    public static PhaseStepResult Ok(int itemCount = 0, object? typedResult = null)
        => new() { Status = "ok", ItemCount = itemCount, TypedResult = typedResult };

    public static PhaseStepResult Skipped(object? typedResult = null)
        => new() { Status = "skipped", ItemCount = 0, TypedResult = typedResult };
}

public sealed class PipelineState
{
    public IReadOnlyList<RomCandidate>? AllCandidates { get; private set; }
    public IReadOnlyList<RomCandidate>? ProcessingCandidates { get; private set; }
    public IReadOnlyList<DedupeGroup>? AllGroups { get; private set; }
    public IReadOnlyList<DedupeGroup>? GameGroups { get; private set; }
    public IReadOnlySet<string>? JunkRemovedPaths { get; private set; }
    public DatAuditResult? DatAuditResult { get; private set; }
    public DatRenameResult? DatRenameResult { get; private set; }

    public void SetScanOutput(IReadOnlyList<RomCandidate> allCandidates, IReadOnlyList<RomCandidate> processingCandidates)
    {
        if (AllCandidates is not null || ProcessingCandidates is not null)
            throw new InvalidOperationException("Scan output was already assigned.");

        AllCandidates = allCandidates;
        ProcessingCandidates = processingCandidates;
    }

    public void SetDedupeOutput(IReadOnlyList<DedupeGroup> allGroups, IReadOnlyList<DedupeGroup> gameGroups)
    {
        if (AllGroups is not null || GameGroups is not null)
            throw new InvalidOperationException("Dedupe output was already assigned.");

        AllGroups = allGroups;
        GameGroups = gameGroups;
    }

    public void SetJunkPaths(IReadOnlySet<string> junkRemovedPaths)
    {
        if (JunkRemovedPaths is not null)
            throw new InvalidOperationException("Junk output was already assigned.");

        JunkRemovedPaths = junkRemovedPaths;
    }

    public void SetDatAuditOutput(DatAuditResult result)
    {
        if (DatAuditResult is not null)
            throw new InvalidOperationException("DatAudit output was already assigned.");

        DatAuditResult = result;
    }

    public void SetDatRenameOutput(DatRenameResult result)
    {
        if (DatRenameResult is not null)
            throw new InvalidOperationException("DatRename output was already assigned.");

        DatRenameResult = result;
    }
}

public sealed record ScanPhaseResult(
    IReadOnlyList<RomCandidate> AllCandidates,
    IReadOnlyList<RomCandidate> ProcessingCandidates,
    int UnknownCount,
    IReadOnlyDictionary<string, int> UnknownReasonCounts,
    int FilteredNonGameCount);

public sealed record DedupePhaseResult(
    IReadOnlyList<DedupeGroup> AllGroups,
    IReadOnlyList<DedupeGroup> GameGroups,
    int LoserCount);

public sealed record JunkPhaseResult(
    MovePhaseResult MoveResult,
    IReadOnlySet<string> RemovedPaths);

public sealed class StandardPhaseStepActions
{
    public Func<PipelineState, CancellationToken, PhaseStepResult>? DatAudit { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> Deduplicate { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> JunkRemoval { get; init; }
    public Func<PipelineState, CancellationToken, PhaseStepResult>? DatRename { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> Move { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> ConsoleSort { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> WinnerConversion { get; init; }
}

/// <summary>
/// Generic action-based phase step that eliminates boilerplate wrapper classes.
/// Replaces the former per-phase classes (DatAuditPhaseStep, DeduplicatePhaseStep, etc.).
/// </summary>
public sealed class ActionPhaseStep : IPhaseStep
{
    private readonly Func<PipelineState, CancellationToken, PhaseStepResult> _execute;

    public ActionPhaseStep(string name, Func<PipelineState, CancellationToken, PhaseStepResult> execute)
    {
        Name = name;
        _execute = execute;
    }

    public string Name { get; }

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        => _execute(state, cancellationToken);
}

public sealed class PhasePlanBuilder : IPhasePlanBuilder
{
    public IReadOnlyList<IPhaseStep> Build(RunOptions options, StandardPhaseStepActions actions)
    {
        var phases = new List<IPhaseStep>();

        if (options.EnableDatAudit && actions.DatAudit is not null)
            phases.Add(new ActionPhaseStep("DatAudit", actions.DatAudit));

        phases.Add(new ActionPhaseStep("Deduplicate", actions.Deduplicate));
        phases.Add(new ActionPhaseStep("JunkRemoval", actions.JunkRemoval));

        if (options.EnableDatRename && options.Mode == "Move" && actions.DatRename is not null)
            phases.Add(new ActionPhaseStep("DatRename", actions.DatRename));

        if (options.Mode == "Move")
            phases.Add(new ActionPhaseStep("Move", actions.Move));

        if (options.SortConsole && options.Mode == "Move")
            phases.Add(new ActionPhaseStep("ConsoleSort", actions.ConsoleSort));

        if (options.ConvertFormat is not null && options.Mode == "Move")
            phases.Add(new ActionPhaseStep("WinnerConversion", actions.WinnerConversion));

        return phases;
    }
}

public sealed class DeferredAnalysisPhaseStep : IPhaseStep
{
    private readonly Action<PipelineState, CancellationToken> _execute;

    public DeferredAnalysisPhaseStep(Action<PipelineState, CancellationToken> execute)
    {
        _execute = execute;
    }

    public string Name => "DeferredAnalysis";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
    {
        _execute(state, cancellationToken);
        return PhaseStepResult.Ok();
    }
}

public sealed class ReportPhaseStep : IPhaseStep
{
    private readonly Func<string?> _execute;

    public ReportPhaseStep(Func<string?> execute)
    {
        _execute = execute;
    }

    public string Name => "Report";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var reportPath = _execute();
        return PhaseStepResult.Ok(reportPath is null ? 0 : 1, reportPath);
    }
}

public sealed class AuditSealPhaseStep : IPhaseStep
{
    private readonly Action _execute;

    public AuditSealPhaseStep(Action execute)
    {
        _execute = execute;
    }

    public string Name => "AuditSeal";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _execute();
        return PhaseStepResult.Ok();
    }
}
