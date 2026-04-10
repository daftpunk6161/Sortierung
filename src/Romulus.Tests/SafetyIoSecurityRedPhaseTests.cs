using System.IO.Compression;
using System.Reflection;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED PHASE ONLY:
/// Diese Tests definieren strengere Safety-/Security-Anforderungen und sind absichtlich ROT,
/// bis die Implementierung nachgezogen wird.
/// </summary>
public sealed class SafetyIoSecurityRedPhaseTests : IDisposable
{
    private readonly string _tempDir;

    public SafetyIoSecurityRedPhaseTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_REDSEC_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch { }
        }
    }

    [Fact]
    public void ResolveChildPathWithinRoot_NtfsAlternateDataStream_RejectsPath()
    {
        var root = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(root);

        var fs = new FileSystemAdapter();
        var resolved = fs.ResolveChildPathWithinRoot(root, "safe.txt:evilstream");

        // Erwartung (RED): ADS-Syntax muss als unsafe geblockt werden.
        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveChildPathWithinRoot_TrailingDotPath_RejectsPath()
    {
        var root = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(root);

        var fs = new FileSystemAdapter();
        var resolved = fs.ResolveChildPathWithinRoot(root, @"sub...\game.zip");

        // Erwartung (RED): Windows-Name-Normalisierung mit trailing dots soll präventiv blockiert werden.
        Assert.Null(resolved);
    }

    [Fact]
    public void MoveItemSafely_DestinationEscapeViaDotDot_IsBlocked()
    {
        var root = Path.Combine(_tempDir, "root");
        var outside = Path.Combine(_tempDir, "outside");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);

        var source = Path.Combine(root, "game.zip");
        File.WriteAllText(source, "dummy");

        var escapedDest = Path.Combine(root, "..", "outside", "game.zip");
        var fs = new FileSystemAdapter();

        // Erwartung (RED): Escape-Ziel soll blockiert werden statt real bewegt zu werden.
        Assert.Throws<InvalidOperationException>(() => fs.MoveItemSafely(source, escapedDest));
    }

    [Fact]
    public void MoveItemSafely_LockedSource_ReturnsNullWithoutThrowing()
    {
        var root = Path.Combine(_tempDir, "root");
        Directory.CreateDirectory(root);

        var source = Path.Combine(root, "locked.bin");
        var destination = Path.Combine(root, "moved.bin");
        File.WriteAllText(source, "locked-content");

        var fs = new FileSystemAdapter();

        using var lockHandle = new FileStream(source, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        string? movedPath = null;
        var ex = Record.Exception(() => movedPath = fs.MoveItemSafely(source, destination));

        // Erwartung (RED): Bei Lock kein harter IO-Crash, stattdessen null.
        Assert.Null(ex);
        Assert.Null(movedPath);
    }

    [Fact]
    public void DeleteFile_ReadOnlyFile_DeletesAfterAttributeClear()
    {
        var file = Path.Combine(_tempDir, "readonly.txt");
        File.WriteAllText(file, "content");
        File.SetAttributes(file, FileAttributes.ReadOnly);

        var fs = new FileSystemAdapter();

        // Erwartung (RED): ReadOnly-Datei wird robust gelöscht.
        fs.DeleteFile(file);

        Assert.False(File.Exists(file));
    }

    [Fact]
    public void NormalizePath_ExtendedLengthPrefix_IsRejected()
    {
        var path = @"\\?\C:\Windows\System32";

        // Erwartung (RED): Extended-Length Prefix soll als unsafe/path-bypass abgelehnt werden.
        var normalized = SafetyValidator.NormalizePath(path);

        Assert.Null(normalized);
    }

    [Fact]
    public void ConvertArchive_HighCompressionRatio_IsRejectedAsZipBomb()
    {
        var zipPath = Path.Combine(_tempDir, "bomb.zip");

        using (var zipStream = new FileStream(zipPath, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(zipStream, ZipArchiveMode.Create))
        {
            var hugeEntry = archive.CreateEntry("track01.bin", CompressionLevel.SmallestSize);
            using (var entryStream = hugeEntry.Open())
            {
                // 5 MiB Nullbytes komprimieren extrem gut.
                var chunk = new byte[1024 * 1024];
                for (int i = 0; i < 5; i++)
                    entryStream.Write(chunk);
            }

            var cue = archive.CreateEntry("game.cue", CompressionLevel.NoCompression);
            using (var cueStream = cue.Open())
            using (var writer = new StreamWriter(cueStream))
                writer.Write("FILE \"track01.bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n");
        }

        var converter = new FormatConverterAdapter(new ToolRunnerAlwaysFails());
        var target = new ConversionTarget(".chd", "chdman", "createcd");

        var result = converter.Convert(zipPath, target);

        // Erwartung (RED): eigener Zip-Bomb-Ratio-Guard mit dediziertem Fehlercode.
        Assert.Equal(ConversionOutcome.Error, result.Outcome);
        Assert.Contains("archive-compression-ratio-exceeded", result.Reason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DatRepository_SecureXmlSettings_UsesDtdProcessingProhibit()
    {
        var method = typeof(DatRepositoryAdapter).GetMethod(
            "CreateSecureXmlSettings",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(method);

        var settings = method!.Invoke(null, null) as System.Xml.XmlReaderSettings;
        Assert.NotNull(settings);

        // Erwartung (RED): DTD vollständig verbieten statt ignorieren.
        Assert.Equal(System.Xml.DtdProcessing.Prohibit, settings!.DtdProcessing);
    }

    [Fact]
    public void Rollback_WithoutMetadataSidecar_IsBlockedUntilVerified()
    {
        var restoreRoot = Path.Combine(_tempDir, "restore");
        var trashRoot = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(restoreRoot);
        Directory.CreateDirectory(trashRoot);

        var currentPath = Path.Combine(trashRoot, "game.zip");
        File.WriteAllText(currentPath, "trash-file");
        var originalPath = Path.Combine(restoreRoot, "game.zip");

        var auditPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(
            auditPath,
            string.Join("\n",
                "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
                $"{restoreRoot},{originalPath},{currentPath},MOVE,GAME,,test,2026-03-19T10:00:00Z"));

        var service = new AuditSigningService(new FakeRollbackFileSystem());
        var result = service.Rollback(
            auditPath,
            allowedRestoreRoots: new[] { restoreRoot },
            allowedCurrentRoots: new[] { trashRoot },
            dryRun: true);

        // Erwartung (RED): Ohne .meta.json darf kein Rollback geplant werden.
        Assert.Equal(0, result.DryRunPlanned);
        Assert.True(result.Failed > 0);
    }

    [Fact]
    public void Rollback_WhenCurrentPathIsReparsePoint_SkipsMove()
    {
        var restoreRoot = Path.Combine(_tempDir, "restore");
        var trashRoot = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(restoreRoot);
        Directory.CreateDirectory(trashRoot);

        var currentPath = Path.Combine(trashRoot, "game.zip");
        File.WriteAllText(currentPath, "trash-file");
        var originalPath = Path.Combine(restoreRoot, "game.zip");

        var auditPath = Path.Combine(_tempDir, "audit2.csv");
        File.WriteAllText(
            auditPath,
            string.Join("\n",
                "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
                $"{restoreRoot},{originalPath},{currentPath},MOVE,GAME,,test,2026-03-19T10:00:00Z"));

        var fakeFs = new FakeRollbackFileSystem();
        var service = new AuditSigningService(fakeFs);
        service.WriteMetadataSidecar(auditPath, 1);

        var result = service.Rollback(
            auditPath,
            allowedRestoreRoots: new[] { restoreRoot },
            allowedCurrentRoots: new[] { trashRoot },
            dryRun: false);

        // Erwartung (RED): Reparse-Point sollte vor Move erkannt und übersprungen werden.
        Assert.Equal(0, fakeFs.MoveCalls);
        Assert.True(result.SkippedUnsafe > 0);
    }

    [Fact]
    public void FormatConverter_HasCompressionRatioLimitConstant()
    {
        // Erwartung (RED): Explizite Ratio-Grenze als konstante Schutzmaßnahme.
        var ratioField = typeof(FormatConverterAdapter).GetField(
            "MaxCompressionRatio",
            BindingFlags.NonPublic | BindingFlags.Static);

        Assert.NotNull(ratioField);
        var ratio = (double)ratioField!.GetValue(null)!;
        Assert.InRange(ratio, 1.0, 100.0);
    }

    private sealed class ToolRunnerAlwaysFails : IToolRunner
    {
        public string? FindTool(string toolName) => @"C:\mock\" + toolName + ".exe";

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(1, "simulated-failure", false);

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(1, "simulated-failure", false);
    }

    private sealed class FakeRollbackFileSystem : IFileSystem
    {
        public int MoveCalls { get; private set; }

        public bool TestPath(string literalPath, string pathType = "Any")
            => File.Exists(literalPath) || Directory.Exists(literalPath);

        public string EnsureDirectory(string path)
        {
            Directory.CreateDirectory(path);
            return Path.GetFullPath(path);
        }

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => [];

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            MoveCalls++;
            File.Move(sourcePath, destinationPath);
            return destinationPath;
        }

        public bool MoveDirectorySafely(string sourcePath, string destinationPath)
        {
            Directory.Move(sourcePath, destinationPath);
            return true;
        }

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => Path.Combine(rootPath, relativePath);

        public bool IsReparsePoint(string path)
            => true;

        public void DeleteFile(string path)
            => File.Delete(path);

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
            => File.Copy(sourcePath, destinationPath, overwrite);
    }
}
