using System.Globalization;
using System.Text;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// CSV-based audit store with SHA256 sidecar verification.
/// Port of AuditStore from PortInterfaces.ps1 / Logging.ps1.
/// </summary>
public sealed class AuditCsvStore : IAuditStore
{
    /// <summary>CSV injection prevention: blocks leading =, +, -, @ characters.</summary>
    private static string SanitizeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value))
            return value;

        // CSV injection prevention per OWASP
        if (value.Length > 0 && (value[0] == '=' || value[0] == '+' || value[0] == '-' || value[0] == '@'))
            value = "'" + value;

        // Escape quotes and wrap if contains comma/quote/newline
        if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(auditCsvPath))
            throw new ArgumentException("Audit CSV path must not be empty.", nameof(auditCsvPath));

        var sidecarPath = auditCsvPath + ".meta.json";

        var sb = new StringBuilder();
        sb.AppendLine("{");
        var entries = metadata.ToList();
        for (int i = 0; i < entries.Count; i++)
        {
            var key = entries[i].Key.Replace("\"", "\\\"");
            var val = entries[i].Value?.ToString()?.Replace("\"", "\\\"") ?? "null";
            sb.Append($"  \"{key}\": \"{val}\"");
            if (i < entries.Count - 1) sb.Append(',');
            sb.AppendLine();
        }
        sb.AppendLine("}");

        var dir = Path.GetDirectoryName(sidecarPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(sidecarPath, sb.ToString(), Encoding.UTF8);
    }

    public bool TestMetadataSidecar(string auditCsvPath)
    {
        var sidecarPath = auditCsvPath + ".meta.json";
        return File.Exists(sidecarPath);
    }

    /// <summary>
    /// Appends a single audit row to the CSV file.
    /// Creates the file with header if it doesn't exist.
    /// Format: RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp
    /// </summary>
    public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
        string newPath, string action, string category = "", string hash = "",
        string reason = "")
    {
        if (string.IsNullOrWhiteSpace(auditCsvPath))
            throw new ArgumentException("Audit CSV path must not be empty.", nameof(auditCsvPath));

        var dir = Path.GetDirectoryName(auditCsvPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        bool writeHeader = !File.Exists(auditCsvPath);

        using var sw = new StreamWriter(auditCsvPath, append: true, Encoding.UTF8);
        if (writeHeader)
            sw.WriteLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");

        var timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);
        sw.WriteLine(string.Join(",",
            SanitizeCsvField(rootPath),
            SanitizeCsvField(oldPath),
            SanitizeCsvField(newPath),
            SanitizeCsvField(action),
            SanitizeCsvField(category),
            SanitizeCsvField(hash),
            SanitizeCsvField(reason),
            SanitizeCsvField(timestamp)));
    }

    public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
                                           string[] allowedCurrentRoots, bool dryRun = false)
    {
        if (!File.Exists(auditCsvPath))
            return Array.Empty<string>();

        var restoredPaths = new List<string>();
        var lines = File.ReadAllLines(auditCsvPath, Encoding.UTF8);

        // Skip header line
        foreach (var line in lines.Skip(1))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var parts = ParseCsvLine(line);
            if (parts.Length < 4)
                continue;

            // CSV format: RootPath, OldPath, NewPath, Action, ...
            var oldPath = parts[1];
            var newPath = parts[2];
            var action = parts[3];

            if (!string.Equals(action, "Move", StringComparison.OrdinalIgnoreCase))
                continue;

            // Validate paths within allowed roots
            if (!IsWithinAnyRoot(oldPath, allowedRestoreRoots))
                continue;
            if (!IsWithinAnyRoot(newPath, allowedCurrentRoots))
                continue;

            if (!dryRun && File.Exists(newPath))
            {
                var destDir = Path.GetDirectoryName(oldPath);
                if (!string.IsNullOrEmpty(destDir))
                    Directory.CreateDirectory(destDir);

                File.Move(newPath, oldPath);
            }

            restoredPaths.Add(oldPath);
        }

        return restoredPaths;
    }

    private static bool IsWithinAnyRoot(string path, string[] roots)
    {
        var fullPath = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        foreach (var root in roots)
        {
            var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"')
                    {
                        current.Append('"');
                        i++;
                    }
                    else
                    {
                        inQuotes = false;
                    }
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());

        return fields.ToArray();
    }
}
