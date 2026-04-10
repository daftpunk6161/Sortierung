using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Phase 3 / TASK-024: Expanded DatAuditPipelinePhase integration tests.
/// Covers all 5 status paths, candidate enrichment, headerless hash candidates,
/// and verifies no filesystem side effects.
/// </summary>
public sealed class DatAuditPipelinePhaseExpandedTests
{
    [Fact]
    public void Execute_AllFiveStatuses_CorrectCounts()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash-have", "Mario", "mario.nes");
        datIndex.Add("NES", "hash-wrong", "Contra", "Contra (World).nes");
        datIndex.Add("NES", "hash-dup1", "Zelda NES", "zelda.nes");
        datIndex.Add("FDS", "hash-dup1", "Zelda FDS", "zelda.fds");

        var candidates = new[]
        {
            // Have: hash matches, filename matches
            new RomCandidate { MainPath = @"C:\roms\mario.nes", ConsoleKey = "NES", Hash = "hash-have" },
            // HaveWrongName: hash matches, filename different
            new RomCandidate { MainPath = @"C:\roms\contra-bad.nes", ConsoleKey = "NES", Hash = "hash-wrong" },
            // Miss: known console, hash not in index
            new RomCandidate { MainPath = @"C:\roms\unknown.nes", ConsoleKey = "NES", Hash = "hash-miss" },
            // Unknown: no hash
            new RomCandidate { MainPath = @"C:\roms\nohash.nes", ConsoleKey = "NES", Hash = null },
            // Ambiguous: no console key, hash matches multiple consoles
            new RomCandidate { MainPath = @"C:\roms\ambig.bin", ConsoleKey = "", Hash = "hash-dup1" },
        };

        var result = ExecutePhase(candidates, datIndex);

        Assert.Equal(1, result.HaveCount);
        Assert.Equal(1, result.HaveWrongNameCount);
        Assert.Equal(1, result.MissCount);
        Assert.Equal(1, result.UnknownCount);
        Assert.Equal(1, result.AmbiguousCount);
        Assert.Equal(5, result.Entries.Count);
    }

    [Fact]
    public void Execute_SetsGameNameAndRomFileName_ForHaveCandidate()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash1", "Super Mario Bros.", "Super Mario Bros. (USA).nes");

        var candidates = new[]
        {
            new RomCandidate { MainPath = @"C:\roms\Super Mario Bros. (USA).nes", ConsoleKey = "NES", Hash = "hash1" }
        };

        var result = ExecutePhase(candidates, datIndex);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(DatAuditStatus.Have, entry.Status);
        Assert.Equal("Super Mario Bros.", entry.DatGameName);
        Assert.Equal("Super Mario Bros. (USA).nes", entry.DatRomFileName);
    }

    [Fact]
    public void Execute_WithHeaderlessHash_PrefersHeaderlessForClassification()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "headerless-hash", "Contra", "Contra (USA).nes");

        var candidates = new[]
        {
            new RomCandidate
            {
                MainPath = @"C:\roms\Contra (USA).nes",
                ConsoleKey = "NES",
                Hash = "headered-hash-no-match",
                HeaderlessHash = "headerless-hash"
            }
        };

        var result = ExecutePhase(candidates, datIndex);

        var entry = Assert.Single(result.Entries);
        Assert.Equal(DatAuditStatus.Have, entry.Status);
        Assert.Equal("Contra", entry.DatGameName);
    }

    [Fact]
    public void Execute_ConfidenceValues_MatchStatus()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash1", "Game1", "game1.nes");

        var candidates = new[]
        {
            new RomCandidate { MainPath = @"C:\roms\game1.nes", ConsoleKey = "NES", Hash = "hash1" },
            new RomCandidate { MainPath = @"C:\roms\nohash.nes", ConsoleKey = "NES", Hash = null },
        };

        var result = ExecutePhase(candidates, datIndex);

        var haveEntry = result.Entries.First(e => e.Status == DatAuditStatus.Have);
        var unknownEntry = result.Entries.First(e => e.Status == DatAuditStatus.Unknown);

        Assert.Equal(100, haveEntry.Confidence);
        Assert.Equal(60, unknownEntry.Confidence);
    }

    [Fact]
    public void Execute_NoFilesystemSideEffects()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash1", "Game", "game.nes");

        var candidates = new[]
        {
            new RomCandidate { MainPath = @"C:\roms\game.nes", ConsoleKey = "NES", Hash = "hash1" }
        };

        var fs = new TrackingFileSystem();
        var result = ExecutePhaseWithFs(candidates, datIndex, fs);

        Assert.Empty(fs.MoveCalls);
        Assert.Empty(fs.DeleteCalls);
        Assert.Empty(fs.CopyCalls);
        Assert.Empty(fs.RenameCalls);
    }

    [Fact]
    public void Execute_EmptyCandidateList_ReturnsZeroCounts()
    {
        var result = ExecutePhase(Array.Empty<RomCandidate>(), new DatIndex());

        Assert.Empty(result.Entries);
        Assert.Equal(0, result.HaveCount);
        Assert.Equal(0, result.HaveWrongNameCount);
        Assert.Equal(0, result.MissCount);
        Assert.Equal(0, result.UnknownCount);
        Assert.Equal(0, result.AmbiguousCount);
    }

    [Fact]
    public void Execute_DuplicateHashes_EachClassifiedIndependently()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "same-hash", "Game", "game.nes");

        var candidates = new[]
        {
            new RomCandidate { MainPath = @"C:\roms\game.nes", ConsoleKey = "NES", Hash = "same-hash" },
            new RomCandidate { MainPath = @"C:\roms\game-copy.nes", ConsoleKey = "NES", Hash = "same-hash" },
        };

        var result = ExecutePhase(candidates, datIndex);

        Assert.Equal(2, result.Entries.Count);
        // First matches by name
        Assert.Equal(DatAuditStatus.Have, result.Entries[0].Status);
        // Second has wrong name
        Assert.Equal(DatAuditStatus.HaveWrongName, result.Entries[1].Status);
        Assert.Equal(1, result.HaveCount);
        Assert.Equal(1, result.HaveWrongNameCount);
    }

    [Fact]
    public void Execute_CancellationToken_IsRespected()
    {
        var datIndex = new DatIndex();
        datIndex.Add("NES", "hash1", "Game", "game.nes");

        var candidates = new[]
        {
            new RomCandidate { MainPath = @"C:\roms\game.nes", ConsoleKey = "NES", Hash = "hash1" }
        };

        var cts = new CancellationTokenSource();
        cts.Cancel();

        Assert.Throws<OperationCanceledException>(() =>
            ExecutePhaseWithToken(candidates, datIndex, cts.Token));
    }

    // ── Helpers ───────────────────────────────────────────────────

    private static DatAuditResult ExecutePhase(IReadOnlyList<RomCandidate> candidates, DatIndex datIndex)
    {
        var options = new RunOptions { Mode = "DryRun" };
        var context = CreateContext(options);
        return new DatAuditPipelinePhase().Execute(
            new DatAuditInput(candidates, datIndex, options),
            context,
            CancellationToken.None);
    }

    private static DatAuditResult ExecutePhaseWithFs(
        IReadOnlyList<RomCandidate> candidates, DatIndex datIndex, IFileSystem fs)
    {
        var options = new RunOptions { Mode = "DryRun" };
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        var context = new PipelineContext
        {
            Options = options,
            FileSystem = fs,
            AuditStore = new NoOpAuditStore(),
            Metrics = metrics,
            OnProgress = _ => { }
        };
        return new DatAuditPipelinePhase().Execute(
            new DatAuditInput(candidates, datIndex, options),
            context,
            CancellationToken.None);
    }

    private static DatAuditResult ExecutePhaseWithToken(
        IReadOnlyList<RomCandidate> candidates, DatIndex datIndex, CancellationToken token)
    {
        var options = new RunOptions { Mode = "DryRun" };
        var context = CreateContext(options);
        return new DatAuditPipelinePhase().Execute(
            new DatAuditInput(candidates, datIndex, options),
            context,
            token);
    }

    private static PipelineContext CreateContext(RunOptions options)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
            FileSystem = new NoOpFileSystem(),
            AuditStore = new NoOpAuditStore(),
            Metrics = metrics,
            OnProgress = _ => { }
        };
    }

    /// <summary>FileSystem that tracks all calls to detect side effects.</summary>
    private sealed class TrackingFileSystem : IFileSystem
    {
        public List<string> MoveCalls { get; } = [];
        public List<string> DeleteCalls { get; } = [];
        public List<string> CopyCalls { get; } = [];
        public List<string> RenameCalls { get; } = [];

        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null) => [];
        public string? MoveItemSafely(string s, string d) { MoveCalls.Add(s); return d; }
        public bool MoveDirectorySafely(string s, string d) { MoveCalls.Add(s); return true; }
        public string? ResolveChildPathWithinRoot(string r, string rel) => Path.Combine(r, rel);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) => DeleteCalls.Add(path);
        public void CopyFile(string s, string d, bool o = false) => CopyCalls.Add(s);
        public string? RenameItemSafely(string s, string n) { RenameCalls.Add(s); return Path.Combine(Path.GetDirectoryName(s) ?? "", n); }
    }

    private sealed class NoOpFileSystem : IFileSystem
    {
        public bool TestPath(string literalPath, string pathType = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null) => [];
        public string? MoveItemSafely(string s, string d) => d;
        public bool MoveDirectorySafely(string s, string d) => true;
        public string? ResolveChildPathWithinRoot(string r, string rel) => Path.Combine(r, rel);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string s, string d, bool o = false) { }
        public string? RenameItemSafely(string s, string n) => Path.Combine(Path.GetDirectoryName(s) ?? "", n);
    }

    private sealed class NoOpAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string p, IDictionary<string, object> m) { }
        public bool TestMetadataSidecar(string p) => true;
        public void Flush(string p) { }
        public IReadOnlyList<string> Rollback(string p, string[] a, string[] c, bool d = false) => [];
        public void AppendAuditRow(string p, string r, string o, string n, string a, string cat = "", string h = "", string reason = "") { }
    }
}
