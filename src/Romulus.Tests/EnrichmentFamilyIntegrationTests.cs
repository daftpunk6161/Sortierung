using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Metrics;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

public sealed class EnrichmentFamilyIntegrationTests : IDisposable
{
    private readonly string _tempDir;

    public EnrichmentFamilyIntegrationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_EnrichmentFamily_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Execute_CrossFamilyDatMatch_EscalatesToBlocked()
    {
        var root = Path.Combine(_tempDir, "cross-family");
        var ps1Folder = Path.Combine(root, "PS1");
        Directory.CreateDirectory(ps1Folder);
        var filePath = CreateFile(ps1Folder, "mismatch.bin", 128);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        // Hash exists only in VITA DAT entry, while folder/detector hints PS1.
        var datIndex = new DatIndex();
        datIndex.Add("VITA", hash!, "Vita Game", "vita-game.bin", isBios: false);

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "PS1",
                DisplayName: "PlayStation",
                DiscBased: true,
                UniqueExts: ["cue"],
                AmbigExts: ["bin", "iso"],
                FolderAliases: ["PS1", "PlayStation"],
                Family: PlatformFamily.RedumpDisc,
                HashStrategy: "track-sha1"),
            new ConsoleInfo(
                Key: "VITA",
                DisplayName: "PS Vita",
                DiscBased: false,
                UniqueExts: ["vpk"],
                AmbigExts: ["bin"],
                FolderAliases: ["VITA", "PSVita"],
                Family: PlatformFamily.Hybrid,
                HashStrategy: "container-sha1")
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
        Assert.Equal("VITA", candidate.ConsoleKey);
        Assert.Equal(DecisionClass.Blocked, candidate.DecisionClass);
        Assert.Equal(SortDecision.Blocked, candidate.SortDecision);
        Assert.Equal(ConflictType.CrossFamily, candidate.DetectionConflictType);
    }

    [Fact]
    public void Execute_HybridFamily_DoesNotUseNameOnlyDatFallback()
    {
        var root = Path.Combine(_tempDir, "hybrid-name-fallback");
        var vitaFolder = Path.Combine(root, "VITA");
        Directory.CreateDirectory(vitaFolder);

        var stem = "NameOnlyCandidate";
        var filePath = CreateFile(vitaFolder, stem + ".chd", 140);

        var hashService = new FileHashService();
        var actualHash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(actualHash));

        // Same game name exists, but hash intentionally does not match.
        var datIndex = new DatIndex();
        datIndex.Add("VITA", "0000000000000000000000000000000000000000", stem, stem + ".chd", isBios: false);

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "VITA",
                DisplayName: "PS Vita",
                DiscBased: false,
                UniqueExts: ["vpk"],
                AmbigExts: ["chd", "bin"],
                FolderAliases: ["VITA", "PSVita"],
                Family: PlatformFamily.Hybrid,
                HashStrategy: "container-sha1")
        ]);

        var scan = new[] { new ScannedFileEntry(root, filePath, ".chd") };
        var options = new RunOptions
        {
            Roots = [root],
            Extensions = [".chd"],
            Mode = "DryRun",
            HashType = "SHA1"
        };

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(scan, detector, hashService, null, datIndex),
            CreateContext(options),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.False(candidate.DatMatch);
        Assert.Equal("VITA", candidate.ConsoleKey);
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
