using System.Text.Json;

namespace Romulus.Infrastructure.Audit;

public sealed record AuditRollbackRootSet(
    IReadOnlyList<string> RestoreRoots,
    IReadOnlyList<string> CurrentRoots);

public static class AuditRollbackRootResolver
{
    public static AuditRollbackRootSet Resolve(string auditCsvPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditCsvPath);

        var restoreRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var currentRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var csvRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        TryReadMetadataRoots(auditCsvPath, "AllowedRestoreRoots", restoreRoots);
        TryReadMetadataRoots(auditCsvPath, "AllowedCurrentRoots", currentRoots);

        foreach (var root in DeriveRootsFromAuditCsv(auditCsvPath))
            csvRoots.Add(root);

        if (restoreRoots.Count == 0)
        {
            foreach (var root in csvRoots)
                restoreRoots.Add(root);
        }

        if (currentRoots.Count == 0)
        {
            foreach (var root in csvRoots)
                currentRoots.Add(root);
        }

        return new AuditRollbackRootSet(
            restoreRoots.OrderBy(static root => root, StringComparer.OrdinalIgnoreCase).ToArray(),
            currentRoots.OrderBy(static root => root, StringComparer.OrdinalIgnoreCase).ToArray());
    }

    private static void TryReadMetadataRoots(string auditCsvPath, string propertyName, HashSet<string> roots)
    {
        var metaPath = auditCsvPath + ".meta.json";
        if (!File.Exists(metaPath))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(metaPath));
            if (!doc.RootElement.TryGetProperty(propertyName, out var rootElement) || rootElement.ValueKind != JsonValueKind.Array)
                return;

            foreach (var item in rootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;

                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    roots.Add(Path.GetFullPath(value));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or NotSupportedException)
        {
            // Best effort only; callers fall back to CSV-derived roots.
        }
    }

    private static IEnumerable<string> DeriveRootsFromAuditCsv(string auditCsvPath)
    {
        if (!File.Exists(auditCsvPath))
            yield break;

        using var reader = new StreamReader(auditCsvPath);
        _ = reader.ReadLine(); // header
        while (reader.ReadLine() is { } line)
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var rootField = ExtractFirstCsvField(line);
            if (!string.IsNullOrWhiteSpace(rootField))
                yield return Path.GetFullPath(rootField);
        }
    }

    public static string ExtractFirstCsvField(string line)
    {
        if (string.IsNullOrEmpty(line))
            return string.Empty;

        if (line[0] == '"')
        {
            var builder = new System.Text.StringBuilder();
            for (var i = 1; i < line.Length; i++)
            {
                if (line[i] == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        builder.Append('"');
                        i++;
                        continue;
                    }

                    break;
                }

                builder.Append(line[i]);
            }

            return builder.ToString();
        }

        var firstComma = line.IndexOf(',');
        return firstComma <= 0 ? line.Trim() : line[..firstComma].Trim();
    }
}
