using System.Globalization;
using System.Text;
using System.Collections.Concurrent;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;

namespace Romulus.Infrastructure.Audit;

/// <summary>
/// CSV-based audit store with HMAC-signed sidecar verification.
/// Port of AuditStore from PortInterfaces.ps1 / Logging.ps1.
/// </summary>
public sealed class AuditCsvStore : IAuditStore
{
    private readonly AuditSigningService _signingService;
    private static readonly ConcurrentDictionary<string, object> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private const string AuditCsvHeader = "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n";

    public AuditCsvStore(IFileSystem? fs = null, Action<string>? log = null, string? keyFilePath = null)
    {
        _signingService = new AuditSigningService(fs ?? new FileSystemAdapter(), log, keyFilePath);
    }

    /// <summary>CSV injection prevention: blocks leading =, +, -, @ characters.</summary>
    private static string SanitizeCsvField(string value) => AuditCsvParser.SanitizeCsvField(value);

    public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
    {
        if (string.IsNullOrWhiteSpace(auditCsvPath))
            throw new ArgumentException("Audit CSV path must not be empty.", nameof(auditCsvPath));

        // Ensure checkpoint sidecar writes can run before first append in Move phase.
        if (!File.Exists(auditCsvPath))
        {
            var dir = Path.GetDirectoryName(auditCsvPath);
            if (!string.IsNullOrEmpty(dir))
                Directory.CreateDirectory(dir);

            File.WriteAllText(auditCsvPath, AuditCsvHeader, Encoding.UTF8);
        }

        var rowCount = CountAuditRows(auditCsvPath);
        var sidecarPath = _signingService.WriteMetadataSidecar(auditCsvPath, rowCount, metadata);
        if (string.IsNullOrWhiteSpace(sidecarPath) || !File.Exists(sidecarPath))
            throw new IOException($"Failed to write audit sidecar for '{auditCsvPath}'.");
    }

    public bool TestMetadataSidecar(string auditCsvPath)
    {
        var sidecarPath = auditCsvPath + ".meta.json";
        if (!File.Exists(sidecarPath) || !File.Exists(auditCsvPath))
            return false;

        try
        {
            return _signingService.VerifyMetadataSidecar(auditCsvPath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException)
        {
            return false;
        }
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

        var lockObj = FileLocks.GetOrAdd(auditCsvPath, static _ => new object());
        lock (lockObj)
        {
            bool writeHeader = !File.Exists(auditCsvPath);

            // REC-03: Use explicit file stream + Flush(true) for crash-safe durability.
            using var fs = new FileStream(auditCsvPath, FileMode.Append, FileAccess.Write, FileShare.Read);
            using var sw = new StreamWriter(fs, Encoding.UTF8);
            if (writeHeader)
                sw.WriteLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");

            WriteAuditRowCore(sw, new AuditAppendRow(rootPath, oldPath, newPath, action, category, hash, reason));
            sw.Flush();
            fs.Flush(flushToDisk: true);
        }
    }

    public void AppendAuditRows(string auditCsvPath, IReadOnlyList<AuditAppendRow> rows)
    {
        if (string.IsNullOrWhiteSpace(auditCsvPath))
            throw new ArgumentException("Audit CSV path must not be empty.", nameof(auditCsvPath));

        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            return;

        var dir = Path.GetDirectoryName(auditCsvPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        var lockObj = FileLocks.GetOrAdd(auditCsvPath, static _ => new object());
        lock (lockObj)
        {
            var tempPath = auditCsvPath + $".append.{Guid.NewGuid():N}.tmp";

            try
            {
                if (File.Exists(auditCsvPath))
                {
                    File.Copy(auditCsvPath, tempPath, overwrite: true);
                }
                else
                {
                    File.WriteAllText(tempPath, AuditCsvHeader, Encoding.UTF8);
                }

                using (var fs = new FileStream(tempPath, FileMode.Append, FileAccess.Write, FileShare.None))
                using (var sw = new StreamWriter(fs, Encoding.UTF8))
                {
                    foreach (var row in rows)
                        WriteAuditRowCore(sw, row);

                    sw.Flush();
                    fs.Flush(flushToDisk: true);
                }

                File.Move(tempPath, auditCsvPath, overwrite: true);
            }
            finally
            {
                try
                {
                    if (File.Exists(tempPath))
                        File.Delete(tempPath);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    // best effort cleanup only
                }
            }
        }
    }

    public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
                                           string[] allowedCurrentRoots, bool dryRun = false)
    {
        var detailed = _signingService.Rollback(auditCsvPath, allowedRestoreRoots, allowedCurrentRoots, dryRun);
        return dryRun ? detailed.PlannedPaths : detailed.RestoredPaths;
    }

    private static int CountAuditRows(string auditCsvPath)
    {
        if (!File.Exists(auditCsvPath))
            return 0;

        var lineCount = File.ReadLines(auditCsvPath, Encoding.UTF8).Count();
        return Math.Max(0, lineCount - 1);
    }

    private static void WriteAuditRowCore(TextWriter writer, AuditAppendRow row)
    {
        // V2-L07: Consistent UTC timestamps across CLI and API
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        writer.WriteLine(string.Join(",",
            SanitizeCsvField(row.RootPath),
            SanitizeCsvField(row.OldPath),
            SanitizeCsvField(row.NewPath),
            SanitizeCsvField(row.Action),
            SanitizeCsvField(row.Category),
            SanitizeCsvField(row.Hash),
            SanitizeCsvField(row.Reason),
            SanitizeCsvField(timestamp)));
    }
}
