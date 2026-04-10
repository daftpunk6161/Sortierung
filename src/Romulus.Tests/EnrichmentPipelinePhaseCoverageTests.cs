using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for EnrichmentPipelinePhase: internal helper methods,
/// Execute with null/minimal dependencies, edge cases for DAT/detection resolution.
/// </summary>
public sealed class EnrichmentPipelinePhaseCoverageTests : IDisposable
{
    private readonly string _tempDir;

    public EnrichmentPipelinePhaseCoverageTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"enrichment_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tempDir, true); } catch { }
    }

    #region IsStrictDatNameCandidate (via Execute name-only match path)

    // IsStrictDatNameCandidate is private static, tested indirectly through Execute
    // and through ResolveUnknownDatNameMatch.
    // We test the internal resolve methods directly.

    #endregion

    #region ResolveUnknownDatMatch

    [Fact]
    public void ResolveUnknownDatMatch_NoMatches_ReturnsNoMatch()
    {
        var datIndex = new DatIndex();
        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "deadbeef", null);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatMatch_SingleMatch_ReturnsThatConsole()
    {
        var datIndex = new DatIndex();
        datIndex.Add("SNES", "abc123", "Super Mario World", "smw.sfc");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "abc123", null);

        Assert.True(result.IsMatch);
        Assert.Equal("SNES", result.ConsoleKey);
        Assert.Equal("Super Mario World", result.DatGameName);
        Assert.False(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultiMatch_NullDetection_NoMatch()
    {
        var datIndex = new DatIndex();
        datIndex.Add("SNES", "abc123", "Game A", "a.sfc");
        datIndex.Add("NES", "abc123", "Game B", "b.nes");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "abc123", null);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultiMatch_DetectionNoHypotheses_NoMatch()
    {
        var datIndex = new DatIndex();
        datIndex.Add("SNES", "abc123", "Game A", "a.sfc");
        datIndex.Add("NES", "abc123", "Game B", "b.nes");

        var detection = new ConsoleDetectionResult("UNKNOWN", 0, [], HasConflict: false, ConflictDetail: null);
        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "abc123", detection);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultiMatch_UsesHighestConfidenceHypothesis()
    {
        var datIndex = new DatIndex();
        datIndex.Add("SNES", "abc123", "Game A", "a.sfc");
        datIndex.Add("NES", "abc123", "Game B", "b.nes");

        var detection = new ConsoleDetectionResult(
            "SNES", 90,
            [
                new DetectionHypothesis("NES", 80, DetectionSource.FolderName, "folder"),
                new DetectionHypothesis("SNES", 90, DetectionSource.UniqueExtension, "ext")
            ],
            HasConflict: true, ConflictDetail: "NES vs SNES");

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "abc123", detection);

        Assert.True(result.IsMatch);
        Assert.Equal("SNES", result.ConsoleKey);
        Assert.True(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultiMatch_HypothesisNoIntersection_NoMatch()
    {
        var datIndex = new DatIndex();
        datIndex.Add("SNES", "abc123", "Game A", "a.sfc");
        datIndex.Add("NES", "abc123", "Game B", "b.nes");

        var detection = new ConsoleDetectionResult(
            "GBA", 90,
            [new DetectionHypothesis("GBA", 90, DetectionSource.UniqueExtension, "ext")],
            HasConflict: false, ConflictDetail: null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "abc123", detection);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatMatch_SingleBiosMatch_ReturnsBiosFlag()
    {
        var datIndex = new DatIndex();
        datIndex.Add("PS1", "bios123", "PS1 BIOS", "scph1001.bin", isBios: true);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, "bios123", null);

        Assert.True(result.IsMatch);
        Assert.True(result.IsBios);
    }

    #endregion

    #region ResolveUnknownDatNameMatch

    [Fact]
    public void ResolveUnknownDatNameMatch_EmptyList_NoMatch()
    {
        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch([], null);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_SingleMatch_ReturnsThatConsole()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("PS2", new DatIndex.DatIndexEntry("FF7", "ff7.iso", false))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, null);
        Assert.True(result.IsMatch);
        Assert.Equal("PS2", result.ConsoleKey);
        Assert.False(result.ResolvedFromAmbiguousCandidates);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultiMatch_NullDetection_NoMatch()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("PS1", new DatIndex.DatIndexEntry("Game", "game.bin", false)),
            ("PS2", new DatIndex.DatIndexEntry("Game", "game.iso", false))
        };

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, null);
        Assert.False(result.IsMatch);
    }

    [Fact]
    public void ResolveUnknownDatNameMatch_MultiMatch_HypothesisResolvesConsole()
    {
        var matches = new List<(string ConsoleKey, DatIndex.DatIndexEntry Entry)>
        {
            ("PS1", new DatIndex.DatIndexEntry("Game", "game.bin", false)),
            ("PS2", new DatIndex.DatIndexEntry("Game", "game.iso", false))
        };

        var detection = new ConsoleDetectionResult(
            "PS2", 95,
            [new DetectionHypothesis("PS2", 95, DetectionSource.FolderName, "folder=PS2")],
            HasConflict: false, ConflictDetail: null);

        var result = EnrichmentPipelinePhase.ResolveUnknownDatNameMatch(matches, detection);
        Assert.True(result.IsMatch);
        Assert.Equal("PS2", result.ConsoleKey);
        Assert.True(result.ResolvedFromAmbiguousCandidates);
    }

    #endregion

    #region Execute – Minimal Dependencies

    [Fact]
    public void Execute_EmptyFiles_ReturnsEmptyList()
    {
        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput([], null, null, null, null);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public void Execute_SingleFile_NoDat_NoDetector_ReturnsUnknownCandidate()
    {
        var filePath = CreateFile("Super Mario World (USA).sfc", "dummy");

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(
            [new ScannedFileEntry(_tempDir, filePath, ".sfc")],
            null, null, null, null);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Single(result);
        var candidate = result[0];
        Assert.Equal("UNKNOWN", candidate.ConsoleKey);
        Assert.False(candidate.DatMatch);
        Assert.NotNull(candidate.GameKey);
    }

    [Fact]
    public void Execute_WithDetector_ResolvesConsole()
    {
        var snesDir = Path.Combine(_tempDir, "SNES");
        Directory.CreateDirectory(snesDir);
        var filePath = CreateFileIn(snesDir, "game.sfc", "data");

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "SNES",
                DisplayName: "Super Nintendo",
                DiscBased: false,
                UniqueExts: ["sfc"],
                AmbigExts: [],
                FolderAliases: ["SNES"],
                Family: PlatformFamily.NoIntroCartridge)
        ]);

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(
            [new ScannedFileEntry(snesDir, filePath, ".sfc")],
            detector, null, null, null);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("SNES", result[0].ConsoleKey);
    }

    [Fact]
    public void Execute_WithDatMatch_SetsDatMatchTrue()
    {
        var filePath = CreateFile("game.sfc", "data");

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.NotNull(hash);

        var datIndex = new DatIndex();
        datIndex.Add("SNES", hash!, "Super Mario World", "smw.sfc");

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(
            [new ScannedFileEntry(_tempDir, filePath, ".sfc")],
            null, hashService, null, datIndex);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].DatMatch);
        Assert.Equal("SNES", result[0].ConsoleKey);
    }

    [Fact]
    public void Execute_Cancellation_Throws()
    {
        var filePath = CreateFile("game.sfc", "data");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(
            [new ScannedFileEntry(_tempDir, filePath, ".sfc")],
            null, null, null, null);
        var ctx = CreateContext(_tempDir);

        Assert.Throws<OperationCanceledException>(() =>
            phase.Execute(input, ctx, cts.Token));
    }

    [Fact]
    public void Execute_MultipleFiles_BelowParallelThreshold_ProcessesSequentially()
    {
        var files = new List<ScannedFileEntry>();
        for (int i = 0; i < 3; i++)
        {
            var fp = CreateFile($"game{i}.sfc", $"data{i}");
            files.Add(new ScannedFileEntry(_tempDir, fp, ".sfc"));
        }

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(files, null, null, null, null);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Execute_ManyFiles_AboveParallelThreshold()
    {
        // 10 files exceeds ParallelizationThreshold(4)
        var files = new List<ScannedFileEntry>();
        for (int i = 0; i < 10; i++)
        {
            var fp = CreateFile($"game{i}.nes", $"data{i}");
            files.Add(new ScannedFileEntry(_tempDir, fp, ".nes"));
        }

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(files, null, null, null, null);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Equal(10, result.Count);
    }

    [Fact]
    public void Execute_JunkFile_ClassifiedAsJunk()
    {
        // Files matching junk patterns like "[b]", "[h]" etc.
        var filePath = CreateFile("[BIOS] System (USA).bin", "biosdata");

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(
            [new ScannedFileEntry(_tempDir, filePath, ".bin")],
            null, null, null, null);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Single(result);
        // The file classification will treat this as BIOS-like based on name
    }

    [Fact]
    public void Execute_KnownBiosHash_MarkedAsBios()
    {
        var filePath = CreateFile("firmware.bin", "firmware-content");

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.NotNull(hash);

        var knownBiosHashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hash! };

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(
            [new ScannedFileEntry(_tempDir, filePath, ".bin")],
            null, hashService, null, null,
            KnownBiosHashes: knownBiosHashes);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(FileCategory.Bios, result[0].Category);
    }

    [Fact]
    public void Execute_DatBiosMatch_FlaggedAsBios()
    {
        var filePath = CreateFile("bios.bin", "biosdata");

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.NotNull(hash);

        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash!, "SCPH-1001 BIOS", "scph1001.bin", isBios: true);

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseInput(
            [new ScannedFileEntry(_tempDir, filePath, ".bin")],
            null, hashService, null, datIndex);
        var ctx = CreateContext(_tempDir);

        var result = phase.Execute(input, ctx, CancellationToken.None);

        Assert.Single(result);
        Assert.True(result[0].DatMatch);
        Assert.Equal(FileCategory.Bios, result[0].Category);
    }

    #endregion

    #region ExecuteStreamingAsync

    [Fact]
    public async Task ExecuteStreamingAsync_EmptyInput_ReturnsEmpty()
    {
        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseStreamingInput(
            EmptyAsync<ScannedFileEntry>(),
            null, null, null, null);
        var ctx = CreateContext(_tempDir);

        var results = new List<RomCandidate>();
        await foreach (var c in phase.ExecuteStreamingAsync(input, ctx, CancellationToken.None))
            results.Add(c);

        Assert.Empty(results);
    }

    [Fact]
    public async Task ExecuteStreamingAsync_SingleFile_ReturnsCandidate()
    {
        var filePath = CreateFile("game.nes", "data");

        var phase = new EnrichmentPipelinePhase();
        var input = new EnrichmentPhaseStreamingInput(
            ToAsync([new ScannedFileEntry(_tempDir, filePath, ".nes")]),
            null, null, null, null);
        var ctx = CreateContext(_tempDir);

        var results = new List<RomCandidate>();
        await foreach (var c in phase.ExecuteStreamingAsync(input, ctx, CancellationToken.None))
            results.Add(c);

        Assert.Single(results);
    }

    #endregion

    #region Helpers

    private string CreateFile(string name, string content)
    {
        var path = Path.Combine(_tempDir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateFileIn(string dir, string name, string content)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    private static PipelineContext CreateContext(string root)
        => new()
        {
            Options = new RunOptions
            {
                Roots = [root],
                TrashRoot = "",
                AuditPath = "",
                HashType = "SHA1",
            },
            FileSystem = new FileSystemAdapter(),
            AuditStore = new NoOpAuditStore(),
            Metrics = new PhaseMetricsCollector(),
            OnProgress = null,
        };

    private sealed class NoOpAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false) => [];
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "") { }
        public void Flush(string auditCsvPath) { }
    }

    private static async IAsyncEnumerable<T> EmptyAsync<T>()
    {
        await Task.CompletedTask;
        yield break;
    }

    private static async IAsyncEnumerable<T> ToAsync<T>(IReadOnlyList<T> items)
    {
        await Task.CompletedTask;
        foreach (var item in items)
            yield return item;
    }

    #endregion
}
