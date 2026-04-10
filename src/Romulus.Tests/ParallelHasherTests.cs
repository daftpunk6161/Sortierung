using Romulus.Infrastructure.Hashing;
using Xunit;

namespace Romulus.Tests;

public sealed class ParallelHasherTests : IDisposable
{
    private readonly string _tempDir;

    public ParallelHasherTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "hash_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // =========================================================================
    //  GetOptimalThreadCount Tests
    // =========================================================================

    [Fact]
    public void GetOptimalThreadCount_AtLeastOne()
        => Assert.True(ParallelHasher.GetOptimalThreadCount() >= 1);

    [Fact]
    public void GetOptimalThreadCount_RespectsCap()
        => Assert.True(ParallelHasher.GetOptimalThreadCount(2) <= 2);

    // =========================================================================
    //  HashFileSafe Tests
    // =========================================================================

    [Fact]
    public void HashFileSafe_ValidFile_ReturnsHash()
    {
        var path = Path.Combine(_tempDir, "test.bin");
        File.WriteAllBytes(path, [1, 2, 3, 4]);

        var entry = ParallelHasher.HashFileSafe(path);
        Assert.Equal(path, entry.Path);
        Assert.NotNull(entry.Hash);
        Assert.Null(entry.Error);
        Assert.True(entry.Hash.Length > 0);
    }

    [Fact]
    public void HashFileSafe_NonExistent_ReturnsError()
    {
        var entry = ParallelHasher.HashFileSafe(@"C:\nonexistent_file_12345.bin");
        Assert.NotNull(entry.Error);
        Assert.Null(entry.Hash);
    }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("MD5")]
    public void HashFileSafe_DifferentAlgorithms(string algo)
    {
        var path = Path.Combine(_tempDir, $"test_{algo}.bin");
        File.WriteAllBytes(path, [10, 20, 30]);

        var entry = ParallelHasher.HashFileSafe(path, algo);
        Assert.NotNull(entry.Hash);
        Assert.Null(entry.Error);
    }

    [Fact]
    public void HashFileSafe_SameFile_SameHash()
    {
        var path = Path.Combine(_tempDir, "deterministic.bin");
        File.WriteAllBytes(path, [1, 2, 3]);

        var h1 = ParallelHasher.HashFileSafe(path);
        var h2 = ParallelHasher.HashFileSafe(path);
        Assert.Equal(h1.Hash, h2.Hash);
    }

    // =========================================================================
    //  HashFiles (sync) Tests
    // =========================================================================

    [Fact]
    public void HashFiles_EmptyList_ReturnsEmpty()
    {
        var result = ParallelHasher.HashFiles([]);
        Assert.Equal(0, result.TotalFiles);
        Assert.Empty(result.Results);
    }

    [Fact]
    public void HashFiles_MultipleFiles_AllHashed()
    {
        var files = new List<string>();
        for (int i = 0; i < 5; i++)
        {
            var path = Path.Combine(_tempDir, $"file_{i}.bin");
            File.WriteAllBytes(path, BitConverter.GetBytes(i));
            files.Add(path);
        }

        var result = ParallelHasher.HashFiles(files);
        Assert.Equal(5, result.TotalFiles);
        Assert.Equal(5, result.Results.Count);
        Assert.Equal(0, result.Errors);
        Assert.All(result.Results, r => Assert.NotNull(r.Hash));
    }

    [Fact]
    public void HashFiles_SmallBatch_UsesSingleThread()
    {
        var files = new List<string>();
        for (int i = 0; i < 3; i++) // <= 4 => single-thread path
        {
            var path = Path.Combine(_tempDir, $"small_{i}.bin");
            File.WriteAllBytes(path, [42]);
            files.Add(path);
        }

        var result = ParallelHasher.HashFiles(files);
        Assert.Equal("SingleThread", result.Method);
        Assert.Equal(3, result.Results.Count);
    }

    [Fact]
    public async Task HashFilesAsync_SingleThread_RespectsCancellation()
    {
        var files = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var path = Path.Combine(_tempDir, $"cancel_{i}.bin");
            File.WriteAllBytes(path, [42]);
            files.Add(path);
        }

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            ParallelHasher.HashFilesAsync(files, ct: cts.Token));
    }

    [Fact]
    public void HashFiles_LargeBatch_UsesParallel()
    {
        var files = new List<string>();
        for (int i = 0; i < 10; i++) // >4 => parallel path
        {
            var path = Path.Combine(_tempDir, $"large_{i}.bin");
            File.WriteAllBytes(path, BitConverter.GetBytes(i * 100));
            files.Add(path);
        }

        var result = ParallelHasher.HashFiles(files);
        Assert.Equal("Parallel", result.Method);
        Assert.Equal(10, result.Results.Count);
    }

    [Fact]
    public void HashFiles_ProgressCallback_Invoked()
    {
        var files = new List<string>();
        for (int i = 0; i < 3; i++)
        {
            var path = Path.Combine(_tempDir, $"progress_{i}.bin");
            File.WriteAllBytes(path, [1]);
            files.Add(path);
        }

        int callCount = 0;
        ParallelHasher.HashFiles(files, onProgress: (done, total) =>
        {
            Interlocked.Increment(ref callCount);
        });
        Assert.Equal(3, callCount);
    }

    [Fact]
    public void HashFiles_MixedExistingAndMissing_CountsErrors()
    {
        var goodPath = Path.Combine(_tempDir, "good.bin");
        File.WriteAllBytes(goodPath, [1, 2]);

        var result = ParallelHasher.HashFiles([goodPath, @"C:\does_not_exist_12345.bin"]);
        Assert.Equal(2, result.TotalFiles);
        Assert.Equal(1, result.Errors);
    }
}
