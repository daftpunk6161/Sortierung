using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class CrossConsoleDatLookupTests : IDisposable
{
    private readonly string _tempDir;

    public CrossConsoleDatLookupTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_CrossConsoleDat_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Execute_DatFirstCrossConsoleLookup_ResolvesConsoleButCrossFamilyIsBlocked()
    {
        var root = Path.Combine(_tempDir, "cross-console");
        var snesFolder = Path.Combine(root, "SNES");
        Directory.CreateDirectory(snesFolder);
        var filePath = CreateFile(snesFolder, "mystery.bin", 128);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash!, "Actual PS1 Game", "actual.bin", isBios: false);

        // Detector context points to SNES, but DAT-first lookup must still route to PS1.
        // With family-conflict gate, the final decision must be blocked despite DAT match.
        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "SNES",
                DisplayName: "Super Nintendo",
                DiscBased: false,
                UniqueExts: ["sfc"],
                AmbigExts: ["bin"],
                FolderAliases: ["SNES"],
                Family: PlatformFamily.NoIntroCartridge),
            new ConsoleInfo(
                Key: "PS1",
                DisplayName: "PlayStation",
                DiscBased: true,
                UniqueExts: ["cue"],
                AmbigExts: ["bin"],
                FolderAliases: ["PS1"],
                Family: PlatformFamily.RedumpDisc)
        ]);

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(
                [new ScannedFileEntry(root, filePath, ".bin")],
                detector,
                hashService,
                null,
                datIndex),
            CreateContext(root),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.True(candidate.DatMatch);
        Assert.Equal("PS1", candidate.ConsoleKey);
        Assert.Equal(SortDecision.Blocked, candidate.SortDecision);
        Assert.Equal(DecisionClass.Blocked, candidate.DecisionClass);
    }

    [Fact]
    public void ResolveUnknownDatMatch_MultiConsole_UsesHighestHypothesisIntersection()
    {
        const string hash = "same-hash";
        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash, "Game PS1", "game-ps1.bin");
        datIndex.Add("SATURN", hash, "Game Saturn", "game-saturn.bin");

        var detection = new ConsoleDetectionResult(
            "SNES",
            95,
            [
                new DetectionHypothesis("SNES", 95, DetectionSource.FolderName, "folder=SNES"),
                new DetectionHypothesis("PS1", 90, DetectionSource.SerialNumber, "serial=SLUS-12345"),
                new DetectionHypothesis("SATURN", 80, DetectionSource.FilenameKeyword, "keyword=saturn")
            ],
            HasConflict: true,
            ConflictDetail: "SNES vs PS1");

        var resolution = EnrichmentPipelinePhase.ResolveUnknownDatMatch(datIndex, hash, detection);

        Assert.True(resolution.IsMatch);
        Assert.Equal("PS1", resolution.ConsoleKey);
        Assert.True(resolution.ResolvedFromAmbiguousCandidates);
    }

    private static PipelineContext CreateContext(string root)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = new RunOptions
            {
                Roots = [root],
                Extensions = [".bin"],
                Mode = "DryRun",
                HashType = "SHA1"
            },
            FileSystem = new FileSystemAdapter(),
            AuditStore = new AuditCsvStore(),
            Metrics = metrics
        };
    }

    private static string CreateFile(string root, string fileName, int sizeBytes)
    {
        var path = Path.Combine(root, fileName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Enumerable.Range(1, sizeBytes).Select(static i => (byte)(i % 251)).ToArray());
        return path;
    }
}
