using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

public sealed class CliCollectionDiffMergeTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fileSystem = new();

    public CliCollectionDiffMergeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "CliCollectionDiffMerge_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best effort
        }
    }

    [Fact]
    public void CliArgsParser_Diff_ParsesScopesLabelsAndPaging()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");

        var result = CliArgsParser.Parse(
        [
            "diff",
            "--left-roots", leftRoot,
            "--right-roots", rightRoot,
            "--left-label", "Backup",
            "--right-label", "Primary",
            "--extensions", ".sfc,.zip",
            "--offset", "5",
            "--limit", "25"
        ]);

        Assert.Equal(CliCommand.Diff, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal("Backup", result.Options!.LeftLabel);
        Assert.Equal("Primary", result.Options.RightLabel);
        Assert.Equal(5, result.Options.CollectionOffset);
        Assert.Equal(25, result.Options.CollectionLimit);
        Assert.Contains(".sfc", result.Options.Extensions);
        Assert.Contains(".zip", result.Options.Extensions);
    }

    [Fact]
    public void CliArgsParser_Merge_ParsesTargetApplyAuditAndMoveMode()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var auditPath = Path.Combine(_tempDir, "merge-audit.csv");

        var result = CliArgsParser.Parse(
        [
            "merge",
            "--left-roots", leftRoot,
            "--right-roots", rightRoot,
            "--target-root", targetRoot,
            "--allow-moves",
            "--apply",
            "--audit", auditPath,
            "--yes"
        ]);

        Assert.Equal(CliCommand.Merge, result.Command);
        Assert.Equal(0, result.ExitCode);
        Assert.NotNull(result.Options);
        Assert.Equal(targetRoot, result.Options!.TargetRoot);
        Assert.True(result.Options.AllowMoves);
        Assert.True(result.Options.MergeApply);
        Assert.Equal(auditPath, result.Options.AuditPath);
        Assert.True(result.Options.Yes);
    }

    [Fact]
    public void DiffForTests_WritesCollectionCompareJson_ToStdout()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var rightPath = CreateFile(rightRoot, "SNES", "Mario.sfc", "right");
        var index = new FakeCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-1", "fp-1"),
            CreateEntry(rightPath, rightRoot, "SNES", "mario", "hash-1", "fp-1")
        ]);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            CliProgram.SetConsoleOverrides(stdout, stderr);

            var exitCode = CliProgram.DiffForTests(
                new CliRunOptions
                {
                    LeftRoots = [leftRoot],
                    RightRoots = [rightRoot],
                    LeftLabel = "Left",
                    RightLabel = "Right",
                    Extensions = new HashSet<string>([".sfc"], StringComparer.OrdinalIgnoreCase),
                    CollectionLimit = 50
                },
                index,
                _fileSystem);

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var doc = JsonDocument.Parse(stdout.ToString());
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("Summary").GetProperty("TotalEntries").GetInt32());
            Assert.Equal("identical-primary-hash", root.GetProperty("Entries")[0].GetProperty("ReasonCode").GetString());
        }
        finally
        {
            CliProgram.SetConsoleOverrides(null, null);
        }
    }

    [Fact]
    public void MergeForTests_Plan_WritesCollectionMergePlanJson_ToStdout()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var index = new FakeCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
        ]);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            CliProgram.SetConsoleOverrides(stdout, stderr);

            var exitCode = CliProgram.MergeForTests(
                new CliRunOptions
                {
                    LeftRoots = [leftRoot],
                    RightRoots = [rightRoot],
                    TargetRoot = targetRoot,
                    Extensions = new HashSet<string>([".sfc"], StringComparer.OrdinalIgnoreCase),
                    CollectionLimit = 50
                },
                index,
                _fileSystem,
                new AuditCsvStore(_fileSystem));

            Assert.Equal(0, exitCode);
            Assert.Equal(string.Empty, stderr.ToString());

            using var doc = JsonDocument.Parse(stdout.ToString());
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("Summary").GetProperty("CopyToTarget").GetInt32());
            Assert.Equal("merge-copy-to-target", root.GetProperty("Entries")[0].GetProperty("ReasonCode").GetString());
        }
        finally
        {
            CliProgram.SetConsoleOverrides(null, null);
        }
    }

    [Fact]
    public void MergeForTests_Apply_WritesCollectionMergeResultJson_AndAudit()
    {
        var leftRoot = CreateRoot("left");
        var rightRoot = CreateRoot("right");
        var targetRoot = CreateRoot("target");
        var leftPath = CreateFile(leftRoot, "SNES", "Mario.sfc", "left");
        var auditPath = Path.Combine(_tempDir, "merge-apply.csv");
        var index = new FakeCollectionIndex(
        [
            CreateEntry(leftPath, leftRoot, "SNES", "mario", "hash-left", "fp-1")
        ]);

        using var stdout = new StringWriter();
        using var stderr = new StringWriter();

        try
        {
            CliProgram.SetConsoleOverrides(stdout, stderr);

            var exitCode = CliProgram.MergeForTests(
                new CliRunOptions
                {
                    LeftRoots = [leftRoot],
                    RightRoots = [rightRoot],
                    TargetRoot = targetRoot,
                    MergeApply = true,
                    AuditPath = auditPath,
                    Extensions = new HashSet<string>([".sfc"], StringComparer.OrdinalIgnoreCase),
                    CollectionLimit = 50
                },
                index,
                _fileSystem,
                new AuditCsvStore(_fileSystem));

            Assert.Equal(0, exitCode);
            Assert.Contains("[Merge] Audit:", stderr.ToString(), StringComparison.OrdinalIgnoreCase);
            Assert.True(File.Exists(Path.Combine(targetRoot, "SNES", "Mario.sfc")));
            Assert.True(File.Exists(leftPath));
            Assert.True(File.Exists(auditPath));

            using var doc = JsonDocument.Parse(stdout.ToString());
            var root = doc.RootElement;
            Assert.Equal(1, root.GetProperty("Summary").GetProperty("Applied").GetInt32());
            Assert.Equal(auditPath, root.GetProperty("AuditPath").GetString());
            Assert.True(root.GetProperty("RollbackAvailable").GetBoolean());
        }
        finally
        {
            CliProgram.SetConsoleOverrides(null, null);
        }
    }

    private string CreateRoot(string name)
    {
        var root = Path.Combine(_tempDir, name);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string CreateFile(string root, string relativeDirectory, string fileName, string content)
    {
        var directory = Path.Combine(root, relativeDirectory);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, fileName);
        File.WriteAllText(path, content);
        return path;
    }

    private static CollectionIndexEntry CreateEntry(
        string path,
        string root,
        string consoleKey,
        string gameKey,
        string hash,
        string enrichmentFingerprint,
        int regionScore = 0)
    {
        var info = new FileInfo(path);
        return new CollectionIndexEntry
        {
            Path = path,
            Root = root,
            FileName = Path.GetFileName(path),
            Extension = Path.GetExtension(path),
            SizeBytes = info.Length,
            LastWriteUtc = info.LastWriteTimeUtc,
            LastScannedUtc = info.LastWriteTimeUtc,
            EnrichmentFingerprint = enrichmentFingerprint,
            PrimaryHashType = "SHA1",
            PrimaryHash = hash,
            ConsoleKey = consoleKey,
            GameKey = gameKey,
            Region = "EU",
            RegionScore = regionScore,
            FormatScore = 100,
            VersionScore = 1,
            HeaderScore = 10,
            CompletenessScore = 1,
            SizeTieBreakScore = info.Length,
            Category = FileCategory.Game,
            SortDecision = SortDecision.Sort
        };
    }

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly List<CollectionIndexEntry> _entries;

        public FakeCollectionIndex(IReadOnlyList<CollectionIndexEntry>? entries = null)
        {
            _entries = entries?.ToList() ?? [];
        }

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => ValueTask.FromResult(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => ValueTask.FromResult(_entries.Count);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => ValueTask.FromResult(_entries.FirstOrDefault(entry => string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)));

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(entry => paths.Contains(entry.Path, StringComparer.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(
                _entries.Where(entry => string.Equals(entry.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)).ToArray());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
            IReadOnlyList<string> roots,
            IReadOnlyCollection<string> extensions,
            CancellationToken ct = default)
        {
            var normalizedRoots = roots
                .Where(static root => !string.IsNullOrWhiteSpace(root))
                .Select(static root => Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
            var normalizedExtensions = extensions
                .Where(static extension => !string.IsNullOrWhiteSpace(extension))
                .Select(static extension => extension.StartsWith('.') ? extension.ToLowerInvariant() : "." + extension.ToLowerInvariant())
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var scoped = _entries
                .Where(entry =>
                {
                    var normalizedPath = Path.GetFullPath(entry.Path);
                    return normalizedRoots.Any(root => normalizedPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                           && normalizedExtensions.Contains(entry.Extension.ToLowerInvariant());
                })
                .OrderBy(entry => entry.Path, StringComparer.OrdinalIgnoreCase)
                .ThenBy(entry => entry.Path, StringComparer.Ordinal)
                .ToArray();

            return ValueTask.FromResult<IReadOnlyList<CollectionIndexEntry>>(scoped);
        }

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
        {
            foreach (var entry in entries)
            {
                _entries.RemoveAll(existing => string.Equals(existing.Path, entry.Path, StringComparison.OrdinalIgnoreCase));
                _entries.Add(entry);
            }

            return ValueTask.CompletedTask;
        }

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
        {
            foreach (var path in paths)
                _entries.RemoveAll(existing => string.Equals(existing.Path, path, StringComparison.OrdinalIgnoreCase));
            return ValueTask.CompletedTask;
        }

        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default)
            => ValueTask.FromResult<CollectionHashCacheEntry?>(null);

        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => ValueTask.FromResult(0);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<CollectionRunSnapshot>>(Array.Empty<CollectionRunSnapshot>());
    }
}
