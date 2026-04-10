using System.Runtime.CompilerServices;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Streaming scan phase that exposes async file enumeration via contract port.
/// Keeps existing scan safety filters (blocklist, duplicate path guard, set-member pruning).
/// </summary>
public sealed class StreamingScanPipelinePhase : IAsyncFileScanner
{
    private readonly PipelineContext _context;

    public StreamingScanPipelinePhase(PipelineContext context)
    {
        _context = context;
    }

    public async IAsyncEnumerable<ScannedFileEntry> EnumerateFilesAsync(
        IReadOnlyList<string> roots,
        IReadOnlyCollection<string> extensions,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var effectiveExtensions = ExpandSetMemberExtensions(extensions);
        var candidates = new List<ScannedFileEntry>();
        var seenCandidatePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var setMemberPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var root in roots)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _context.OnProgress?.Invoke($"[Scan] {root}: Dateien sammeln…");
            var normalizedRoot = Path.GetFullPath(root);

            var files = _context.FileSystem.GetFilesSafe(root, effectiveExtensions);
            var scanWarnings = _context.FileSystem.ConsumeScanWarnings();
            foreach (var warning in scanWarnings)
                _context.OnProgress?.Invoke($"WARNING: {warning}");

            _context.OnProgress?.Invoke($"[Scan] {root}: {files.Count} Dateien gefunden");

            var processed = 0;
            var fileCount = files.Count;

            foreach (var filePath in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var normalizedPath = Path.GetFullPath(filePath);
                if (!seenCandidatePaths.Add(normalizedPath))
                    continue;

                if (ExecutionHelpers.IsBlocklisted(filePath))
                    continue;

                // TASK-020: Non-ROM extension pre-filter — skip known non-ROM file types early
                var ext = Path.GetExtension(filePath).ToLowerInvariant();
                if (FileClassifier.IsNonRomExtension(ext))
                    continue;

                processed++;
                if (processed % 500 == 0)
                    _context.OnProgress?.Invoke($"[Scan] {processed}/{fileCount} Dateien verarbeitet…");

                var setMembers = PipelinePhaseHelpers.GetSetMembers(filePath, ext, includeM3uMembers: false);
                foreach (var member in setMembers)
                    setMemberPaths.Add(Path.GetFullPath(member));

                candidates.Add(new ScannedFileEntry(
                    normalizedRoot,
                    normalizedPath,
                    ext,
                    TryGetSizeBytes(normalizedPath),
                    TryGetLastWriteUtc(normalizedPath)));
            }
        }

        foreach (var candidate in candidates)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (setMemberPaths.Contains(candidate.Path))
                continue;

            yield return candidate;
            await Task.Yield();
        }
    }

    private static long? TryGetSizeBytes(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).Length : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static DateTime? TryGetLastWriteUtc(string path)
    {
        try
        {
            return File.Exists(path) ? new FileInfo(path).LastWriteTimeUtc : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static IReadOnlyCollection<string> ExpandSetMemberExtensions(IReadOnlyCollection<string> extensions)
    {
        if (extensions.Count == 0)
            return Array.Empty<string>();

        var expanded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var extension in extensions)
        {
            if (string.IsNullOrWhiteSpace(extension))
                continue;

            var normalized = extension.StartsWith('.') ? extension : "." + extension;
            expanded.Add(normalized.ToLowerInvariant());
        }

        if (expanded.Contains(".cue"))
        {
            expanded.Add(".bin");
            expanded.Add(".wav");
            expanded.Add(".iso");
        }

        if (expanded.Contains(".gdi"))
        {
            expanded.Add(".bin");
            expanded.Add(".raw");
            expanded.Add(".iso");
        }

        if (expanded.Contains(".ccd"))
        {
            expanded.Add(".img");
            expanded.Add(".sub");
        }

        if (expanded.Contains(".mds"))
            expanded.Add(".mdf");

        if (expanded.Contains(".m3u") || expanded.Contains(".m3u8"))
        {
            expanded.Add(".cue");
            expanded.Add(".gdi");
            expanded.Add(".ccd");
            expanded.Add(".mds");
        }

        return expanded;
    }
}
