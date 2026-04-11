using System.Text;

namespace Romulus.Infrastructure.Configuration;

/// <summary>
/// Shared settings-file access helpers.
/// Keeps settings loads resilient against short-lived concurrent save operations.
/// </summary>
public static class SettingsFileAccess
{
    private const int DefaultMaxAttempts = 8;
    private const int InitialDelayMs = 25;
    private const int DefaultTotalTimeoutMs = 2000;

    public static string? TryReadAllText(
        string path,
        int maxAttempts = DefaultMaxAttempts,
        int totalTimeoutMs = DefaultTotalTimeoutMs)
        // SYNC-JUSTIFIED: configuration load is consumed by synchronous startup paths.
        => TryReadAllTextAsync(path, maxAttempts, totalTimeoutMs).GetAwaiter().GetResult();

    public static async Task<string?> TryReadAllTextAsync(
        string path,
        int maxAttempts = DefaultMaxAttempts,
        int totalTimeoutMs = DefaultTotalTimeoutMs,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        if (totalTimeoutMs <= 0)
            totalTimeoutMs = DefaultTotalTimeoutMs;

        var delayMs = InitialDelayMs;
        var startedAtMs = Environment.TickCount64;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                using var stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    bufferSize: 4096,
                    FileOptions.SequentialScan);
                using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
                return reader.ReadToEnd();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                if (attempt == maxAttempts)
                    return null;

                var elapsedMs = (int)Math.Max(0, Environment.TickCount64 - startedAtMs);
                var remainingMs = totalTimeoutMs - elapsedMs;
                if (remainingMs <= 0)
                    return null;

                var boundedDelayMs = Math.Min(delayMs, remainingMs);
                await Task.Delay(boundedDelayMs, cancellationToken).ConfigureAwait(false);
                delayMs *= 2;
            }
        }

        return null;
    }
}
