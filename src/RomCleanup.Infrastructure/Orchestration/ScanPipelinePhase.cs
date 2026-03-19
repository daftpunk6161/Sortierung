using RomCleanup.Contracts.Models;
using RomCleanup.Core.SetParsing;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Pipeline phase that scans roots and returns de-duplicated, blocklist-filtered files.
/// </summary>
public sealed class ScanPipelinePhase : IPipelinePhase<RunOptions, List<ScannedFileEntry>>
{
    public string Name => "Scan";

    public List<ScannedFileEntry> Execute(RunOptions input, PipelineContext context, CancellationToken cancellationToken)
    {
        var scannedFiles = new List<ScannedFileEntry>();
        var seenCandidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var setMemberPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in input.Roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            context.OnProgress?.Invoke($"[Scan] {root}: Dateien sammeln…");

            var files = context.FileSystem.GetFilesSafe(root, input.Extensions);
            context.OnProgress?.Invoke($"[Scan] {root}: {files.Count} Dateien gefunden");

            int processed = 0;
            int fileCount = files.Count;
            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedPath = Path.GetFullPath(filePath);
                if (!seenCandidatePaths.Add(normalizedPath))
                    continue;

                if (ExecutionHelpers.IsBlocklisted(filePath))
                    continue;

                processed++;
                if (processed % 500 == 0)
                    context.OnProgress?.Invoke($"[Scan] {processed}/{fileCount} Dateien verarbeitet…");

                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                var setMembers = GetSetMembers(filePath, ext);
                foreach (var member in setMembers)
                    setMemberPaths.Add(Path.GetFullPath(member));

                scannedFiles.Add(new ScannedFileEntry(root, normalizedPath, ext));
            }
        }

        if (setMemberPaths.Count > 0)
            scannedFiles.RemoveAll(f => setMemberPaths.Contains(f.Path));

        return scannedFiles;
    }

    private static IReadOnlyList<string> GetSetMembers(string filePath, string ext)
    {
        return ext switch
        {
            ".cue" => CueSetParser.GetRelatedFiles(filePath),
            ".gdi" => GdiSetParser.GetRelatedFiles(filePath),
            ".ccd" => CcdSetParser.GetRelatedFiles(filePath),
            ".m3u" => M3uPlaylistParser.GetRelatedFiles(filePath),
            _ => Array.Empty<string>()
        };
    }
}

public sealed record ScannedFileEntry(
    string Root,
    string Path,
    string Extension);