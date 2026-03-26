using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Shared conversion logic used by both <see cref="ConvertOnlyPipelinePhase"/>
/// and <see cref="WinnerConversionPipelinePhase"/>.
/// Eliminates duplication and ensures consistent behavior (set-member tracking, audit, verification).
/// </summary>
internal static class ConversionPhaseHelper
{
    internal sealed class ConversionCounters
    {
        public int Converted;
        public int Errors;
        public int Skipped;
        public int Blocked;
    }

    /// <summary>
    /// Convert a single file: invoke converter, verify, audit, move source to trash, handle set members.
    /// Returns the ConversionResult (or null if the file was skipped before conversion was attempted).
    /// </summary>
    internal static ConversionResult? ConvertSingleFile(
        string filePath,
        string consoleKey,
        IFormatConverter converter,
        RunOptions options,
        PipelineContext context,
        ConversionCounters counters,
        bool trackSetMembers,
        CancellationToken cancellationToken)
    {
        var ext = Path.GetExtension(filePath).ToLowerInvariant();
        ConversionTarget? target = null;
        ConversionResult convResult;

        if (converter is FormatConverterAdapter advancedConverter)
        {
            convResult = advancedConverter.ConvertForConsole(filePath, consoleKey, cancellationToken);
        }
        else
        {
            target = converter.GetTargetFormat(consoleKey, ext);
            if (target is null)
                return null; // No target format → not a conversion attempt

            if (string.Equals(ext, target.Extension, StringComparison.OrdinalIgnoreCase))
                return null; // Already in target format → not a conversion attempt

            context.OnProgress?.Invoke($"[Convert] {Path.GetFileName(filePath)} → {target.Extension}");
            convResult = converter.Convert(filePath, target, cancellationToken);
        }

        ProcessConversionResult(convResult, filePath, target, converter, options, context, counters, trackSetMembers, ext);
        return convResult;
    }

    private static void ProcessConversionResult(
        ConversionResult convResult,
        string sourcePath,
        ConversionTarget? target,
        IFormatConverter converter,
        RunOptions options,
        PipelineContext context,
        ConversionCounters counters,
        bool trackSetMembers,
        string sourceExt)
    {
        if (convResult.Outcome == ConversionOutcome.Success)
        {
            var verificationOk = ConversionVerificationHelpers.IsVerificationSuccessful(convResult, converter, target);
            if (verificationOk)
            {
                var convertedPath = convResult.TargetPath;
                if (convertedPath is null)
                {
                    counters.Errors++;
                    return;
                }

                counters.Converted++;
                PipelinePhaseHelpers.AppendConversionAudit(
                    context,
                    options,
                    sourcePath,
                    convertedPath,
                    ConversionVerificationHelpers.ResolveToolName(convResult, target));

                // Move set members (BIN/TRACK files) to trash BEFORE moving the descriptor,
                // because set parsers need the descriptor to resolve members.
                if (trackSetMembers)
                    MoveSetMembersToTrash(context, options, sourcePath, sourceExt);

                PipelinePhaseHelpers.MoveConvertedSourceToTrash(context, options, sourcePath, convertedPath);
            }
            else
            {
                counters.Errors++;
                if (convResult.TargetPath is not null)
                {
                    context.OnProgress?.Invoke($"WARNING: Verification failed for {convResult.TargetPath}");
                    PipelinePhaseHelpers.AppendConversionFailedAudit(
                        context,
                        options,
                        sourcePath,
                        convResult.TargetPath,
                        ConversionVerificationHelpers.ResolveToolName(convResult, target));
                    // SEC-CONV-04: Clean up failed output to prevent orphaned corrupt files
                    try { if (File.Exists(convResult.TargetPath)) File.Delete(convResult.TargetPath); }
                    catch (IOException) { /* best-effort cleanup — file may be locked */ }
                }
            }
        }
        else if (convResult.Outcome == ConversionOutcome.Skipped)
        {
            counters.Skipped++;
        }
        else if (convResult.Outcome == ConversionOutcome.Blocked)
        {
            counters.Blocked++;
        }
        else
        {
            counters.Errors++;
            context.OnProgress?.Invoke($"WARNING: Conversion failed for {sourcePath}: {convResult.Reason}");
            PipelinePhaseHelpers.AppendConversionErrorAudit(context, options, sourcePath, convResult.Reason);
            // SEC-CONV-05: Clean up any partial output left by a failed conversion tool
            if (convResult.TargetPath is not null)
            {
                try { if (File.Exists(convResult.TargetPath)) File.Delete(convResult.TargetPath); }
                catch (IOException) { /* best-effort cleanup — file may be locked */ }
            }
        }
    }

    /// <summary>
    /// Move set members (BIN/TRACK files referenced by CUE/GDI/CCD) to trash.
    /// Must be called BEFORE the descriptor file is moved — set parsers read the descriptor
    /// to resolve member paths.
    /// </summary>
    private static void MoveSetMembersToTrash(
        PipelineContext context,
        RunOptions options,
        string descriptorPath,
        string ext)
    {
        var members = PipelinePhaseHelpers.GetSetMembers(descriptorPath, ext);
        if (members.Count == 0) return;

        var root = PipelinePhaseHelpers.FindRootForPath(descriptorPath, options.Roots);
        if (root is null) return;

        var trashBase = string.IsNullOrEmpty(options.TrashRoot) ? root : options.TrashRoot;
        var trashDir = Path.Combine(trashBase, "_TRASH_CONVERTED");
        context.FileSystem.EnsureDirectory(trashDir);

        foreach (var member in members)
        {
            if (!File.Exists(member)) continue;
            var memberName = Path.GetFileName(member);
            var trashDest = context.FileSystem.ResolveChildPathWithinRoot(trashBase, Path.Combine("_TRASH_CONVERTED", memberName));
            if (trashDest is null) continue;

            try
            {
                context.FileSystem.MoveItemSafely(member, trashDest);
                if (!string.IsNullOrEmpty(options.AuditPath))
                {
                    context.AuditStore.AppendAuditRow(
                        options.AuditPath, root, member, trashDest,
                        "CONVERT", "SET_MEMBER", "", $"set-member-co-move:{ext}");
                }
            }
            catch (Exception ex)
            {
                context.OnProgress?.Invoke($"WARNING: Could not move set member after conversion: {ex.Message}");
            }
        }
    }
}
