using RomCleanup.Contracts.Models;
namespace RomCleanup.Infrastructure.Orchestration;

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

        return scannedFiles;
    }
}