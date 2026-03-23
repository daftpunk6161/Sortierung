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

    [Obsolete("Use Build(RunOptions, StandardPhaseStepActions) instead.")]
    IReadOnlyList<IPhaseStep> BuildStandard(RunOptions options, StandardPhaseStepActions actions);
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
    public IReadOnlyList<DedupeResult>? AllGroups { get; private set; }
    public IReadOnlyList<DedupeResult>? GameGroups { get; private set; }
    public IReadOnlySet<string>? JunkRemovedPaths { get; private set; }

    public void SetScanOutput(IReadOnlyList<RomCandidate> allCandidates, IReadOnlyList<RomCandidate> processingCandidates)
    {
        if (AllCandidates is not null || ProcessingCandidates is not null)
            throw new InvalidOperationException("Scan output was already assigned.");

        AllCandidates = allCandidates;
        ProcessingCandidates = processingCandidates;
    }

    public void SetDedupeOutput(IReadOnlyList<DedupeResult> allGroups, IReadOnlyList<DedupeResult> gameGroups)
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
}

public sealed record ScanPhaseResult(
    IReadOnlyList<RomCandidate> AllCandidates,
    IReadOnlyList<RomCandidate> ProcessingCandidates,
    int UnknownCount,
    IReadOnlyDictionary<string, int> UnknownReasonCounts,
    int FilteredNonGameCount);

public sealed record DedupePhaseResult(
    IReadOnlyList<DedupeResult> AllGroups,
    IReadOnlyList<DedupeResult> GameGroups,
    int LoserCount);

public sealed record JunkPhaseResult(
    MovePhaseResult MoveResult,
    IReadOnlySet<string> RemovedPaths);

public sealed class StandardPhaseStepActions
{
    public required Func<PipelineState, CancellationToken, PhaseStepResult> Deduplicate { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> JunkRemoval { get; init; }
    public Func<PipelineState, CancellationToken, PhaseStepResult>? DatRename { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> Move { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> ConsoleSort { get; init; }
    public required Func<PipelineState, CancellationToken, PhaseStepResult> WinnerConversion { get; init; }
}

public sealed class DeduplicatePhaseStep : IPhaseStep
{
    private readonly Func<PipelineState, CancellationToken, PhaseStepResult> _execute;

    public DeduplicatePhaseStep(Func<PipelineState, CancellationToken, PhaseStepResult> execute)
    {
        _execute = execute;
    }

    public string Name => "Deduplicate";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        => _execute(state, cancellationToken);
}

public sealed class JunkRemovalPhaseStep : IPhaseStep
{
    private readonly Func<PipelineState, CancellationToken, PhaseStepResult> _execute;

    public JunkRemovalPhaseStep(Func<PipelineState, CancellationToken, PhaseStepResult> execute)
    {
        _execute = execute;
    }

    public string Name => "JunkRemoval";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        => _execute(state, cancellationToken);
}

public sealed class MovePhaseStep : IPhaseStep
{
    private readonly Func<PipelineState, CancellationToken, PhaseStepResult> _execute;

    public MovePhaseStep(Func<PipelineState, CancellationToken, PhaseStepResult> execute)
    {
        _execute = execute;
    }

    public string Name => "Move";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        => _execute(state, cancellationToken);
}

public sealed class DatRenamePhaseStep : IPhaseStep
{
    private readonly Func<PipelineState, CancellationToken, PhaseStepResult> _execute;

    public DatRenamePhaseStep(Func<PipelineState, CancellationToken, PhaseStepResult> execute)
    {
        _execute = execute;
    }

    public string Name => "DatRename";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        => _execute(state, cancellationToken);
}

public sealed class ConsoleSortPhaseStep : IPhaseStep
{
    private readonly Func<PipelineState, CancellationToken, PhaseStepResult> _execute;

    public ConsoleSortPhaseStep(Func<PipelineState, CancellationToken, PhaseStepResult> execute)
    {
        _execute = execute;
    }

    public string Name => "ConsoleSort";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        => _execute(state, cancellationToken);
}

public sealed class WinnerConversionPhaseStep : IPhaseStep
{
    private readonly Func<PipelineState, CancellationToken, PhaseStepResult> _execute;

    public WinnerConversionPhaseStep(Func<PipelineState, CancellationToken, PhaseStepResult> execute)
    {
        _execute = execute;
    }

    public string Name => "WinnerConversion";

    public PhaseStepResult Execute(PipelineState state, CancellationToken cancellationToken)
        => _execute(state, cancellationToken);
}

public sealed class PhasePlanBuilder : IPhasePlanBuilder
{
    public IReadOnlyList<IPhaseStep> Build(RunOptions options, StandardPhaseStepActions actions)
    {
        var phases = new List<IPhaseStep>
        {
            new DeduplicatePhaseStep(actions.Deduplicate),
            new JunkRemovalPhaseStep(actions.JunkRemoval)
        };

        if (options.EnableDatRename && options.Mode == "Move" && actions.DatRename is not null)
            phases.Add(new DatRenamePhaseStep(actions.DatRename));

        if (options.Mode == "Move")
            phases.Add(new MovePhaseStep(actions.Move));

        if (options.SortConsole && options.Mode == "Move")
            phases.Add(new ConsoleSortPhaseStep(actions.ConsoleSort));

        if (options.ConvertFormat is not null && options.Mode == "Move")
            phases.Add(new WinnerConversionPhaseStep(actions.WinnerConversion));

        return phases;
    }

    public IReadOnlyList<IPhaseStep> BuildStandard(RunOptions options, StandardPhaseStepActions actions)
        => Build(options, actions);
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
