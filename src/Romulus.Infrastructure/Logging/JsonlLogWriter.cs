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
            Timestamp = DateTime.UtcNow,
            CorrelationId = _correlationId,
            Level = level.ToString(),
            Phase = phase,
            Module = module,
            Action = action,
            Message = message,
            Root = root,
            ErrorClass = errorClass,
            Metrics = metrics
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
}

/// <summary>
/// Handles JSONL log file rotation by size.
/// Mirrors Invoke-JsonlLogRotation from Logging.ps1.
/// </summary>
public static class JsonlLogRotation
{
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
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
        var archiveName = $"{baseName}-{stamp}.jsonl";
        var archivePath = Path.Combine(dir, archiveName);

        try
        {
            File.Move(fullPath, archivePath);
        }
        catch (IOException)
        {
            // File is still held open by a writer — skip rotation
            return;
        }

        if (gzip)
        {
            var gzPath = archivePath + ".gz";
            using (var sourceStream = File.OpenRead(archivePath))
            using (var targetStream = File.Create(gzPath))
            using (var gzStream = new GZipStream(targetStream, CompressionLevel.Optimal))
            {
                sourceStream.CopyTo(gzStream);
            }
            File.Delete(archivePath);
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
}
