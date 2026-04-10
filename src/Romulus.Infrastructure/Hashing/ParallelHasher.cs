using System.Security.Cryptography;
using Romulus.Contracts.Models;

namespace Romulus.Infrastructure.Hashing;

/// <summary>
/// Parallel file hashing using Task-based parallelism.
/// Port of ParallelHashing.ps1 — RunspacePool replaced with Task.WhenAll.
/// </summary>
public sealed class ParallelHasher
{
    /// <summary>
    /// Calculate optimal thread count based on processor count.
    /// </summary>
    public static int GetOptimalThreadCount(int maxThreads = 8)
        => Math.Max(1, Math.Min(Environment.ProcessorCount, maxThreads));

    /// <summary>
    /// Hash a single file safely. Returns (path, hash, error).
    /// </summary>
    public static FileHashEntry HashFileSafe(string path, string algorithm = "SHA1")
    {
        try
        {
            using var stream = File.OpenRead(path);
            string hashHex = algorithm.ToUpperInvariant() switch
            {
                "SHA256" => Convert.ToHexStringLower(SHA256.HashData(stream)),
                "MD5" => Convert.ToHexStringLower(MD5.HashData(stream)),
                "CRC32" or "CRC" => Crc32.HashStream(stream),
                "SHA1" => Convert.ToHexStringLower(SHA1.HashData(stream)),
                _ => throw new ArgumentException($"Unsupported hash algorithm: {algorithm}")
            };
            return new FileHashEntry { Path = path, Hash = hashHex };
        }
        catch (Exception ex)
        {
            return new FileHashEntry { Path = path, Error = ex.Message };
        }
    }

    /// <summary>
    /// Hash multiple files in parallel.
    /// </summary>
    public static async Task<ParallelHashResult> HashFilesAsync(
        IReadOnlyList<string> files,
        string algorithm = "SHA1",
        int maxThreads = 8,
        Action<int, int>? onProgress = null,
        CancellationToken ct = default)
    {
        if (files.Count == 0)
            return new ParallelHashResult { Results = [], TotalFiles = 0 };

        var threadCount = GetOptimalThreadCount(maxThreads);

        if (files.Count <= 4 || threadCount <= 1)
        {
            // Single-threaded for small batches
            return HashFilesSingleThread(files, algorithm, onProgress, ct);
        }

        var results = new FileHashEntry[files.Count];
        int completed = 0;
        int errors = 0;

        var options = new ParallelOptions
        {
            MaxDegreeOfParallelism = threadCount,
            CancellationToken = ct
        };

        await Parallel.ForEachAsync(
            Enumerable.Range(0, files.Count),
            options,
            (index, token) =>
            {
                token.ThrowIfCancellationRequested();
                results[index] = HashFileSafe(files[index], algorithm);
                if (results[index].Error is not null)
                    Interlocked.Increment(ref errors);

                var done = Interlocked.Increment(ref completed);
                onProgress?.Invoke(done, files.Count);
                return ValueTask.CompletedTask;
            });

        return new ParallelHashResult
        {
            Results = results.ToList(),
            TotalFiles = files.Count,
            Errors = errors,
            Method = "Parallel"
        };
    }

    /// <summary>
    /// Synchronous wrapper for parallel hashing.
    /// Uses Task.Run to avoid deadlock on UI threads with SynchronizationContext.
    /// </summary>
    public static ParallelHashResult HashFiles(
        IReadOnlyList<string> files,
        string algorithm = "SHA1",
        int maxThreads = 8,
        Action<int, int>? onProgress = null)
    {
        return Task.Run(() => HashFilesAsync(files, algorithm, maxThreads, onProgress)).GetAwaiter().GetResult();
    }

    private static ParallelHashResult HashFilesSingleThread(
        IReadOnlyList<string> files,
        string algorithm,
        Action<int, int>? onProgress,
        CancellationToken ct)
    {
        var results = new List<FileHashEntry>(files.Count);
        int errors = 0;

        for (int i = 0; i < files.Count; i++)
        {
            ct.ThrowIfCancellationRequested();
            var entry = HashFileSafe(files[i], algorithm);
            results.Add(entry);
            if (entry.Error is not null) errors++;
            onProgress?.Invoke(i + 1, files.Count);
        }

        return new ParallelHashResult
        {
            Results = results,
            TotalFiles = files.Count,
            Errors = errors,
            Method = "SingleThread"
        };
    }
}
