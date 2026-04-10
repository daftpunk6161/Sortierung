using Romulus.Contracts.Models;
using Romulus.Core.Audit;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Regression tests for DatAuditClassifier bugs:
/// - ZIP filename vs inner ROM filename comparison (WrongName inflation)
/// - UNKNOWN/AMBIGUOUS sentinel values treated as real console keys (Miss inflation)
/// </summary>
public sealed class DatAuditClassifierRegressionTests
{
    // ── Bug Fix: ZIP filename vs inner ROM filename ──

    [Fact]
    public void ZipArchive_SameStem_DifferentExtension_ReturnsHave()
    {
        // ZIP file "Super Mario Bros (USA).zip" with inner ROM "Super Mario Bros (USA).nes"
        // Hash matches inner ROM → should be Have, not WrongName
        var index = new DatIndex();
        index.Add("NES", "abc123", "Super Mario Bros", "Super Mario Bros (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "Super Mario Bros (USA).zip",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void SevenZipArchive_SameStem_DifferentExtension_ReturnsHave()
    {
        var index = new DatIndex();
        index.Add("SNES", "def456", "Zelda", "Zelda (USA).sfc");

        var status = DatAuditClassifier.Classify(
            hash: "def456",
            actualFileName: "Zelda (USA).7z",
            consoleKey: "SNES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void ZipArchive_DifferentStem_StillReturnsWrongName()
    {
        // Genuinely different name: "smb.zip" vs "Super Mario Bros (USA).nes"
        var index = new DatIndex();
        index.Add("NES", "abc123", "Super Mario Bros", "Super Mario Bros (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "smb.zip",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.HaveWrongName, status);
    }

    [Fact]
    public void ExactMatch_StillReturnsHave()
    {
        // Uncompressed ROM: exact filename match
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void NullDatRomFileName_ReturnsHave()
    {
        // DAT entry without ROM filename → always treated as name match
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "anything.zip",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void ZipArchive_NoConsoleKey_SingleMatch_SameStem_ReturnsHave()
    {
        // Unknown console, but hash uniquely matches one DAT entry
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "Contra (USA).zip",
            consoleKey: "",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    // ── Bug Fix: UNKNOWN/AMBIGUOUS sentinel values ──

    [Fact]
    public void UnknownConsoleKey_NoHashMatch_ReturnsUnknown_NotMiss()
    {
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "zzz999",
            actualFileName: "SomeGame.zip",
            consoleKey: "UNKNOWN",
            datIndex: index);

        // Previously returned Miss because "UNKNOWN" was treated as a real console
        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    [Fact]
    public void AmbiguousConsoleKey_NoHashMatch_ReturnsUnknown_NotMiss()
    {
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "zzz999",
            actualFileName: "SomeGame.zip",
            consoleKey: "AMBIGUOUS",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    [Fact]
    public void UnknownConsoleKey_HashMatchesOneConsole_ReturnsHave()
    {
        // UNKNOWN console but hash found in exactly one console → Have
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "Contra (USA).zip",
            consoleKey: "UNKNOWN",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void AmbiguousConsoleKey_HashMatchesMultipleConsoles_ReturnsAmbiguous()
    {
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra NES", "Contra (USA).nes");
        index.Add("FDS", "abc123", "Contra FDS", "Contra (USA).fds");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "Contra (USA).zip",
            consoleKey: "AMBIGUOUS",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Ambiguous, status);
    }

    [Fact]
    public void UnknownConsoleKey_CaseInsensitive()
    {
        var index = new DatIndex();

        var status = DatAuditClassifier.Classify(
            hash: "zzz999",
            actualFileName: "Game.zip",
            consoleKey: "unknown",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    // ── Headerless hash with UNKNOWN sentinel ──

    [Fact]
    public void HeaderlessHash_UnknownConsole_DoesNotReturnFalseMiss()
    {
        var index = new DatIndex();
        index.Add("NES", "headerless", "Game", "Game.nes");

        var status = DatAuditClassifier.Classify(
            hash: "regular",
            headerlessHash: "no-match",
            actualFileName: "Game.zip",
            consoleKey: "UNKNOWN",
            datIndex: index);

        // Should NOT be Miss — UNKNOWN is not a real console
        Assert.NotEqual(DatAuditStatus.Miss, status);
    }

    [Fact]
    public void RealConsoleKey_MissingHash_StillReturnsMiss()
    {
        // Verify real console keys still correctly return Miss when DAT IS loaded
        var index = new DatIndex();
        index.Add("NES", "abc123", "Contra", "Contra.nes");

        var status = DatAuditClassifier.Classify(
            hash: "different",
            actualFileName: "Game.nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Miss, status);
    }

    // ── Bug Fix: Console with no DAT → Unknown, not Miss ──

    [Fact]
    public void RealConsoleKey_NoDatLoaded_ReturnsUnknown_NotMiss()
    {
        // NES detected but no NES DAT loaded — cannot claim Miss
        var index = new DatIndex();
        index.Add("SNES", "abc123", "Zelda", "Zelda.sfc");

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "Game.zip",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    [Fact]
    public void EmptyDatIndex_RealConsole_ReturnsUnknown()
    {
        var index = new DatIndex();

        var status = DatAuditClassifier.Classify(
            hash: "abc123",
            actualFileName: "Game.zip",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    [Fact]
    public void RealConsole_DatLoaded_HashNotFound_ReturnsMiss()
    {
        // Confirm: when DAT IS loaded for this console, a missing hash is a real Miss
        var index = new DatIndex();
        index.Add("NES", "known-hash", "Contra", "Contra.nes");

        var status = DatAuditClassifier.Classify(
            hash: "different-hash",
            actualFileName: "Game.zip",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Miss, status);
    }

    // ── Bug Fix: Headerless Miss falls through to regular hash ──

    [Fact]
    public void HeaderlessMiss_RegularHashMatches_ReturnsHave()
    {
        // DAT uses headered SHA1; headerless hash doesn't match, but regular hash does
        var index = new DatIndex();
        index.Add("NES", "headered-sha1", "Super Mario Bros", "Super Mario Bros (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "headered-sha1",
            headerlessHash: "headerless-sha1-no-match",
            actualFileName: "Super Mario Bros (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void HeaderlessMiss_RegularHashAlsoMiss_ReturnsMiss()
    {
        // Both hashes fail for a console with DAT → genuine Miss
        var index = new DatIndex();
        index.Add("NES", "some-hash", "Contra", "Contra.nes");

        var status = DatAuditClassifier.Classify(
            hash: "other-hash",
            headerlessHash: "another-hash",
            actualFileName: "Game.nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Miss, status);
    }

    [Fact]
    public void HeaderlessMiss_NoDatForConsole_ReturnsUnknown()
    {
        // Headerless misses AND console has no DAT → Unknown
        var index = new DatIndex();
        index.Add("SNES", "hash1", "Zelda", "Zelda.sfc");

        var status = DatAuditClassifier.Classify(
            hash: "hash2",
            headerlessHash: "hash3",
            actualFileName: "Game.nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    [Fact]
    public void HeaderlessHave_ReturnsHave_WithoutTryingRegular()
    {
        // Headerless matches → return immediately
        var index = new DatIndex();
        index.Add("NES", "headerless-hash", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "regular-hash-different",
            headerlessHash: "headerless-hash",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }
}
