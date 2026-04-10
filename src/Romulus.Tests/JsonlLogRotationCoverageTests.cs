using System.IO.Compression;
using Romulus.Infrastructure.Logging;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for JsonlLogWriter.RotateIfNeeded and JsonlLogRotation (gzip + prune).
/// Targets 54% line coverage in JsonlLogRotation → near 100%,
/// and 72% line coverage in JsonlLogWriter → near 100%.
/// </summary>
public sealed class JsonlLogRotationCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public JsonlLogRotationCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"logrot-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    // ═══ JsonlLogRotation.Rotate — Gzip branch ════════════════════

    [Fact]
    public void Rotate_WithGzip_CreatesGzArchive()
    {
        var logPath = Path.Combine(_tempDir, "gzip-test.jsonl");
        File.WriteAllText(logPath, new string('x', 200));

        JsonlLogRotation.Rotate(logPath, maxBytes: 100, keepFiles: 5, gzip: true);

        Assert.False(File.Exists(logPath), "Original should be moved");
        var gzFiles = Directory.GetFiles(_tempDir, "gzip-test-*.jsonl.gz");
        Assert.Single(gzFiles);

        // Verify gzip content is valid
        using var fs = File.OpenRead(gzFiles[0]);
        using var gz = new GZipStream(fs, CompressionMode.Decompress);
        using var reader = new StreamReader(gz);
        var content = reader.ReadToEnd();
        Assert.Equal(200, content.Length);
    }

    [Fact]
    public void Rotate_WithGzip_DeletesPlainArchive()
    {
        var logPath = Path.Combine(_tempDir, "gzclean.jsonl");
        File.WriteAllText(logPath, new string('y', 150));

        JsonlLogRotation.Rotate(logPath, maxBytes: 50, gzip: true);

        // Plain .jsonl archive should not exist — only .gz
        var plainArchives = Directory.GetFiles(_tempDir, "gzclean-*.jsonl")
            .Where(f => !f.EndsWith(".gz"))
            .ToArray();
        Assert.Empty(plainArchives);

        var gzArchives = Directory.GetFiles(_tempDir, "gzclean-*.jsonl.gz");
        Assert.Single(gzArchives);
    }

    // ═══ JsonlLogRotation.Rotate — Prune old archives ═════════════

    [Fact]
    public void Rotate_PrunesOldArchives_PlainText()
    {
        var logPath = Path.Combine(_tempDir, "prune.jsonl");

        // Pre-create 4 "old" archives
        for (int i = 0; i < 4; i++)
        {
            var archivePath = Path.Combine(_tempDir, $"prune-2024010{i}-000000.jsonl");
            File.WriteAllText(archivePath, $"archive-{i}");
        }

        // Write a large current log
        File.WriteAllText(logPath, new string('z', 300));

        JsonlLogRotation.Rotate(logPath, maxBytes: 100, keepFiles: 2, gzip: false);

        // Should have the new archive + 1 kept = 2 kept total, rest pruned
        var archives = Directory.GetFiles(_tempDir, "prune-*.jsonl")
            .Where(f => !string.Equals(f, logPath, StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.True(archives.Length <= 2, $"Expected <= 2 kept archives, got {archives.Length}");
    }

    [Fact]
    public void Rotate_PrunesOldArchives_Gzip()
    {
        var logPath = Path.Combine(_tempDir, "prunegz.jsonl");

        // Pre-create 5 old .gz archives
        for (int i = 0; i < 5; i++)
        {
            var archivePath = Path.Combine(_tempDir, $"prunegz-2024010{i}-000000.jsonl.gz");
            File.WriteAllText(archivePath, $"archive-{i}");
        }

        File.WriteAllText(logPath, new string('a', 200));

        JsonlLogRotation.Rotate(logPath, maxBytes: 50, keepFiles: 2, gzip: true);

        var gzArchives = Directory.GetFiles(_tempDir, "prunegz-*.jsonl.gz");
        Assert.True(gzArchives.Length <= 2, $"Expected <= 2 .gz archives, got {gzArchives.Length}");
    }

    // ═══ JsonlLogRotation.Rotate — Edge cases ═════════════════════

    [Fact]
    public void Rotate_FileDoesNotExist_NoOp()
    {
        var logPath = Path.Combine(_tempDir, "nonexistent.jsonl");
        JsonlLogRotation.Rotate(logPath); // should not throw
        Assert.False(File.Exists(logPath));
    }

    [Fact]
    public void Rotate_FileUnderMaxBytes_NoOp()
    {
        var logPath = Path.Combine(_tempDir, "tiny.jsonl");
        File.WriteAllText(logPath, "small");

        JsonlLogRotation.Rotate(logPath, maxBytes: 1024);

        Assert.True(File.Exists(logPath)); // not rotated
        Assert.Equal("small", File.ReadAllText(logPath));
    }

    // ═══ JsonlLogWriter.RotateIfNeeded ════════════════════════════

    [Fact]
    public void RotateIfNeeded_SmallFile_DoesNotRotate()
    {
        var logPath = Path.Combine(_tempDir, "writer-small.jsonl");
        using var writer = new JsonlLogWriter(logPath);
        writer.Info("TEST", "action", "msg");

        writer.RotateIfNeeded(maxBytes: 10 * 1024 * 1024);

        Assert.True(File.Exists(logPath));
        var archives = Directory.GetFiles(_tempDir, "writer-small-*.jsonl");
        Assert.Empty(archives);
    }

    [Fact]
    public void RotateIfNeeded_LargeFile_RotatesAndReopensWriter()
    {
        var logPath = Path.Combine(_tempDir, "writer-big.jsonl");
        var writer = new JsonlLogWriter(logPath);

        // Write enough data to exceed threshold
        for (int i = 0; i < 100; i++)
            writer.Info("TEST", "action", new string('x', 100));

        writer.RotateIfNeeded(maxBytes: 500);

        // After rotation, verify we can still write
        writer.Info("TEST", "post-rotate", "still works");
        writer.Dispose();

        Assert.True(File.Exists(logPath));
        var content = File.ReadAllText(logPath);
        Assert.Contains("post-rotate", content);
    }

    [Fact]
    public void RotateIfNeeded_WithGzip_CreatesGzArchive()
    {
        var logPath = Path.Combine(_tempDir, "writer-gz.jsonl");
        using var writer = new JsonlLogWriter(logPath);

        for (int i = 0; i < 50; i++)
            writer.Info("TEST", "bulk", new string('y', 200));

        writer.RotateIfNeeded(maxBytes: 500, gzip: true);

        var gzFiles = Directory.GetFiles(_tempDir, "writer-gz-*.jsonl.gz");
        Assert.Single(gzFiles);

        // Verify can still write
        writer.Info("TEST", "after-gz-rotate", "ok");
        Assert.True(File.Exists(logPath));
    }

    [Fact]
    public void RotateIfNeeded_FileDeletedExternally_NoOp()
    {
        var logPath = Path.Combine(_tempDir, "writer-deleted.jsonl");
        using var writer = new JsonlLogWriter(logPath);
        writer.Info("TEST", "action", "msg");

        // Externally delete the file (simulating external cleanup)
        writer.Dispose();
        File.Delete(logPath);

        // Recreate the writer to test RotateIfNeeded on a pathological state
        using var writer2 = new JsonlLogWriter(logPath);
        writer2.RotateIfNeeded(maxBytes: 1); // file just created, might be small
    }

    [Fact]
    public void RotateIfNeeded_MultipleLevels_AllWrite()
    {
        var logPath = Path.Combine(_tempDir, "writer-levels.jsonl");
        var writer = new JsonlLogWriter(logPath, LogLevel.Debug);

        writer.Debug("M", "debug msg");
        writer.Info("M", "info-action", "info msg");
        writer.Warning("M", "warn msg");
        writer.Error("M", "error msg");
        writer.Dispose();

        var lines = File.ReadAllLines(logPath);
        Assert.Equal(4, lines.Length);
    }
}
