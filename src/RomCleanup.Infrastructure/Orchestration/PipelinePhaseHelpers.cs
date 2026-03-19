using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

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
            context.AuditStore.AppendAuditRow(options.AuditPath, root, sourcePath, targetPath, "CONVERT", "GAME", "", $"format-convert:{toolName}");
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

        var root = FindRootForPath(sourcePath, options.Roots);
        if (root is null)
            return;

        var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
        var trashDir = Path.Combine(trashBase, "_TRASH_CONVERTED");
        context.FileSystem.EnsureDirectory(trashDir);
        var fileName = Path.GetFileName(sourcePath);
        var trashDest = context.FileSystem.ResolveChildPathWithinRoot(trashBase, Path.Combine("_TRASH_CONVERTED", fileName));
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
}
