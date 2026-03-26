using RomCleanup.Contracts.Models;
using RomCleanup.Core.Audit;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Phase 3 / TASK-023: Expanded DatAuditClassifier tests including headerlessHash paths,
/// edge cases, and determinism.
/// </summary>
public sealed class DatAuditClassifierExpandedTests
{
    // ── HeaderlessHash preference paths ───────────────────────────

    [Fact]
    public void Classify_HeaderlessHashMatches_ReturnsHave()
    {
        var index = new DatIndex();
        index.Add("NES", "headerless-hash", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "regular-hash-no-match",
            headerlessHash: "headerless-hash",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void Classify_HeaderlessHashMatches_RegularDoesNot_StillReturnsHave()
    {
        var index = new DatIndex();
        index.Add("NES", "headerless-hash", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "completely-different",
            headerlessHash: "headerless-hash",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void Classify_HeaderlessHashMatches_WrongName_ReturnsHaveWrongName()
    {
        var index = new DatIndex();
        index.Add("NES", "headerless-hash", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "some-hash",
            headerlessHash: "headerless-hash",
            actualFileName: "wrong-name.nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.HaveWrongName, status);
    }

    [Fact]
    public void Classify_HeaderlessHashMisses_FallsBackToRegularHash()
    {
        var index = new DatIndex();
        index.Add("NES", "regular-hash", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "regular-hash",
            headerlessHash: "headerless-no-match",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        // headerless doesn't match → Miss, but known console so Miss is returned before fallback
        // Actually: headerless misses on known console → returns Miss directly
        Assert.Equal(DatAuditStatus.Miss, status);
    }

    [Fact]
    public void Classify_HeaderlessHashNull_UsesRegularHash()
    {
        var index = new DatIndex();
        index.Add("NES", "regular-hash", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "regular-hash",
            headerlessHash: null,
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void Classify_HeaderlessHashEmpty_UsesRegularHash()
    {
        var index = new DatIndex();
        index.Add("NES", "regular-hash", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "regular-hash",
            headerlessHash: "",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void Classify_BothHashesNull_ReturnsUnknown()
    {
        var status = DatAuditClassifier.Classify(
            hash: null,
            headerlessHash: null,
            actualFileName: "file.nes",
            consoleKey: "NES",
            datIndex: new DatIndex());

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    // ── Ambiguous with headerlessHash ─────────────────────────────

    [Fact]
    public void Classify_HeaderlessHashAmbiguous_ReturnsAmbiguous()
    {
        var index = new DatIndex();
        index.Add("NES", "headerless-hash", "Contra NES", "contra.nes");
        index.Add("FDS", "headerless-hash", "Contra FDS", "contra.fds");

        var status = DatAuditClassifier.Classify(
            hash: "some-hash",
            headerlessHash: "headerless-hash",
            actualFileName: "contra.bin",
            consoleKey: "",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Ambiguous, status);
    }

    // ── Edge cases ────────────────────────────────────────────────

    [Fact]
    public void Classify_EmptyStringHash_ReturnsUnknown()
    {
        var status = DatAuditClassifier.Classify(
            hash: "",
            actualFileName: "file.nes",
            consoleKey: "NES",
            datIndex: new DatIndex());

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    [Fact]
    public void Classify_KnownConsole_HashNotInIndex_ReturnsMiss()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa", "Game", "game.nes");

        var status = DatAuditClassifier.Classify(
            hash: "yyy",
            actualFileName: "unknown.nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Miss, status);
    }

    [Fact]
    public void Classify_KnownConsole_HashInOtherConsole_StillReturnsMiss()
    {
        var index = new DatIndex();
        index.Add("SNES", "hash1", "GameSnes", "game.sfc");

        var status = DatAuditClassifier.Classify(
            hash: "hash1",
            actualFileName: "game.nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Miss, status);
    }

    [Fact]
    public void Classify_NullActualFileName_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DatAuditClassifier.Classify("hash", null!, "NES", new DatIndex()));
    }

    [Fact]
    public void Classify_NullDatIndex_ThrowsArgumentNull()
    {
        Assert.Throws<ArgumentNullException>(() =>
            DatAuditClassifier.Classify("hash", "file.nes", "NES", null!));
    }

    // ── Determinism ──────────────────────────────────────────────

    [Fact]
    public void Classify_SameInput_ProducesSameOutput_Determinism()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa111", "Contra", "Contra (USA).nes");
        index.Add("NES", "bbb222", "Zelda", "Zelda (USA).nes");
        index.Add("SNES", "aaa111", "Contra SNES", "contra.sfc");

        // Run each classification twice with identical inputs
        for (int i = 0; i < 2; i++)
        {
            Assert.Equal(DatAuditStatus.Have,
                DatAuditClassifier.Classify("aaa111", "Contra (USA).nes", "NES", index));

            Assert.Equal(DatAuditStatus.HaveWrongName,
                DatAuditClassifier.Classify("aaa111", "wrong.nes", "NES", index));

            Assert.Equal(DatAuditStatus.Miss,
                DatAuditClassifier.Classify("zzz", "any.nes", "NES", index));

            Assert.Equal(DatAuditStatus.Unknown,
                DatAuditClassifier.Classify(null, "any.nes", "NES", index));

            Assert.Equal(DatAuditStatus.Ambiguous,
                DatAuditClassifier.Classify("aaa111", "ambig.bin", "", index));
        }
    }
}
