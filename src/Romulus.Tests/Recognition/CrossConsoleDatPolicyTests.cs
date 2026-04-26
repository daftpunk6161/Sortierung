using System.IO.Compression;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Orchestration;
using Romulus.Tests.TestFixtures;
using Xunit;

namespace Romulus.Tests.Recognition;

/// <summary>
/// Cross-Console-DAT-Policy switch (FamilyDatPolicy.EnableCrossConsoleLookup)
/// gap coverage for ALL hash stages.
///
/// Existing coverage: container stage (Stage 3) is verified by
/// EnrichmentPipelinePhaseAuditPhase3And4Tests.Execute_KnownConsoleHash_WithCrossConsoleLookupDisabled.
///
/// This suite closes the remaining stage gaps:
///  1.  Stage 1 (Archive inner hash, .zip)        - cross-console disabled = no DAT match
///  2.  Stage 2 (Headerless hash, NES/SNES style) - cross-console disabled = no DAT match
///  3.  Stage 4 (Name-only fallback)              - cross-console disabled = no DAT match
///        even when the family policy explicitly allows name-only matching.
/// </summary>
public sealed class CrossConsoleDatPolicyTests : IDisposable
{
    private readonly string _tempDir;

    public CrossConsoleDatPolicyTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_C1_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void EnrichmentPipeline_ArchiveInnerHashStageWithCrossConsoleDisabled_DoesNotMatchOtherConsole()
    {
        // Build a real ZIP with a single inner ROM. DAT entry is registered ONLY
        // for a non-resolved console (PS1) - cross-console disabled MUST suppress the match.
        var snesFolder = Path.Combine(_tempDir, "SNES");
        Directory.CreateDirectory(snesFolder);

        var innerName = "rom.bin";
        var innerBytes = Enumerable.Range(1, 64).Select(i => (byte)(i % 251)).ToArray();
        var innerSha1 = Convert.ToHexString(System.Security.Cryptography.SHA1.HashData(innerBytes));

        var zipPath = Path.Combine(snesFolder, "set.zip");
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            var entry = zip.CreateEntry(innerName);
            using var s = entry.Open();
            s.Write(innerBytes);
        }

        var datIndex = new DatIndex();
        datIndex.Add("PS1", innerSha1, "Other Console Game", romFileName: innerName, isBios: false);

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "SNES", DisplayName: "SNES", DiscBased: false,
                UniqueExts: ["sfc"], AmbigExts: ["zip"], FolderAliases: ["SNES"],
                Family: PlatformFamily.NoIntroCartridge)
        ]);

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(
                [new ScannedFileEntry(_tempDir, zipPath, ".zip")],
                detector,
                new FileHashService(),
                new ArchiveHashService(),
                datIndex,
                FamilyDatStrategyResolver: new FixedFamilyDatPolicyResolver(new FamilyDatPolicy(
                    PreferArchiveInnerHash: true,
                    UseHeaderlessHash: false,
                    UseContainerHash: true,
                    AllowNameOnlyDatMatch: false,
                    RequireStrictNameForNameOnly: false,
                    EnableCrossConsoleLookup: false))),
            EnrichmentTestHarness.BuildContext(new RunOptions
            {
                Roots = [_tempDir],
                Extensions = [".zip"],
                Mode = "DryRun",
                HashType = "SHA1"
            }),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.False(candidate.DatMatch,
            $"Unexpected DatMatch. ConsoleKey={candidate.ConsoleKey}, MatchKind={candidate.PrimaryMatchKind}, Tier={candidate.EvidenceTier}");
        Assert.NotEqual("PS1", candidate.ConsoleKey);
    }

    [Fact]
    public void EnrichmentPipeline_HeaderlessHashStageWithCrossConsoleDisabled_DoesNotMatchOtherConsole()
    {
        // Resolve console to SNES, but DAT entry for the headerless hash is registered
        // ONLY for NES. With cross-console disabled, no match.
        var snesFolder = Path.Combine(_tempDir, "SNES");
        Directory.CreateDirectory(snesFolder);
        var filePath = Path.Combine(snesFolder, "Game.sfc");
        File.WriteAllBytes(filePath, Enumerable.Range(1, 96).Select(i => (byte)(i % 251)).ToArray());

        const string headerlessHash = "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA";
        var headerless = new StubHeaderlessHasher(headerlessHash);

        var datIndex = new DatIndex();
        datIndex.Add("NES", headerlessHash, "NES Game", "game.nes", isBios: false);

        var detector = new ConsoleDetector([
            new ConsoleInfo(
                Key: "SNES", DisplayName: "SNES", DiscBased: false,
                UniqueExts: ["sfc"], AmbigExts: [], FolderAliases: ["SNES"],
                Family: PlatformFamily.NoIntroCartridge),
            new ConsoleInfo(
                Key: "NES", DisplayName: "NES", DiscBased: false,
                UniqueExts: ["nes"], AmbigExts: [], FolderAliases: ["NES"],
                Family: PlatformFamily.NoIntroCartridge)
        ]);

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(
                [new ScannedFileEntry(_tempDir, filePath, ".sfc")],
                detector,
                new FileHashService(),
                null,
                datIndex,
                HeaderlessHasher: headerless,
                FamilyDatStrategyResolver: new FixedFamilyDatPolicyResolver(new FamilyDatPolicy(
                    PreferArchiveInnerHash: false,
                    UseHeaderlessHash: true,
                    UseContainerHash: false,
                    AllowNameOnlyDatMatch: false,
                    RequireStrictNameForNameOnly: false,
                    EnableCrossConsoleLookup: false))),
            EnrichmentTestHarness.BuildContext(new RunOptions
            {
                Roots = [_tempDir],
                Extensions = [".sfc"],
                Mode = "DryRun",
                HashType = "SHA1"
            }),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.False(candidate.DatMatch);
        Assert.Equal("SNES", candidate.ConsoleKey);
    }

    [Fact]
    public void EnrichmentPipeline_NameOnlyFallbackStageWithCrossConsoleDisabled_DoesNotMatchOtherConsole()
    {
        // Unknown console scenario: detector has no signal, name-only fallback would
        // normally match. With cross-console disabled, the unknown-console name-only
        // path is suppressed entirely.
        var filePath = Path.Combine(_tempDir, "Some Disc Game (USA).iso");
        File.WriteAllBytes(filePath, Enumerable.Range(1, 256).Select(i => (byte)(i % 251)).ToArray());

        var datIndex = new DatIndex();
        // Register a DAT entry for the stem under a console the detector cannot resolve from this path.
        datIndex.Add("PS1", "deadbeef-not-the-actual-hash",
            gameName: "Some Disc Game (USA)",
            romFileName: "Some Disc Game (USA).iso",
            isBios: false);

        // Empty detector -> resolves UNKNOWN.
        var detector = new ConsoleDetector([]);

        var phase = new EnrichmentPipelinePhase();
        var result = phase.Execute(
            new EnrichmentPhaseInput(
                [new ScannedFileEntry(_tempDir, filePath, ".iso")],
                detector,
                new FileHashService(),
                null,
                datIndex,
                FamilyDatStrategyResolver: new FixedFamilyDatPolicyResolver(new FamilyDatPolicy(
                    PreferArchiveInnerHash: false,
                    UseHeaderlessHash: false,
                    UseContainerHash: true,
                    AllowNameOnlyDatMatch: true,
                    RequireStrictNameForNameOnly: false,
                    EnableCrossConsoleLookup: false))),
            EnrichmentTestHarness.BuildContext(new RunOptions
            {
                Roots = [_tempDir],
                Extensions = [".iso"],
                Mode = "DryRun",
                HashType = "SHA1"
            }),
            CancellationToken.None);

        var candidate = Assert.Single(result);
        Assert.False(candidate.DatMatch);
        Assert.NotEqual(MatchKind.DatNameOnlyMatch, candidate.PrimaryMatchKind);
    }

    private sealed class StubHeaderlessHasher(string hash) : IHeaderlessHasher
    {
        public string? ComputeHeaderlessHash(string filePath, string consoleKey, string hashType = "SHA1") => hash;
    }
}
