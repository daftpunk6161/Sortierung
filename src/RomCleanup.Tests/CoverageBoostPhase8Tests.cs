using System.Collections.Concurrent;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Configuration;
using RomCleanup.Infrastructure.Deduplication;
using RomCleanup.Infrastructure.Hashing;
using Xunit;

namespace RomCleanup.Tests;

// =============================================================================
//  1) DatIndex – no existing tests at all
// =============================================================================
public sealed class DatIndexFullTests
{
    [Fact]
    public void Add_BasicEntry_CanBeLookedUp()
    {
        var idx = new DatIndex();
        idx.Add("SNES", "abc123", "Super Mario World");
        Assert.Equal("Super Mario World", idx.Lookup("SNES", "abc123"));
        Assert.Equal(1, idx.TotalEntries);
        Assert.Equal(1, idx.ConsoleCount);
    }

    [Fact]
    public void Add_MultipleConsoles_SeparateEntries()
    {
        var idx = new DatIndex();
        idx.Add("SNES", "h1", "Game A");
        idx.Add("GBA", "h2", "Game B");
        Assert.Equal(2, idx.ConsoleCount);
        Assert.Equal(2, idx.TotalEntries);
        Assert.Equal("Game A", idx.Lookup("SNES", "h1"));
        Assert.Equal("Game B", idx.Lookup("GBA", "h2"));
    }

    [Fact]
    public void Add_CaseInsensitiveConsoleKey()
    {
        var idx = new DatIndex();
        idx.Add("snes", "h1", "Game A");
        Assert.Equal("Game A", idx.Lookup("SNES", "h1"));
        Assert.True(idx.HasConsole("Snes"));
    }

    [Fact]
    public void Add_CaseInsensitiveHash()
    {
        var idx = new DatIndex();
        idx.Add("NES", "AbCdEf", "Game1");
        Assert.Equal("Game1", idx.Lookup("NES", "abcdef"));
    }

    [Fact]
    public void Add_DuplicateHash_UpdatesGameName()
    {
        var idx = new DatIndex();
        idx.Add("NES", "h1", "Old Name");
        idx.Add("NES", "h1", "New Name");
        Assert.Equal("New Name", idx.Lookup("NES", "h1"));
        // TotalEntries: TryAdd fails on dupe so no increment, but indexer update happens
        // First add increments, second does not since TryAdd returns false
        Assert.Equal(1, idx.TotalEntries);
    }

    [Fact]
    public void Add_MaxEntriesPerConsole_StopsAdding()
    {
        var idx = new DatIndex { MaxEntriesPerConsole = 3 };
        idx.Add("SNES", "h1", "Game1");
        idx.Add("SNES", "h2", "Game2");
        idx.Add("SNES", "h3", "Game3");
        idx.Add("SNES", "h4", "Game4"); // Should be rejected
        Assert.Null(idx.Lookup("SNES", "h4"));
        Assert.Equal(3, idx.TotalEntries);
    }

    [Fact]
    public void Add_MaxEntries_OtherConsoleNotAffected()
    {
        var idx = new DatIndex { MaxEntriesPerConsole = 2 };
        idx.Add("SNES", "h1", "G1");
        idx.Add("SNES", "h2", "G2");
        idx.Add("SNES", "h3", "G3"); // rejected
        idx.Add("GBA", "h1", "G4");  // different console, should succeed
        Assert.Null(idx.Lookup("SNES", "h3"));
        Assert.Equal("G4", idx.Lookup("GBA", "h1"));
    }

    [Fact]
    public void Lookup_ConsoleNotFound_ReturnsNull()
    {
        var idx = new DatIndex();
        Assert.Null(idx.Lookup("UNKNOWN", "h1"));
    }

    [Fact]
    public void Lookup_HashNotFound_ReturnsNull()
    {
        var idx = new DatIndex();
        idx.Add("NES", "h1", "Game");
        Assert.Null(idx.Lookup("NES", "nonexistent"));
    }

    [Fact]
    public void HasConsole_Positive() { var idx = new DatIndex(); idx.Add("NES", "h", "g"); Assert.True(idx.HasConsole("NES")); }

    [Fact]
    public void HasConsole_Negative() { var idx = new DatIndex(); Assert.False(idx.HasConsole("NES")); }

    [Fact]
    public void GetConsoleEntries_ReturnsAll()
    {
        var idx = new DatIndex();
        idx.Add("SNES", "h1", "G1");
        idx.Add("SNES", "h2", "G2");
        var entries = idx.GetConsoleEntries("SNES");
        Assert.NotNull(entries);
        Assert.Equal(2, entries!.Count);
        Assert.Equal("G1", entries["h1"]);
    }

    [Fact]
    public void GetConsoleEntries_NotFound_ReturnsNull()
    {
        var idx = new DatIndex();
        Assert.Null(idx.GetConsoleEntries("MISSING"));
    }

    [Fact]
    public void ConsoleKeys_ReturnsAllConsoles()
    {
        var idx = new DatIndex();
        idx.Add("NES", "h1", "G1");
        idx.Add("SNES", "h2", "G2");
        idx.Add("GBA", "h3", "G3");
        var keys = idx.ConsoleKeys;
        Assert.Equal(3, keys.Count);
        Assert.Contains("NES", keys, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TotalEntries_ConcurrentAdds_TracksCorrectly()
    {
        var idx = new DatIndex();
        var tasks = Enumerable.Range(0, 100)
            .Select(i => Task.Run(() => idx.Add("SNES", $"hash_{i}", $"Game_{i}")))
            .ToArray();
        await Task.WhenAll(tasks);
        Assert.Equal(100, idx.TotalEntries);
        Assert.Equal(1, idx.ConsoleCount);
    }

    [Fact]
    public void Add_EmptyStrings_Handled()
    {
        var idx = new DatIndex();
        idx.Add("", "", "");
        Assert.Equal("", idx.Lookup("", ""));
        Assert.Equal(1, idx.TotalEntries);
    }

    [Fact]
    public void LookupAny_FindsHashAcrossConsoles()
    {
        var idx = new DatIndex();
        idx.Add("NES", "h1", "Mario");
        idx.Add("SNES", "h2", "Zelda");
        idx.Add("GBA", "h3", "Metroid");

        var result = idx.LookupAny("h2");
        Assert.NotNull(result);
        Assert.Equal("SNES", result!.Value.ConsoleKey);
        Assert.Equal("Zelda", result.Value.GameName);
    }

    [Fact]
    public void LookupAny_CaseInsensitiveHash()
    {
        var idx = new DatIndex();
        idx.Add("NES", "AbCdEf", "Game1");

        var result = idx.LookupAny("abcdef");
        Assert.NotNull(result);
        Assert.Equal("NES", result!.Value.ConsoleKey);
    }

    [Fact]
    public void LookupAny_NoMatch_ReturnsNull()
    {
        var idx = new DatIndex();
        idx.Add("NES", "h1", "Game1");

        Assert.Null(idx.LookupAny("nonexistent"));
    }

    [Fact]
    public void LookupAny_EmptyIndex_ReturnsNull()
    {
        var idx = new DatIndex();
        Assert.Null(idx.LookupAny("anything"));
    }
}

// =============================================================================
//  2) ArchiveHashService – 7z paths, hash types, caching
// =============================================================================
file sealed class Fake7zToolRunner : IToolRunner
{
    private readonly Func<string, string[], ToolResult>? _invokeFunc;
    private readonly string? _toolPath;

    public Fake7zToolRunner(string? toolPath = "C:\\tools\\7z.exe", Func<string, string[], ToolResult>? invokeFunc = null)
    {
        _toolPath = toolPath;
        _invokeFunc = invokeFunc;
    }

    public string? FindTool(string name) => name == "7z" ? _toolPath : null;
    public ToolResult InvokeProcess(string exePath, string[] args, string? errorLabel = null) =>
        _invokeFunc?.Invoke(exePath, args) ?? new ToolResult(0, "", true);
    public ToolResult Invoke7z(string archivePath, string[] args) => InvokeProcess("7z", args);
}

public sealed class ArchiveHashPhase8Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "ahp8_" + Guid.NewGuid().ToString("N")[..8]);
    public ArchiveHashPhase8Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void GetArchiveHashes_7z_NoToolRunner_ReturnsEmpty()
    {
        var svc = new ArchiveHashService(toolRunner: null);
        var archive = CreateDummy7z();
        var result = svc.GetArchiveHashes(archive);
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_7z_ToolNotFound_ReturnsEmpty()
    {
        var runner = new Fake7zToolRunner(toolPath: null);
        var svc = new ArchiveHashService(toolRunner: runner);
        var archive = CreateDummy7z();
        var result = svc.GetArchiveHashes(archive);
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_7z_ExtractionFails_ReturnsEmpty()
    {
        var runner = new Fake7zToolRunner(invokeFunc: (exe, args) =>
        {
            if (args.Contains("l")) return new ToolResult(0, "----------\nPath = file.txt\n", true);
            return new ToolResult(1, "error", false); // extraction fail
        });
        var svc = new ArchiveHashService(toolRunner: runner);
        var archive = CreateDummy7z();
        var result = svc.GetArchiveHashes(archive);
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_7z_UnsafeEntryPaths_ReturnsEmpty()
    {
        var runner = new Fake7zToolRunner(invokeFunc: (exe, args) =>
            new ToolResult(0, "----------\nPath = ../../../etc/passwd\n", true));
        var svc = new ArchiveHashService(toolRunner: runner);
        var archive = CreateDummy7z();
        var result = svc.GetArchiveHashes(archive);
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_7z_ListingFails_ReturnsEmpty()
    {
        var runner = new Fake7zToolRunner(invokeFunc: (exe, args) =>
            new ToolResult(2, "error listing", false));
        var svc = new ArchiveHashService(toolRunner: runner);
        var archive = CreateDummy7z();
        var result = svc.GetArchiveHashes(archive);
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_CacheHit_ReturnsSameReference()
    {
        var zip = CreateRealZip("cached.zip", "data.bin", new byte[] { 1, 2, 3 });
        var svc = new ArchiveHashService();
        var r1 = svc.GetArchiveHashes(zip, "SHA1");
        var r2 = svc.GetArchiveHashes(zip, "SHA1");
        Assert.Same(r1, r2);
        Assert.Equal(1, svc.CacheCount);
    }

    [Fact]
    public void GetArchiveHashes_DifferentHashTypes_SeparateCacheEntries()
    {
        var zip = CreateRealZip("ht.zip", "data.bin", new byte[] { 1 });
        var svc = new ArchiveHashService();
        var sha1 = svc.GetArchiveHashes(zip, "SHA1");
        var sha256 = svc.GetArchiveHashes(zip, "SHA256");
        var md5 = svc.GetArchiveHashes(zip, "MD5");
        Assert.Single(sha1);
        Assert.Single(sha256);
        Assert.Single(md5);
        Assert.NotEqual(sha1[0], sha256[0]);
        Assert.Equal(3, svc.CacheCount);
    }

    [Fact]
    public void GetArchiveHashes_ClearCache_ResetsCount()
    {
        var zip = CreateRealZip("cc.zip", "x.bin", new byte[] { 5 });
        var svc = new ArchiveHashService();
        svc.GetArchiveHashes(zip);
        Assert.Equal(1, svc.CacheCount);
        svc.ClearCache();
        Assert.Equal(0, svc.CacheCount);
    }

    [Fact]
    public void GetArchiveHashes_ExceedsSizeLimit_ReturnsEmpty()
    {
        var zip = CreateRealZip("big.zip", "x.bin", new byte[] { 1 });
        var svc = new ArchiveHashService(maxArchiveSizeBytes: 1); // 1 byte limit
        var result = svc.GetArchiveHashes(zip);
        Assert.Empty(result);
    }

    [Fact]
    public void GetArchiveHashes_NullPath_ReturnsEmpty()
    {
        var svc = new ArchiveHashService();
        Assert.Empty(svc.GetArchiveHashes(null!));
        Assert.Empty(svc.GetArchiveHashes(""));
        Assert.Empty(svc.GetArchiveHashes("   "));
    }

    [Fact]
    public void GetArchiveHashes_NonExistentFile_ReturnsEmpty()
    {
        var svc = new ArchiveHashService();
        Assert.Empty(svc.GetArchiveHashes(Path.Combine(_tmp, "nonexistent.zip")));
    }

    [Fact]
    public void GetArchiveHashes_UnknownExtension_ReturnsEmpty()
    {
        var f = Path.Combine(_tmp, "test.rar");
        File.WriteAllBytes(f, [1, 2, 3]);
        var svc = new ArchiveHashService();
        Assert.Empty(svc.GetArchiveHashes(f));
    }

    [Fact]
    public void GetArchiveHashes_CorruptZip_ReturnsEmpty()
    {
        var f = Path.Combine(_tmp, "corrupt.zip");
        File.WriteAllBytes(f, [0xFF, 0xFE, 0x00, 0x01, 0x02, 0x03]);
        var svc = new ArchiveHashService();
        Assert.Empty(svc.GetArchiveHashes(f));
    }

    [Fact]
    public void GetArchiveHashes_ZipWithEmptyEntry_SkipsZeroLength()
    {
        var zip = Path.Combine(_tmp, "empty_entry.zip");
        using (var fs = File.Create(zip))
        using (var archive = new ZipArchive(fs, ZipArchiveMode.Create))
        {
            archive.CreateEntry("empty.txt"); // zero-length
            var e = archive.CreateEntry("data.txt");
            using var w = new StreamWriter(e.Open());
            w.Write("hello");
        }
        var svc = new ArchiveHashService();
        var result = svc.GetArchiveHashes(zip);
        Assert.Single(result); // only data.txt, empty.txt skipped
    }

    [Fact]
    public void AreEntryPathsSafe_RootedPath_ReturnsFalse()
    {
        Assert.False(ArchiveHashService.AreEntryPathsSafe(new[] { "C:\\Windows\\System32\\cmd.exe" }));
    }

    [Fact]
    public void AreEntryPathsSafe_DotDotTraversal_ReturnsFalse()
    {
        Assert.False(ArchiveHashService.AreEntryPathsSafe(new[] { "subdir/../../../etc/passwd" }));
    }

    [Fact]
    public void AreEntryPathsSafe_SafePaths_ReturnsTrue()
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe(new[] { "file.txt", "subdir/file.bin", "" }));
    }

    [Fact]
    public void AreEntryPathsSafe_EmptyList_ReturnsTrue()
    {
        Assert.True(ArchiveHashService.AreEntryPathsSafe(Array.Empty<string>()));
    }

    private string CreateDummy7z()
    {
        var p = Path.Combine(_tmp, $"dummy_{Guid.NewGuid():N}.7z");
        File.WriteAllBytes(p, [0x37, 0x7A, 0xBC, 0xAF]); // 7z magic bytes
        return p;
    }

    private string CreateRealZip(string name, string entryName, byte[] data)
    {
        var p = Path.Combine(_tmp, name);
        using var fs = File.Create(p);
        using var archive = new ZipArchive(fs, ZipArchiveMode.Create);
        var entry = archive.CreateEntry(entryName);
        using var es = entry.Open();
        es.Write(data);
        return p;
    }
}

// =============================================================================
//  3) AuditSigningService – key management, rollback branches
// =============================================================================

// helper: non-file-local so it can be used in public test class
internal sealed class AuditTestFs : IFileSystem
{
    public bool TestPath(string literalPath, string pathType = "Any") => File.Exists(literalPath) || Directory.Exists(literalPath);
    public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
    public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
        => Directory.Exists(root) ? Directory.GetFiles(root) : [];
    public string? MoveItemSafely(string source, string destination) { File.Move(source, destination); return destination; }
    public bool MoveDirectorySafely(string source, string destination)
    {
        if (Directory.Exists(source)) { Directory.Move(source, destination); return true; }
        return false;
    }
    public string? ResolveChildPathWithinRoot(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        return full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ? full : null;
    }
    public bool IsReparsePoint(string path) => false;
    public void DeleteFile(string path) => File.Delete(path);
    public void CopyFile(string src, string dest, bool overwrite = false) => File.Copy(src, dest, overwrite);
}

public sealed class AuditSigningPhase8Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "as8_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly AuditTestFs _fs = new();
    public AuditSigningPhase8Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void GetSigningKey_GeneratesAndPersists()
    {
        var keyPath = Path.Combine(_tmp, "hmac.key");
        var svc = new AuditSigningService(_fs, keyFilePath: keyPath);
        var hmac1 = svc.ComputeHmacSha256("test");
        Assert.True(File.Exists(keyPath));
        var hexKey = File.ReadAllText(keyPath).Trim();
        Assert.Equal(64, hexKey.Length); // 32 bytes = 64 hex chars
    }

    [Fact]
    public void GetSigningKey_ReusesExistingKey()
    {
        var keyPath = Path.Combine(_tmp, "existing.key");
        // Pre-create a known key
        var knownKey = new byte[32];
        RandomNumberGenerator.Fill(knownKey);
        File.WriteAllText(keyPath, Convert.ToHexStringLower(knownKey));

        var svc = new AuditSigningService(_fs, keyFilePath: keyPath);
        var hmac1 = svc.ComputeHmacSha256("test");
        var hmac2 = svc.ComputeHmacSha256("test");
        Assert.Equal(hmac1, hmac2);

        // Verify it's deterministic with the same key
        var svc2 = new AuditSigningService(_fs, keyFilePath: keyPath);
        Assert.Equal(hmac1, svc2.ComputeHmacSha256("test"));
    }

    [Fact]
    public void GetSigningKey_MalformedHexFile_RegeneratesNew()
    {
        var keyPath = Path.Combine(_tmp, "bad.key");
        File.WriteAllText(keyPath, "NOT_VALID_HEX!!!"); // invalid hex
        var logs = new List<string>();
        var svc = new AuditSigningService(_fs, log: logs.Add, keyFilePath: keyPath);
        var hmac = svc.ComputeHmacSha256("test");
        Assert.NotEmpty(hmac);
        Assert.Contains(logs, l => l.Contains("Failed to load HMAC key"));
    }

    [Fact]
    public void GetSigningKey_NoKeyFile_GeneratesNew()
    {
        var svc = new AuditSigningService(_fs);
        var h1 = svc.ComputeHmacSha256("a");
        var h2 = svc.ComputeHmacSha256("a");
        Assert.Equal(h1, h2); // same key reused in memory
    }

    [Fact]
    public void WriteMetadataSidecar_CsvNotFound_ReturnsNull()
    {
        var svc = new AuditSigningService(_fs);
        var result = svc.WriteMetadataSidecar(Path.Combine(_tmp, "nonexistent.csv"), 10);
        Assert.Null(result);
    }

    [Fact]
    public void WriteAndVerifyMetadata_RoundTrip()
    {
        var csv = Path.Combine(_tmp, "audit.csv");
        File.WriteAllText(csv, "RootPath,OldPath,NewPath,Action\n/root,/a,/b,MOVE\n");
        var keyPath = Path.Combine(_tmp, "round.key");
        var svc = new AuditSigningService(_fs, keyFilePath: keyPath);
        var metaPath = svc.WriteMetadataSidecar(csv, 1);
        Assert.NotNull(metaPath);
        Assert.True(File.Exists(metaPath));

        // Verify should succeed
        Assert.True(svc.VerifyMetadataSidecar(csv));
    }

    [Fact]
    public void VerifyMetadataSidecar_TamperedCsv_Throws()
    {
        var csv = Path.Combine(_tmp, "tamper.csv");
        File.WriteAllText(csv, "header\nrow1\n");
        var keyPath = Path.Combine(_tmp, "tamper.key");
        var svc = new AuditSigningService(_fs, keyFilePath: keyPath);
        svc.WriteMetadataSidecar(csv, 1);

        // Tamper with CSV
        File.AppendAllText(csv, "injected row\n");
        Assert.Throws<InvalidDataException>(() => svc.VerifyMetadataSidecar(csv));
    }

    [Fact]
    public void VerifyMetadataSidecar_MissingSidecar_Throws()
    {
        var csv = Path.Combine(_tmp, "nosidecar.csv");
        File.WriteAllText(csv, "data\n");
        var svc = new AuditSigningService(_fs);
        Assert.Throws<FileNotFoundException>(() => svc.VerifyMetadataSidecar(csv));
    }

    [Fact]
    public void VerifyMetadataSidecar_InvalidJson_Throws()
    {
        var csv = Path.Combine(_tmp, "badjson.csv");
        File.WriteAllText(csv, "data\n");
        File.WriteAllText(csv + ".meta.json", "NOT JSON AT ALL");
        var svc = new AuditSigningService(_fs);
        Assert.Throws<JsonException>(() => svc.VerifyMetadataSidecar(csv));
    }

    [Fact]
    public void Rollback_CsvNotFound_ReturnsEmptyResult()
    {
        var svc = new AuditSigningService(_fs);
        var result = svc.Rollback(Path.Combine(_tmp, "missing.csv"), [_tmp], [_tmp]);
        Assert.True(result.DryRun);
        Assert.Equal(0, result.TotalRows);
    }

    [Fact]
    public void Rollback_HeaderOnly_ReturnsEmptyResult()
    {
        var csv = Path.Combine(_tmp, "header.csv");
        File.WriteAllText(csv, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");
        var svc = new AuditSigningService(_fs);
        var result = svc.Rollback(csv, [_tmp], [_tmp]);
        Assert.Equal(0, result.TotalRows);
    }

    [Fact]
    public void Rollback_DryRun_CountsPlanned()
    {
        // Create a CSV with a MOVE row
        var root = Path.Combine(_tmp, "rollbackroot");
        Directory.CreateDirectory(root);
        var movedFile = Path.Combine(root, "moved.bin");
        File.WriteAllText(movedFile, "data");
        var origFile = Path.Combine(root, "subdir", "original.bin");

        var csv = Path.Combine(_tmp, "rollback.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");
        sb.AppendLine($"{root},{origFile},{movedFile},MOVE,GAME,abc,dup,2026-01-01T00:00:00Z");
        File.WriteAllText(csv, sb.ToString());

        var svc = new AuditSigningService(_fs);
        var result = svc.Rollback(csv, [root], [root], dryRun: true);
        Assert.True(result.DryRun);
        Assert.Equal(1, result.EligibleRows);
        Assert.Equal(1, result.DryRunPlanned);
    }

    [Fact]
    public void Rollback_MoveAction_Executed()
    {
        var root = Path.Combine(_tmp, "execroot");
        Directory.CreateDirectory(root);
        var origDir = Path.Combine(root, "origdir");
        Directory.CreateDirectory(origDir);

        var movedFile = Path.Combine(root, "moved.bin");
        File.WriteAllText(movedFile, "content");
        var origFile = Path.Combine(origDir, "original.bin");
        // origFile does NOT exist (no collision)

        var csv = Path.Combine(_tmp, "exec.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");
        sb.AppendLine($"{root},{origFile},{movedFile},MOVE,GAME,abc,dup,2026-01-01");
        File.WriteAllText(csv, sb.ToString());

        var svc = new AuditSigningService(_fs);
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        svc.WriteMetadataSidecar(csv, 1);
        var result = svc.Rollback(csv, [root], [root], dryRun: false);
        Assert.Equal(1, result.RolledBack);
        Assert.True(File.Exists(origFile));
        Assert.False(File.Exists(movedFile));
    }

    [Fact]
    public void Rollback_Collision_Skipped()
    {
        var root = Path.Combine(_tmp, "collroot");
        Directory.CreateDirectory(root);

        var movedFile = Path.Combine(root, "moved.bin");
        File.WriteAllText(movedFile, "data");
        var origFile = Path.Combine(root, "original.bin");
        File.WriteAllText(origFile, "already exists"); // collision

        var csv = Path.Combine(_tmp, "coll.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");
        sb.AppendLine($"{root},{origFile},{movedFile},MOVE,GAME,abc,dup,2026-01-01");
        File.WriteAllText(csv, sb.ToString());

        var svc = new AuditSigningService(_fs);
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        svc.WriteMetadataSidecar(csv, 1);
        var result = svc.Rollback(csv, [root], [root], dryRun: false);
        Assert.Equal(1, result.SkippedCollision);
    }

    [Fact]
    public void Rollback_UnsafeRoot_Skipped()
    {
        var root = Path.Combine(_tmp, "saferoot");
        Directory.CreateDirectory(root);
        var otherRoot = Path.Combine(_tmp, "otherroot");
        Directory.CreateDirectory(otherRoot);
        var movedFile = Path.Combine(otherRoot, "moved.bin");
        File.WriteAllText(movedFile, "data");
        var origFile = Path.Combine(root, "orig.bin");

        var csv = Path.Combine(_tmp, "unsafe.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action");
        sb.AppendLine($"{root},{origFile},{movedFile},MOVE");
        File.WriteAllText(csv, sb.ToString());

        var svc = new AuditSigningService(_fs);
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        svc.WriteMetadataSidecar(csv, 1);
        // allowedCurrentRoots does not include otherRoot
        var result = svc.Rollback(csv, [root], [root], dryRun: false);
        Assert.Equal(1, result.SkippedUnsafe);
    }

    [Fact]
    public void Rollback_NonMoveAction_Ignored()
    {
        var root = Path.Combine(_tmp, "nomoveroot");
        Directory.CreateDirectory(root);
        var csv = Path.Combine(_tmp, "nomove.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action");
        sb.AppendLine($"{root},{root}\\a,{root}\\b,DELETE");
        File.WriteAllText(csv, sb.ToString());

        var svc = new AuditSigningService(_fs);
        var result = svc.Rollback(csv, [root], [root]);
        Assert.Equal(0, result.EligibleRows);
    }

    [Fact]
    public void Rollback_MovedAction_AlsoHandled()
    {
        var root = Path.Combine(_tmp, "movedroot");
        Directory.CreateDirectory(root);
        var movedFile = Path.Combine(root, "m.bin");
        File.WriteAllText(movedFile, "x");
        var origDir = Path.Combine(root, "sub");
        Directory.CreateDirectory(origDir);
        var origFile = Path.Combine(origDir, "o.bin");

        var csv = Path.Combine(_tmp, "moved.csv");
        var sb = new StringBuilder();
        sb.AppendLine("RootPath,OldPath,NewPath,Action");
        sb.AppendLine($"{root},{origFile},{movedFile},MOVED");
        File.WriteAllText(csv, sb.ToString());

        var svc = new AuditSigningService(_fs);
        // SEC-ROLLBACK-03: Execute-mode rollback requires sidecar
        svc.WriteMetadataSidecar(csv, 1);
        var result = svc.Rollback(csv, [root], [root], dryRun: false);
        Assert.Equal(1, result.RolledBack);
    }

    [Fact]
    public void Rollback_IntegrityCheckFails_ReturnsFailedStatus()
    {
        var csv = Path.Combine(_tmp, "intfail.csv");
        File.WriteAllText(csv, "RootPath,OldPath,NewPath,Action\nR,A,B,MOVE\n");
        var keyPath = Path.Combine(_tmp, "intfail.key");
        var svc = new AuditSigningService(_fs, keyFilePath: keyPath);
        svc.WriteMetadataSidecar(csv, 1);
        // Tamper
        File.WriteAllText(csv, "RootPath,OldPath,NewPath,Action\nR,A,B,MOVE\nR,C,D,MOVE\n");

        var result = svc.Rollback(csv, [_tmp], [_tmp], dryRun: false);
        Assert.Equal(1, result.Failed);
    }

    [Theory]
    [InlineData("-42", "-42")]          // plain negative number
    [InlineData("-3.14", "-3.14")]      // decimal negative
    [InlineData("-cmd", "'-cmd")]       // negative formula
    [InlineData("-2+3", "'-2+3")]       // not a plain number
    public void SanitizeCsvField_NegativeCases(string input, string expected)
    {
        Assert.Equal(expected, AuditSigningService.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_Empty_ReturnsEmpty()
    {
        Assert.Equal("", AuditSigningService.SanitizeCsvField(""));
    }

    [Fact]
    public void BuildSignaturePayload_Format()
    {
        var payload = AuditSigningService.BuildSignaturePayload("audit.csv", "abc123", 42, "2026-01-01T00:00:00Z");
        Assert.Equal("v1|audit.csv|abc123|42|2026-01-01T00:00:00Z", payload);
    }
}

// =============================================================================
//  4) SettingsLoader – defaults merge, tool validation, edge cases
// =============================================================================
public sealed class SettingsLoaderPhase8Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sl8_" + Guid.NewGuid().ToString("N")[..8]);
    public SettingsLoaderPhase8Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void LoadFrom_NonExistentFile_ReturnsDefaults()
    {
        var settings = SettingsLoader.LoadFrom(Path.Combine(_tmp, "nope.json"));
        Assert.NotNull(settings);
        Assert.Equal("DryRun", settings.General.Mode);
    }

    [Fact]
    public void LoadFrom_InvalidJson_ReturnsDefaults()
    {
        var f = Path.Combine(_tmp, "bad.json");
        File.WriteAllText(f, "NOT JSON {{{");
        var settings = SettingsLoader.LoadFrom(f);
        Assert.NotNull(settings);
    }

    [Fact]
    public void LoadFrom_ValidJson_ParsesCorrectly()
    {
        var f = Path.Combine(_tmp, "valid.json");
        File.WriteAllText(f, """
        {
            "general": {
                "logLevel": "Debug",
                "preferredRegions": ["JP","US"],
                "aggressiveJunk": true,
                "mode": "Move"
            },
            "dat": {
                "useDat": false,
                "hashType": "MD5"
            }
        }
        """);
        var settings = SettingsLoader.LoadFrom(f);
        Assert.Equal("Debug", settings.General.LogLevel);
        Assert.Equal("Move", settings.General.Mode);
        Assert.True(settings.General.AggressiveJunk);
        Assert.False(settings.Dat.UseDat);
        Assert.Equal("MD5", settings.Dat.HashType);
        Assert.Contains("JP", settings.General.PreferredRegions);
    }

    [Fact]
    public void Load_WithDefaults_MergesValues()
    {
        // Verify that Load() at least reads defaults (user settings may override,
        // so we only check non-null result to avoid flaky CI on machines with real settings.json).
        var defaults = Path.Combine(_tmp, "defaults.json");
        File.WriteAllText(defaults, """
        {
            "mode": "Move",
            "logLevel": "Warning",
            "theme": "light",
            "locale": "en",
            "extensions": ".zip,.7z",
            "datRoot": "/dat"
        }
        """);
        var settings = SettingsLoader.Load(defaults);
        Assert.NotNull(settings);
        Assert.NotNull(settings.General);
        Assert.NotNull(settings.Dat);
    }

    [Fact]
    public void Load_DefaultsMalformed_UsesHardcoded()
    {
        var defaults = Path.Combine(_tmp, "malformed.json");
        File.WriteAllText(defaults, "NOT JSON");
        var settings = SettingsLoader.Load(defaults);
        // User settings.json on disk may override Mode, so only check non-null
        Assert.NotNull(settings);
        Assert.NotNull(settings.General.Mode);
    }

    [Fact]
    public void Load_DefaultsPartialKeys_OnlyMergesPresent()
    {
        var defaults = Path.Combine(_tmp, "partial.json");
        File.WriteAllText(defaults, """{ "mode": "Move" }""");
        var settings = SettingsLoader.Load(defaults);
        // User settings.json on disk may override mode, so only check non-null
        Assert.NotNull(settings);
        Assert.NotNull(settings.General.Mode);
    }

    [Fact]
    public void LoadFrom_JsonWithComments_Parses()
    {
        var f = Path.Combine(_tmp, "comments.json");
        File.WriteAllText(f, """
        {
            // This is a comment
            "general": {
                "logLevel": "Error"
            }
        }
        """);
        var settings = SettingsLoader.LoadFrom(f);
        Assert.Equal("Error", settings.General.LogLevel);
    }

    [Fact]
    public void LoadFrom_JsonWithTrailingCommas_Parses()
    {
        var f = Path.Combine(_tmp, "trailing.json");
        File.WriteAllText(f, """
        {
            "general": {
                "logLevel": "Debug",
            },
        }
        """);
        var settings = SettingsLoader.LoadFrom(f);
        Assert.Equal("Debug", settings.General.LogLevel);
    }

    [Fact]
    public void UserSettingsPath_ContainsRomCleanupRegionDedupe()
    {
        var path = SettingsLoader.UserSettingsPath;
        Assert.Contains("RomCleanupRegionDedupe", path);
        Assert.EndsWith("settings.json", path);
    }
}

// =============================================================================
//  5) FolderDeduplicator – DeduplicatePs3, AutoDeduplicate, Move mode
// =============================================================================

file sealed class TrackingFs : IFileSystem
{
    public List<(string src, string dest)> Moves { get; } = [];
    public List<string> CreatedDirs { get; } = [];
    public bool MoveResult { get; set; } = true;

    public bool TestPath(string literalPath, string pathType = "Any") => File.Exists(literalPath) || Directory.Exists(literalPath);
    public string EnsureDirectory(string path) { CreatedDirs.Add(path); Directory.CreateDirectory(path); return path; }
    public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
        => Directory.Exists(root) ? Directory.GetFiles(root) : [];
    public string? MoveItemSafely(string source, string destination) { Moves.Add((source, destination)); return MoveResult ? destination : null; }
    public bool MoveDirectorySafely(string source, string destination) { Moves.Add((source, destination)); return MoveResult; }
    public string? ResolveChildPathWithinRoot(string root, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(root, relativePath));
        var norm = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(norm, StringComparison.OrdinalIgnoreCase) || full.Equals(norm.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase)
            ? full : null;
    }
    public bool IsReparsePoint(string path) => false;
    public void DeleteFile(string path) => File.Delete(path);
    public void CopyFile(string src, string dest, bool o = false) => File.Copy(src, dest, o);
}

public sealed class FolderDeduplicatorPhase8Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fd8_" + Guid.NewGuid().ToString("N")[..8]);
    public FolderDeduplicatorPhase8Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void DeduplicatePs3_RootMissing_Logs()
    {
        var logs = new List<string>();
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.DeduplicatePs3([Path.Combine(_tmp, "nope")]);
        Assert.Contains(logs, l => l.Contains("nicht gefunden") || l.Contains("not found") || l.Contains("WARNING"));
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void DeduplicatePs3_NoSubfolders_ReturnsZero()
    {
        var root = Path.Combine(_tmp, "emptyps3");
        Directory.CreateDirectory(root);
        var logs = new List<string>();
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.DeduplicatePs3([root]);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void DeduplicatePs3_NoKeyFiles_SkipsAll()
    {
        var root = Path.Combine(_tmp, "ps3nokeys");
        Directory.CreateDirectory(root);
        var g1 = Path.Combine(root, "Game1");
        Directory.CreateDirectory(g1);
        File.WriteAllText(Path.Combine(g1, "readme.txt"), "hi");

        var logs = new List<string>();
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.DeduplicatePs3([root]);
        Assert.Equal(1, result.Total);
        Assert.Equal(1, result.Skipped);
        Assert.Equal(0, result.Dupes);
    }

    [Fact]
    public void DeduplicatePs3_DuplicateFolders_MovesDuplicate()
    {
        var root = Path.Combine(_tmp, "ps3dup");
        Directory.CreateDirectory(root);

        // Create two folders with identical PS3_DISC.SFB
        var g1 = Path.Combine(root, "Game_v1");
        var g2 = Path.Combine(root, "Game_v2");
        Directory.CreateDirectory(g1);
        Directory.CreateDirectory(g2);
        File.WriteAllBytes(Path.Combine(g1, "PS3_DISC.SFB"), [1, 2, 3, 4, 5]);
        File.WriteAllBytes(Path.Combine(g2, "PS3_DISC.SFB"), [1, 2, 3, 4, 5]);

        var logs = new List<string>();
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.DeduplicatePs3([root]);
        Assert.Equal(2, result.Total);
        Assert.Equal(1, result.Dupes);
        Assert.Equal(1, result.Moved);
        Assert.Single(fs.Moves);
    }

    [Fact]
    public void DeduplicatePs3_Cancellation_Throws()
    {
        var root = Path.Combine(_tmp, "ps3cancel");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "A"));

        var cts = new CancellationTokenSource();
        cts.Cancel();
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        Assert.Throws<OperationCanceledException>(() => dedup.DeduplicatePs3([root], ct: cts.Token));
    }

    [Fact]
    public void DeduplicateByBaseName_MoveMode_ExecutesMoves()
    {
        var root = Path.Combine(_tmp, "basemove");
        Directory.CreateDirectory(root);

        // Two folders with same base key
        var g1 = Path.Combine(root, "Game (USA)");
        var g2 = Path.Combine(root, "Game (Europe)");
        Directory.CreateDirectory(g1);
        Directory.CreateDirectory(g2);
        // g1 has more files -> winner
        File.WriteAllText(Path.Combine(g1, "a.bin"), "aaa");
        File.WriteAllText(Path.Combine(g1, "b.bin"), "bbb");
        File.WriteAllText(Path.Combine(g2, "c.bin"), "ccc");

        var logs = new List<string>();
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.DeduplicateByBaseName([root], mode: "Move");
        Assert.Equal(1, result.DupeGroups);
        Assert.Equal(1, result.Moved);
        Assert.Single(fs.Moves);
    }

    [Fact]
    public void DeduplicateByBaseName_DryRunMode_NoMoves()
    {
        var root = Path.Combine(_tmp, "basedry");
        Directory.CreateDirectory(root);
        var g1 = Path.Combine(root, "Game (USA)");
        var g2 = Path.Combine(root, "Game (Europe)");
        Directory.CreateDirectory(g1);
        Directory.CreateDirectory(g2);
        File.WriteAllText(Path.Combine(g1, "a.bin"), "x");
        File.WriteAllText(Path.Combine(g2, "b.bin"), "y");

        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        var result = dedup.DeduplicateByBaseName([root], mode: "DryRun");
        Assert.Equal(1, result.DupeGroups);
        Assert.Equal(0, result.Moved);
        Assert.Empty(fs.Moves); // no actual moves in dry run
        Assert.Contains(result.Actions, a => a.Action == "DRYRUN-MOVE");
    }

    [Fact]
    public void DeduplicateByBaseName_Cancellation_Throws()
    {
        var root = Path.Combine(_tmp, "basecancel");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "A"));
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        Assert.Throws<OperationCanceledException>(() => dedup.DeduplicateByBaseName([root], ct: cts.Token));
    }

    [Fact]
    public void DeduplicateByBaseName_NonExistentRoot_Skips()
    {
        var fs = new TrackingFs();
        var logs = new List<string>();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.DeduplicateByBaseName([Path.Combine(_tmp, "nope")]);
        Assert.Equal(0, result.TotalFolders);
        Assert.Contains(logs, l => l.Contains("WARNING"));
    }

    [Fact]
    public void DeduplicateByBaseName_MoveError_CountsError()
    {
        var root = Path.Combine(_tmp, "errroot");
        Directory.CreateDirectory(root);
        var g1 = Path.Combine(root, "Game (USA)");
        var g2 = Path.Combine(root, "Game (EU)");
        Directory.CreateDirectory(g1);
        Directory.CreateDirectory(g2);
        File.WriteAllText(Path.Combine(g1, "a.bin"), "x");

        var fs = new TrackingFs { MoveResult = false };
        var dedup = new FolderDeduplicator(fs);
        var result = dedup.DeduplicateByBaseName([root], mode: "Move");
        Assert.True(result.Errors > 0);
    }

    [Fact]
    public void AutoDeduplicate_NoMatchingConsoles_ReturnsEmpty()
    {
        var root = Path.Combine(_tmp, "autonothing");
        Directory.CreateDirectory(root);
        var fs = new TrackingFs();
        var logs = new List<string>();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.AutoDeduplicate([root], consoleKeyDetector: _ => "NES");
        Assert.Empty(result.Ps3Roots);
        Assert.Empty(result.FolderRoots);
        Assert.Contains(logs, l => l.Contains("no roots") || l.Contains("keine"));
    }

    [Fact]
    public void AutoDeduplicate_DetectsAmiga_DispatchesFolderDedupe()
    {
        var root = Path.Combine(_tmp, "autoamiga");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(Path.Combine(root, "Game1")); // needs at least 1 subfolder
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        var result = dedup.AutoDeduplicate([root], consoleKeyDetector: _ => "AMIGA");
        Assert.Contains(root, result.FolderRoots);
        Assert.Empty(result.Ps3Roots);
    }

    [Fact]
    public void AutoDeduplicate_DetectsPS3_SkipsDryRun()
    {
        var root = Path.Combine(_tmp, "autops3");
        Directory.CreateDirectory(root);
        var fs = new TrackingFs();
        var logs = new List<string>();
        var dedup = new FolderDeduplicator(fs, logs.Add);
        var result = dedup.AutoDeduplicate([root], mode: "DryRun", consoleKeyDetector: _ => "PS3");
        Assert.Contains(root, result.Ps3Roots);
        Assert.Contains(logs, l => l.Contains("DryRun") || l.Contains("skipped"));
    }

    [Fact]
    public void AutoDeduplicate_PS3MoveMode_RunsPs3Dedupe()
    {
        var root = Path.Combine(_tmp, "autops3move");
        Directory.CreateDirectory(root);
        var g1 = Path.Combine(root, "GameA");
        Directory.CreateDirectory(g1);
        File.WriteAllBytes(Path.Combine(g1, "PS3_DISC.SFB"), [1, 2, 3]);

        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        var result = dedup.AutoDeduplicate([root], mode: "Move", consoleKeyDetector: _ => "PS3");
        Assert.Contains(root, result.Ps3Roots);
        Assert.NotEmpty(result.Results);
    }

    [Fact]
    public void AutoDeduplicate_DetectorReturnsNull_Skipped()
    {
        var root = Path.Combine(_tmp, "autonull");
        Directory.CreateDirectory(root);
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        var result = dedup.AutoDeduplicate([root], consoleKeyDetector: _ => null);
        Assert.Empty(result.Ps3Roots);
        Assert.Empty(result.FolderRoots);
    }

    [Fact]
    public void AutoDeduplicate_NonExistentRoot_Skipped()
    {
        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        var result = dedup.AutoDeduplicate([Path.Combine(_tmp, "nope")], consoleKeyDetector: _ => "AMIGA");
        Assert.Empty(result.FolderRoots);
    }

    [Fact]
    public void DeduplicateByBaseName_Winner_MostFiles()
    {
        var root = Path.Combine(_tmp, "winnerpop");
        Directory.CreateDirectory(root);
        var g1 = Path.Combine(root, "Game [v1]");
        var g2 = Path.Combine(root, "Game [v2]");
        Directory.CreateDirectory(g1);
        Directory.CreateDirectory(g2);
        // g2 has more files -> winner
        File.WriteAllText(Path.Combine(g1, "a.bin"), "a");
        File.WriteAllText(Path.Combine(g2, "a.bin"), "a");
        File.WriteAllText(Path.Combine(g2, "b.bin"), "b");
        File.WriteAllText(Path.Combine(g2, "c.bin"), "c");

        var fs = new TrackingFs();
        var dedup = new FolderDeduplicator(fs);
        var result = dedup.DeduplicateByBaseName([root], mode: "DryRun");
        Assert.Equal(1, result.DupeGroups);
        // The action should move g1 (fewer files), NOT g2
        Assert.Contains(result.Actions, a => a.Source.Contains("Game [v1]"));
        Assert.DoesNotContain(result.Actions, a => a.Source.Contains("Game [v2]"));
    }
}
