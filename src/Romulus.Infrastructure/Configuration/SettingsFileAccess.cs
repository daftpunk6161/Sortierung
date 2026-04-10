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

    public static string? TryReadAllText(string path, int maxAttempts = DefaultMaxAttempts)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;

        var delayMs = InitialDelayMs;
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

                Thread.Sleep(delayMs);
                delayMs *= 2;
            }
        }

        return null;
    }
}
