using Romulus.Contracts.Models;
using Romulus.Core.Audit;
using Xunit;

namespace Romulus.Tests;

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

        // headerless doesn't match → falls through to regular hash → Have
        Assert.Equal(DatAuditStatus.Have, status);
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
    public void Classify_KnownConsole_NoDatLoaded_ReturnsUnknown()
    {
        var index = new DatIndex();
        index.Add("SNES", "hash1", "GameSnes", "game.sfc");

        var status = DatAuditClassifier.Classify(
            hash: "hash1",
            actualFileName: "game.nes",
            consoleKey: "NES",
            datIndex: index);

        // NES has no DAT loaded → Unknown (not Miss)
        Assert.Equal(DatAuditStatus.Unknown, status);
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

    // ── ClassifyFull – richer result ─────────────────────────────

    [Fact]
    public void ClassifyFull_KnownConsole_ReturnsMatchedEntryDetails()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Contra", "Contra (USA).nes");

        var result = DatAuditClassifier.ClassifyFull("hash1", null, "Contra (USA).nes", "NES", index);

        Assert.Equal(DatAuditStatus.Have, result.Status);
        Assert.Equal("Contra", result.DatGameName);
        Assert.Equal("Contra (USA).nes", result.DatRomFileName);
        Assert.Equal("NES", result.ResolvedConsoleKey);
    }

    [Fact]
    public void ClassifyFull_WrongName_ReturnsExpectedRomFileName()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Contra", "Contra (USA).nes");

        var result = DatAuditClassifier.ClassifyFull("hash1", null, "wrong.zip", "NES", index);

        Assert.Equal(DatAuditStatus.HaveWrongName, result.Status);
        Assert.Equal("Contra (USA).nes", result.DatRomFileName);
    }

    [Fact]
    public void ClassifyFull_UnknownConsole_SingleMatch_ResolvesConsoleKey()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Contra", "Contra (USA).nes");

        var result = DatAuditClassifier.ClassifyFull("hash1", null, "Contra (USA).nes", null, index);

        Assert.Equal(DatAuditStatus.Have, result.Status);
        Assert.Equal("NES", result.ResolvedConsoleKey);
        Assert.Equal("Contra", result.DatGameName);
    }

    [Fact]
    public void ClassifyFull_Miss_ReturnsNullDetails()
    {
        var index = new DatIndex();
        index.Add("NES", "hash1", "Contra", "Contra (USA).nes");

        var result = DatAuditClassifier.ClassifyFull("no-match", null, "file.nes", "NES", index);

        Assert.Equal(DatAuditStatus.Miss, result.Status);
        Assert.Null(result.DatGameName);
        Assert.Null(result.DatRomFileName);
    }

    [Fact]
    public void ClassifyFull_HeaderlessHash_ReturnsMatchedDetails()
    {
        var index = new DatIndex();
        index.Add("NES", "headerless-hash", "Contra", "Contra (USA).nes");

        var result = DatAuditClassifier.ClassifyFull(
            hash: "regular-hash",
            headerlessHash: "headerless-hash",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, result.Status);
        Assert.Equal("Contra", result.DatGameName);
    }
}
