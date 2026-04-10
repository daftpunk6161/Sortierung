using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

public sealed class FileSystemAllowedRootTests : IDisposable
{
    private readonly string _tempDir;

    public FileSystemAllowedRootTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "FsAllowedRoot_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void MoveItemSafely_WithAllowedRootOutsideDestination_ReturnsNull()
    {
        IFileSystem fs = new FileSystemAdapter();
        var source = Path.Combine(_tempDir, "a.txt");
        var outside = Path.Combine(_tempDir, "outside");
        var target = Path.Combine(outside, "a.txt");
        var allowedRoot = Path.Combine(_tempDir, "allowed");

        Directory.CreateDirectory(outside);
        Directory.CreateDirectory(allowedRoot);
        File.WriteAllText(source, "x");

        var result = fs.MoveItemSafely(source, target, allowedRoot);

        Assert.Null(result);
        Assert.True(File.Exists(source));
    }

    [Fact]
    public void MoveItemSafely_WithAllowedRootInsideDestination_MovesFile()
    {
        IFileSystem fs = new FileSystemAdapter();
        var source = Path.Combine(_tempDir, "b.txt");
        var allowedRoot = Path.Combine(_tempDir, "allowed");
        var target = Path.Combine(allowedRoot, "b.txt");

        Directory.CreateDirectory(allowedRoot);
        File.WriteAllText(source, "x");

        var result = fs.MoveItemSafely(source, target, allowedRoot);

        Assert.NotNull(result);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
    }
}
