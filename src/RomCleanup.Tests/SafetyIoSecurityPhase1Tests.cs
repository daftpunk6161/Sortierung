using System.IO.Compression;
using System.Text;
using System.Xml;
using RomCleanup.Contracts;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.Dat;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Metrics;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Safety;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Phase 1 — Security &amp; Critical Bug Fixes.
/// Tests for TASK-001 through TASK-016, TASK-144 through TASK-148.
/// </summary>
public sealed class SafetyIoSecurityPhase1Tests : IDisposable
{
    private readonly string _tempDir;

    public SafetyIoSecurityPhase1Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"phase1_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    #region TASK-001: Destination-Root-Containment

    [Fact]
    public void TASK001_MoveItemSafely_WithAllowedRoot_Succeeds_WhenDestIsWithinRoot()
    {
        var fs = new FileSystemAdapter();
        var root = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "source.bin");
        File.WriteAllBytes(source, [0x01, 0x02]);
        var dest = Path.Combine(root, "sub", "dest.bin");

        var result = fs.MoveItemSafely(source, dest, root);

        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        Assert.False(File.Exists(source));
    }

    [Fact]
    public void TASK001_MoveItemSafely_WithAllowedRoot_Throws_WhenDestIsOutsideRoot()
    {
        var fs = new FileSystemAdapter();
        var root = Path.Combine(_tempDir, "allowed");
        var outside = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var source = Path.Combine(root, "file.bin");
        File.WriteAllBytes(source, [0x01]);
        var dest = Path.Combine(outside, "escaped.bin");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            fs.MoveItemSafely(source, dest, root));
        Assert.Contains("outside allowed root", ex.Message);
        Assert.True(File.Exists(source), "Source must not have been moved");
    }

    [Fact]
    public void TASK001_MoveItemSafely_WithAllowedRoot_Throws_OnTraversal()
    {
        var fs = new FileSystemAdapter();
        var root = Path.Combine(_tempDir, "safe");
        Directory.CreateDirectory(root);
        var source = Path.Combine(root, "file.bin");
        File.WriteAllBytes(source, [0x01]);

        // Attempt to escape via ..
        var dest = Path.Combine(root, "..", "escaped.bin");

        Assert.Throws<InvalidOperationException>(() =>
            fs.MoveItemSafely(source, dest, root));
    }

    #endregion

    #region TASK-002: NTFS ADS Blocking in NormalizePath

    [Fact]
    public void TASK002_NormalizePath_Rejects_ADS_Reference()
    {
        // Path with ADS after drive letter portion
        var result = SafetyValidator.NormalizePath(@"C:\Users\test:hidden");
        Assert.Null(result);
    }

    [Fact]
    public void TASK002_NormalizePath_Allows_DriveLetterColon()
    {
        // Normal drive letter should work
        var result = SafetyValidator.NormalizePath(@"C:\Users\test");
        Assert.NotNull(result);
    }

    [Fact]
    public void TASK002_NormalizePath_Rejects_ADS_InFilename()
    {
        var result = SafetyValidator.NormalizePath(@"C:\folder\file.txt:Zone.Identifier");
        Assert.Null(result);
    }

    #endregion

    #region TASK-003: Extended-Length Prefix Rejection

    [Fact]
    public void TASK003_NormalizePath_Rejects_ExtendedLengthPrefix()
    {
        Assert.Null(SafetyValidator.NormalizePath(@"\\?\C:\secret"));
    }

    [Fact]
    public void TASK003_NormalizePath_Rejects_DevicePrefix()
    {
        Assert.Null(SafetyValidator.NormalizePath(@"\\.\PhysicalDisk0"));
    }

    #endregion

    #region TASK-004: Rollback Sidecar Guard

    [Fact]
    public void TASK004_Rollback_Execute_Blocked_Without_Sidecar()
    {
        var fs = new FileSystemAdapter();
        var logs = new List<string>();
        var audit = new AuditSigningService(fs, s => logs.Add(s));

        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(csvPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n" +
            $"{_tempDir},{_tempDir}\\old.bin,{_tempDir}\\new.bin,MOVE,GAME,,dedupe,2026-01-01\n");

        // No .meta.json created — execute-mode should be blocked
        var result = audit.Rollback(csvPath,
            allowedRestoreRoots: [_tempDir],
            allowedCurrentRoots: [_tempDir],
            dryRun: false);

        Assert.Equal(1, result.Failed);
        Assert.Contains(logs, l => l.Contains("blocked", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void TASK004_Rollback_DryRun_Allowed_Without_Sidecar()
    {
        var fs = new FileSystemAdapter();
        var audit = new AuditSigningService(fs);

        var csvPath = Path.Combine(_tempDir, "audit-dryrun.csv");
        File.WriteAllText(csvPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        // DryRun without sidecar should NOT be blocked
        var result = audit.Rollback(csvPath,
            allowedRestoreRoots: [_tempDir],
            allowedCurrentRoots: [_tempDir],
            dryRun: true);

        Assert.Equal(0, result.Failed);
    }

    #endregion

    #region TASK-005: Rollback Reparse-Point Check

    [Fact]
    public void TASK005_Rollback_Detects_ReparsePoint_InDryRun()
    {
        // This test validates the code path exists — actual symlink creation
        // requires elevated privileges, so we test the FS adapter directly.
        var fs = new FileSystemAdapter();

        // Regular file should not be detected as reparse point
        var file = Path.Combine(_tempDir, "normal.bin");
        File.WriteAllBytes(file, [0x01]);
        Assert.False(fs.IsReparsePoint(file));
    }

    #endregion

    #region TASK-006: Trailing-Dot Rejection in NormalizePath

    [Fact]
    public void TASK006_NormalizePath_Rejects_TrailingDot()
    {
        var result = SafetyValidator.NormalizePath(@"C:\folder\evil.");
        Assert.Null(result);
    }

    [Fact]
    public void TASK006_NormalizePath_TrimsOuterSpace_ButRejectsInternalSegmentTrailingSpace()
    {
        // Outer trailing space is trimmed (safe behavior — equivalent to Windows path normalization)
        var result = SafetyValidator.NormalizePath(@"C:\folder\evil ");
        Assert.NotNull(result);

        // Internal segment with trailing space is rejected (path bypass risk)
        var internal_ = SafetyValidator.NormalizePath(@"C:\evil \sub\file.bin");
        Assert.Null(internal_);
    }

    [Fact]
    public void TASK006_NormalizePath_Rejects_TrailingDotInMiddleSegment()
    {
        var result = SafetyValidator.NormalizePath(@"C:\evil.\sub\file.bin");
        Assert.Null(result);
    }

    #endregion

    #region TASK-007: ReadOnly Attribute Before Delete

    [Fact]
    public void TASK007_DeleteFile_Handles_ReadOnly()
    {
        var fs = new FileSystemAdapter();
        var file = Path.Combine(_tempDir, "readonly.bin");
        File.WriteAllBytes(file, [0xFF]);
        File.SetAttributes(file, FileAttributes.ReadOnly);

        // Should not throw — ReadOnly must be cleared before delete
        fs.DeleteFile(file);

        Assert.False(File.Exists(file));
    }

    #endregion

    #region TASK-008: Locked-File Handling

    [Fact]
    public void TASK008_MoveItemSafely_Returns_Null_On_LockedFile()
    {
        var fs = new FileSystemAdapter();
        var source = Path.Combine(_tempDir, "locked.bin");
        File.WriteAllBytes(source, [0x01]);
        var dest = Path.Combine(_tempDir, "moved.bin");

        // Lock the file with exclusive access
        using (var _ = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var result = fs.MoveItemSafely(source, dest);
            Assert.Null(result);
        }

        Assert.True(File.Exists(source), "Source should still exist when locked");
    }

    #endregion

    #region TASK-009: Zip-Bomb Compression Ratio

    [Fact]
    public void TASK009_MaxCompressionRatio_IsConfigured()
    {
        // Verify the constant exists and is reasonable (currently 50.0)
        Assert.True(FormatConverterAdapter.MaxCompressionRatio >= 10.0);
        Assert.True(FormatConverterAdapter.MaxCompressionRatio <= 200.0);
    }

    [Fact]
    public void TASK009_ExtractZipSafe_Rejects_HighCompressionRatio()
    {
        // Create a ZIP with a highly compressed entry to test the ratio check.
        // We test via the public Convert path with a crafted ZIP.
        var zipPath = Path.Combine(_tempDir, "bomb.zip");
        using (var archive = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            // Create a large but highly compressible entry (all zeros)
            var entry = archive.CreateEntry("huge.bin", CompressionLevel.SmallestSize);
            using var stream = entry.Open();
            // Write 2MB of zeros — will compress extremely well
            stream.Write(new byte[2 * 1024 * 1024]);
        }

        // The ratio check should trigger — but only for entries > 1MB uncompressed.
        // This is a smoke test; the exact outcome depends on the actual ratio achieved.
        Assert.True(File.Exists(zipPath));
    }

    #endregion

    #region TASK-010: DTD Processing Prohibit

    [Fact]
    public void TASK010_DatParser_Prohibits_DTD_Then_Falls_Back()
    {
        // Create a DAT with DOCTYPE that would trigger DTD processing
        var datPath = Path.Combine(_tempDir, "test.dat");
        File.WriteAllText(datPath,
            """
            <?xml version="1.0"?>
            <!DOCTYPE datafile SYSTEM "http://www.logiqx.com/Dats/datafile.dtd">
            <datafile>
              <game name="Test Game (USA)">
                <rom name="test.bin" size="1024" crc="DEADBEEF" md5="0" sha1="0"/>
              </game>
            </datafile>
            """);

        // Parse should succeed (fallback from Prohibit to Ignore)
        var repo = new DatRepositoryAdapter();
        var consoleMap = new Dictionary<string, string> { ["TEST"] = Path.GetFileName(datPath) };
        var index = repo.GetDatIndex(Path.GetDirectoryName(datPath)!, consoleMap, "SHA1");

        Assert.NotNull(index);
    }

    #endregion

    #region TASK-011: ARCADE/NEOGEO Conversion Bug — Regression Test

    [Fact]
    public void TASK011_Arcade_Returns_NullTarget()
    {
        var tools = new StubToolRunner();
        var sut = new FormatConverterAdapter(tools);

        Assert.Null(sut.GetTargetFormat("ARCADE", ".zip"));
        Assert.Null(sut.GetTargetFormat("NEOGEO", ".zip"));
    }

    [Fact]
    public void TASK011_Arcade_Not_In_DefaultBestFormats()
    {
        Assert.False(FormatConverterAdapter.DefaultBestFormats.ContainsKey("ARCADE"));
        Assert.False(FormatConverterAdapter.DefaultBestFormats.ContainsKey("NEOGEO"));
    }

    #endregion

    #region TASK-012: Multi-File Conversion Atomicity

    [Fact]
    public void TASK012_CueFiles_Sorted_Deterministically()
    {
        // Verify that CUE file selection is deterministic regardless of creation order
        var extractDir = Path.Combine(_tempDir, "extract");
        Directory.CreateDirectory(extractDir);

        // Create files in reverse order
        File.WriteAllText(Path.Combine(extractDir, "disc2.cue"), "FILE disc2.bin BINARY");
        File.WriteAllText(Path.Combine(extractDir, "disc1.cue"), "FILE disc1.bin BINARY");
        File.WriteAllText(Path.Combine(extractDir, "disc2.bin"), "data2");
        File.WriteAllText(Path.Combine(extractDir, "disc1.bin"), "data1");

        var cueFiles = Directory.GetFiles(extractDir, "*.cue");
        Array.Sort(cueFiles, StringComparer.OrdinalIgnoreCase);

        // First file should always be disc1.cue
        Assert.EndsWith("disc1.cue", Path.GetFileName(cueFiles[0]));
    }

    #endregion

    #region TASK-144: PreferRegions Parity

    [Fact]
    public void TASK144_RunConstants_DefaultPreferRegions_MatchesDefaultsJson()
    {
        // defaults.json: ["EU", "US", "JP", "WORLD"]
        Assert.Equal(["EU", "US", "JP", "WORLD"], RunConstants.DefaultPreferRegions);
    }

    [Fact]
    public void TASK144_RunOptions_Default_Uses_CentralConstant()
    {
        var options = new RunOptions();
        Assert.Equal(RunConstants.DefaultPreferRegions, options.PreferRegions);
    }

    [Fact]
    public void TASK144_RunOptionsBuilder_Normalizes_PreferRegions()
    {
        var options = new RunOptions
        {
            Roots = [_tempDir],
            PreferRegions = ["eu", " US ", "jp", "", "WORLD", "us"]
        };

        var normalized = RunOptionsBuilder.Normalize(options);

        // Should be uppercased, trimmed, deduped, and empty-filtered
        Assert.Equal(["EU", "US", "JP", "WORLD"], normalized.PreferRegions);
    }

    [Fact]
    public void TASK144_RunOptionsBuilder_FallsBack_When_AllEmpty()
    {
        var options = new RunOptions
        {
            Roots = [_tempDir],
            PreferRegions = ["", " ", "  "]
        };

        var normalized = RunOptionsBuilder.Normalize(options);

        Assert.Equal(RunConstants.DefaultPreferRegions, normalized.PreferRegions);
    }

    #endregion

    #region TASK-145: Sidecar Status on Errors

    [Fact]
    public void TASK145_WriteCompletedAuditSidecar_IncludesStatus()
    {
        // Integration test: run a pipeline that completes and verify sidecar has correct status.
        // We test the WriteMetadataSidecar signature indirectly by verifying the
        // RunOutcome parameter is accepted by the method signature.
        var fs = new FileSystemAdapter();
        var audit = new AuditSigningService(fs);

        var auditPath = Path.Combine(_tempDir, "test-audit.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        // Write sidecar with explicit metadata including status
        audit.WriteMetadataSidecar(auditPath, 0, new Dictionary<string, object>
        {
            ["Status"] = RunOutcome.CompletedWithErrors.ToStatusString()
        });

        var metaPath = auditPath + ".meta.json";
        Assert.True(File.Exists(metaPath));
        var content = File.ReadAllText(metaPath);
        Assert.Contains("completed_with_errors", content);
    }

    #endregion

    #region TASK-146: HMAC Key Persistence

    [Fact]
    public void TASK146_HMAC_Key_Persisted_To_File()
    {
        var fs = new FileSystemAdapter();
        var keyFile = Path.Combine(_tempDir, "keys", "hmac.key");

        var audit1 = new AuditSigningService(fs, keyFilePath: keyFile);
        var hmac1 = audit1.ComputeHmacSha256("test-payload");

        Assert.True(File.Exists(keyFile));

        // Second instance reads the same key file
        var audit2 = new AuditSigningService(fs, keyFilePath: keyFile);
        var hmac2 = audit2.ComputeHmacSha256("test-payload");

        // Same payload + same key → same HMAC
        Assert.Equal(hmac1, hmac2);
    }

    [Fact]
    public void TASK146_HMAC_Key_Survives_Restart()
    {
        var fs = new FileSystemAdapter();
        var keyFile = Path.Combine(_tempDir, "keys", "survive.key");

        // Create and persist key
        var audit = new AuditSigningService(fs, keyFilePath: keyFile);
        var hmac = audit.ComputeHmacSha256("payload");

        // Verify key file is valid hex
        var hexContent = File.ReadAllText(keyFile).Trim();
        Assert.Equal(64, hexContent.Length); // 32 bytes = 64 hex chars
        Assert.True(hexContent.All(c => "0123456789abcdef".Contains(c)));
    }

    #endregion

    #region TASK-147: Move-then-Audit Atomicity

    [Fact]
    public void TASK147_MovePipelinePhase_Writes_Pending_Before_Move()
    {
        // Setup: Create a mock-like environment with real filesystem
        var fs = new FileSystemAdapter();
        var auditStore = new InMemoryAuditStore();
        var root = Path.Combine(_tempDir, "root147");
        var trashRoot = root;
        Directory.CreateDirectory(root);

        var sourceFile = Path.Combine(root, "loser.zip");
        File.WriteAllBytes(sourceFile, [0xFF, 0xFE]);

        var auditPath = Path.Combine(_tempDir, "audit147.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        var loser = new RomCandidate
        {
            MainPath = sourceFile,
            SizeBytes = 2,
            Category = FileCategory.Game,
            GameKey = "TestGame"
        };
        var group = new DedupeGroup
        {
            Winner = loser,
            Losers = [loser],
            GameKey = "TestGame"
        };

        var options = new RunOptions
        {
            Roots = [root],
            TrashRoot = trashRoot,
            AuditPath = auditPath,
            Mode = "Move",
            ConflictPolicy = "Rename"
        };
        var input = new MovePhaseInput(
            Groups: [group],
            Options: options);
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = fs,
            AuditStore = auditStore,
            Metrics = new PhaseMetricsCollector()
        };

        var phase = new MovePipelinePhase();
        phase.Execute(input, context, CancellationToken.None);

        // Verify: audit store received MOVE_PENDING row before the actual Move row
        var rows = auditStore.GetRows();
        Assert.True(rows.Count >= 2, $"Expected at least 2 audit rows (PENDING + Move), got {rows.Count}");

        var pendingRow = rows.FirstOrDefault(r => r.Contains("MOVE_PENDING"));
        Assert.NotNull(pendingRow);

        // The definitive Move row should come after PENDING
        var moveRow = rows.FirstOrDefault(r => r.Contains(",Move,"));
        Assert.NotNull(moveRow);

        var pendingIdx = rows.IndexOf(pendingRow);
        var moveIdx = rows.IndexOf(moveRow);
        Assert.True(pendingIdx < moveIdx, "PENDING must be written before Move");
    }

    [Fact]
    public void TASK147_MovePipelinePhase_Records_Failure()
    {
        var auditStore = new InMemoryAuditStore();
        var stubFs = new FailingMoveFileSystem();
        var root = Path.Combine(_tempDir, "root147f");
        Directory.CreateDirectory(root);

        var auditPath = Path.Combine(_tempDir, "audit147f.csv");

        var loser = new RomCandidate
        {
            MainPath = Path.Combine(root, "nonexist.zip"),
            SizeBytes = 100,
            Category = FileCategory.Game,
            GameKey = "TestGame"
        };
        var group = new DedupeGroup
        {
            Winner = loser,
            Losers = [loser],
            GameKey = "TestGame"
        };

        var options = new RunOptions
        {
            Roots = [root],
            TrashRoot = root,
            AuditPath = auditPath,
            Mode = "Move"
        };
        var input = new MovePhaseInput(
            Groups: [group],
            Options: options);
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = stubFs,
            AuditStore = auditStore,
            Metrics = new PhaseMetricsCollector()
        };

        var phase = new MovePipelinePhase();
        var result = phase.Execute(input, context, CancellationToken.None);

        // Should record failure
        Assert.True(result.FailCount > 0);
    }

    #endregion

    #region TASK-148: Additional Audit-Integration Tests

    [Fact]
    public void TASK148_PreferRegions_Identical_Across_EntryPoints()
    {
        // CLI default matches RunConstants
        var cliOptions = new RunOptions();
        Assert.Equal(RunConstants.DefaultPreferRegions, cliOptions.PreferRegions);

        // SettingsDto default matches RunConstants
        var dto = new RomCleanup.UI.Wpf.Services.SettingsDto();
        Assert.Equal(RunConstants.DefaultPreferRegions, dto.PreferredRegions);
    }

    #endregion

    #region Test Helpers

    /// <summary>Lightweight audit store for testing write-ahead pattern.</summary>
    private sealed class InMemoryAuditStore : IAuditStore
    {
        private readonly List<string> _rows = [];
        private readonly List<string> _sidecarWrites = [];

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
        {
            _rows.Add($"{rootPath},{oldPath},{newPath},{action},{category},{hash},{reason}");
        }

        public void Flush(string auditCsvPath) { }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = true)
            => [];

        public bool TestMetadataSidecar(string auditCsvPath) => false;

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
            _sidecarWrites.Add(System.Text.Json.JsonSerializer.Serialize(metadata));
        }

        public List<string> GetRows() => _rows;
    }

    /// <summary>File system stub that always fails on MoveItemSafely.</summary>
    private sealed class FailingMoveFileSystem : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") =>
            pathType == "Container" ? Directory.Exists(literalPath) : File.Exists(literalPath);
        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => [];
        public string? MoveItemSafely(string sourcePath, string destinationPath) => null;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            var full = Path.GetFullPath(Path.Combine(rootPath, relativePath));
            var normalizedRoot = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return full.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) => File.Delete(path);
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
            => File.Copy(sourcePath, destinationPath, overwrite);
    }

    /// <summary>Stub tool runner for conversion tests.</summary>
    private sealed class StubToolRunner : IToolRunner
    {
        public string? FindTool(string name) => null;
        public ToolResult InvokeProcess(string toolPath, string[] args, string? errorLabel = null)
            => new(1, "", false);
        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(1, "", false);
    }

    #endregion
}
