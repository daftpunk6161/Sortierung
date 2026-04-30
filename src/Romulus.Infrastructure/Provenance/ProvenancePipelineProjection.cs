using System;
using System.Collections.Generic;
using System.IO;
using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Provenance;

/// <summary>
/// Wave 7 — T-W7-PROVENANCE-TRAIL Step 2.
/// Pure projection from <see cref="RunResult"/> to a deterministic list of
/// <see cref="ProvenanceEntry"/> events that the orchestrator appends to the
/// per-ROM Trail after the audit-sidecar commit (ADR-0024 §5).
///
/// <para>
/// <strong>Single Source of Truth:</strong> RunOrchestrator MUST NOT compute
/// provenance events inline. GUI / CLI / API consume the resulting Trail via
/// <see cref="JsonlProvenanceStore"/> — keeping this projection pure and
/// deterministic guarantees that every Run produces the same events for
/// the same RunResult, regardless of entry point.
/// </para>
///
/// <para>
/// <strong>Privacy:</strong> Detail-Felder enthalten nur Datei-Namen
/// (<see cref="Path.GetFileName(string?)"/>), nie volle Pfade. Konsistent
/// mit DecisionExplainerProjection.
/// </para>
/// </summary>
public static class ProvenancePipelineProjection
{
    /// <summary>
    /// Build the deterministic event sequence to append to the per-ROM Trail.
    /// Order: per candidate, the events Imported -> Verified -> Moved are emitted
    /// before Converted events. Within each category, original collection order
    /// is preserved.
    /// </summary>
    public static IReadOnlyList<ProvenanceEntry> ProjectEvents(
        RunResult result,
        string auditRunId,
        string timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(result);
        if (string.IsNullOrWhiteSpace(auditRunId))
            throw new ArgumentException("auditRunId Pflicht (ADR-0024 §4).", nameof(auditRunId));
        if (string.IsNullOrWhiteSpace(timestampUtc))
            throw new ArgumentException("timestampUtc Pflicht.", nameof(timestampUtc));

        var output = new List<ProvenanceEntry>();
        var movedPaths = result.MoveResult?.MovedSourcePaths;

        foreach (var c in result.AllCandidates)
        {
            if (string.IsNullOrWhiteSpace(c.Hash)) continue;

            // Imported
            output.Add(new ProvenanceEntry(
                Fingerprint: c.Hash!,
                AuditRunId: auditRunId,
                EventKind: ProvenanceEventKind.Imported,
                TimestampUtc: timestampUtc,
                Sha256: c.Hash,
                ConsoleKey: c.ConsoleKey,
                DatMatchId: null,
                Detail: null));

            // Verified
            if (c.DatMatch)
            {
                output.Add(new ProvenanceEntry(
                    Fingerprint: c.Hash!,
                    AuditRunId: auditRunId,
                    EventKind: ProvenanceEventKind.Verified,
                    TimestampUtc: timestampUtc,
                    Sha256: c.Hash,
                    ConsoleKey: c.ConsoleKey,
                    DatMatchId: string.IsNullOrWhiteSpace(c.DatGameName) ? "matched" : c.DatGameName,
                    Detail: null));
            }

            // Moved
            if (movedPaths is not null && movedPaths.Contains(c.MainPath))
            {
                output.Add(new ProvenanceEntry(
                    Fingerprint: c.Hash!,
                    AuditRunId: auditRunId,
                    EventKind: ProvenanceEventKind.Moved,
                    TimestampUtc: timestampUtc,
                    Sha256: c.Hash,
                    ConsoleKey: c.ConsoleKey,
                    DatMatchId: null,
                    Detail: SafeFileName(c.MainPath)));
            }
        }

        // Converted
        var report = result.ConversionReport;
        if (report is not null)
        {
            // Build a quick lookup: source-path -> hash.
            var hashByPath = new Dictionary<string, RomCandidate>(StringComparer.OrdinalIgnoreCase);
            foreach (var c in result.AllCandidates)
            {
                if (!string.IsNullOrEmpty(c.MainPath) && !string.IsNullOrWhiteSpace(c.Hash))
                    hashByPath[c.MainPath] = c;
            }

            foreach (var r in report.Results)
            {
                if (r.Outcome != ConversionOutcome.Success) continue;
                if (!hashByPath.TryGetValue(r.SourcePath, out var src)) continue;

                var detail = !string.IsNullOrWhiteSpace(r.TargetPath)
                    ? SafeFileName(r.SourcePath) + " -> " + SafeFileName(r.TargetPath)
                    : SafeFileName(r.SourcePath);

                output.Add(new ProvenanceEntry(
                    Fingerprint: src.Hash!,
                    AuditRunId: auditRunId,
                    EventKind: ProvenanceEventKind.Converted,
                    TimestampUtc: timestampUtc,
                    Sha256: src.Hash,
                    ConsoleKey: src.ConsoleKey,
                    DatMatchId: null,
                    Detail: detail));
            }
        }

        return output;
    }

    private static string SafeFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "";
        try { return Path.GetFileName(path); }
        catch (ArgumentException) { return ""; }
    }
}
