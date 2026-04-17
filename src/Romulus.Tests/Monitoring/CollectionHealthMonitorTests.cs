using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Monitoring;
using Romulus.CLI;
using Xunit;

namespace Romulus.Tests.Monitoring;

public sealed class CollectionHealthMonitorTests
{
    // ═══ ScoreToGrade ════════════════════════════════════════════════

    [Theory]
    [InlineData(100, "A")]
    [InlineData(95, "A")]
    [InlineData(90, "A")]
    [InlineData(89, "B")]
    [InlineData(80, "B")]
    [InlineData(79, "C")]
    [InlineData(70, "C")]
    [InlineData(69, "D")]
    [InlineData(50, "D")]
    [InlineData(49, "F")]
    [InlineData(0, "F")]
    public void ScoreToGrade_MapsCorrectly(int score, string expectedGrade)
    {
        Assert.Equal(expectedGrade, CollectionHealthMonitor.ScoreToGrade(score));
    }

    // ═══ GenerateFromCandidates ══════════════════════════════════════

    [Fact]
    public void GenerateFromCandidates_EmptyCandidates_ReturnsZeroScore()
    {
        var report = CollectionHealthMonitor.GenerateFromCandidates([]);

        Assert.Equal(0, report.HealthScore);
        Assert.Equal("F", report.Grade);
        Assert.Equal(0, report.Breakdown.TotalFiles);
        Assert.False(report.Integrity.HasBaseline);
    }

    [Fact]
    public void GenerateFromCandidates_PerfectCollection_ReturnsHighScore()
    {
        var candidates = new[]
        {
            MakeCandidate("Game1.zip", FileCategory.Game, datMatch: true),
            MakeCandidate("Game2.zip", FileCategory.Game, datMatch: true),
            MakeCandidate("Game3.zip", FileCategory.Game, datMatch: true)
        };

        var report = CollectionHealthMonitor.GenerateFromCandidates(candidates);

        Assert.True(report.HealthScore >= 90, $"Expected >= 90 but got {report.HealthScore}");
        Assert.Equal("A", report.Grade);
        Assert.Equal(3, report.Breakdown.TotalFiles);
        Assert.Equal(3, report.Breakdown.Games);
        Assert.Equal(0, report.Breakdown.Junk);
        Assert.Equal(3, report.Breakdown.DatVerified);
        Assert.Equal(100.0, report.Breakdown.VerifiedPercent);
    }

    [Fact]
    public void GenerateFromCandidates_WithJunk_ReducesScore()
    {
        var candidates = new[]
        {
            MakeCandidate("Game1.zip", FileCategory.Game, datMatch: true),
            MakeCandidate("Junk1.zip", FileCategory.Junk),
            MakeCandidate("Junk2.zip", FileCategory.Junk)
        };

        var report = CollectionHealthMonitor.GenerateFromCandidates(candidates);

        Assert.True(report.HealthScore < 100);
        Assert.Equal(2, report.Breakdown.Junk);
        Assert.True(report.Breakdown.JunkPercent > 60);
    }

    [Fact]
    public void GenerateFromCandidates_WithDuplicates_ReducesScore()
    {
        var winner = MakeCandidate("Game1.zip", FileCategory.Game, datMatch: true, consoleKey: "snes");
        var loser = MakeCandidate("Game1_dup.zip", FileCategory.Game, consoleKey: "snes");
        var candidates = new[] { winner, loser };
        var groups = new[]
        {
            new DedupeGroup { Winner = winner, Losers = [loser] }
        };

        var report = CollectionHealthMonitor.GenerateFromCandidates(candidates, groups);

        Assert.Equal(1, report.Breakdown.Duplicates);
        Assert.True(report.Breakdown.DuplicatePercent > 0);
    }

    [Fact]
    public void GenerateFromCandidates_WithConsoleFilter_OnlyCountsFilteredConsole()
    {
        var candidates = new[]
        {
            MakeCandidate("SNES_Game.zip", FileCategory.Game, datMatch: true, consoleKey: "snes"),
            MakeCandidate("NES_Game.zip", FileCategory.Game, datMatch: true, consoleKey: "nes"),
            MakeCandidate("NES_Junk.zip", FileCategory.Junk, consoleKey: "nes")
        };

        var report = CollectionHealthMonitor.GenerateFromCandidates(candidates, consoleFilter: "snes");

        Assert.Equal(1, report.Breakdown.TotalFiles);
        Assert.Equal(1, report.Breakdown.Games);
        Assert.Equal(0, report.Breakdown.Junk);
        Assert.Equal("snes", report.ConsoleFilter);
    }

    [Fact]
    public void GenerateFromCandidates_SetsGeneratedUtc()
    {
        var before = DateTime.UtcNow;
        var report = CollectionHealthMonitor.GenerateFromCandidates([MakeCandidate("g.zip", FileCategory.Game)]);
        var after = DateTime.UtcNow;

        Assert.InRange(report.GeneratedUtc, before, after);
    }

    [Fact]
    public void GenerateFromCandidates_IntegrityAlwaysNoBaseline()
    {
        var report = CollectionHealthMonitor.GenerateFromCandidates(
            [MakeCandidate("g.zip", FileCategory.Game, datMatch: true)]);

        Assert.False(report.Integrity.HasBaseline);
        Assert.Equal(0, report.Integrity.IntactCount);
        Assert.False(report.Integrity.BitRotRisk);
    }

    // ═══ GenerateReportAsync (no index) ═════════════════════════════

    [Fact]
    public async Task GenerateReportAsync_NoIndex_ReturnsEmptyReport()
    {
        var monitor = new CollectionHealthMonitor();

        var report = await monitor.GenerateReportAsync();

        Assert.Equal(0, report.HealthScore);
        Assert.Equal(0, report.Breakdown.TotalFiles);
    }

    [Fact]
    public async Task GenerateReportAsync_WithIndex_ConsoleFilter_LoadsByConsole()
    {
        var entries = new[]
        {
            MakeIndexEntry("Game1.zip", FileCategory.Game, datMatch: true),
            MakeIndexEntry("Game2.zip", FileCategory.Game, datMatch: false)
        };
        var index = new FakeCollectionIndex(entries);
        var monitor = new CollectionHealthMonitor(index);

        var report = await monitor.GenerateReportAsync(consoleFilter: "snes");

        Assert.Equal(2, report.Breakdown.TotalFiles);
        Assert.Equal(1, report.Breakdown.DatVerified);
        Assert.Equal(50.0, report.Breakdown.VerifiedPercent);
    }

    [Fact]
    public async Task GenerateReportAsync_WithIndex_Roots_LoadsByScope()
    {
        var entries = new[]
        {
            MakeIndexEntry("g1.zip", FileCategory.Game, datMatch: true),
            MakeIndexEntry("g2.zip", FileCategory.Game, datMatch: true),
            MakeIndexEntry("j.zip", FileCategory.Junk)
        };
        var index = new FakeCollectionIndex(entries);
        var monitor = new CollectionHealthMonitor(index);

        var report = await monitor.GenerateReportAsync(roots: [@"C:\roms"]);

        Assert.Equal(3, report.Breakdown.TotalFiles);
        Assert.Equal(1, report.Breakdown.Junk);
    }

    // ═══ Helpers ═════════════════════════════════════════════════════

    private static RomCandidate MakeCandidate(string name, FileCategory cat,
        bool datMatch = false, string consoleKey = "snes")
    {
        return new RomCandidate
        {
            MainPath = @$"C:\roms\{consoleKey}\{name}",
            GameKey = Path.GetFileNameWithoutExtension(name),
            ConsoleKey = consoleKey,
            Region = "US",
            Extension = Path.GetExtension(name),
            SizeBytes = 1024,
            Category = cat,
            DatMatch = datMatch
        };
    }

    private static CollectionIndexEntry MakeIndexEntry(string name, FileCategory cat, bool datMatch = false)
    {
        return new CollectionIndexEntry
        {
            Path = @$"C:\roms\snes\{name}",
            Extension = Path.GetExtension(name),
            SizeBytes = 1024,
            GameKey = Path.GetFileNameWithoutExtension(name),
            ConsoleKey = "snes",
            Category = cat,
            DatMatch = datMatch
        };
    }

    private sealed class FakeCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionIndexEntry> _entries;

        public FakeCollectionIndex(IReadOnlyList<CollectionIndexEntry> entries) => _entries = entries;

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => new(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => new(_entries.Count);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => new(_entries.FirstOrDefault(e => e.Path == path));

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => new(_entries.Where(e => paths.Contains(e.Path)).ToList());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => new(_entries.ToList());

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default)
            => new(_entries.ToList());

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default) => default;
        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => default;
        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default) => new((CollectionHashCacheEntry?)null);
        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default) => default;
        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default) => default;
        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default) => new(0);
        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default) => new((IReadOnlyList<CollectionRunSnapshot>)[]);
    }
}

public sealed class CliHealthSubcommandTests
{
    [Fact]
    public void Health_WithConsole_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse(["health", "--console", "snes"]);

        Assert.Equal(CliCommand.Health, result.Command);
        Assert.Equal("snes", result.Options!.ConsoleKey);
    }

    [Fact]
    public void Health_WithRoots_ParsesCorrectly()
    {
        var result = CliArgsParser.Parse(["health", "--roots", @"C:\roms\snes;C:\roms\nes"]);

        Assert.Equal(CliCommand.Health, result.Command);
        Assert.Equal(2, result.Options!.Roots.Length);
    }

    [Fact]
    public void Health_WithJson_ParsesExportFormat()
    {
        var result = CliArgsParser.Parse(["health", "--console", "snes", "--json"]);

        Assert.Equal(CliCommand.Health, result.Command);
        Assert.Equal("json", result.Options!.ExportFormat);
    }

    [Fact]
    public void Health_NoRootsOrConsole_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["health"]);

        Assert.NotEqual(0, result.ExitCode);
    }

    [Fact]
    public void Health_UnknownFlag_ReturnsValidationError()
    {
        var result = CliArgsParser.Parse(["health", "--unknown"]);

        Assert.NotEqual(0, result.ExitCode);
    }
}
