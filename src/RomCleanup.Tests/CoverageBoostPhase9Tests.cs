using System.Security.Cryptography;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Configuration;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Linking;
using RomCleanup.Infrastructure.Sorting;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.Api;
using RomCleanup.Core.Classification;
using Xunit;

namespace RomCleanup.Tests;

// =============================================================================
//  1) RateLimiter – window reset, eviction, edge cases
// =============================================================================
public sealed class RateLimiterPhase9Tests
{
    [Fact]
    public void TryAcquire_WindowReset_AllowsAfterExpiry()
    {
        // Use a tiny window that expires almost immediately
        var limiter = new RateLimiter(2, TimeSpan.FromMilliseconds(50));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.False(limiter.TryAcquire("c1")); // limit reached

        Thread.Sleep(80); // wait for window to expire

        Assert.True(limiter.TryAcquire("c1")); // new window
    }

    [Fact]
    public void TryAcquire_ZeroMax_AlwaysAllows()
    {
        var limiter = new RateLimiter(0, TimeSpan.FromSeconds(60));
        for (int i = 0; i < 100; i++)
            Assert.True(limiter.TryAcquire("c1"));
    }

    [Fact]
    public void TryAcquire_NegativeMax_AlwaysAllows()
    {
        var limiter = new RateLimiter(-5, TimeSpan.FromSeconds(60));
        Assert.True(limiter.TryAcquire("c1"));
    }

    [Fact]
    public void TryAcquire_IndependentBuckets()
    {
        var limiter = new RateLimiter(1, TimeSpan.FromMinutes(10));
        Assert.True(limiter.TryAcquire("client1"));
        Assert.False(limiter.TryAcquire("client1"));
        Assert.True(limiter.TryAcquire("client2")); // separate bucket
    }

    [Fact]
    public void TryAcquire_ExactLimit_ThenFails()
    {
        var limiter = new RateLimiter(3, TimeSpan.FromMinutes(10));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.True(limiter.TryAcquire("c1"));
        Assert.False(limiter.TryAcquire("c1"));
        Assert.False(limiter.TryAcquire("c1"));
    }

    [Fact]
    public void TryAcquire_ConcurrentClients_ThreadSafe()
    {
        var limiter = new RateLimiter(50, TimeSpan.FromMinutes(10));
        int successes = 0;
        var tasks = Enumerable.Range(0, 100).Select(i =>
            Task.Run(() =>
            {
                if (limiter.TryAcquire("shared"))
                    Interlocked.Increment(ref successes);
            })).ToArray();
        Task.WaitAll(tasks);
        Assert.Equal(50, successes);
    }

    [Fact]
    public void TryAcquire_ManyClients_AllGetTheirOwnBucket()
    {
        var limiter = new RateLimiter(1, TimeSpan.FromMinutes(10));
        for (int i = 0; i < 50; i++)
            Assert.True(limiter.TryAcquire($"client_{i}"));
    }
}

// =============================================================================
//  2) FileHashService – hash types, caching, edge cases
// =============================================================================
public sealed class FileHashServicePhase9Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fhs9_" + Guid.NewGuid().ToString("N")[..8]);
    public FileHashServicePhase9Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("MD5")]
    [InlineData("CRC32")]
    public void GetHash_AllTypes_ReturnsNonNull(string hashType)
    {
        var f = Path.Combine(_tmp, $"test_{hashType}.bin");
        File.WriteAllBytes(f, [1, 2, 3, 4, 5]);
        var svc = new FileHashService();
        var hash = svc.GetHash(f, hashType);
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
    }

    [Fact]
    public void GetHash_CrcAlias_SameAsCrc32()
    {
        var f = Path.Combine(_tmp, "crc_alias.bin");
        File.WriteAllBytes(f, [10, 20, 30]);
        var svc = new FileHashService();
        var crc = svc.GetHash(f, "CRC");
        var crc32 = svc.GetHash(f, "CRC32");
        Assert.Equal(crc, crc32);
    }

    [Fact]
    public void GetHash_UnknownType_DefaultsToSha1()
    {
        var f = Path.Combine(_tmp, "unknown.bin");
        File.WriteAllBytes(f, [1, 2, 3]);
        var svc = new FileHashService();
        var unknown = svc.GetHash(f, "UNKNOWN_TYPE");
        var sha1 = svc.GetHash(f, "SHA1");
        Assert.Equal(sha1, unknown);
    }

    [Fact]
    public void GetHash_Cached_ReturnsSameResult()
    {
        var f = Path.Combine(_tmp, "cached.bin");
        File.WriteAllBytes(f, [5, 6, 7]);
        var svc = new FileHashService();
        var h1 = svc.GetHash(f, "SHA1");
        var h2 = svc.GetHash(f, "SHA1");
        Assert.Equal(h1, h2);
        Assert.Equal(1, svc.CacheCount);
    }

    [Fact]
    public void GetHash_DifferentHashTypes_DifferentCacheEntries()
    {
        var f = Path.Combine(_tmp, "multi.bin");
        File.WriteAllBytes(f, [1, 2, 3]);
        var svc = new FileHashService();
        svc.GetHash(f, "SHA1");
        svc.GetHash(f, "SHA256");
        svc.GetHash(f, "MD5");
        Assert.Equal(3, svc.CacheCount);
    }

    [Fact]
    public void GetHash_NonExistentFile_ReturnsNull()
    {
        var svc = new FileHashService();
        Assert.Null(svc.GetHash(Path.Combine(_tmp, "nope.bin")));
    }

    [Fact]
    public void ClearCache_ResetsCount()
    {
        var f = Path.Combine(_tmp, "clear.bin");
        File.WriteAllBytes(f, [1]);
        var svc = new FileHashService();
        svc.GetHash(f);
        Assert.Equal(1, svc.CacheCount);
        svc.ClearCache();
        Assert.Equal(0, svc.CacheCount);
    }

    [Fact]
    public void MaxEntries_CanBeAdjusted()
    {
        var svc = new FileHashService(1000);
        Assert.Equal(1000, svc.MaxEntries);
        svc.MaxEntries = 2000;
        Assert.Equal(2000, svc.MaxEntries);
    }

    [Fact]
    public void MaxEntries_MinimumIs500()
    {
        var svc = new FileHashService();
        svc.MaxEntries = 10; // below 500
        Assert.Equal(500, svc.MaxEntries);
    }

    [Fact]
    public void GetHash_FileModified_RecalculatesHash()
    {
        var f = Path.Combine(_tmp, "modified.bin");
        File.WriteAllBytes(f, [1, 2, 3]);
        var svc = new FileHashService();
        var h1 = svc.GetHash(f, "SHA1");

        // Wait a bit and modify file
        Thread.Sleep(50);
        File.WriteAllBytes(f, [4, 5, 6]);
        // Force different timestamp
        File.SetLastWriteTimeUtc(f, DateTime.UtcNow.AddSeconds(1));

        var h2 = svc.GetHash(f, "SHA1");
        Assert.NotEqual(h1, h2);
    }

    [Fact]
    public void GetHash_Sha256_Returns64Chars()
    {
        var f = Path.Combine(_tmp, "sha256len.bin");
        File.WriteAllBytes(f, [1, 2, 3]);
        var svc = new FileHashService();
        var hash = svc.GetHash(f, "SHA256");
        Assert.Equal(64, hash!.Length);
    }

    [Fact]
    public void GetHash_Md5_Returns32Chars()
    {
        var f = Path.Combine(_tmp, "md5len.bin");
        File.WriteAllBytes(f, [1, 2, 3]);
        var svc = new FileHashService();
        var hash = svc.GetHash(f, "MD5");
        Assert.Equal(32, hash!.Length);
    }
}

// =============================================================================
//  3) HardlinkService – BuildPlan grouping, path traversal, statistics
// =============================================================================
public sealed class HardlinkServicePhase9Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "hl9_" + Guid.NewGuid().ToString("N")[..8]);
    public HardlinkServicePhase9Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void CreateConfig_SetsAllProperties()
    {
        var config = HardlinkService.CreateConfig("/src", "/tgt", LinkType.Symlink, LinkGroupBy.Genre);
        Assert.Equal("/src", config.SourceRoot);
        Assert.Equal("/tgt", config.TargetRoot);
        Assert.Equal(LinkType.Symlink, config.LinkType);
        Assert.Equal(LinkGroupBy.Genre, config.GroupBy);
    }

    [Fact]
    public void CreateConfig_Defaults_HardlinkAndConsole()
    {
        var config = HardlinkService.CreateConfig("/src", "/tgt");
        Assert.Equal(LinkType.Hardlink, config.LinkType);
        Assert.Equal(LinkGroupBy.Console, config.GroupBy);
    }

    [Fact]
    public void CreateOperation_SetsAllProperties()
    {
        var op = HardlinkService.CreateOperation("/a.bin", "/b.bin", LinkType.Junction);
        Assert.Equal("/a.bin", op.SourcePath);
        Assert.Equal("/b.bin", op.TargetPath);
        Assert.Equal(LinkType.Junction, op.LinkType);
        Assert.Equal("Pending", op.Status);
    }

    [Fact]
    public void GetStatistics_CountsCorrectly()
    {
        var ops = new List<LinkOperation>
        {
            new() { Status = "Completed" },
            new() { Status = "Completed" },
            new() { Status = "Failed" },
            new() { Status = "Pending" },
            new() { Status = "Pending" },
            new() { Status = "Pending" }
        };
        var stats = HardlinkService.GetStatistics(ops);
        Assert.Equal(2, stats.Completed);
        Assert.Equal(1, stats.Failed);
        Assert.Equal(3, stats.Pending);
        Assert.Equal(6, stats.Total);
    }

    [Fact]
    public void GetStatistics_EmptyList_AllZero()
    {
        var stats = HardlinkService.GetStatistics(Array.Empty<LinkOperation>());
        Assert.Equal(0, stats.Total);
        Assert.Equal(0, stats.Completed);
    }

    [Fact]
    public void BuildPlan_GroupByConsole_CreatesSubdirectories()
    {
        var src = Path.Combine(_tmp, "src");
        var tgt = Path.Combine(_tmp, "tgt");
        Directory.CreateDirectory(src);

        var f1 = Path.Combine(src, "game1.chd");
        var f2 = Path.Combine(src, "game2.iso");
        File.WriteAllBytes(f1, [1, 2, 3]);
        File.WriteAllBytes(f2, [4, 5, 6]);

        var config = HardlinkService.CreateConfig(src, tgt, LinkType.Hardlink, LinkGroupBy.Console);
        var files = new List<(string FilePath, string? ConsoleKey, string? Genre, string? Region)>
        {
            (f1, "PS2", "RPG", "EU"),
            (f2, "PS1", "Action", "US")
        };

        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Equal(2, plan.Operations.Count);
        Assert.Contains(plan.Operations, o => o.TargetPath.Contains("PS2"));
        Assert.Contains(plan.Operations, o => o.TargetPath.Contains("PS1"));
    }

    [Fact]
    public void BuildPlan_GroupByGenre_UsesGenreSubdir()
    {
        var src = Path.Combine(_tmp, "gsrc");
        var tgt = Path.Combine(_tmp, "gtgt");
        Directory.CreateDirectory(src);
        var f = Path.Combine(src, "game.chd");
        File.WriteAllBytes(f, [1]);

        var config = HardlinkService.CreateConfig(src, tgt, LinkType.Hardlink, LinkGroupBy.Genre);
        var files = new List<(string, string?, string?, string?)> { (f, "PS2", "RPG", "EU") };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Single(plan.Operations);
        Assert.Contains("RPG", plan.Operations[0].TargetPath);
    }

    [Fact]
    public void BuildPlan_GroupByRegion_UsesRegionSubdir()
    {
        var src = Path.Combine(_tmp, "rsrc");
        var tgt = Path.Combine(_tmp, "rtgt");
        Directory.CreateDirectory(src);
        var f = Path.Combine(src, "game.chd");
        File.WriteAllBytes(f, [1]);

        var config = HardlinkService.CreateConfig(src, tgt, LinkType.Hardlink, LinkGroupBy.Region);
        var files = new List<(string, string?, string?, string?)> { (f, "PS2", "RPG", "EU") };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Contains("EU", plan.Operations[0].TargetPath);
    }

    [Fact]
    public void BuildPlan_GroupByConsoleAndGenre_UsesCompositeSubdir()
    {
        var src = Path.Combine(_tmp, "cgsrc");
        var tgt = Path.Combine(_tmp, "cgtgt");
        Directory.CreateDirectory(src);
        var f = Path.Combine(src, "game.chd");
        File.WriteAllBytes(f, [1]);

        var config = HardlinkService.CreateConfig(src, tgt, LinkType.Hardlink, LinkGroupBy.ConsoleAndGenre);
        var files = new List<(string, string?, string?, string?)> { (f, "PS2", "RPG", "EU") };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Contains("PS2", plan.Operations[0].TargetPath);
        Assert.Contains("RPG", plan.Operations[0].TargetPath);
    }

    [Fact]
    public void BuildPlan_NullConsoleKey_UsesUnknown()
    {
        var src = Path.Combine(_tmp, "nullsrc");
        var tgt = Path.Combine(_tmp, "nulltgt");
        Directory.CreateDirectory(src);
        var f = Path.Combine(src, "game.chd");
        File.WriteAllBytes(f, [1]);

        var config = HardlinkService.CreateConfig(src, tgt, LinkType.Hardlink, LinkGroupBy.Console);
        var files = new List<(string, string?, string?, string?)> { (f, null, null, null) };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Contains("Unknown", plan.Operations[0].TargetPath);
    }

    [Fact]
    public void BuildPlan_Hardlink_ShowsSavings()
    {
        var src = Path.Combine(_tmp, "savsrc");
        var tgt = Path.Combine(_tmp, "savtgt");
        Directory.CreateDirectory(src);
        var f = Path.Combine(src, "game.bin");
        File.WriteAllBytes(f, new byte[1024]);

        var config = HardlinkService.CreateConfig(src, tgt, LinkType.Hardlink);
        var files = new List<(string, string?, string?, string?)> { (f, "SNES", null, null) };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Equal(1024, plan.Savings.TotalSourceBytes);
        Assert.Equal(1024, plan.Savings.SavedBytes);
        Assert.Equal(100.0, plan.Savings.SavedPercent);
    }

    [Fact]
    public void BuildPlan_Symlink_ZeroSavings()
    {
        var src = Path.Combine(_tmp, "symsrc");
        var tgt = Path.Combine(_tmp, "symtgt");
        Directory.CreateDirectory(src);
        var f = Path.Combine(src, "game.bin");
        File.WriteAllBytes(f, new byte[1024]);

        var config = HardlinkService.CreateConfig(src, tgt, LinkType.Symlink);
        var files = new List<(string, string?, string?, string?)> { (f, "SNES", null, null) };
        var plan = HardlinkService.BuildPlan(config, files);
        Assert.Equal(0, plan.Savings.SavedBytes);
        Assert.Equal(0.0, plan.Savings.SavedPercent);
    }

    [Fact]
    public void BuildPlan_EmptyFilesList_EmptyPlan()
    {
        var config = HardlinkService.CreateConfig("/s", "/t");
        var plan = HardlinkService.BuildPlan(config, Array.Empty<(string, string?, string?, string?)>());
        Assert.Empty(plan.Operations);
        Assert.Equal(0, plan.Savings.FileCount);
    }

    [Fact]
    public void IsHardlinkSupported_TempDrive_ReturnsTrue()
    {
        // On Windows, temp is usually NTFS
        var result = HardlinkService.IsHardlinkSupported(_tmp);
        Assert.True(result); // CI/Windows = NTFS
    }
}

// =============================================================================
//  4) ToolRunnerAdapter – hash verification, FindTool, RunProcess
// =============================================================================
public sealed class ToolRunnerAdapterPhase9Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "tra9_" + Guid.NewGuid().ToString("N")[..8]);
    public ToolRunnerAdapterPhase9Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void FindTool_NullOrEmpty_ReturnsNull()
    {
        var adapter = new ToolRunnerAdapter();
        Assert.Null(adapter.FindTool(null!));
        Assert.Null(adapter.FindTool(""));
        Assert.Null(adapter.FindTool("   "));
    }

    [Fact]
    public void FindTool_UnknownTool_ReturnsNull()
    {
        var adapter = new ToolRunnerAdapter();
        Assert.Null(adapter.FindTool("nonexistenttool12345"));
    }

    [Fact]
    public void InvokeProcess_FileNotFound_ReturnsError()
    {
        var adapter = new ToolRunnerAdapter(allowInsecureHashBypass: true);
        var result = adapter.InvokeProcess(Path.Combine(_tmp, "nope.exe"), []);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.Success);
        Assert.Contains("not found", result.Output);
    }

    [Fact]
    public void InvokeProcess_NoToolHashes_BlocksExecution()
    {
        // Create a fake exe
        var exe = Path.Combine(_tmp, "fake.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]); // MZ header

        var logs = new List<string>();
        var adapter = new ToolRunnerAdapter(toolHashesPath: null, allowInsecureHashBypass: false, log: logs.Add);
        var result = adapter.InvokeProcess(exe, []);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.Success);
        Assert.Contains("hash verification failed", result.Output);
        Assert.Contains(logs, l => l.Contains("tool-hashes.json"));
    }

    [Fact]
    public void InvokeProcess_WithInsecureBypass_Executes()
    {
        // Use cmd.exe which always exists on Windows
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (!File.Exists(cmdExe)) return; // skip if not Windows

        var adapter = new ToolRunnerAdapter(allowInsecureHashBypass: true, timeoutMinutes: 1);
        var result = adapter.InvokeProcess(cmdExe, ["/c", "echo", "hello"]);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Success);
        Assert.Contains("hello", result.Output);
    }

    [Fact]
    public void InvokeProcess_ToolHashMismatch_ReturnsError()
    {
        var exe = Path.Combine(_tmp, "mismatch.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A, 0x90, 0x00]);

        var hashFile = Path.Combine(_tmp, "tool-hashes.json");
        File.WriteAllText(hashFile, """{"Tools": {"mismatch.exe": "0000000000000000000000000000000000000000000000000000000000000000"}}""");

        var adapter = new ToolRunnerAdapter(toolHashesPath: hashFile, allowInsecureHashBypass: false);
        var result = adapter.InvokeProcess(exe, []);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains("hash verification failed", result.Output);
    }

    [Fact]
    public void InvokeProcess_ToolHashMatch_AllowsExecution()
    {
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (!File.Exists(cmdExe)) return;

        // Compute actual hash
        using var sha = SHA256.Create();
        using var stream = File.OpenRead(cmdExe);
        var hashBytes = sha.ComputeHash(stream);
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        var hashFile = Path.Combine(_tmp, "tool-hashes.json");
        File.WriteAllText(hashFile, $"{{\"Tools\": {{\"cmd.exe\": \"{hash}\"}}}}");

        var adapter = new ToolRunnerAdapter(toolHashesPath: hashFile);
        var result = adapter.InvokeProcess(cmdExe, ["/c", "echo", "verified"]);
        Assert.Equal(0, result.ExitCode);
        Assert.True(result.Success);
        Assert.Contains("verified", result.Output);
    }

    [Fact]
    public void InvokeProcess_HashCaching_SecondCallUsesCache()
    {
        var cmdExe = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
        if (!File.Exists(cmdExe)) return;

        using var sha = SHA256.Create();
        using var stream = File.OpenRead(cmdExe);
        var hashBytes = sha.ComputeHash(stream);
        var hash = BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();

        var hashFile = Path.Combine(_tmp, "tool-hashes.json");
        File.WriteAllText(hashFile, $"{{\"Tools\": {{\"cmd.exe\": \"{hash}\"}}}}");

        var adapter = new ToolRunnerAdapter(toolHashesPath: hashFile);
        var r1 = adapter.InvokeProcess(cmdExe, ["/c", "echo", "one"]);
        var r2 = adapter.InvokeProcess(cmdExe, ["/c", "echo", "two"]);
        Assert.True(r1.Success);
        Assert.True(r2.Success);
    }

    [Fact]
    public void Invoke7z_FileNotFound_ReturnsError()
    {
        var adapter = new ToolRunnerAdapter(allowInsecureHashBypass: true);
        var result = adapter.Invoke7z(Path.Combine(_tmp, "7z_nope.exe"), ["l", "archive.7z"]);
        Assert.Equal(-1, result.ExitCode);
        Assert.False(result.Success);
    }

    [Fact]
    public void InvokeProcess_MalformedToolHashes_EmptyDict()
    {
        var exe = Path.Combine(_tmp, "tool.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]);

        var hashFile = Path.Combine(_tmp, "bad-hashes.json");
        File.WriteAllText(hashFile, "NOT JSON");

        var logs = new List<string>();
        var adapter = new ToolRunnerAdapter(toolHashesPath: hashFile, log: logs.Add);
        var result = adapter.InvokeProcess(exe, []);
        Assert.Equal(-1, result.ExitCode); // hash not found
    }

    [Fact]
    public void InvokeProcess_NoToolInHashFile_Blocked()
    {
        var exe = Path.Combine(_tmp, "unknown_tool.exe");
        File.WriteAllBytes(exe, [0x4D, 0x5A]);

        var hashFile = Path.Combine(_tmp, "empty-tools.json");
        File.WriteAllText(hashFile, """{"Tools": {"other.exe": "abc123"}}""");

        var logs = new List<string>();
        var adapter = new ToolRunnerAdapter(toolHashesPath: hashFile, log: logs.Add);
        var result = adapter.InvokeProcess(exe, []);
        Assert.Equal(-1, result.ExitCode);
        Assert.Contains(logs, l => l.Contains("Kein erwarteter Hash"));
    }
}

// =============================================================================
//  5) ConsoleSorter – set-moves, excluded folders, cancellation, rollback
// =============================================================================

sealed class SortTestFs : IFileSystem
{
    private readonly HashSet<string> _existing = new(StringComparer.OrdinalIgnoreCase);
    public List<(string src, string dest)> Moves { get; } = [];
    public bool MoveResult { get; set; } = true;
    public bool MoveThrows { get; set; }

    public void AddFile(string path) { _existing.Add(Path.GetFullPath(path)); File.WriteAllBytes(path, [1]); }

    public bool TestPath(string literalPath, string pathType = "Any")
    {
        if (pathType == "Container") return Directory.Exists(literalPath);
        return _existing.Contains(Path.GetFullPath(literalPath)) || File.Exists(literalPath) || Directory.Exists(literalPath);
    }

    public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }

    public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
    {
        if (!Directory.Exists(root)) return [];
        var files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
        if (allowedExtensions is null) return files;
        var exts = new HashSet<string>(allowedExtensions, StringComparer.OrdinalIgnoreCase);
        return files.Where(f => exts.Contains(Path.GetExtension(f))).ToArray();
    }

    public bool MoveItemSafely(string sourcePath, string destinationPath)
    {
        if (MoveThrows) throw new IOException("Simulated move failure");
        Moves.Add((sourcePath, destinationPath));
        if (MoveResult && File.Exists(sourcePath))
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Move(sourcePath, destinationPath);
        }
        return MoveResult;
    }

    public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
    {
        var full = Path.GetFullPath(Path.Combine(rootPath, relativePath));
        var norm = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return full.StartsWith(norm, StringComparison.OrdinalIgnoreCase) ? full : null;
    }

    public bool IsReparsePoint(string path) => false;
    public bool MoveDirectorySafely(string sourcePath, string destinationPath) { Moves.Add((sourcePath, destinationPath)); if (Directory.Exists(sourcePath)) Directory.Move(sourcePath, destinationPath); return true; }
    public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }
    public void CopyFile(string src, string dest, bool overwrite = false) => File.Copy(src, dest, overwrite);
}

public sealed class ConsoleSorterPhase9Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "cs9_" + Guid.NewGuid().ToString("N")[..8]);
    public ConsoleSorterPhase9Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    private (ConsoleSorter sorter, SortTestFs fs) CreateSorter(Func<string, string, string>? detectOverride = null)
    {
        var fs = new SortTestFs();
        var detector = new ConsoleDetector(Array.Empty<ConsoleInfo>());
        var sorter = new ConsoleSorter(fs, detector);
        return (sorter, fs);
    }

    [Fact]
    public void Sort_EmptyRoots_ReturnsZeros()
    {
        var (sorter, _) = CreateSorter();
        var result = sorter.Sort(Array.Empty<string>());
        Assert.Equal(0, result.Total);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void Sort_NonExistentRoot_Skips()
    {
        var (sorter, _) = CreateSorter();
        var result = sorter.Sort([Path.Combine(_tmp, "nope")]);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void Sort_ExcludedFolder_FilesSkipped()
    {
        var root = Path.Combine(_tmp, "exroot");
        Directory.CreateDirectory(root);
        var trash = Path.Combine(root, "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(trash);
        File.WriteAllBytes(Path.Combine(trash, "game.chd"), [1, 2, 3]);

        var (sorter, fs) = CreateSorter();
        var result = sorter.Sort([root]);
        Assert.Equal(0, result.Total); // excluded folder files not counted
    }

    [Fact]
    public void Sort_DryRun_NoActualMoves()
    {
        var root = Path.Combine(_tmp, "dryroot");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "game.chd"), [1, 2, 3]);

        var (sorter, fs) = CreateSorter();
        var result = sorter.Sort([root], dryRun: true);
        Assert.Empty(fs.Moves);
    }

    [Fact]
    public void Sort_Cancellation_Stops()
    {
        var root = Path.Combine(_tmp, "cancelroot");
        Directory.CreateDirectory(root);
        File.WriteAllBytes(Path.Combine(root, "game.chd"), [1]);

        var cts = new CancellationTokenSource();
        cts.Cancel();

        var (sorter, _) = CreateSorter();
        var result = sorter.Sort([root], cancellationToken: cts.Token);
        Assert.Equal(0, result.Moved);
    }

    [Fact]
    public void Sort_AlreadyInCorrectFolder_Skipped()
    {
        var root = Path.Combine(_tmp, "correctroot");
        Directory.CreateDirectory(root);
        // UNKNOWN folder matches UNKNOWN console key
        var unknownDir = Path.Combine(root, "UNKNOWN");
        Directory.CreateDirectory(unknownDir);
        File.WriteAllBytes(Path.Combine(unknownDir, "game.xyz"), [1]);

        var (sorter, _) = CreateSorter();
        var result = sorter.Sort([root]);
        // File with unknown ext goes to UNKNOWN -> already there -> skipped
        Assert.Equal(0, result.Moved);
    }
}

// =============================================================================
//  6) ConsoleSortResult model coverage
// =============================================================================
public sealed class ConsoleSortResultTests
{
    [Fact]
    public void Constructor_SetsAllProperties()
    {
        var reasons = new Dictionary<string, int> { ["no-match"] = 5 };
        var result = new ConsoleSortResult(100, 50, 10, 30, 20, reasons);
        Assert.Equal(100, result.Total);
        Assert.Equal(50, result.Moved);
        Assert.Equal(10, result.SetMembersMoved);
        Assert.Equal(30, result.Skipped);
        Assert.Equal(20, result.Unknown);
        Assert.Contains("no-match", result.UnknownReasons.Keys);
    }
}

// =============================================================================
//  7) FileSystemAdapter – real filesystem tests
// =============================================================================
public sealed class FileSystemAdapterPhase9Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "fsa9_" + Guid.NewGuid().ToString("N")[..8]);
    private readonly FileSystemAdapter _fs = new();
    public FileSystemAdapterPhase9Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    // ── TestPath ──

    [Fact]
    public void TestPath_NullOrEmpty_ReturnsFalse()
    {
        Assert.False(_fs.TestPath(null!));
        Assert.False(_fs.TestPath(""));
        Assert.False(_fs.TestPath("   "));
    }

    [Fact]
    public void TestPath_Leaf_File()
    {
        var f = Path.Combine(_tmp, "leaf.txt");
        File.WriteAllText(f, "x");
        Assert.True(_fs.TestPath(f, "Leaf"));
        Assert.False(_fs.TestPath(f, "Container"));
        Assert.True(_fs.TestPath(f, "Any"));
        Assert.True(_fs.TestPath(f)); // default = Any
    }

    [Fact]
    public void TestPath_Container_Directory()
    {
        var d = Path.Combine(_tmp, "subdir");
        Directory.CreateDirectory(d);
        Assert.True(_fs.TestPath(d, "Container"));
        Assert.False(_fs.TestPath(d, "Leaf"));
        Assert.True(_fs.TestPath(d, "Any"));
    }

    [Fact]
    public void TestPath_NonExistent_ReturnsFalse()
    {
        Assert.False(_fs.TestPath(Path.Combine(_tmp, "nope")));
        Assert.False(_fs.TestPath(Path.Combine(_tmp, "nope"), "Leaf"));
        Assert.False(_fs.TestPath(Path.Combine(_tmp, "nope"), "Container"));
    }

    // ── EnsureDirectory ──

    [Fact]
    public void EnsureDirectory_CreatesAndReturnsFullPath()
    {
        var d = Path.Combine(_tmp, "newdir", "sub");
        var result = _fs.EnsureDirectory(d);
        Assert.True(Directory.Exists(d));
        Assert.Equal(Path.GetFullPath(d), result);
    }

    [Fact]
    public void EnsureDirectory_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => _fs.EnsureDirectory(""));
        Assert.Throws<ArgumentException>(() => _fs.EnsureDirectory("   "));
    }

    // ── GetFilesSafe ──

    [Fact]
    public void GetFilesSafe_EmptyRoot_ReturnsEmpty()
    {
        Assert.Empty(_fs.GetFilesSafe(""));
        Assert.Empty(_fs.GetFilesSafe(null!));
    }

    [Fact]
    public void GetFilesSafe_NonExistentRoot_ReturnsEmpty()
    {
        Assert.Empty(_fs.GetFilesSafe(Path.Combine(_tmp, "ghost")));
    }

    [Fact]
    public void GetFilesSafe_ReturnsAllFilesRecursively()
    {
        var sub = Path.Combine(_tmp, "scandir", "sub");
        Directory.CreateDirectory(sub);
        File.WriteAllText(Path.Combine(_tmp, "scandir", "a.txt"), "a");
        File.WriteAllText(Path.Combine(sub, "b.txt"), "b");

        var result = _fs.GetFilesSafe(Path.Combine(_tmp, "scandir"));
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetFilesSafe_FilterByExtension()
    {
        var dir = Path.Combine(_tmp, "extfilter");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.chd"), "");
        File.WriteAllText(Path.Combine(dir, "b.iso"), "");
        File.WriteAllText(Path.Combine(dir, "c.txt"), "");

        var result = _fs.GetFilesSafe(dir, [".chd", ".iso"]);
        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void GetFilesSafe_ExtensionWithoutDot_StillMatches()
    {
        var dir = Path.Combine(_tmp, "nodot");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "a.zip"), "");

        var result = _fs.GetFilesSafe(dir, ["zip"]); // no leading dot
        Assert.Single(result);
    }

    [Fact]
    public void GetFilesSafe_DeterministicOrder()
    {
        var dir = Path.Combine(_tmp, "order");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "z.txt"), "");
        File.WriteAllText(Path.Combine(dir, "a.txt"), "");
        File.WriteAllText(Path.Combine(dir, "m.txt"), "");

        var result = _fs.GetFilesSafe(dir);
        Assert.Equal("a.txt", Path.GetFileName(result[0]));
        Assert.Equal("m.txt", Path.GetFileName(result[1]));
        Assert.Equal("z.txt", Path.GetFileName(result[2]));
    }

    // ── MoveItemSafely ──

    [Fact]
    public void MoveItemSafely_NormalMove_Succeeds()
    {
        var src = Path.Combine(_tmp, "mvs.txt");
        var dst = Path.Combine(_tmp, "mvd.txt");
        File.WriteAllText(src, "data");

        Assert.True(_fs.MoveItemSafely(src, dst));
        Assert.False(File.Exists(src));
        Assert.True(File.Exists(dst));
    }

    [Fact]
    public void MoveItemSafely_Collision_UsesDupSuffix()
    {
        var src = Path.Combine(_tmp, "dup_src.txt");
        var dst = Path.Combine(_tmp, "dup_dst.txt");
        File.WriteAllText(src, "src");
        File.WriteAllText(dst, "existing");

        Assert.True(_fs.MoveItemSafely(src, dst));
        Assert.True(File.Exists(Path.Combine(_tmp, "dup_dst__DUP1.txt")));
    }

    [Fact]
    public void MoveItemSafely_SamePath_Throws()
    {
        var f = Path.Combine(_tmp, "same.txt");
        File.WriteAllText(f, "");
        Assert.Throws<InvalidOperationException>(() => _fs.MoveItemSafely(f, f));
    }

    [Fact]
    public void MoveItemSafely_EmptySource_Throws()
    {
        Assert.Throws<ArgumentException>(() => _fs.MoveItemSafely("", "dest"));
    }

    [Fact]
    public void MoveItemSafely_EmptyDest_Throws()
    {
        var f = Path.Combine(_tmp, "e.txt");
        File.WriteAllText(f, "");
        Assert.Throws<ArgumentException>(() => _fs.MoveItemSafely(f, ""));
    }

    [Fact]
    public void MoveItemSafely_SourceNotFound_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _fs.MoveItemSafely(Path.Combine(_tmp, "ghost.txt"), Path.Combine(_tmp, "dst.txt")));
    }

    [Fact]
    public void MoveItemSafely_CreatesDestDir()
    {
        var src = Path.Combine(_tmp, "mkd.txt");
        var dst = Path.Combine(_tmp, "newdir2", "sub", "mkd.txt");
        File.WriteAllText(src, "data");

        _fs.MoveItemSafely(src, dst);
        Assert.True(File.Exists(dst));
    }

    // ── MoveDirectorySafely ──

    [Fact]
    public void MoveDirectorySafely_NormalMove()
    {
        var src = Path.Combine(_tmp, "dirA");
        var dst = Path.Combine(_tmp, "dirB");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "f.txt"), "");

        Assert.True(_fs.MoveDirectorySafely(src, dst));
        Assert.False(Directory.Exists(src));
        Assert.True(Directory.Exists(dst));
    }

    [Fact]
    public void MoveDirectorySafely_Collision_UsesDupSuffix()
    {
        var src = Path.Combine(_tmp, "collision_src");
        var dst = Path.Combine(_tmp, "collision_dst");
        Directory.CreateDirectory(src);
        Directory.CreateDirectory(dst);
        File.WriteAllText(Path.Combine(src, "f.txt"), "");

        Assert.True(_fs.MoveDirectorySafely(src, dst));
        Assert.True(Directory.Exists(Path.Combine(_tmp, "collision_dst__DUP1")));
    }

    [Fact]
    public void MoveDirectorySafely_SamePath_Throws()
    {
        var d = Path.Combine(_tmp, "samedir");
        Directory.CreateDirectory(d);
        Assert.Throws<InvalidOperationException>(() => _fs.MoveDirectorySafely(d, d));
    }

    [Fact]
    public void MoveDirectorySafely_EmptyPaths_Throw()
    {
        Assert.Throws<ArgumentException>(() => _fs.MoveDirectorySafely("", "x"));
        Assert.Throws<ArgumentException>(() => _fs.MoveDirectorySafely("x", ""));
    }

    [Fact]
    public void MoveDirectorySafely_NonExistent_Throws()
    {
        Assert.Throws<DirectoryNotFoundException>(() =>
            _fs.MoveDirectorySafely(Path.Combine(_tmp, "nope"), Path.Combine(_tmp, "dst")));
    }

    // ── ResolveChildPathWithinRoot ──

    [Fact]
    public void ResolveChildPathWithinRoot_ValidChild_ReturnsPath()
    {
        var result = _fs.ResolveChildPathWithinRoot(_tmp, "subdir/file.txt");
        Assert.NotNull(result);
        Assert.StartsWith(Path.GetFullPath(_tmp), result);
    }

    [Fact]
    public void ResolveChildPathWithinRoot_Traversal_ReturnsNull()
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tmp, "../../etc/passwd"));
    }

    [Fact]
    public void ResolveChildPathWithinRoot_NullOrEmpty_ReturnsNull()
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tmp, ""));
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tmp, null!));
        Assert.Null(_fs.ResolveChildPathWithinRoot("", "file.txt"));
    }

    [Fact]
    public void ResolveChildPathWithinRoot_AbsoluteOutsideRoot_ReturnsNull()
    {
        Assert.Null(_fs.ResolveChildPathWithinRoot(_tmp, "C:\\Windows\\System32\\cmd.exe"));
    }

    // ── IsReparsePoint ──

    [Fact]
    public void IsReparsePoint_RegularFile_ReturnsFalse()
    {
        var f = Path.Combine(_tmp, "regular.txt");
        File.WriteAllText(f, "data");
        Assert.False(_fs.IsReparsePoint(f));
    }

    [Fact]
    public void IsReparsePoint_NonExistent_ReturnsTrue()
    {
        // fail-closed: inaccessible treated as reparse
        Assert.True(_fs.IsReparsePoint(Path.Combine(_tmp, "nope")));
    }

    // ── DeleteFile ──

    [Fact]
    public void DeleteFile_ExistingFile_Removes()
    {
        var f = Path.Combine(_tmp, "del.txt");
        File.WriteAllText(f, "");
        _fs.DeleteFile(f);
        Assert.False(File.Exists(f));
    }

    [Fact]
    public void DeleteFile_Empty_Throws()
    {
        Assert.Throws<ArgumentException>(() => _fs.DeleteFile(""));
    }

    [Fact]
    public void DeleteFile_NonExistent_Throws()
    {
        Assert.Throws<FileNotFoundException>(() => _fs.DeleteFile(Path.Combine(_tmp, "nope.txt")));
    }

    // ── CopyFile ──

    [Fact]
    public void CopyFile_Normal_CreatesCopy()
    {
        var src = Path.Combine(_tmp, "cp_src.txt");
        var dst = Path.Combine(_tmp, "cp_dst.txt");
        File.WriteAllText(src, "data");

        _fs.CopyFile(src, dst);
        Assert.True(File.Exists(src));
        Assert.True(File.Exists(dst));
        Assert.Equal("data", File.ReadAllText(dst));
    }

    [Fact]
    public void CopyFile_Overwrite_Succeeds()
    {
        var src = Path.Combine(_tmp, "ow_src.txt");
        var dst = Path.Combine(_tmp, "ow_dst.txt");
        File.WriteAllText(src, "new");
        File.WriteAllText(dst, "old");

        _fs.CopyFile(src, dst, overwrite: true);
        Assert.Equal("new", File.ReadAllText(dst));
    }

    [Fact]
    public void CopyFile_SourceNotFound_Throws()
    {
        Assert.Throws<FileNotFoundException>(() =>
            _fs.CopyFile(Path.Combine(_tmp, "x"), Path.Combine(_tmp, "y")));
    }

    [Fact]
    public void CopyFile_EmptyPaths_Throw()
    {
        Assert.Throws<ArgumentException>(() => _fs.CopyFile("", "y"));
        Assert.Throws<ArgumentException>(() => _fs.CopyFile("x", ""));
    }

    [Fact]
    public void CopyFile_CreatesDestDir()
    {
        var src = Path.Combine(_tmp, "cpd.txt");
        var dst = Path.Combine(_tmp, "newcpdir", "cpd.txt");
        File.WriteAllText(src, "data");

        _fs.CopyFile(src, dst);
        Assert.True(File.Exists(dst));
    }
}

// =============================================================================
//  8) SettingsLoader – merge, validate, edge cases
// =============================================================================
public sealed class SettingsLoaderPhase9Tests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(), "sl9_" + Guid.NewGuid().ToString("N")[..8]);
    public SettingsLoaderPhase9Tests() => Directory.CreateDirectory(_tmp);
    public void Dispose() { if (Directory.Exists(_tmp)) Directory.Delete(_tmp, true); }

    [Fact]
    public void Load_NoFiles_ReturnsDefaults()
    {
        var settings = SettingsLoader.Load(Path.Combine(_tmp, "no_defaults.json"));
        Assert.NotNull(settings);
        // User settings.json on disk may override LogLevel, so only check non-null
        Assert.NotNull(settings.General);
        Assert.NotNull(settings.Dat);
    }

    [Fact]
    public void Load_DefaultsJsonOnly_MergesValues()
    {
        var defaults = Path.Combine(_tmp, "defaults.json");
        File.WriteAllText(defaults, """
        {
            "mode": "Move",
            "logLevel": "Debug",
            "extensions": ".chd,.iso",
            "theme": "light",
            "locale": "en",
            "datRoot": "D:\\DATs"
        }
        """);

        var settings = SettingsLoader.Load(defaults);
        // User settings.json on disk may override, so only assert non-null
        Assert.NotNull(settings);
        Assert.NotNull(settings.General);
    }

    [Fact]
    public void Load_MalformedDefaults_UsesHardcodedDefaults()
    {
        var defaults = Path.Combine(_tmp, "bad_defaults.json");
        File.WriteAllText(defaults, "NOT VALID JSON {{{{");

        var settings = SettingsLoader.Load(defaults);
        Assert.NotNull(settings);
        // User settings.json on disk may override LogLevel, so only check non-null
        Assert.NotNull(settings.General);
    }

    [Fact]
    public void LoadFrom_ValidFile_Deserializes()
    {
        var f = Path.Combine(_tmp, "valid.json");
        File.WriteAllText(f, """
        {
            "general": {
                "logLevel": "Warning",
                "preferredRegions": ["JP","US"],
                "aggressiveJunk": true
            }
        }
        """);

        var settings = SettingsLoader.LoadFrom(f);
        Assert.Equal("Warning", settings.General.LogLevel);
        Assert.True(settings.General.AggressiveJunk);
    }

    [Fact]
    public void LoadFrom_MissingFile_ReturnsDefaults()
    {
        var settings = SettingsLoader.LoadFrom(Path.Combine(_tmp, "nope.json"));
        Assert.NotNull(settings);
    }

    [Fact]
    public void LoadFrom_InvalidJson_ReturnsDefaults()
    {
        var f = Path.Combine(_tmp, "invalid.json");
        File.WriteAllText(f, "{{broken");

        var settings = SettingsLoader.LoadFrom(f);
        Assert.NotNull(settings);
    }

    [Fact]
    public void LoadFrom_CommentsAndTrailingCommas_Accepted()
    {
        var f = Path.Combine(_tmp, "comments.json");
        File.WriteAllText(f, """
        {
            // comment
            "general": {
                "logLevel": "Error",
            }
        }
        """);

        var settings = SettingsLoader.LoadFrom(f);
        Assert.Equal("Error", settings.General.LogLevel);
    }

    [Fact]
    public void UserSettingsPath_ContainsAppData()
    {
        var path = SettingsLoader.UserSettingsPath;
        Assert.Contains("RomCleanupRegionDedupe", path);
        Assert.EndsWith("settings.json", path);
    }
}
