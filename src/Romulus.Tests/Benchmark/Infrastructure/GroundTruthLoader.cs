using System.Text.Json;
using Romulus.Tests.Benchmark.Models;

namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Loads ground-truth entries from JSONL files in benchmark/ground-truth/.
/// Each line is a separate JSON object matching the GroundTruthEntry schema.
/// </summary>
internal sealed class GroundTruthLoader
{
    private const int MaxReadAttempts = 5;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads all entries from all .jsonl files in ground-truth directory.
    /// </summary>
    public static List<GroundTruthEntry> LoadAll()
    {
        var files = BenchmarkPaths.AllJsonlFiles;
        var entries = new List<GroundTruthEntry>();

        foreach (var file in files.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            entries.AddRange(LoadFile(file));
        }

        return entries;
    }

    /// <summary>
    /// Loads entries from a single JSONL file.
    /// </summary>
    public static List<GroundTruthEntry> LoadFile(string path)
    {
        for (int attempt = 0; attempt < MaxReadAttempts; attempt++)
        {
            try
            {
                return LoadFileSnapshot(path);
            }
            catch (IOException) when (attempt < MaxReadAttempts - 1)
            {
                Thread.Sleep(GetRetryDelayMs(attempt));
            }
            catch (InvalidOperationException ex) when (attempt < MaxReadAttempts - 1 && IsTransientJsonlReadFailure(ex))
            {
                Thread.Sleep(GetRetryDelayMs(attempt));
            }
        }

        return LoadFileSnapshot(path);
    }

    private static List<GroundTruthEntry> LoadFileSnapshot(string path)
    {
        var entries = new List<GroundTruthEntry>();
        var seenIds = new HashSet<string>(StringComparer.Ordinal);

        // Use FileShare.ReadWrite to tolerate concurrent writes from dataset-generation tests.
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        using var reader = new StreamReader(fs);
        var lines = reader.ReadToEnd().Split('\n');

        for (int lineNum = 0; lineNum < lines.Length; lineNum++)
        {
            var line = lines[lineNum].Trim();
            if (string.IsNullOrEmpty(line) || line.StartsWith("//"))
                continue;

            try
            {
                var entry = JsonSerializer.Deserialize<GroundTruthEntry>(line, JsonOptions);
                if (entry is not null)
                {
                    if (!seenIds.Add(entry.Id))
                    {
                        throw new InvalidOperationException(
                            $"Duplicate ground-truth id '{entry.Id}' in {Path.GetFileName(path)} (line {lineNum + 1}).");
                    }

                    entries.Add(entry);
                }
            }
            catch (JsonException ex)
            {
                throw new InvalidOperationException(
                    $"Failed to parse JSONL line {lineNum + 1} in {Path.GetFileName(path)}: {ex.Message}", ex);
            }
        }

        return entries;
    }

    private static bool IsTransientJsonlReadFailure(InvalidOperationException ex) =>
        ex.Message.StartsWith("Failed to parse JSONL line ", StringComparison.Ordinal);

    private static int GetRetryDelayMs(int attempt) => 100 * (attempt + 1);

    /// <summary>
    /// Loads entries from a specific ground-truth dataset by file name (without directory).
    /// </summary>
    public static List<GroundTruthEntry> LoadSet(string setFileName)
    {
        var path = Path.Combine(BenchmarkPaths.GroundTruthDir, setFileName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"Ground-truth set not found: {setFileName}", path);
        return LoadFile(path);
    }

    /// <summary>
    /// Returns entries grouped by the JSONL file they came from.
    /// Key is the filename without directory.
    /// </summary>
    public static Dictionary<string, List<GroundTruthEntry>> LoadGroupedBySet()
    {
        var result = new Dictionary<string, List<GroundTruthEntry>>(StringComparer.OrdinalIgnoreCase);

        foreach (var file in BenchmarkPaths.AllJsonlFiles.OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var entries = LoadFile(file);
            if (entries.Count > 0)
                result[Path.GetFileName(file)] = entries;
        }

        return result;
    }
}
