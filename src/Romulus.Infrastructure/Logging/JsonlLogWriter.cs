using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Romulus.Infrastructure.Logging;

/// <summary>
/// A single structured JSONL log entry.
/// Mirrors the schema from Logging.ps1.
/// </summary>
public sealed class StructuredLogEntry
{
    [JsonPropertyName("schemaVersion")]
    public string SchemaVersion { get; set; } = "romulus-jsonl-log-v1";

    [JsonPropertyName("ts")]
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("correlationId")]
    public string CorrelationId { get; set; } = "";

    [JsonPropertyName("level")]
    public string Level { get; set; } = "Info";

    [JsonPropertyName("phase")]
    public string Phase { get; set; } = "";

    [JsonPropertyName("module")]
    public string Module { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("root")]
    public string Root { get; set; } = "";

    [JsonPropertyName("errorClass")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? ErrorClass { get; set; }

    [JsonPropertyName("metrics")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, object>? Metrics { get; set; }
}

/// <summary>
/// Log levels matching the PowerShell Logging.ps1 numeric scheme.
/// </summary>
public enum LogLevel
{
    Debug = 10,
    Info = 20,
    Warning = 30,
    Error = 40
}

/// <summary>
/// Writes structured JSONL log entries to a file.
/// Thread-safe via lock. Supports rotation by size with optional GZIP.
/// </summary>
public sealed class JsonlLogWriter : IDisposable
{
    private const int MaxStringFieldChars = 8192;
    private const int MaxRootFieldChars = 4096;

    private readonly string _logPath;
    private readonly string _correlationId;
    private readonly LogLevel _minLevel;
    private readonly object _lock = new();
    private StreamWriter? _writer;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = false
    };

    public JsonlLogWriter(string logPath, LogLevel minLevel = LogLevel.Info, string? correlationId = null)
    {
        _logPath = Path.GetFullPath(logPath);
        _minLevel = minLevel;
        _correlationId = correlationId ?? Guid.NewGuid().ToString("N");

        var dir = Path.GetDirectoryName(_logPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        _writer = new StreamWriter(_logPath, append: true, encoding: Encoding.UTF8)
        {
            AutoFlush = true
        };
    }

    public string CorrelationId => _correlationId;

    public void Write(LogLevel level, string module, string action, string message,
        string phase = "", string root = "", string? errorClass = null,
        Dictionary<string, object>? metrics = null)
    {
        if (level < _minLevel) return;

        var entry = new StructuredLogEntry
        {
            SchemaVersion = "romulus-jsonl-log-v1",
            Timestamp = DateTime.UtcNow,
            CorrelationId = _correlationId,
            Level = level.ToString(),
            Phase = LimitField(phase, MaxStringFieldChars),
            Module = LimitField(module, MaxStringFieldChars),
            Action = LimitField(action, MaxStringFieldChars),
            Message = LimitField(message, MaxStringFieldChars),
            // Root is an explicit log field and is intentionally allowed by policy;
            // arbitrary paths in messages still get bounded by MaxStringFieldChars.
            Root = LimitField(root, MaxRootFieldChars),
            ErrorClass = string.IsNullOrEmpty(errorClass) ? null : LimitField(errorClass, MaxStringFieldChars),
            Metrics = LimitMetrics(metrics)
        };

        var json = JsonSerializer.Serialize(entry, JsonOpts);

        lock (_lock)
        {
            _writer?.WriteLine(json);
        }
    }

    public void Debug(string module, string message) =>
        Write(LogLevel.Debug, module, "", message);

    public void Info(string module, string action, string message, string phase = "") =>
        Write(LogLevel.Info, module, action, message, phase);

    public void Warning(string module, string message, string? errorClass = null) =>
        Write(LogLevel.Warning, module, "", message, errorClass: errorClass);

    public void Error(string module, string message, string errorClass = "Critical") =>
        Write(LogLevel.Error, module, "", message, errorClass: errorClass);

    /// <summary>
    /// Rotate the log file if it exceeds maxBytes, properly disposing and
    /// recreating the writer so the active stream is not broken.
    /// Thread-safe: shares _lock with Write() to prevent concurrent access.
    /// </summary>
    public void RotateIfNeeded(long maxBytes = 10 * 1024 * 1024, int keepFiles = 5, bool gzip = false)
    {
        lock (_lock)
        {
            if (!File.Exists(_logPath))
                return;

            var fi = new FileInfo(_logPath);
            if (fi.Length < maxBytes)
                return;

            // Close current writer before moving the file
            _writer?.Dispose();
            _writer = null;

            JsonlLogRotation.Rotate(_logPath, maxBytes, keepFiles, gzip);

            // Reopen writer on the (now-empty) log path
            _writer = new StreamWriter(_logPath, append: true, encoding: Encoding.UTF8)
            {
                AutoFlush = true
            };
        }
    }

    public void Dispose()
    {
        lock (_lock)
        {
            _writer?.Dispose();
            _writer = null;
        }
    }

    private static string LimitField(string? value, int maxChars)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        return value.Length <= maxChars
            ? value
            : value[..maxChars] + "...[truncated]";
    }

    private static Dictionary<string, object>? LimitMetrics(Dictionary<string, object>? metrics)
    {
        if (metrics is null || metrics.Count == 0)
            return metrics;

        var sanitized = new Dictionary<string, object>(metrics.Count, StringComparer.Ordinal);
        foreach (var (key, value) in metrics)
        {
            var safeKey = LimitField(key, 256);
            sanitized[safeKey] = value is string text
                ? LimitField(text, MaxStringFieldChars)
                : value;
        }

        return sanitized;
    }
}

/// <summary>
/// Handles JSONL log file rotation by size.
/// Mirrors Invoke-JsonlLogRotation from Logging.ps1.
/// </summary>
public static class JsonlLogRotation
{
    private static long s_rotationCounter;

    /// <summary>
    /// Rotates the log file if it exceeds maxBytes.
    /// Archives with timestamp, optionally compresses with GZIP.
    /// Keeps up to keepFiles archived logs.
    /// </summary>
    public static void Rotate(string logPath, long maxBytes = 10 * 1024 * 1024,
        int keepFiles = 5, bool gzip = false)
    {
        var fullPath = Path.GetFullPath(logPath);
        if (!File.Exists(fullPath)) return;

        var fi = new FileInfo(fullPath);
        if (fi.Length < maxBytes) return;

        var dir = fi.DirectoryName ?? ".";
        var baseName = Path.GetFileNameWithoutExtension(fi.Name);
        var archivePath = BuildUniqueArchivePath(dir, baseName, gzip);

        try
        {
            File.Move(fullPath, archivePath);
        }
        catch (IOException)
        {
            // File is still held open by a writer — skip rotation
            System.Diagnostics.Trace.TraceWarning($"JSONL rotation skipped because the log is locked: {fullPath}");
            return;
        }

        if (gzip)
        {
            var gzPath = archivePath + ".gz";
            var gzTempPath = gzPath + $".{Environment.ProcessId}.{Guid.NewGuid():N}.tmp";
            using (var sourceStream = File.OpenRead(archivePath))
            using (var targetStream = new FileStream(gzTempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            using (var gzStream = new GZipStream(targetStream, CompressionLevel.Optimal))
            {
                sourceStream.CopyTo(gzStream);
            }
            try
            {
                File.Move(gzTempPath, gzPath, overwrite: false);
                File.Delete(archivePath);
            }
            catch (IOException ex)
            {
                System.Diagnostics.Trace.TraceWarning($"JSONL gzip promotion failed: {ex.Message}");
                throw;
            }
        }

        // Prune old archives
        var pattern = gzip ? $"{baseName}-*.jsonl.gz" : $"{baseName}-*.jsonl";
        var archives = Directory.GetFiles(dir, pattern)
            .Where(f => !string.Equals(f, fullPath, StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f => f)
            .ToArray();

        for (int i = keepFiles; i < archives.Length; i++)
        {
            File.Delete(archives[i]);
        }
    }

    private static string BuildUniqueArchivePath(string dir, string baseName, bool gzip)
    {
        for (var attempt = 0; attempt < 1024; attempt++)
        {
            var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmssfff");
            var counter = Interlocked.Increment(ref s_rotationCounter);
            var archiveName = $"{baseName}-{stamp}-{Environment.ProcessId}-{counter:D8}.jsonl";
            var archivePath = Path.Combine(dir, archiveName);
            var finalPath = gzip ? archivePath + ".gz" : archivePath;
            if (!File.Exists(archivePath) && !File.Exists(finalPath))
                return archivePath;
        }

        throw new IOException("Could not allocate a collision-free JSONL archive path.");
    }
}
