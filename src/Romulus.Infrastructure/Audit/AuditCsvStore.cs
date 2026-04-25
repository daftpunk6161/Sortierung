using System.Globalization;
using System.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
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
    private readonly Action<string>? _log;
    private static readonly ConcurrentDictionary<string, FileLockHandle> FileLocks = new(StringComparer.OrdinalIgnoreCase);
    private const string AuditCsvHeader = "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n";

    public AuditCsvStore(IFileSystem? fs = null, Action<string>? log = null, string? keyFilePath = null)
    {
        _log = log;
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

            AtomicFileWriter.WriteAllText(auditCsvPath, AuditCsvHeader, Encoding.UTF8);
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

        AppendAuditRows(auditCsvPath, [new AuditAppendRow(rootPath, oldPath, newPath, action, category, hash, reason)]);
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

        var lockHandle = AcquireFileLock(auditCsvPath);
        Mutex? crossProcessMutex = null;
        try
        {
            crossProcessMutex = AcquireCrossProcessMutex(auditCsvPath, _log);
            lock (lockHandle.Sync)
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
                        AtomicFileWriter.WriteAllText(tempPath, AuditCsvHeader, Encoding.UTF8);
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
                    _signingService.WriteMetadataSidecar(auditCsvPath, CountAuditRows(auditCsvPath));
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
        finally
        {
            if (crossProcessMutex is not null)
            {
                crossProcessMutex.ReleaseMutex();
                crossProcessMutex.Dispose();
            }
            ReleaseFileLock(auditCsvPath, lockHandle);
        }
    }

    public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
                                           string[] allowedCurrentRoots, bool dryRun = false)
    {
        var detailed = _signingService.Rollback(auditCsvPath, allowedRestoreRoots, allowedCurrentRoots, dryRun);
        return dryRun ? detailed.PlannedPaths : detailed.RestoredPaths;
    }

    internal static int CountAuditRows(string auditCsvPath)
    {
        if (!File.Exists(auditCsvPath))
            return 0;

        var logicalRows = 0;
        var current = new StringBuilder();
        var inQuotes = false;

        using var reader = new StreamReader(auditCsvPath, Encoding.UTF8);
        int value;
        while ((value = reader.Read()) >= 0)
        {
            var c = (char)value;
            current.Append(c);

            if (c == '"')
            {
                if (inQuotes && reader.Peek() == '"')
                {
                    current.Append((char)reader.Read());
                    continue;
                }

                inQuotes = !inQuotes;
            }

            if ((c == '\n' || c == '\r') && !inQuotes)
            {
                var row = current.ToString().TrimEnd('\r', '\n');
                current.Clear();
                if (row.Length > 0)
                {
                    TryValidateCsvRow(row);
                    logicalRows++;
                }
            }
        }

        if (current.Length > 0)
        {
            var row = current.ToString();
            TryValidateCsvRow(row);
            logicalRows++;
        }

        return Math.Max(0, logicalRows - 1);
    }

    private static void TryValidateCsvRow(string row)
    {
        try
        {
            _ = AuditCsvParser.ParseCsvLine(row);
        }
        catch (InvalidDataException)
        {
            // Sidecar row counting must remain best-effort for rollback audits:
            // corrupt rows are handled by rollback verification and must not
            // prevent metadata creation for the valid rows around them.
        }
    }

    private static void WriteAuditRowCore(TextWriter writer, AuditAppendRow row)
    {
        // V2-L07: Consistent UTC timestamps across CLI and API
        var timestamp = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        writer.WriteLine(string.Join(",",
            AuditCsvParser.SanitizeSpreadsheetCsvField(row.RootPath),
            AuditCsvParser.SanitizeSpreadsheetCsvField(row.OldPath),
            AuditCsvParser.SanitizeSpreadsheetCsvField(row.NewPath),
            AuditCsvParser.SanitizeSpreadsheetCsvField(row.Action),
            AuditCsvParser.SanitizeSpreadsheetCsvField(row.Category),
            AuditCsvParser.SanitizeSpreadsheetCsvField(row.Hash),
            AuditCsvParser.SanitizeSpreadsheetCsvField(row.Reason),
            SanitizeCsvField(timestamp)));
    }

    private static FileLockHandle AcquireFileLock(string auditCsvPath)
    {
        // R2-014 FIX: Add retry limit to prevent infinite loop in pathological race condition.
        const int maxRetries = 100;
        for (int attempt = 0; attempt < maxRetries; attempt++)
        {
            var handle = FileLocks.GetOrAdd(auditCsvPath, static _ => new FileLockHandle());
            Interlocked.Increment(ref handle.RefCount);
            if (ReferenceEquals(FileLocks.GetOrAdd(auditCsvPath, handle), handle))
                return handle;

            ReleaseFileLock(auditCsvPath, handle);
        }

        throw new InvalidOperationException($"Failed to acquire file lock for '{auditCsvPath}' after {maxRetries} retries.");
    }

    private static Mutex AcquireCrossProcessMutex(string auditCsvPath, Action<string>? log)
    {
        var mutex = new Mutex(false, BuildCrossProcessMutexName(auditCsvPath));
        try
        {
            _ = mutex.WaitOne();
        }
        catch (AbandonedMutexException)
        {
            // Previous process terminated while holding the mutex.
            // The current process now owns it and can safely continue.
            log?.Invoke($"Audit mutex was abandoned for '{auditCsvPath}'. Verifying CSV tail before writing.");
            EnsureAuditFileEndsWithNewline(auditCsvPath);
        }

        return mutex;
    }

    private static void EnsureAuditFileEndsWithNewline(string auditCsvPath)
    {
        if (!File.Exists(auditCsvPath))
            return;

        using var stream = new FileStream(auditCsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        if (stream.Length == 0)
            return;

        stream.Seek(-1, SeekOrigin.End);
        var last = stream.ReadByte();
        if (last is '\n' or '\r')
            return;

        throw new InvalidDataException("Audit CSV appears to end with a partial row after an abandoned mutex.");
    }

    private static string BuildCrossProcessMutexName(string auditCsvPath)
    {
        var normalized = Path.GetFullPath(auditCsvPath).ToUpperInvariant();
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
        var suffix = Convert.ToHexString(hash.AsSpan(0, 16));

        return OperatingSystem.IsWindows()
            ? $"Global\\Romulus.AuditCsv.{suffix}"
            : $"Romulus.AuditCsv.{suffix}";
    }

    private static void ReleaseFileLock(string auditCsvPath, FileLockHandle handle)
    {
        if (Interlocked.Decrement(ref handle.RefCount) != 0)
            return;

        FileLocks.TryRemove(new KeyValuePair<string, FileLockHandle>(auditCsvPath, handle));
    }

    private sealed class FileLockHandle
    {
        public object Sync { get; } = new();
        public int RefCount;
    }
}
