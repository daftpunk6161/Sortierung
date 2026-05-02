using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.ViewModels;
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
}
