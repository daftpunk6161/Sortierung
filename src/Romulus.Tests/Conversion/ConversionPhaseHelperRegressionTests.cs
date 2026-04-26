using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using System.Diagnostics;
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

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Skipped, result!.Outcome);
        Assert.Equal("dry-run-planned", result.Reason);
        Assert.Equal(Path.Combine(_root, "preview.chd"), result.TargetPath);
        Assert.False(converter.ConvertCalled);
        Assert.Equal(0, counters.Converted);
        Assert.Equal(0, counters.Errors);
    }

    [Fact]
    public void ConvertOnlyPhase_SuccessfulCueConversion_MovesDescriptorAndSetMembersToConvertedTrash()
    {
        var sourcePath = Path.Combine(_root, "game.cue");
        var trackPath = Path.Combine(_root, "track01.bin");
        File.WriteAllText(sourcePath, "FILE \"track01.bin\" BINARY");
        File.WriteAllBytes(trackPath, [1, 2, 3, 4]);

        var targetPath = Path.Combine(_root, "game.chd");
        var converter = new SuccessfulConverter(targetPath);
        var options = new RunOptions
        {
            Roots = [_root],
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd",
            Extensions = [".cue", ".bin"]
        };

        var result = new ConvertOnlyPipelinePhase().Execute(
            new ConvertOnlyPhaseInput(
                [new RomCandidate { MainPath = sourcePath, ConsoleKey = "PS1", Extension = ".cue", Category = FileCategory.Game, GameKey = "game" }],
                options,
                converter),
            CreateContext(RunConstants.ModeMove, [".cue", ".bin"]),
            CancellationToken.None);

        Assert.Equal(1, result.Converted);
        Assert.False(File.Exists(sourcePath));
        Assert.False(File.Exists(trackPath));
        Assert.True(File.Exists(Path.Combine(_root, RunConstants.WellKnownFolders.TrashConverted, "game.cue")));
        Assert.True(File.Exists(Path.Combine(_root, RunConstants.WellKnownFolders.TrashConverted, "track01.bin")));
    }

    [Fact]
    public void WinnerConversionPhase_SuccessfulCueConversion_MovesDescriptorAndSetMembersToConvertedTrash()
    {
        var sourcePath = Path.Combine(_root, "winner.cue");
        var trackPath = Path.Combine(_root, "winner-track.bin");
        File.WriteAllText(sourcePath, "FILE \"winner-track.bin\" BINARY");
        File.WriteAllBytes(trackPath, [1, 2, 3, 4]);

        var targetPath = Path.Combine(_root, "winner.chd");
        var converter = new SuccessfulConverter(targetPath);
        var options = new RunOptions
        {
            Roots = [_root],
            Mode = RunConstants.ModeMove,
            ConvertFormat = "chd",
            Extensions = [".cue", ".bin"]
        };
        var group = new DedupeGroup
        {
            GameKey = "winner",
            Winner = new RomCandidate { MainPath = sourcePath, ConsoleKey = "PS1", Extension = ".cue", Category = FileCategory.Game, GameKey = "winner" },
            Losers = []
        };

        var result = new WinnerConversionPipelinePhase().Execute(
            new WinnerConversionPhaseInput([group], options, new HashSet<string>(StringComparer.OrdinalIgnoreCase), converter),
            CreateContext(RunConstants.ModeMove, [".cue", ".bin"]),
            CancellationToken.None);

        Assert.Equal(1, result.Converted);
        Assert.False(File.Exists(sourcePath));
        Assert.False(File.Exists(trackPath));
        Assert.True(File.Exists(Path.Combine(_root, RunConstants.WellKnownFolders.TrashConverted, "winner.cue")));
        Assert.True(File.Exists(Path.Combine(_root, RunConstants.WellKnownFolders.TrashConverted, "winner-track.bin")));
    }

    [Fact]
    public void WinnerConversionPhase_DryRun_ReportsPlanWithoutCallingConvert()
    {
        var sourcePath = Path.Combine(_root, "dryrun.iso");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);

        var converter = new RecordingConverter();
        var options = new RunOptions
        {
            Roots = [_root],
            Mode = RunConstants.ModeDryRun,
            ConvertFormat = "chd",
            Extensions = [".iso"]
        };
        var group = new DedupeGroup
        {
            GameKey = "dryrun",
            Winner = new RomCandidate { MainPath = sourcePath, ConsoleKey = "PS1", Extension = ".iso", Category = FileCategory.Game, GameKey = "dryrun" },
            Losers = []
        };

        var result = new WinnerConversionPipelinePhase().Execute(
            new WinnerConversionPhaseInput([group], options, new HashSet<string>(StringComparer.OrdinalIgnoreCase), converter),
            CreateContext(RunConstants.ModeDryRun, [".iso"]),
            CancellationToken.None);

        Assert.Equal(0, result.Converted);
        Assert.Single(result.ConversionResults);
        Assert.Equal("dry-run-planned", result.ConversionResults[0].Reason);
        Assert.False(converter.ConvertCalled);
        Assert.True(File.Exists(sourcePath));
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

    [Fact]
    public void ConvertSingleFile_SourceOutsideRoots_BlocksBeforeConversion()
    {
        var outsideRoot = Path.Combine(Path.GetTempPath(), "Romulus.ConversionPhaseHelper.Outside", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(outsideRoot);
        try
        {
            var sourcePath = Path.Combine(outsideRoot, "outside.iso");
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
                    Mode = RunConstants.ModeMove,
                    Extensions = [".iso"]
                },
                CreateContext(RunConstants.ModeMove),
                counters,
                trackSetMembers: false,
                CancellationToken.None);

            Assert.Null(result);
            Assert.False(converter.ConvertCalled);
            Assert.Equal(1, counters.Blocked);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outsideRoot))
                    Directory.Delete(outsideRoot, true);
            }
            catch
            {
            }
        }
    }

    [Fact]
    public void ConvertSingleFile_SourceMoveFailure_WithRollbackFailure_ReportsRollbackPartialFailure()
    {
        var sourcePath = Path.Combine(_root, "rollback-fail.cue");
        var localTrackPath = Path.Combine(_root, "rollback-track.bin");

        File.WriteAllText(sourcePath, "FILE \"rollback-track.bin\" BINARY");
        File.WriteAllBytes(localTrackPath, [1, 2, 3, 4]);

        var targetPath = Path.Combine(_root, "rollback-fail.chd");
        var converter = new SuccessfulConverter(targetPath);
        var counters = new ConversionPhaseHelper.ConversionCounters();
        var fileSystem = new RollbackFailingFileSystem(sourcePath, localTrackPath);

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
        Assert.Equal("rollback-partial-failure", result.Reason);
    }

    [Fact]
    public void ConvertSingleFile_CleanupFailure_AppendsCleanupFailureAudit()
    {
        var sourcePath = Path.Combine(_root, "cleanup.iso");
        var targetPath = Path.Combine(_root, "cleanup.chd");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);
        File.WriteAllBytes(targetPath, [7, 8, 9, 10]);

        var fileSystem = new DeleteFailingFileSystem(targetPath);
        var auditStore = new RecordingAuditStore();
        var converter = new ErrorOutcomeConverter(targetPath);
        var counters = new ConversionPhaseHelper.ConversionCounters();

        var result = ConversionPhaseHelper.ConvertSingleFile(
            sourcePath,
            "PS1",
            converter,
            new RunOptions
            {
                Roots = [_root],
                Mode = RunConstants.ModeMove,
                Extensions = [".iso"],
                AuditPath = Path.Combine(_root, "audit.csv")
            },
            CreateContext(RunConstants.ModeMove, [".iso"], fileSystem, auditStore),
            counters,
            trackSetMembers: false,
            CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(ConversionOutcome.Error, result!.Outcome);
        Assert.Contains(
            auditStore.Rows,
            row => row.Reason.Contains("cleanup-output-failed", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ConvertSingleFile_WhenErrorAuditAppendThrows_WritesTraceFallback()
    {
        var sourcePath = Path.Combine(_root, "trace-fallback.iso");
        var targetPath = Path.Combine(_root, "trace-fallback.chd");
        File.WriteAllBytes(sourcePath, [1, 2, 3, 4]);

        var converter = new SuccessfulConverter(targetPath);
        var counters = new ConversionPhaseHelper.ConversionCounters();
        var fileSystem = new SourceMoveFailingFileSystem(sourcePath);
        var auditStore = new ThrowingAuditStore();

        var traceWriter = new StringWriter();
        var listener = new TextWriterTraceListener(traceWriter);
        Trace.Listeners.Add(listener);
        try
        {
            var result = ConversionPhaseHelper.ConvertSingleFile(
                sourcePath,
                "PS1",
                converter,
                new RunOptions
                {
                    Roots = [_root],
                    Mode = RunConstants.ModeMove,
                    Extensions = [".iso"],
                    AuditPath = Path.Combine(_root, "audit-throws.csv")
                },
                CreateContext(RunConstants.ModeMove, [".iso"], fileSystem, auditStore),
                counters,
                trackSetMembers: false,
                CancellationToken.None);

            Assert.NotNull(result);
            Assert.Equal(ConversionOutcome.Error, result!.Outcome);
        }
        finally
        {
            listener.Flush();
            Trace.Listeners.Remove(listener);
            listener.Dispose();
        }

        var traceOutput = traceWriter.ToString();
        Assert.Contains("conversion error audit", traceOutput, StringComparison.OrdinalIgnoreCase);
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

    private PipelineContext CreateContext(string mode, IReadOnlyList<string>? extensions = null, IFileSystem? fileSystem = null, IAuditStore? auditStore = null)
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
            AuditStore = auditStore ?? new AuditCsvStore(),
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

    private sealed class RollbackFailingFileSystem(string blockedSourcePath, string rollbackDestinationPath) : IFileSystem
    {
        private readonly FileSystemAdapter _inner = new();
        private readonly string _blockedSourcePath = Path.GetFullPath(blockedSourcePath);
        private readonly string _rollbackDestinationPath = Path.GetFullPath(rollbackDestinationPath);

        public bool TestPath(string literalPath, string pathType = "Any") => _inner.TestPath(literalPath, pathType);
        public string EnsureDirectory(string path) => _inner.EnsureDirectory(path);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => _inner.GetFilesSafe(root, extensions);

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            var fullSource = Path.GetFullPath(sourcePath);
            var fullDestination = Path.GetFullPath(destinationPath);

            if (string.Equals(fullSource, _blockedSourcePath, StringComparison.OrdinalIgnoreCase))
                return null;

            if (string.Equals(fullDestination, _rollbackDestinationPath, StringComparison.OrdinalIgnoreCase)
                && fullSource.Contains(RunConstants.WellKnownFolders.TrashConverted, StringComparison.OrdinalIgnoreCase))
            {
                throw new IOException("forced-rollback-failure");
            }

            return _inner.MoveItemSafely(sourcePath, destinationPath);
        }

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => _inner.ResolveChildPathWithinRoot(rootPath, relativePath);
        public bool IsReparsePoint(string path) => _inner.IsReparsePoint(path);
        public void DeleteFile(string path) => _inner.DeleteFile(path);
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) => _inner.CopyFile(sourcePath, destinationPath, overwrite);
    }

    private sealed class DeleteFailingFileSystem(string blockedDeletePath) : IFileSystem
    {
        private readonly FileSystemAdapter _inner = new();
        private readonly string _blockedDeletePath = Path.GetFullPath(blockedDeletePath);

        public bool TestPath(string literalPath, string pathType = "Any") => _inner.TestPath(literalPath, pathType);
        public string EnsureDirectory(string path) => _inner.EnsureDirectory(path);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? extensions = null) => _inner.GetFilesSafe(root, extensions);
        public string? MoveItemSafely(string sourcePath, string destinationPath) => _inner.MoveItemSafely(sourcePath, destinationPath);
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) => _inner.ResolveChildPathWithinRoot(rootPath, relativePath);
        public bool IsReparsePoint(string path) => _inner.IsReparsePoint(path);

        public void DeleteFile(string path)
        {
            if (string.Equals(Path.GetFullPath(path), _blockedDeletePath, StringComparison.OrdinalIgnoreCase))
                throw new IOException("forced-delete-failure");

            _inner.DeleteFile(path);
        }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) => _inner.CopyFile(sourcePath, destinationPath, overwrite);
    }

    private sealed class ErrorOutcomeConverter(string targetPath) : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, targetPath, ConversionOutcome.Error, "tool-error");

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    private sealed class RecordingAuditStore : IAuditStore
    {
        public List<AuditAppendRow> Rows { get; } = [];

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false) => [];

        public void AppendAuditRow(
            string auditCsvPath,
            string rootPath,
            string oldPath,
            string newPath,
            string action,
            string category = "",
            string hash = "",
            string reason = "")
        {
            Rows.Add(new AuditAppendRow(rootPath, oldPath, newPath, action, category, hash, reason));
        }

        public void AppendAuditRows(string auditCsvPath, IReadOnlyList<AuditAppendRow> rows)
        {
            Rows.AddRange(rows);
        }
    }

    private sealed class ThrowingAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public void Flush(string auditCsvPath) { }
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false) => [];

        public void AppendAuditRow(
            string auditCsvPath,
            string rootPath,
            string oldPath,
            string newPath,
            string action,
            string category = "",
            string hash = "",
            string reason = "")
        {
            throw new IOException("forced-audit-append-failure");
        }
    }
}
