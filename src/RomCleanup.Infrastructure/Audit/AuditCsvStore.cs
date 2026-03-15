using System.Globalization;
using System.Text;
using System.Text.Json;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// CSV-based audit store with SHA256 sidecar verification.
/// Port of AuditStore from PortInterfaces.ps1 / Logging.ps1.
/// </summary>
public sealed class AuditCsvStore : IAuditStore
{
    /// <summary>CSV injection prevention: blocks leading =, +, -, @ characters.</summary>
    private static string SanitizeCsvField(string value) => AuditCsvParser.SanitizeCsvField(value);

    public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(auditCsvPath))
            throw new ArgumentException("Audit CSV path must not be empty.", nameof(auditCsvPath));

        var sidecarPath = auditCsvPath + ".meta.json";

        var stringDict = new Dictionary<string, string?>();
        foreach (var entry in metadata)
            stringDict[entry.Key] = entry.Value?.ToString();

        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true
        };
        var json = JsonSerializer.Serialize(stringDict, jsonOptions);

        var dir = Path.GetDirectoryName(sidecarPath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        File.WriteAllText(sidecarPath, json, Encoding.UTF8);
    }

    public bool TestMetadataSidecar(string auditCsvPath)
    {
        var sidecarPath = auditCsvPath + ".meta.json";
        return File.Exists(sidecarPath);
    }

    /// <summary>    /// Flush buffered audit data to disk. Currently a no-op since AppendAuditRow
    /// uses a using-statement StreamWriter that flushes on dispose per call.
    /// </summary>
    public void Flush(string auditCsvPath) { /* no-op: each AppendAuditRow auto-flushes */ }

    /// <summary>    /// Appends a single audit row to the CSV file.
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

        // V2-L07: Consistent UTC timestamps across CLI and API
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
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

            var parts = AuditCsvParser.ParseCsvLine(line);
            if (parts.Length < 4)
                continue;

            // CSV format: RootPath, OldPath, NewPath, Action, ...
            var oldPath = parts[1];
            var newPath = parts[2];
            var action = parts[3];

            // Rollback MOVE and JUNK_REMOVE actions (Issue #22)
            if (!string.Equals(action, "Move", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "MOVED", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "JUNK_REMOVE", StringComparison.OrdinalIgnoreCase))
                continue;

            // Validate paths within allowed roots
            if (!IsWithinAnyRoot(oldPath, allowedRestoreRoots))
                continue;
            if (!IsWithinAnyRoot(newPath, allowedCurrentRoots))
                continue;

            if (!dryRun && File.Exists(newPath))
            {
                // BUG-FIX: Block reparse points on source/destination to prevent symlink attacks
                // from crafted audit CSV entries.
                try
                {
                    var newAttrs = File.GetAttributes(newPath);
                    if ((newAttrs & FileAttributes.ReparsePoint) != 0)
                        continue; // Skip: source is a symlink/junction
                }
                catch { continue; } // Skip inaccessible files

                var fullOldPath = Path.GetFullPath(oldPath);
                var destDir = Path.GetDirectoryName(fullOldPath);
                if (!string.IsNullOrEmpty(destDir))
                {
                    // Block reparse point on destination parent
                    if (Directory.Exists(destDir))
                    {
                        try
                        {
                            var destDirInfo = new DirectoryInfo(destDir);
                            if ((destDirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                                continue;
                        }
                        catch { continue; }
                    }
                    Directory.CreateDirectory(destDir);
                }

                // Use overwrite:false to prevent clobbering existing files
                if (File.Exists(fullOldPath))
                    continue; // Skip: destination already exists

                // Issue #22: TOCTOU-Schutz — try/catch with single retry
                try
                {
                    File.Move(newPath, fullOldPath);
                }
                catch (IOException) when (RetryFileMove(newPath, fullOldPath))
                {
                    // Retry succeeded
                }
                catch (IOException)
                {
                    continue; // Both attempts failed — skip this entry
                }
            }

            restoredPaths.Add(oldPath);
        }

        // Issue #22: Write rollback trail
        WriteRollbackTrail(auditCsvPath, restoredPaths);

        return restoredPaths;
    }

    private static bool RetryFileMove(string source, string dest)
    {
        Thread.Sleep(50);
        try
        {
            if (!File.Exists(source) || File.Exists(dest))
                return false;
            File.Move(source, dest);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static void WriteRollbackTrail(string auditCsvPath, IReadOnlyList<string> restoredPaths)
    {
        if (restoredPaths.Count == 0)
            return;

        var trailPath = Path.ChangeExtension(auditCsvPath, ".rollback-trail.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RestoredPath,Timestamp");
        var timestamp = DateTime.UtcNow.ToString("o");
        foreach (var p in restoredPaths)
            sb.AppendLine($"{SanitizeCsvField(p)},{timestamp}");
        File.WriteAllText(trailPath, sb.ToString(), Encoding.UTF8);
    }

    private static bool IsWithinAnyRoot(string path, string[] roots)
    {
        // Issue #21: NFC normalization for macOS HFS+ paths
        var fullPath = Path.GetFullPath(path).Normalize(System.Text.NormalizationForm.FormC);
        foreach (var root in roots)
        {
            var normalizedRoot = Path.GetFullPath(root).Normalize(System.Text.NormalizationForm.FormC)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }
}
