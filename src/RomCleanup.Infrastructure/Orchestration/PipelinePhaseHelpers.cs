using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.SetParsing;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Shared helpers for pipeline phases. Centralizes FindRootForPath and
/// conversion audit methods to eliminate code duplication across phases.
/// </summary>
internal static class PipelinePhaseHelpers
{
    /// <summary>
    /// Finds which configured root directory contains the given file path.
    /// SEC-MOVE-01: Appends separator to prevent C:\Roms matching C:\Roms-Other.
    /// </summary>
    internal static string? FindRootForPath(string filePath, IReadOnlyList<string> roots)
    {
        var fullPath = Path.GetFullPath(filePath);
        foreach (var root in roots)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                               + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return root;
        }

        return null;
    }

    internal static void AppendConversionAudit(PipelineContext context, RunOptions options, string sourcePath, string? targetPath, string toolName)
    {
        if (string.IsNullOrEmpty(options.AuditPath) || string.IsNullOrEmpty(targetPath))
            return;

        var root = FindRootForPath(sourcePath, options.Roots);
        if (root is not null)
            context.AuditStore.AppendAuditRow(options.AuditPath, root, sourcePath, targetPath, RunConstants.AuditActions.Convert, "GAME", "", $"format-convert:{toolName}");
    }

    internal static void AppendConversionFailedAudit(PipelineContext context, RunOptions options, string sourcePath, string? targetPath, string toolName)
    {
        if (string.IsNullOrEmpty(options.AuditPath) || string.IsNullOrEmpty(targetPath))
            return;

        var root = FindRootForPath(sourcePath, options.Roots);
        if (root is not null)
            context.AuditStore.AppendAuditRow(options.AuditPath, root, sourcePath, targetPath, "CONVERT_FAILED", "GAME", "", $"verify-failed:{toolName}");
    }

    internal static void AppendConversionErrorAudit(PipelineContext context, RunOptions options, string sourcePath, string? reason)
    {
        if (string.IsNullOrEmpty(options.AuditPath))
            return;

        var root = FindRootForPath(sourcePath, options.Roots);
        if (root is not null)
            context.AuditStore.AppendAuditRow(options.AuditPath, root, sourcePath, "", "CONVERT_ERROR", "GAME", "", $"convert-error:{reason}");
    }

    internal static void MoveConvertedSourceToTrash(PipelineContext context, RunOptions options, string sourcePath, string? convertedPath)
    {
        if (string.IsNullOrEmpty(convertedPath) || !File.Exists(convertedPath))
            return;

        // SEC-CONV-08: Verify converted output is a real file, not a reparse point/junction
        try
        {
            var attrs = File.GetAttributes(convertedPath);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                context.OnProgress?.Invoke($"WARNING: Converted output is a reparse point, source not trashed: {convertedPath}");
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.OnProgress?.Invoke($"WARNING: Cannot verify converted output, source not trashed: {convertedPath}");
            return;
        }

        var root = FindRootForPath(sourcePath, options.Roots);
        if (root is null)
            return;

        var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
        var trashDir = Path.Combine(trashBase, RunConstants.WellKnownFolders.TrashConverted);
        context.FileSystem.EnsureDirectory(trashDir);
        var fileName = Path.GetFileName(sourcePath);
        var trashDest = context.FileSystem.ResolveChildPathWithinRoot(trashBase, Path.Combine(RunConstants.WellKnownFolders.TrashConverted, fileName));
        if (trashDest is null)
            return;

        try
        {
            context.FileSystem.MoveItemSafely(sourcePath, trashDest);
        }
        catch (Exception ex)
        {
            context.OnProgress?.Invoke($"WARNING: Could not move source after conversion: {ex.Message}");
        }
    }

    /// <summary>
    /// Resolves set members (multi-file disc sets) for a given file extension.
    /// <paramref name="includeM3uMembers"/>: when true, parses .m3u playlist members;
    /// when false, treats .m3u targets as standalone scan candidates for DAT/hash matching.
    /// </summary>
    internal static IReadOnlyList<string> GetSetMembers(string filePath, string ext, bool includeM3uMembers = true)
    {
        return ext switch
        {
            ".cue" => CueSetParser.GetRelatedFiles(filePath),
            ".gdi" => GdiSetParser.GetRelatedFiles(filePath),
            ".ccd" => CcdSetParser.GetRelatedFiles(filePath),
            ".m3u" => includeM3uMembers ? M3uPlaylistParser.GetRelatedFiles(filePath) : Array.Empty<string>(),
            _ => Array.Empty<string>()
        };
    }
}
