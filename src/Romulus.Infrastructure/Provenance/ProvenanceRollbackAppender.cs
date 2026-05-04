using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;

namespace Romulus.Infrastructure.Provenance;

/// <summary>
/// Central rollback-to-provenance bridge. Rollback execution remains owned by
/// AuditSigningService; this class only projects successful rollback rows from
/// the already verified audit CSV into per-ROM RolledBack events.
/// </summary>
public static class ProvenanceRollbackAppender
{
    public static int TryAppendRolledBackEvents(
        IProvenanceStore? store,
        AuditRollbackResult rollback,
        string timestampUtc,
        Action<string>? log = null)
    {
        if (store is null || rollback.DryRun || rollback.RolledBack <= 0)
            return 0;

        try
        {
            var auditRunId = ResolveRollbackAuditRunId(rollback);
            var events = ProjectEvents(rollback, auditRunId, timestampUtc);
            foreach (var entry in events)
                store.Append(entry);

            return events.Count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            log?.Invoke($"[Provenance] Rollback trail append skipped: {ex.GetType().Name}: {ex.Message}");
            return 0;
        }
    }

    public static IReadOnlyList<ProvenanceEntry> ProjectEvents(
        AuditRollbackResult rollback,
        string auditRunId,
        string timestampUtc)
    {
        ArgumentNullException.ThrowIfNull(rollback);
        if (rollback.DryRun || rollback.RolledBack <= 0 || rollback.RestoredPaths.Count == 0)
            return Array.Empty<ProvenanceEntry>();
        if (string.IsNullOrWhiteSpace(rollback.AuditCsvPath))
            throw new ArgumentException("AuditCsvPath is required for rollback provenance.", nameof(rollback));
        if (string.IsNullOrWhiteSpace(auditRunId))
            throw new ArgumentException("auditRunId Pflicht.", nameof(auditRunId));
        if (string.IsNullOrWhiteSpace(timestampUtc))
            throw new ArgumentException("timestampUtc Pflicht.", nameof(timestampUtc));

        var remainingRestoredPaths = rollback.RestoredPaths
            .GroupBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var output = new List<ProvenanceEntry>();
        foreach (var line in ReadAuditRowsReverse(rollback.AuditCsvPath))
        {
            string[] fields;
            try { fields = AuditCsvParser.ParseCsvLine(line); }
            catch (InvalidDataException) { continue; }

            if (fields.Length < 6)
                continue;

            var oldPath = fields[1];
            var newPath = fields[2];
            var action = fields[3];
            var hash = fields[5];
            if (!IsValidFingerprint(hash))
                continue;

            var normalizedAction = AuditSigningService.NormalizeRollbackAction(action);
            if (normalizedAction is null)
                continue;

            var restoredPath = ResolveRestoredPath(normalizedAction, oldPath, newPath);
            if (!TryConsumeRestoredPath(remainingRestoredPaths, restoredPath))
                continue;

            output.Add(new ProvenanceEntry(
                Fingerprint: hash,
                AuditRunId: auditRunId,
                EventKind: ProvenanceEventKind.RolledBack,
                TimestampUtc: timestampUtc,
                Sha256: hash,
                ConsoleKey: null,
                DatMatchId: null,
                Detail: BuildDetail(normalizedAction, oldPath, newPath, restoredPath)));
        }

        return output;
    }

    private static string ResolveRollbackAuditRunId(AuditRollbackResult rollback)
    {
        var path = !string.IsNullOrWhiteSpace(rollback.RollbackAuditPath)
            ? rollback.RollbackAuditPath
            : rollback.AuditCsvPath;
        var runId = Path.GetFileNameWithoutExtension(path);
        return string.IsNullOrWhiteSpace(runId) ? "rollback" : runId;
    }

    private static string ResolveRestoredPath(string normalizedAction, string oldPath, string newPath)
    {
        var isCopyAction = string.Equals(normalizedAction, RunConstants.AuditActions.Copy, StringComparison.OrdinalIgnoreCase);
        var isConvertCreateAction = string.Equals(normalizedAction, RunConstants.AuditActions.Convert, StringComparison.OrdinalIgnoreCase);
        return isCopyAction || isConvertCreateAction ? newPath : oldPath;
    }

    private static bool TryConsumeRestoredPath(Dictionary<string, int> remaining, string path)
    {
        if (!remaining.TryGetValue(path, out var count) || count <= 0)
            return false;

        if (count == 1)
            remaining.Remove(path);
        else
            remaining[path] = count - 1;
        return true;
    }

    private static string BuildDetail(string normalizedAction, string oldPath, string newPath, string restoredPath)
    {
        var action = SafeToken(normalizedAction);
        var restoredName = SafeFileName(restoredPath);
        if (string.Equals(normalizedAction, RunConstants.AuditActions.Copy, StringComparison.OrdinalIgnoreCase)
            || string.Equals(normalizedAction, RunConstants.AuditActions.Convert, StringComparison.OrdinalIgnoreCase))
        {
            return $"removed {restoredName} ({action})";
        }

        return $"{SafeFileName(newPath)} -> {SafeFileName(oldPath)} ({action})";
    }

    private static IEnumerable<string> ReadAuditRowsReverse(string auditCsvPath)
    {
        if (!File.Exists(auditCsvPath))
            yield break;

        var rows = File.ReadAllLines(auditCsvPath)
            .Skip(1)
            .Where(static line => !string.IsNullOrWhiteSpace(line))
            .Reverse();
        foreach (var row in rows)
            yield return row;
    }

    private static bool IsValidFingerprint(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 4)
            return false;

        foreach (var c in value)
        {
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
                return false;
        }

        return true;
    }

    private static string SafeFileName(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return "";

        try { return Path.GetFileName(path); }
        catch (ArgumentException) { return ""; }
    }

    private static string SafeToken(string value)
        => new(value.Where(static c => char.IsLetterOrDigit(c) || c is '_' or '-').ToArray());
}
