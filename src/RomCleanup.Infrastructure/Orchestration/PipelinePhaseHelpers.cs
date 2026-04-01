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

    internal static void AppendConversionSourceAudit(
        PipelineContext context,
        RunOptions options,
        string sourcePath,
        string? trashPath,
        string category,
        string reason)
    {
        if (string.IsNullOrEmpty(options.AuditPath) || string.IsNullOrEmpty(trashPath))
            return;

        var root = FindRootForPath(sourcePath, options.Roots);
        if (root is not null)
        {
            context.AuditStore.AppendAuditRow(
                options.AuditPath,
                root,
                sourcePath,
                trashPath,
                RunConstants.AuditActions.ConvertSource,
                category,
                "",
                reason);
        }
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

    internal static string? MoveConvertedSourceToTrash(PipelineContext context, RunOptions options, string sourcePath, string? convertedPath)
    {
        if (string.IsNullOrEmpty(convertedPath) || !File.Exists(convertedPath))
            return null;

        // SEC-CONV-08: Verify converted output is a real file, not a reparse point/junction
        try
        {
            var attrs = File.GetAttributes(convertedPath);
            if ((attrs & FileAttributes.ReparsePoint) != 0)
            {
                context.OnProgress?.Invoke($"WARNING: Converted output is a reparse point, source not trashed: {convertedPath}");
                return null;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            context.OnProgress?.Invoke($"WARNING: Cannot verify converted output, source not trashed: {convertedPath}");
            return null;
        }

        if (!TryMovePathToConvertedTrash(context, options, sourcePath, out var trashDest, out var failureReason))
        {
            if (!string.IsNullOrWhiteSpace(failureReason))
                context.OnProgress?.Invoke($"WARNING: Could not move source after conversion: {failureReason}");
            return null;
        }

        return trashDest;
    }

    internal static bool TryMovePathToConvertedTrash(
        PipelineContext context,
        RunOptions options,
        string sourcePath,
        out string? trashDestinationPath,
        out string? failureReason)
    {
        trashDestinationPath = null;
        failureReason = null;

        var root = FindRootForPath(sourcePath, options.Roots);
        if (root is null)
        {
            failureReason = $"path-not-within-allowed-roots:{sourcePath}";
            return false;
        }

        var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
        var trashDir = Path.Combine(trashBase, RunConstants.WellKnownFolders.TrashConverted);
        context.FileSystem.EnsureDirectory(trashDir);

        var fileName = Path.GetFileName(sourcePath);
        var requestedTrashDest = context.FileSystem.ResolveChildPathWithinRoot(
            trashBase,
            Path.Combine(RunConstants.WellKnownFolders.TrashConverted, fileName));
        if (requestedTrashDest is null)
        {
            failureReason = $"trash-destination-invalid:{sourcePath}";
            return false;
        }

        try
        {
            trashDestinationPath = context.FileSystem.MoveItemSafely(sourcePath, requestedTrashDest);
            if (string.IsNullOrWhiteSpace(trashDestinationPath))
            {
                failureReason = $"move-to-trash-failed:{sourcePath}";
                return false;
            }

            return true;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            failureReason = ex.Message;
            return false;
        }
    }

    internal static IReadOnlyList<string> GetConversionOutputPaths(ConversionResult conversionResult)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var paths = new List<string>();

        if (!string.IsNullOrWhiteSpace(conversionResult.TargetPath) && seen.Add(conversionResult.TargetPath))
            paths.Add(conversionResult.TargetPath);

        foreach (var additionalPath in conversionResult.AdditionalTargetPaths)
        {
            if (!string.IsNullOrWhiteSpace(additionalPath) && seen.Add(additionalPath))
                paths.Add(additionalPath);
        }

        return paths;
    }

    /// <summary>
    /// Resolves set members (multi-file disc sets) for a given file extension.
    /// <paramref name="includeM3uMembers"/>: when true, parses .m3u playlist members;
    /// when false, treats .m3u targets as standalone scan candidates for DAT/hash matching.
    /// </summary>
    internal static IReadOnlyList<string> GetSetMembers(string filePath, string ext, bool includeM3uMembers = true)
    {
        return SetDescriptorSupport.GetRelatedFiles(filePath, ext, includeM3uMembers);
    }
}
