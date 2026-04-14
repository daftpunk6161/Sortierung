using Romulus.Contracts.Models;
using Romulus.Infrastructure.FileSystem;
using System.Text.Json;

namespace Romulus.Infrastructure.Audit;

/// <summary>
/// Audit-based rollback service.
/// Lives in Infrastructure so CLI/API/WPF can share the same rollback behavior.
/// </summary>
public static class RollbackService
{
    private static int CountAffectedRollbackRows(string auditPath)
    {
        if (!File.Exists(auditPath))
            return 1;

        try
        {
            using var stream = new FileStream(auditPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream);

            // Skip CSV header row if present.
            _ = reader.ReadLine();

            var count = 0;
            while (!reader.EndOfStream)
            {
                var line = reader.ReadLine();
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var fields = AuditCsvParser.ParseCsvLine(line);
                if (fields.Length >= 4)
                    count++;
            }

            // R6-006 FIX: Return actual count — Math.Max(1, 0) returned 1 for empty
            // audit files, displaying misleading "1 Failed" in rollback results.
            return count;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return 1;
        }
    }

    private static AuditRollbackRootSet ResolveRootSet(string auditPath, IReadOnlyList<string> fallbackRoots)
    {
        var resolved = AuditRollbackRootResolver.Resolve(auditPath);
        if (resolved.RestoreRoots.Count > 0 || resolved.CurrentRoots.Count > 0)
            return resolved;

        var normalizedFallback = fallbackRoots
            .Where(static root => !string.IsNullOrWhiteSpace(root))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static root => root, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new AuditRollbackRootSet(normalizedFallback, normalizedFallback);
    }

    /// <summary>
    /// Execute a rollback from the given audit CSV file.
    /// Returns a rollback result with integrity-verified statistics.
    /// </summary>
    public static AuditRollbackResult Execute(string auditPath, IReadOnlyList<string> roots, string? keyFilePath = null)
    {
        var fs = new FileSystemAdapter();
        var signingService = new AuditSigningService(fs, keyFilePath: keyFilePath ?? AuditSecurityPaths.GetDefaultSigningKeyPath());
        var rootSet = ResolveRootSet(auditPath, roots);

        // Preserve explicit integrity failure semantics used by UI/tests.
        if (File.Exists(auditPath + ".meta.json"))
        {
            try
            {
                signingService.VerifyMetadataSidecar(auditPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or InvalidDataException or JsonException)
            {
                return new AuditRollbackResult
                {
                    AuditCsvPath = auditPath,
                    Failed = CountAffectedRollbackRows(auditPath),
                    DryRun = false
                };
            }
        }

        return signingService.Rollback(auditPath, rootSet.RestoreRoots, rootSet.CurrentRoots, dryRun: false);
    }

    /// <summary>
    /// TASK-175: Pre-flight trash integrity check. Runs a DryRun rollback to detect
    /// missing trash files before actual rollback. Never moves any files.
    /// </summary>
    public static AuditRollbackResult VerifyTrashIntegrity(string auditPath, IReadOnlyList<string> roots, string? keyFilePath = null)
    {
        if (!File.Exists(auditPath))
            return new AuditRollbackResult { DryRun = true };

        var fs = new FileSystemAdapter();
        var signingService = new AuditSigningService(fs, keyFilePath: keyFilePath ?? AuditSecurityPaths.GetDefaultSigningKeyPath());
        var rootSet = ResolveRootSet(auditPath, roots);

        return signingService.Rollback(auditPath, rootSet.RestoreRoots, rootSet.CurrentRoots, dryRun: true);
    }
}
