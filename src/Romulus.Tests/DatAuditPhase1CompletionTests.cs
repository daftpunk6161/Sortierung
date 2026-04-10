using Romulus.Contracts.Models;
using Romulus.Core.Audit;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Phase 1 / TASK-009: Completion tests for DatAuditStatus enum, DatIndex lookups, and model invariants.
/// </summary>
public sealed class DatAuditPhase1CompletionTests
{
    // ── DatAuditStatus enum completeness ──────────────────────────

    [Fact]
    public void DatAuditStatus_HasExactlyFiveMembers()
    {
        var values = Enum.GetValues<DatAuditStatus>();
        Assert.Equal(5, values.Length);
    }

    [Theory]
    [InlineData(DatAuditStatus.Have)]
    [InlineData(DatAuditStatus.HaveWrongName)]
    [InlineData(DatAuditStatus.Miss)]
    [InlineData(DatAuditStatus.Unknown)]
    [InlineData(DatAuditStatus.Ambiguous)]
    public void DatAuditStatus_ContainsExpectedMember(DatAuditStatus status)
    {
        Assert.True(Enum.IsDefined(status));
    }

    // ── DatIndex.LookupWithFilename edge cases ────────────────────

    [Fact]
    public void LookupWithFilename_NonExistentHash_ReturnsNull()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa111", "Contra", "Contra (USA).nes");

        var result = index.LookupWithFilename("NES", "not-there");
        Assert.Null(result);
    }

    [Fact]
    public void LookupWithFilename_NonExistentConsole_ReturnsNull()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa111", "Contra", "Contra (USA).nes");

        var result = index.LookupWithFilename("SNES", "aaa111");
        Assert.Null(result);
    }

    [Fact]
    public void LookupWithFilename_CrossConsole_ReturnsCorrectPerConsole()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Contra NES", "Contra (USA).nes");
        index.Add("SNES", "hash1", "Contra SNES", "Contra (USA).sfc");

        var nes = index.LookupWithFilename("NES", "hash1");
        var snes = index.LookupWithFilename("SNES", "hash1");

        Assert.NotNull(nes);
        Assert.Equal("Contra NES", nes.Value.GameName);
        Assert.Equal("Contra (USA).nes", nes.Value.RomFileName);

        Assert.NotNull(snes);
        Assert.Equal("Contra SNES", snes.Value.GameName);
        Assert.Equal("Contra (USA).sfc", snes.Value.RomFileName);
    }

    [Fact]
    public void LookupWithFilename_WithNullRomFileName_StillReturnsEntry()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Contra");

        var result = index.LookupWithFilename("NES", "hash1");
        Assert.NotNull(result);
        Assert.Equal("Contra", result.Value.GameName);
        Assert.Null(result.Value.RomFileName);
    }

    // ── DatAuditEntry construction ────────────────────────────────

    [Fact]
    public void DatAuditEntry_AllFieldsRoundTrip()
    {
        var entry = new DatAuditEntry(
            FilePath: @"C:\roms\NES\contra.nes",
            Hash: "abc123",
            Status: DatAuditStatus.Have,
            DatGameName: "Contra",
            DatRomFileName: "Contra (USA).nes",
            ConsoleKey: "NES",
            Confidence: 100);

        Assert.Equal(@"C:\roms\NES\contra.nes", entry.FilePath);
        Assert.Equal("abc123", entry.Hash);
        Assert.Equal(DatAuditStatus.Have, entry.Status);
        Assert.Equal("Contra", entry.DatGameName);
        Assert.Equal("Contra (USA).nes", entry.DatRomFileName);
        Assert.Equal("NES", entry.ConsoleKey);
        Assert.Equal(100, entry.Confidence);
    }

    // ── DatAuditResult construction ───────────────────────────────

    [Fact]
    public void DatAuditResult_CountsAndEntries_RoundTrip()
    {
        var entries = new[]
        {
            new DatAuditEntry("a.nes", "h1", DatAuditStatus.Have, null, null, "NES", 100),
            new DatAuditEntry("b.nes", "h2", DatAuditStatus.Miss, null, null, "NES", 50)
        };

        var result = new DatAuditResult(
            Entries: entries,
            HaveCount: 1,
            HaveWrongNameCount: 0,
            MissCount: 1,
            UnknownCount: 0,
            AmbiguousCount: 0);

        Assert.Equal(2, result.Entries.Count);
        Assert.Equal(1, result.HaveCount);
        Assert.Equal(1, result.MissCount);
    }

    // ── DatRenameProposal construction ────────────────────────────

    [Fact]
    public void DatRenameProposal_FieldsRoundTrip()
    {
        var proposal = new DatRenameProposal(
            SourcePath: @"C:\roms\bad.nes",
            TargetFileName: "Contra (USA).nes",
            Status: DatAuditStatus.HaveWrongName,
            ConflictReason: null);

        Assert.Equal(@"C:\roms\bad.nes", proposal.SourcePath);
        Assert.Equal("Contra (USA).nes", proposal.TargetFileName);
        Assert.Equal(DatAuditStatus.HaveWrongName, proposal.Status);
        Assert.Null(proposal.ConflictReason);
    }

    // ── LookupAllByHash ──────────────────────────────────────────

    [Fact]
    public void LookupAllByHash_NonExistentHash_ReturnsEmpty()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Game1");

        var result = index.LookupAllByHash("not-there");
        Assert.Empty(result);
    }

    [Fact]
    public void LookupAllByHash_MultipleConsoles_ReturnsAll()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Game NES", "game.nes");
        index.Add("FDS", "hash1", "Game FDS", "game.fds");

        var result = index.LookupAllByHash("hash1");
        Assert.Equal(2, result.Count);
    }
}
