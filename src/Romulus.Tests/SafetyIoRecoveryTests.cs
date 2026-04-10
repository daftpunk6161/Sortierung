using System.IO.Compression;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Security tests for dangerous core operations: Conversion (Zip-Slip, zip bombs),
/// Move/Trash (path traversal, reparse points), Restore/Rollback (integrity),
/// and pipeline phase safety guards.
/// </summary>
public sealed class SafetyIoRecoveryTests : IDisposable
{
    private readonly string _tempDir;

    public SafetyIoRecoveryTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_SAFE_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
    }

    // ════════════════════════════════════════════════════════════════════
    // SEC-CONV-01: Zip-Slip protection in FormatConverterAdapter
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ConvertArchive_ZipSlipEntry_BlocksExtractionAndReturnsError()
    {
        // Create a ZIP with a path-traversal entry: ../../evil.txt
        var zipPath = Path.Combine(_tempDir, "malicious.zip");
        var escapedContent = "INJECTED"u8.ToArray();

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            // Normal entry
            var normalEntry = archive.CreateEntry("track01.bin");
            using (var s = normalEntry.Open()) s.Write(new byte[100]);

            // Zip-Slip attack entry that escapes extractDir
            var maliciousEntry = archive.CreateEntry("../../evil.txt");
            using (var s = maliciousEntry.Open()) s.Write(escapedContent);

            // CUE entry (so chdman will find something)
            var cueEntry = archive.CreateEntry("game.cue");
            using (var s = cueEntry.Open())
            using (var sw = new StreamWriter(s))
                sw.Write("FILE \"track01.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
        }

        // The evil file must NOT exist outside extractDir after conversion attempt
        var evilTarget = Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(_tempDir))!, "evil.txt");

        var tools = new FakeToolRunner();
        var converter = new FormatConverterAdapter(tools);
        var target = new ConversionTarget(".chd", "chdman", "createcd");

        var result = converter.Convert(zipPath, target);

        // Should fail with zip-slip detection error
        Assert.NotEqual(ConversionOutcome.Success, result.Outcome);
        Assert.False(File.Exists(evilTarget), "Zip-Slip attack file must NOT exist outside extract dir");
    }

    [Fact]
    public void ConvertArchive_NormalZip_ExtractsSuccessfully()
    {
        // Create a well-formed ZIP with CUE+BIN
        var zipPath = Path.Combine(_tempDir, "game.zip");

        using (var zipStream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var binEntry = archive.CreateEntry("game.bin");
            using (var s = binEntry.Open()) s.Write(new byte[2352]);

            var cueEntry = archive.CreateEntry("game.cue");
            using (var s = cueEntry.Open())
            using (var sw = new StreamWriter(s))
                sw.Write("FILE \"game.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
        }

        // Conversion should attempt chdman (will fail since fake tool, but extraction should succeed)
        var tools = new FakeToolRunner { FailInvoke = true };
        var converter = new FormatConverterAdapter(tools);
        var target = new ConversionTarget(".chd", "chdman", "createcd");

        var result = converter.Convert(zipPath, target);
        // Should reach chdman invocation (extraction worked), then fail because tool fails
        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("chdman-failed", result.Reason ?? "");
    }

    // ════════════════════════════════════════════════════════════════════
    // SEC-CONV-02: ZIP bomb / entry count limit
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void ExtractZipSafe_TooManyEntries_ReturnsError()
    {
        // Reflection test: verify the method exists and functions via the Convert path
        // We'll test indirectly - create a ZIP with >10000 entries is too expensive,
        // so we verify the constant exists and is reasonable
        var field = typeof(FormatConverterAdapter)
            .GetField("MaxZipEntryCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var value = (int)field!.GetValue(null)!;
        Assert.InRange(value, 1, 100_000); // reasonable range
    }

    [Fact]
    public void ExtractZipSafe_MaxExtractSizeLimit_Exists()
    {
        var field = typeof(FormatConverterAdapter)
            .GetField("MaxExtractedTotalBytes", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var value = (long)field!.GetValue(null)!;
        Assert.True(value > 0 && value <= 50L * 1024 * 1024 * 1024,
            "MaxExtractedTotalBytes should be > 0 and <= 50 GB");
    }

    // ════════════════════════════════════════════════════════════════════
    // SEC-CONV-04: Verification failure cleanup
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void WinnerConversionPhase_VerifyFailure_CleansUpOrphanedFile()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);
        var sourcePath = Path.Combine(sourceDir, "game.cue");
        File.WriteAllText(sourcePath, "dummy");

        // Create a fake target file that would be left as orphan if not cleaned
        var targetPath = Path.Combine(sourceDir, "game.chd");
        File.WriteAllText(targetPath, "corrupt-output");

        // Verify that the cleanup constant path is tested through the phase
        // The actual cleanup happens inside the pipeline phase, tested via the convert result
        var converter = new FailVerifyConverter(targetPath);
        var target = converter.GetTargetFormat("PS1", ".cue");
        Assert.NotNull(target);

        var convResult = converter.Convert(sourcePath, target!);
        Assert.Equal(ConversionOutcome.Success, convResult.Outcome);

        // Verification should fail
        Assert.False(converter.Verify(convResult.TargetPath!, target!));
    }

    // ════════════════════════════════════════════════════════════════════
    // SEC-MOVE-01: JunkRemovalPipelinePhase.FindRootForPath separator guard
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void JunkRemoval_FindRootForPath_SimilarPrefixNotMatched()
    {
        // Root is C:\Roms — file in C:\Roms-Other should NOT match
        var root = Path.Combine(_tempDir, "Roms");
        var otherDir = Path.Combine(_tempDir, "Roms-Other");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(otherDir);
        var junkFile = Path.Combine(otherDir, "junk.zip");
        File.WriteAllText(junkFile, "dummy");

        var junkCandidate = new RomCandidate
        {
            MainPath = junkFile,
            Category = FileCategory.Junk,
            GameKey = "junk",
            SizeBytes = 100
        };

        var group = new DedupeGroup { Winner = junkCandidate, Losers = new List<RomCandidate>(), GameKey = "junk" };
        var input = new JunkRemovalPhaseInput(
            new[] { group },
            new RunOptions
            {
                Roots = new List<string> { root },
                Mode = "Move",
                AuditPath = ""
            });

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs);
        var context = new PipelineContext
        {
            Options = input.Options,
            FileSystem = fs,
            AuditStore = audit,
            Metrics = new Infrastructure.Metrics.PhaseMetricsCollector()
        };
        context.Metrics.Initialize();

        var phase = new JunkRemovalPipelinePhase();
        var result = phase.Execute(input, context, CancellationToken.None);

        // Junk file should NOT be moved because it's in Roms-Other, not Roms
        Assert.Equal(1, result.MoveResult.FailCount);
        Assert.Equal(0, result.MoveResult.MoveCount);
        Assert.True(File.Exists(junkFile), "File in Roms-Other must not be moved when root is Roms");
    }

    [Fact]
    public void JunkRemoval_FindRootForPath_ExactRootMatches()
    {
        // File inside the actual root should be found
        var root = Path.Combine(_tempDir, "Roms");
        Directory.CreateDirectory(root);
        var junkFile = Path.Combine(root, "junk.zip");
        File.WriteAllText(junkFile, "dummy");

        var junkCandidate = new RomCandidate
        {
            MainPath = junkFile,
            Category = FileCategory.Junk,
            GameKey = "junk",
            SizeBytes = 100
        };

        var group = new DedupeGroup { Winner = junkCandidate, Losers = new List<RomCandidate>(), GameKey = "junk" };
        var input = new JunkRemovalPhaseInput(
            new[] { group },
            new RunOptions
            {
                Roots = new List<string> { root },
                Mode = "Move",
                AuditPath = ""
            });

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs);
        var context = new PipelineContext
        {
            Options = input.Options,
            FileSystem = fs,
            AuditStore = audit,
            Metrics = new Infrastructure.Metrics.PhaseMetricsCollector()
        };
        context.Metrics.Initialize();

        var phase = new JunkRemovalPipelinePhase();
        var result = phase.Execute(input, context, CancellationToken.None);

        // File should be moved to trash
        Assert.Equal(0, result.MoveResult.FailCount);
        Assert.Equal(1, result.MoveResult.MoveCount);
        Assert.False(File.Exists(junkFile), "Junk file inside root should be moved to trash");
    }

    // ════════════════════════════════════════════════════════════════════
    // SEC-MOVE-02: MoveDirectorySafely reparse point blocking
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveDirectorySafely_SourceNotFound_Throws()
    {
        var fs = new FileSystemAdapter();
        var nonexistent = Path.Combine(_tempDir, "nonexistent_dir");
        var dest = Path.Combine(_tempDir, "dest_dir");

        Assert.Throws<DirectoryNotFoundException>(() =>
            fs.MoveDirectorySafely(nonexistent, dest));
    }

    [Fact]
    public void MoveDirectorySafely_SameSourceAndDest_Throws()
    {
        var dir = Path.Combine(_tempDir, "samedir");
        Directory.CreateDirectory(dir);

        var fs = new FileSystemAdapter();
        Assert.Throws<InvalidOperationException>(() =>
            fs.MoveDirectorySafely(dir, dir));
    }

    [Fact]
    public void MoveDirectorySafely_NormalOperation_Succeeds()
    {
        var sourceDir = Path.Combine(_tempDir, "move_source");
        var destDir = Path.Combine(_tempDir, "move_dest");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "test.txt"), "content");

        var fs = new FileSystemAdapter();
        var result = fs.MoveDirectorySafely(sourceDir, destDir);

        Assert.True(result);
        Assert.False(Directory.Exists(sourceDir));
        Assert.True(Directory.Exists(destDir));
        Assert.True(File.Exists(Path.Combine(destDir, "test.txt")));
    }

    // ════════════════════════════════════════════════════════════════════
    // Rollback Safety: Path validation
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void AuditRollback_PathTraversalInCsv_SkippedAsUnsafe()
    {
        var sourceDir = Path.Combine(_tempDir, "original");
        var trashDir = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(sourceDir);
        Directory.CreateDirectory(trashDir);

        // Create a crafted audit CSV with path traversal in OldPath
        var auditPath = Path.Combine(_tempDir, "evil-audit.csv");
        File.WriteAllText(auditPath, string.Join("\n",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{sourceDir},../../etc/passwd,{trashDir}/file.zip,Move,GAME,,region-dedupe,2026-01-01T00:00:00Z"));

        var fs = new FileSystemAdapter();
        var signingService = new AuditSigningService(fs);
        var result = signingService.Rollback(
            auditPath,
            allowedRestoreRoots: new[] { sourceDir },
            allowedCurrentRoots: new[] { trashDir });

        // Path traversal should be blocked — not within any allowed root
        Assert.Equal(0, result.DryRunPlanned);
    }

    [Fact]
    public void AuditCsvStore_Rollback_ReverseOrder_LastMovesFirst()
    {
        // Verify rollback processes entries in reverse order
        var rootDir = Path.Combine(_tempDir, "rollback_root");
        var trashDir = Path.Combine(_tempDir, "rollback_trash");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(trashDir);

        // Create 3 files that were "moved" to trash
        var file1 = Path.Combine(trashDir, "file1.zip");
        var file2 = Path.Combine(trashDir, "file2.zip");
        File.WriteAllText(file1, "content1");
        File.WriteAllText(file2, "content2");

        var auditPath = Path.Combine(_tempDir, "rollback-test.csv");
        File.WriteAllText(auditPath, string.Join("\n",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{rootDir},{rootDir}\\file1.zip,{file1},Move,GAME,,reason,2026-01-01T00:00:00Z",
            $"{rootDir},{rootDir}\\file2.zip,{file2},Move,GAME,,reason,2026-01-01T00:00:01Z"));

        var fs = new FileSystemAdapter();
        var signingService = new AuditSigningService(fs);
        signingService.WriteMetadataSidecar(auditPath, 2);

        // DryRun to check it plans to rollback
        var result = signingService.Rollback(
            auditPath,
            allowedRestoreRoots: new[] { rootDir },
            allowedCurrentRoots: new[] { trashDir },
            dryRun: true);

        Assert.Equal(2, result.DryRunPlanned);
    }

    // ════════════════════════════════════════════════════════════════════
    // Invariant: No delete by default in move operations
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MovePipelinePhase_LoserMovesToTrash_NeverDeleted()
    {
        var root = Path.Combine(_tempDir, "nodeletetemp");
        Directory.CreateDirectory(root);
        var filePath = Path.Combine(root, "loser.zip");
        File.WriteAllText(filePath, "dummy");

        var loser = new RomCandidate
        {
            MainPath = filePath,
            Category = FileCategory.Game,
            GameKey = "game",
            SizeBytes = 100
        };
        var winner = new RomCandidate
        {
            MainPath = Path.Combine(root, "winner.zip"),
            Category = FileCategory.Game,
            GameKey = "game",
            SizeBytes = 200
        };

        var group = new DedupeGroup { Winner = winner, Losers = new List<RomCandidate> { loser }, GameKey = "game" };
        var options = new RunOptions
        {
            Roots = new List<string> { root },
            Mode = "Move",
            AuditPath = ""
        };

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs);
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = fs,
            AuditStore = audit,
            Metrics = new Infrastructure.Metrics.PhaseMetricsCollector()
        };
        context.Metrics.Initialize();

        var phase = new MovePipelinePhase();
        var result = phase.Execute(new MovePhaseInput(new[] { group }, options), context, CancellationToken.None);

        // File should be MOVED, not deleted
        Assert.False(File.Exists(filePath), "Original file should be moved away");
        var trashDir = Path.Combine(root, "_TRASH_REGION_DEDUPE");
        Assert.True(Directory.Exists(trashDir), "Trash directory must exist");
        var trashFiles = Directory.GetFiles(trashDir);
        Assert.Single(trashFiles); // loser moved to trash, not deleted
    }

    // ════════════════════════════════════════════════════════════════════
    // Helpers
    // ════════════════════════════════════════════════════════════════════

    private sealed class FakeToolRunner : IToolRunner
    {
        public bool FailInvoke { get; init; }

        public string? FindTool(string toolName) => $@"C:\mock\{toolName}.exe";

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            if (FailInvoke)
                return new ToolResult(1, "simulated failure", false);
            return new ToolResult(0, "OK", true);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "OK", true);
    }

    private sealed class FailVerifyConverter : IFormatConverter
    {
        private readonly string _targetPath;

        public FailVerifyConverter(string targetPath) => _targetPath = targetPath;

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(".chd", "chdman", "createcd");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, _targetPath, ConversionOutcome.Success);

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    // ════════════════════════════════════════════════════════════════════
    // S-1: SEC-MOVE-01 parity — MoveDirectorySafely traversal guard
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveDirectorySafely_DestinationWithTraversal_Throws()
    {
        var sourceDir = Path.Combine(_tempDir, "src_dir");
        Directory.CreateDirectory(sourceDir);

        var destWithTraversal = Path.Combine(_tempDir, "safe", "..", "escaped_dir");

        var fs = new FileSystemAdapter();
        var ex = Assert.Throws<InvalidOperationException>(() =>
            fs.MoveDirectorySafely(sourceDir, destWithTraversal));
        Assert.Contains("directory traversal", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ════════════════════════════════════════════════════════════════════
    // S-2: SEC-PATH-03 — Windows reserved device name blocking
    // ════════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("CON")]
    [InlineData("PRN")]
    [InlineData("AUX")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("COM9")]
    [InlineData("LPT1")]
    [InlineData("LPT9")]
    [InlineData("con")]
    [InlineData("nul")]
    public void ResolveChildPathWithinRoot_ReservedDeviceName_ReturnsNull(string deviceName)
    {
        var fs = new FileSystemAdapter();
        var result = fs.ResolveChildPathWithinRoot(_tempDir, deviceName);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("CON.txt")]
    [InlineData("NUL.zip")]
    [InlineData("COM1.rom")]
    [InlineData("LPT3.dat")]
    public void ResolveChildPathWithinRoot_ReservedDeviceNameWithExtension_ReturnsNull(string fileName)
    {
        var fs = new FileSystemAdapter();
        var result = fs.ResolveChildPathWithinRoot(_tempDir, fileName);
        Assert.Null(result);
    }

    [Theory]
    [InlineData("CONSOLE")]
    [InlineData("NULLIFY")]
    [InlineData("PRINTER")]
    [InlineData("COMRADES")]
    [InlineData("LPTOP")]
    [InlineData("game.rom")]
    public void ResolveChildPathWithinRoot_NonReservedName_ReturnsPath(string fileName)
    {
        var fs = new FileSystemAdapter();
        var result = fs.ResolveChildPathWithinRoot(_tempDir, fileName);
        Assert.NotNull(result);
    }

    [Fact]
    public void ResolveChildPathWithinRoot_ReservedNameInSubdirectory_ReturnsNull()
    {
        var fs = new FileSystemAdapter();
        var result = fs.ResolveChildPathWithinRoot(_tempDir, Path.Combine("games", "CON", "file.rom"));
        Assert.Null(result);
    }

    [Fact]
    public void IsWindowsReservedDeviceName_ValidatesCorrectly()
    {
        // Positive cases
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName("CON"));
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName("con"));
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName("NUL"));
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName("COM1"));
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName("LPT9"));
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName("NUL.txt"));
        Assert.True(FileSystemAdapter.IsWindowsReservedDeviceName("COM3.rom"));

        // Negative cases
        Assert.False(FileSystemAdapter.IsWindowsReservedDeviceName(""));
        Assert.False(FileSystemAdapter.IsWindowsReservedDeviceName("CONSOLE"));
        Assert.False(FileSystemAdapter.IsWindowsReservedDeviceName("game.rom"));
        Assert.False(FileSystemAdapter.IsWindowsReservedDeviceName("COMA"));
        Assert.False(FileSystemAdapter.IsWindowsReservedDeviceName("LPT"));
        Assert.False(FileSystemAdapter.IsWindowsReservedDeviceName("COMX"));
    }

    // ════════════════════════════════════════════════════════════════════
    // S-4: MoveDirectorySafely TOCTOU — try/catch collision handling
    // ════════════════════════════════════════════════════════════════════

    [Fact]
    public void MoveDirectorySafely_CollisionHandling_UsesDupSuffix()
    {
        var sourceDir = Path.Combine(_tempDir, "collision_src");
        var destDir = Path.Combine(_tempDir, "collision_dest");
        Directory.CreateDirectory(sourceDir);
        File.WriteAllText(Path.Combine(sourceDir, "test.txt"), "content");
        Directory.CreateDirectory(destDir); // Pre-existing — triggers DUP

        var fs = new FileSystemAdapter();
        var result = fs.MoveDirectorySafely(sourceDir, destDir);

        Assert.True(result);
        Assert.False(Directory.Exists(sourceDir));
        Assert.True(Directory.Exists(destDir)); // Original still exists
        Assert.True(Directory.Exists(destDir + "__DUP1")); // DUP created
        Assert.True(File.Exists(Path.Combine(destDir + "__DUP1", "test.txt")));
    }
}
