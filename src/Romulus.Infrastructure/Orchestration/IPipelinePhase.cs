using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metrics;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Generic contract for a single pipeline phase in the run orchestration flow.
/// </summary>
public interface IPipelinePhase<in TIn, out TOut>
{
    string Name { get; }

    TOut Execute(TIn input, PipelineContext context, CancellationToken cancellationToken);
}

/// <summary>
/// Shared services and state passed to pipeline phases.
/// </summary>
public sealed class PipelineContext
{
    public required RunOptions Options { get; init; }

    public required IFileSystem FileSystem { get; init; }

    public required IAuditStore AuditStore { get; init; }

    public required PhaseMetricsCollector Metrics { get; init; }

    public Action<string>? OnProgress { get; init; }
}