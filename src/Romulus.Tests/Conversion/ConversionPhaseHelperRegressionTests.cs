using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ConversionPhaseHelperRegressionTests : IDisposable
{
    private readonly string _root;

    public ConversionPhaseHelperRegressionTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.ConversionPhaseHelper", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ConvertSingleFile_VerifyFailed_ReturnsErrorOutcomeAndIncrementsErrorCounter()
    {
        var sourcePath = Path.Combine(_root, "game.iso");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);

        var targetPath = Path.Combine(_root, "game.chd");
        var converter = new VerifyFailingConverter(targetPath);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            sourcePath,
            "PS1",
            converter,
            new RunOptions
            {
                Roots = [_root],
                Mode = RunConstants.ModeMove,
                Extensions = [".iso"]
            },
            CreateContext(RunConstants.ModeMove),
            counters,
            trackSetMembers: false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Error, result!.Outcome);
        Assert.Equal(1, counters.Errors);
        Assert.Equal(0, counters.Converted);
    }

    [Fact]
    public void ConvertSingleFile_DryRun_SkipsConversion()
    {
        var sourcePath = Path.Combine(_root, "preview.iso");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);

        var converter = new RecordingConverter();
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            sourcePath,
            "PS1",
            converter,
            new RunOptions
            {
                Roots = [_root],
                Mode = RunConstants.ModeDryRun,
                Extensions = [".iso"]
            },
            CreateContext(RunConstants.ModeDryRun),
            counters,
            trackSetMembers: false,
            CancellationToken.None);

        Assert.Null(result);
        Assert.False(converter.ConvertCalled);
        Assert.Equal(0, counters.Converted);
        Assert.Equal(0, counters.Errors);
    }

    [Fact]
    public void ConvertSingleFile_VerifyFailure_CleansAllAdditionalTargets()
    {
        var sourcePath = Path.Combine(_root, "multi-disc.cue");
        File.WriteAllText(sourcePath, "FILE \"track01.bin\" BINARY");

        var primaryTargetPath = Path.Combine(_root, "disc1.chd");
        var secondaryTargetPath = Path.Combine(_root, "disc2.chd");
        var converter = new VerifyFailingMultiOutputConverter(primaryTargetPath, secondaryTargetPath);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            sourcePath,
            "PS1",
            converter,
            new RunOptions
            {
                Roots = [_root],
                Mode = RunConstants.ModeMove,
                Extensions = [".cue"]
            },
            CreateContext(RunConstants.ModeMove, [".cue"]),
            counters,
            trackSetMembers: false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Error, result!.Outcome);
        Assert.False(File.Exists(primaryTargetPath));
        Assert.False(File.Exists(secondaryTargetPath));
    }

    [Fact]
    public void ConvertSingleFile_SetMemberMoveFailure_RollsBackMovedMembersAndPreservesSource()
    {
        var sourcePath = Path.Combine(_root, "game.cue");
        var localTrackPath = Path.Combine(_root, "track01.bin");

        File.WriteAllText(sourcePath, "FILE \"track01.bin\" BINARY");
        File.WriteAllBytes(localTrackPath, [1, 2, 3, 4]);

        var targetPath = Path.Combine(_root, "game.chd");
        var converter = new SuccessfulConverter(targetPath);
        var counters = new ConversionPhaseHelper.ConversionCounters();
        var fileSystem = new SourceMoveFailingFileSystem(sourcePath);

        var result = ConversionPhaseHelper.ConvertSingleFile(
            sourcePath,
            "PS1",
            converter,
            new RunOptions
            {
                Roots = [_root],
                Mode = RunConstants.ModeMove,
                Extensions = [".cue", ".bin"]
            },
            CreateContext(RunConstants.ModeMove, [".cue", ".bin"], fileSystem),
            counters,
            trackSetMembers: true,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Error, result!.Outcome);
        Assert.Equal(1, counters.Errors);
        Assert.True(File.Exists(sourcePath));
        Assert.True(File.Exists(localTrackPath));
        Assert.False(File.Exists(targetPath));
        Assert.False(File.Exists(Path.Combine(_root, RunConstants.WellKnownFolders.TrashConverted, Path.GetFileName(localTrackPath))));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private PipelineContext CreateContext(string mode, IReadOnlyList<string>? extensions = null, IFileSystem? fileSystem = null)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();

        return new PipelineContext
        {
            Options = new RunOptions
            {
                Roots = [_root],
                Mode = mode,
                Extensions = extensions ?? [".iso"]
            },
            FileSystem = fileSystem ?? new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(),
            Metrics = metrics
        };
    }

    private sealed class VerifyFailingConverter(string targetPath) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            File.WriteAllBytes(targetPath, [7, 8, 9, 10]);
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    private sealed class RecordingConverter : IFormatConverter
    {
        public bool ConvertCalled { get; private set; }

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            ConvertCalled = true;
            return new ConversionResult(sourcePath, Path.ChangeExtension(sourcePath, ".chd"), ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    private sealed class VerifyFailingMultiOutputConverter(string primaryTargetPath, string secondaryTargetPath) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            File.WriteAllBytes(primaryTargetPath, [1, 2, 3, 4]);
            File.WriteAllBytes(secondaryTargetPath, [5, 6, 7, 8]);
            return new ConversionResult(sourcePath, primaryTargetPath, ConversionOutcome.Success)
            {
                AdditionalTargetPaths = [secondaryTargetPath]
            };
        }

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    private sealed class SuccessfulConverter(string targetPath) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            File.WriteAllBytes(targetPath, [9, 10, 11, 12]);
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success);
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    private sealed class SourceMoveFailingFileSystem(string blockedSourcePath) : IFileSystem
    {
        private readonly FileSystemAdapter _inner = new();

        public bool TestPath(string literalPath, string pathType = "Any") => _inner.TestPath(literalPath, pathType);
        public string EnsureDirectory(string path) => _inner.EnsureDirectory(path);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => _inner.GetFilesSafe(root, extensions);

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            if (string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(blockedSourcePath), StringComparison.OrdinalIgnoreCase))
                return null;

            return _inner.MoveItemSafely(sourcePath, destinationPath);
        }

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => _inner.ResolveChildPathWithinRoot(rootPath, relativePath);
        public bool IsReparsePoint(string path) => _inner.IsReparsePoint(path);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) => _inner.CopyFile(sourcePath, destinationPath, overwrite);
    }
}
