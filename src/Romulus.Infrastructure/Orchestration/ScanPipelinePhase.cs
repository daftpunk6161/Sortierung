using Romulus.Contracts.Models;
namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that scans roots and returns de-duplicated, blocklist-filtered files.
/// </summary>
public sealed class ScanPipelinePhase : IPipelinePhase<RunOptions, List<ScannedFileEntry>>
{
    public string Name => "Scan";

    public List<ScannedFileEntry> Execute(RunOptions input, PipelineContext context, CancellationToken cancellationToken)
    {
        var streaming = new StreamingScanPipelinePhase(context)
            .EnumerateFilesAsync(input.Roots, input.Extensions, cancellationToken);

        var scannedFiles = new List<ScannedFileEntry>();
        var enumerator = streaming.GetAsyncEnumerator(cancellationToken);
        try
        {
            while (enumerator.MoveNextAsync().AsTask().GetAwaiter().GetResult())
                scannedFiles.Add(enumerator.Current);
        }
        finally
        {
            enumerator.DisposeAsync().AsTask().GetAwaiter().GetResult();
        }

        // Deterministic ordering: sort by normalized path so root-order permutation
        // does not affect enumeration order (fixes Scan_RootOrderPermutation test).
        scannedFiles.Sort((a, b) => string.Compare(a.Path, b.Path, StringComparison.OrdinalIgnoreCase));

        return scannedFiles;
    }
}