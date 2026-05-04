using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;
using System.Text.Json;
using Xunit;

namespace Romulus.Tests.Wpf;

public sealed class Wave7Wave8UiProjectionTests
{
    [Fact]
    public void DashboardProjection_DedupeGroupsExposeMultiDatConflict()
    {
        var selected = new DatMatch("NES", "Game", "game.nes", false, null, "SHA1", "no-intro");
        var resolution = new MultiDatResolution(
            SelectedMatch: selected,
            Candidates:
            [
                selected,
                new DatMatch("SNES", "Game", "game.sfc", false, null, "SHA1", "redump")
            ],
            IsConflict: true,
            Reason: "preferred-source",
            RankedCandidates: Array.Empty<MultiDatResolutionCandidate>());
        var winner = new RomCandidate
        {
            MainPath = @"C:\roms\winner.nes",
            GameKey = "game",
            Region = "USA",
            RegionScore = 100,
            FormatScore = 10,
            VersionScore = 1,
            Hash = "abcdef0123456789",
            MultiDatResolution = resolution,
            DecisionClass = DecisionClass.DatVerified,
            EvidenceTier = EvidenceTier.Tier0_ExactDat,
            PrimaryMatchKind = MatchKind.ExactDatHash,
            PlatformFamily = PlatformFamily.NoIntroCartridge
        };
        var result = new RunResult
        {
            TotalFilesScanned = 1,
            WinnerCount = 1,
            DedupeGroups =
            [
                new DedupeGroup
                {
                    GameKey = "game",
                    Winner = winner
                }
            ],
            AllCandidates = [winner]
        };

        var dashboard = DashboardProjection.From(
            RunProjectionFactory.Create(result),
            result,
            isConvertOnlyRun: false);

        var group = Assert.Single(dashboard.DedupeGroups);
        Assert.True(group.Winner.HasDatConflict);
        Assert.Equal("preferred-source", group.Winner.DatResolutionReason);
        Assert.Equal("abcdef0123456789", group.Winner.Fingerprint);
        Assert.Contains("NES/no-intro:Game", group.Winner.DatCandidatesSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void RunViewModel_ApplyProvenanceTrail_OpensSharedDrawerProjection()
    {
        var vm = new RunViewModel();
        var trail = new ProvenanceTrail(
            Fingerprint: "abcdef0123456789",
            IsValid: true,
            FailureReason: null,
            TrustScore: 80,
            Entries:
            [
                new ProvenanceEntry(
                    Fingerprint: "abcdef0123456789",
                    AuditRunId: "run-1",
                    EventKind: ProvenanceEventKind.Verified,
                    TimestampUtc: "2026-05-02T10:00:00.0000000Z",
                    Sha256: "abcdef0123456789",
                    ConsoleKey: "NES",
                    DatMatchId: "Nintendo - NES.dat",
                    Detail: "verified")
            ]);

        vm.ApplyProvenanceTrail(trail);

        Assert.True(vm.IsProvenanceDrawerOpen);
        Assert.Equal("Provenance abcdef0123456789", vm.ProvenanceTitle);
        Assert.Equal("Trust 80/100 · 1 Events", vm.ProvenanceStatus);
        var item = Assert.Single(vm.ProvenanceEntries);
        Assert.Equal("Verified", item.EventKind);
        Assert.Equal("Nintendo - NES.dat", item.DatMatchId);
    }

    [Fact]
    public async Task PolicyGovernanceViewModel_ValidatesWithSharedPolicyEngine()
    {
        var vm = new PolicyGovernanceViewModel(collectionIndex: new FakePolicyCollectionIndex(
        [
            new CollectionIndexEntry
            {
                Path = @"C:\roms\game.sfc",
                Root = @"C:\roms",
                FileName = "game.sfc",
                Extension = ".sfc",
                ConsoleKey = "SNES",
                GameKey = "game",
                Region = "US"
            }
        ]))
        {
            RootsText = @"C:\roms",
            ExtensionsText = ".sfc",
            PolicyText = """
                id: all-zip
                name: Alle ZIP
                allowedExtensions: [.zip]
                """
        };

        await vm.ValidateCommand.ExecuteAsync(null);

        Assert.False(vm.IsCompliant);
        Assert.Equal(1, vm.ViolationCount);
        Assert.Equal("allowed-extensions", Assert.Single(vm.Violations).RuleId);
    }

    [Fact]
    public async Task PolicyGovernanceViewModel_ExportPreservesReportMetadata()
    {
        var dir = Path.Combine(Path.GetTempPath(), "rom-wpf-policy-export-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var outputPath = Path.Combine(dir, "policy-report.json");
            var vm = new PolicyGovernanceViewModel(
                collectionIndex: new FakePolicyCollectionIndex(
                [
                    new CollectionIndexEntry
                    {
                        Path = @"C:\roms\game.sfc",
                        Root = @"C:\roms",
                        FileName = "game.sfc",
                        Extension = ".sfc",
                        ConsoleKey = "SNES",
                        GameKey = "game",
                        Region = "US"
                    }
                ]),
                dialog: new ExportDialog(outputPath))
            {
                RootsText = @"C:\roms",
                ExtensionsText = ".sfc",
                PolicyText = """
                    id: all-zip
                    name: Alle ZIP
                    allowedExtensions: [.zip]
                    """
            };

            await vm.ValidateCommand.ExecuteAsync(null);
            vm.ExportReportCommand.Execute(null);

            using var doc = JsonDocument.Parse(await File.ReadAllTextAsync(outputPath));
            Assert.Equal("all-zip", doc.RootElement.GetProperty("policyId").GetString());
            Assert.Equal("Alle ZIP", doc.RootElement.GetProperty("policyName").GetString());
            Assert.Equal(1, doc.RootElement.GetProperty("snapshot").GetProperty("totalEntries").GetInt32());
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private sealed class FakePolicyCollectionIndex : ICollectionIndex
    {
        private readonly IReadOnlyList<CollectionIndexEntry> _entries;

        public FakePolicyCollectionIndex(IReadOnlyList<CollectionIndexEntry> entries) => _entries = entries;
        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default) => new(new CollectionIndexMetadata());
        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default) => new(_entries.Count);
        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default) => new(_entries.FirstOrDefault(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase)));
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => new(_entries.Where(e => paths.Contains(e.Path, StringComparer.OrdinalIgnoreCase)).ToArray());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default) => new(_entries.Where(e => string.Equals(e.ConsoleKey, consoleKey, StringComparison.OrdinalIgnoreCase)).ToArray());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default) => new(_entries);
        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default) => default;
        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => default;
        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default) => new((CollectionHashCacheEntry?)null);
        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default) => default;
        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default) => default;
        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default) => new(0);
        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default) => new(Array.Empty<CollectionRunSnapshot>());
    }

    private sealed class ExportDialog(string savePath) : IDialogService
    {
        public string? BrowseFolder(string title = "") => null;
        public string? BrowseFile(string title = "", string filter = "") => null;
        public string? SaveFile(string title = "", string filter = "", string? defaultFileName = null) => savePath;
        public bool Confirm(string message, string title = "") => true;
        public void Info(string message, string title = "") { }
        public void Error(string message, string title = "") { }
        public ConfirmResult YesNoCancel(string message, string title = "") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }
}
