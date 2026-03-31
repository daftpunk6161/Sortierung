using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Conversion;
using System.Collections.Concurrent;

namespace RomCleanup.Infrastructure.Orchestration;

/// <summary>
/// Shared conversion logic used by both <see cref="ConvertOnlyPipelinePhase"/>
/// and <see cref="WinnerConversionPipelinePhase"/>.
/// Eliminates duplication and ensures consistent behavior (set-member tracking, audit, verification).
/// </summary>
internal static class ConversionPhaseHelper
{
    // Conservative by design: external conversion tools are I/O-heavy and may
    // allocate large temporary artifacts. We parallelize enough for throughput,
    // but cap concurrency to avoid destabilizing the box or creating excessive
    // temp-space pressure.
    internal const int MaxParallelConversions = 2;

    internal sealed class ConversionCounters
    {
        public int Converted;
        public int Errors;
        public int Skipped;
        public int Blocked;
    }

    internal sealed record ConversionWorkItem(
        int Index,
        string FilePath,
        string ConsoleKey,
        bool TrackSetMembers,
        bool SkipBeforeConversion);

    internal sealed record ConversionBatchResult(
        int Converted,
        int Errors,
        int Skipped,
        int Blocked,
        IReadOnlyList<ConversionResult> Results);

    internal static ConversionBatchResult ExecuteBatch(
        IReadOnlyList<ConversionWorkItem> workItems,
        IFormatConverter converter,
        RunOptions options,
        PipelineContext context,
        string progressUnitLabel,
        CancellationToken cancellationToken)
    {
        if (workItems.Count == 0)
            return new ConversionBatchResult(0, 0, 0, 0, Array.Empty<ConversionResult>());

        var orderedResults = new ConversionResult?[workItems.Count];
        var serializationLocks = new ConcurrentDictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        var progressLock = new object();
        var converted = 0;
        var errors = 0;
        var skipped = 0;
        var blocked = 0;
        var completed = 0;

        Action<string>? synchronizedProgress = null;
        if (context.OnProgress is not null)
        {
            synchronizedProgress = message =>
            {
                lock (progressLock)
                    context.OnProgress(message);
            };
        }

        var workerContext = new PipelineContext
        {
            Options = context.Options,
            FileSystem = context.FileSystem,
            AuditStore = context.AuditStore,
            Metrics = context.Metrics,
            OnProgress = synchronizedProgress
        };

        void ProcessWorkItem(ConversionWorkItem item)
        {
            cancellationToken.ThrowIfCancellationRequested();

            ConversionResult? conversionResult = null;
            var localCounters = new ConversionCounters();

            if (!item.SkipBeforeConversion)
            {
                var serializationKey = BuildSerializationKey(item.FilePath);
                var gate = serializationLocks.GetOrAdd(serializationKey, static _ => new object());
                lock (gate)
                {
                    conversionResult = ConvertSingleFile(
                        item.FilePath,
                        item.ConsoleKey,
                        converter,
                        options,
                        workerContext,
                        localCounters,
                        item.TrackSetMembers,
                        cancellationToken);
                }
            }

            orderedResults[item.Index] = conversionResult;

            if (localCounters.Converted != 0)
                Interlocked.Add(ref converted, localCounters.Converted);
            if (localCounters.Errors != 0)
                Interlocked.Add(ref errors, localCounters.Errors);
            if (localCounters.Skipped != 0)
                Interlocked.Add(ref skipped, localCounters.Skipped);
            if (localCounters.Blocked != 0)
                Interlocked.Add(ref blocked, localCounters.Blocked);

            var done = Interlocked.Increment(ref completed);
            if ((done % 25 == 0 || done == workItems.Count) && synchronizedProgress is not null)
            {
                synchronizedProgress(
                    $"[Convert] Fortschritt: {done}/{workItems.Count} {progressUnitLabel} " +
                    $"(ok={Volatile.Read(ref converted)}, skip={Volatile.Read(ref skipped)}, " +
                    $"blocked={Volatile.Read(ref blocked)}, err={Volatile.Read(ref errors)})");
            }
        }

        var maxDegreeOfParallelism = GetParallelism(workItems.Count);
        if (maxDegreeOfParallelism <= 1)
        {
            foreach (var item in workItems)
                ProcessWorkItem(item);
        }
        else
        {
            Parallel.ForEach(
                workItems,
                new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = maxDegreeOfParallelism
                },
                ProcessWorkItem);
        }

        var results = orderedResults
            .Where(static r => r is not null)
            .Select(static r => r!)
            .ToArray();

        return new ConversionBatchResult(
            Converted: converted,
            Errors: errors,
            Skipped: skipped,
            Blocked: blocked,
            Results: results);
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
        if (string.Equals(options.Mode, RunConstants.ModeDryRun, StringComparison.OrdinalIgnoreCase))
            return null;

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

        convResult = ProcessConversionResult(convResult, filePath, target, converter, options, context, counters, trackSetMembers, ext);
        return convResult;
    }

    private static ConversionResult ProcessConversionResult(
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
                    return convResult;
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
                convResult = convResult with { Outcome = ConversionOutcome.Error };
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

        return convResult;
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
        var trashDir = Path.Combine(trashBase, RunConstants.WellKnownFolders.TrashConverted);
        context.FileSystem.EnsureDirectory(trashDir);

        foreach (var member in members)
        {
            if (!File.Exists(member)) continue;

            // SEC-MOVE-06: Validate set member path is within an allowed root.
            // CUE/GDI/CCD parsers return paths resolved from the descriptor content;
            // a crafted descriptor could reference paths outside the configured roots.
            var memberRoot = PipelinePhaseHelpers.FindRootForPath(member, options.Roots);
            if (memberRoot is null)
            {
                context.OnProgress?.Invoke($"WARNING: Set member outside allowed roots, skipped: {member}");
                continue;
            }

            var memberName = Path.GetFileName(member);
            var trashDest = context.FileSystem.ResolveChildPathWithinRoot(trashBase, Path.Combine(RunConstants.WellKnownFolders.TrashConverted, memberName));
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

    private static int GetParallelism(int workItemCount)
    {
        if (workItemCount <= 1)
            return 1;

        return Math.Max(1, Math.Min(MaxParallelConversions, Math.Min(Environment.ProcessorCount, workItemCount)));
    }

    private static string BuildSerializationKey(string filePath)
    {
        var fullPath = Path.GetFullPath(filePath);
        var directory = Path.GetDirectoryName(fullPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(fullPath);

        // Conservative collision domain:
        // conversions typically materialize final artifacts as <dir>\<basename>.<targetExt>
        // and later trash the original by file name. Serializing by directory+basename
        // preserves sequential semantics for sources that would contend for the same
        // derived outputs without globally disabling conversion parallelism.
        return Path.Combine(directory, baseName);
    }
}
