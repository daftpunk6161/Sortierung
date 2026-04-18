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

    // ── F-07 Regression: Pre-Detect DAT match + Detection disagreement ───────

    [Fact]
    public void Execute_PreDetectDatMatchesPs1_DetectorSaysPs2_IntraFamily_DatWins_DatVerified()
    {
        // Scenario F-07 (intra-family, hash match):
        // A file's hash matches a PS1 DAT entry, but folder-name detection says PS2.
        // Both are in the RedumpDisc family.
        //
        // Expected: DatVerified — exact hash match is the highest authority.
        // The file IS a PS1 game regardless of which folder it's stored in.
        // Intra-family folder disagreement does NOT override a hash-verified identity.
        var root = Path.Combine(_tempDir, "intra-family-conflict");
        var ps2Folder = Path.Combine(root, "PS2");
        Directory.CreateDirectory(ps2Folder);
        var filePath = CreateFile(ps2Folder, "ambiguous.bin", 192);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        // DAT records this hash under PS1
        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash!, "PS1 Game", "game.bin", isBios: false);

        // Detector knows both PS1 and PS2 in the same family (RedumpDisc)
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
                Key: "PS2",
                DisplayName: "PlayStation 2",
                DiscBased: true,
                UniqueExts: [],
                AmbigExts: ["bin", "iso"],
                FolderAliases: ["PS2", "PlayStation 2"],
                Family: PlatformFamily.RedumpDisc,
                HashStrategy: "track-sha1"),
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

        // DAT authority wins on ConsoleKey (PS1 hash match)
        Assert.True(candidate.DatMatch, "DAT must have matched the PS1 hash");
        Assert.Equal("PS1", candidate.ConsoleKey);

        // Hash-verified DAT match is the highest authority — DatVerified even with folder disagreement.
        // The file is identified as PS1 by hash; the PS2 folder is a misplacement.
        Assert.Equal(SortDecision.DatVerified, candidate.SortDecision);
        Assert.Equal(DecisionClass.DatVerified, candidate.DecisionClass);
    }

    [Fact]
    public void Execute_PreDetectDatMatchesPs1_NoDetectorConflict_YieldsDatVerified()
    {
        // Baseline for F-07: when DAT matches PS1 and detection also agrees → DatVerified.
        var root = Path.Combine(_tempDir, "no-conflict");
        var ps1Folder = Path.Combine(root, "PS1");
        Directory.CreateDirectory(ps1Folder);
        var filePath = CreateFile(ps1Folder, "ps1game.bin", 192);

        var hashService = new FileHashService();
        var hash = hashService.GetHash(filePath, "SHA1");
        Assert.False(string.IsNullOrWhiteSpace(hash));

        var datIndex = new DatIndex();
        datIndex.Add("PS1", hash!, "PS1 Game", "game.bin", isBios: false);

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
        Assert.Equal(SortDecision.DatVerified, candidate.SortDecision);
    }
}
