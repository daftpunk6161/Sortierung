using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public class EnrichmentPipelinePhaseAuditPhase3And4Tests : IDisposable
{
    private readonly string _tempDir;

    public EnrichmentPipelinePhaseAuditPhase3And4Tests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_EnrichmentAudit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Execute_DatBiosMatch_OverridesCategoryToBios()
    {
        var root = Path.Combine(_tempDir, "phase3");
        Directory.CreateDirectory(root);
        var filePath = CreateFile(root, "mystery.bin", 32);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var datIndex = new DatIndex();
        datIndex.Add("PSX", hash!, "PlayStation BIOS", "SCPH1001.BIN", isBios: true);

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "SNES",
                DisplayName: "SNES",
                DiscBased: false,
                UniqueExts: ["sfc"],
                AmbigExts: [],
                FolderAliases: ["SNES"])
        ]);

        var scan = new[] { new ScannedFileEntry(root, filePath, ".bin") };
        var options = new RunOptions
        {
            Roots = [root],
            Extensions = [".bin"],
            Mode = "DryRun",
            HashType = "SHA1"
        };

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(scan, detector, hashService, null, datIndex),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.True(candidate.DatMatch);
        Assert.Equal("PSX", candidate.ConsoleKey);
        Assert.Equal("PlayStation BIOS", candidate.DatGameName);
        Assert.Equal(FileCategory.Bios, candidate.Category);
        Assert.Equal(SortDecision.DatVerified, candidate.SortDecision);
    }

    [Fact]
    public void Execute_UnknownConsoleAmbiguousDat_UsesHypothesisIntersectionAndSetsReview()
    {
        var root = Path.Combine(_tempDir, "phase4");
        var snesFolder = Path.Combine(root, "SNES");
        Directory.CreateDirectory(snesFolder);
        var filePath = CreateFile(snesFolder, "mystery (PS1).bin", 48);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var datIndex = new DatIndex();
        datIndex.Add("MD", hash!, "Game MD", "game-md.bin");
        datIndex.Add("PS1", hash!, "Game PS1", "game-ps1.bin");

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "PS1",
                DisplayName: "PlayStation",
                DiscBased: true,
                UniqueExts: ["iso"],
                AmbigExts: [],
                FolderAliases: ["PlayStation", "PS1"]),
            new ConsoleInfo(
                Key: "MD",
                DisplayName: "Mega Drive",
                DiscBased: false,
                UniqueExts: ["md"],
                AmbigExts: [],
                FolderAliases: ["MegaDrive", "Genesis"]),
            new ConsoleInfo(
                Key: "SNES",
                DisplayName: "SNES",
                DiscBased: false,
                UniqueExts: ["sfc"],
                AmbigExts: [],
                FolderAliases: ["SNES"])
        ]);

        var scan = new[] { new ScannedFileEntry(root, filePath, ".bin") };
        var options = new RunOptions
        {
            Roots = [root],
            Extensions = [".bin"],
            Mode = "DryRun",
            HashType = "SHA1"
        };

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(scan, detector, hashService, null, datIndex),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.True(candidate.DatMatch);
        Assert.Equal("PS1", candidate.ConsoleKey);
        Assert.Equal("Game PS1", candidate.DatGameName);
        Assert.Equal(SortDecision.Review, candidate.SortDecision);
    }

    [Fact]
    public void Execute_KnownBiosHash_OverridesCategoryToBiosWithoutDat()
    {
        var root = Path.Combine(_tempDir, "phase3_known_hash");
        Directory.CreateDirectory(root);
        var filePath = CreateFile(root, "mystery.rom", 64);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "SNES",
                DisplayName: "SNES",
                DiscBased: false,
                UniqueExts: ["sfc"],
                AmbigExts: [],
                FolderAliases: ["SNES"])
        ]);

        var scan = new[] { new ScannedFileEntry(root, filePath, ".rom") };
        var options = new RunOptions
        {
            Roots = [root],
            Extensions = [".rom"],
            Mode = "DryRun",
            HashType = "SHA1"
        };

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(
                scan,
                detector,
                hashService,
                null,
                null,
                null,
                new HashSet<string>(StringComparer.OrdinalIgnoreCase) { hash! }),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.Equal(FileCategory.Bios, candidate.Category);
    }

    [Fact]
    public void Execute_DatHashMatch_OverridesJunkToGame()
    {
        var root = Path.Combine(_tempDir, "phase3_dat_game");
        Directory.CreateDirectory(root);
        var filePath = CreateFile(root, "sample (Demo).bin", 96);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash!, "Retail Game", "sample.bin", isBios: false);

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "PS1",
                DisplayName: "PlayStation",
                DiscBased: true,
                UniqueExts: ["cue"],
                AmbigExts: ["bin"],
                FolderAliases: ["PS1", "PlayStation"])
        ]);

        var scan = new[] { new ScannedFileEntry(root, filePath, ".bin") };
        var options = new RunOptions
        {
            Roots = [root],
            Extensions = [".bin"],
            Mode = "DryRun",
            HashType = "SHA1"
        };

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(scan, detector, hashService, null, datIndex),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.True(candidate.DatMatch);
        Assert.Equal(FileCategory.Game, candidate.Category);
        Assert.Equal("PS1", candidate.ConsoleKey);
        Assert.Equal(SortDecision.DatVerified, candidate.SortDecision);
    }

    [Fact]
    public void Execute_UnknownConsoleDatHashMatch_UpgradesToDatVerified()
    {
        var root = Path.Combine(_tempDir, "phase3_unknown_dat");
        Directory.CreateDirectory(root);
        var filePath = CreateFile(root, "mystery.rom", 80);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash!, "Known Game", "known-game.bin", isBios: false);

        var scan = new[] { new ScannedFileEntry(root, filePath, ".rom") };
        var options = new RunOptions
        {
            Roots = [root],
            Extensions = [".rom"],
            Mode = "DryRun",
            HashType = "SHA1"
        };

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(scan, null, hashService, null, datIndex),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.True(candidate.DatMatch);
        Assert.Equal("PS1", candidate.ConsoleKey);
        Assert.Equal(FileCategory.Game, candidate.Category);
        Assert.Equal(SortDecision.DatVerified, candidate.SortDecision);
    }

    private PipelineContext CreateContext(RunOptions options)
    {
        var metrics = new PhaseMetricsCollector();
        metrics.Initialize();
        return new PipelineContext
        {
            Options = options,
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
