using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

public class FileClassifierTests
{
    // ── BIOS detection ──────────────────────────────────────────────────

    [Theory]
    [InlineData("[BIOS] PlayStation (Europe)")]
    [InlineData("BIOS - something")]
    [InlineData("bios test rom")]
    [InlineData("Sony PS2 (Firmware)")]
    [InlineData("Game Boy Advance (BIOS)")]
    public void Bios_DetectedCorrectly(string name)
    {
        Assert.Equal(FileCategory.Bios, FileClassifier.Classify(name));
    }

    // ── Standard junk tags ──────────────────────────────────────────────

    [Theory]
    [InlineData("Game (Demo)")]
    [InlineData("Game (Beta)")]
    [InlineData("Game (Beta 2)")]
    [InlineData("Game (Alpha)")]
    [InlineData("Game (Proto)")]
    [InlineData("Game (Prototype)")]
    [InlineData("Game (Sample)")]
    [InlineData("Game (Preview)")]
    [InlineData("Game (Pre-Release)")]
    [InlineData("Game (Promo)")]
    [InlineData("Game (Kiosk Demo)")]
    [InlineData("Game (Kiosk)")]
    [InlineData("Game (Debug)")]
    [InlineData("Game (Trial)")]
    [InlineData("Game (Trial Version)")]
    [InlineData("Game (Hack)")]
    [InlineData("Game (Pirate)")]
    [InlineData("Game (Homebrew)")]
    [InlineData("Game (Aftermarket)")]
    [InlineData("Game (Bootleg)")]
    [InlineData("Game (Translated)")]
    [InlineData("Game (Translation)")]
    [InlineData("Game (Unl)")]
    [InlineData("Game (Unlicensed)")]
    [InlineData("Game (Not for Resale)")]
    [InlineData("Game (NFR)")]
    [InlineData("Game (Program)")]
    [InlineData("Game (Application)")]
    [InlineData("Game (Utility)")]
    [InlineData("Game (Enhancement Chip)")]
    [InlineData("Game (Test Program)")]
    [InlineData("Game (Test Cartridge)")]
    [InlineData("Game (Competition Cart)")]
    [InlineData("Game (Service Disc)")]
    [InlineData("Game (Diagnostic)")]
    [InlineData("Game (Check Program)")]
    [InlineData("Game (Taikenban)")]
    [InlineData("Game (Location Test)")]
    public void JunkTags_Standard_DetectedAsJunk(string name)
    {
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify(name));
    }

    // ── Bracket junk tags ───────────────────────────────────────────────

    [Theory]
    [InlineData("Game [b]")]
    [InlineData("Game [b1]")]
    [InlineData("Game [h]")]
    [InlineData("Game [h2]")]
    [InlineData("Game [p]")]
    [InlineData("Game [t1]")]
    [InlineData("Game [f]")]
    [InlineData("Game [o]")]
    [InlineData("Game [cr ")]
    [InlineData("Game [tr ")]
    [InlineData("Game [m ")]
    public void JunkBracketTags_DetectedAsJunk(string name)
    {
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify(name));
    }

    // ── Standard junk words ─────────────────────────────────────────────

    [Theory]
    [InlineData("Some demo game")]
    [InlineData("Game trial version")]
    [InlineData("Game sample version")]
    [InlineData("not for resale edition")]
    [InlineData("gamelist.xml")]
    [InlineData("gamelist.xml.old")]
    [InlineData("gamelist.xml.bak")]
    public void JunkWords_Standard_DetectedAsJunk(string name)
    {
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify(name));
    }

    // ── Aggressive junk ─────────────────────────────────────────────────

    [Theory]
    [InlineData("Game (WIP)")]
    [InlineData("Game (Work in Progress)")]
    [InlineData("Game (Dev Build)")]
    [InlineData("Game (QA Build)")]
    [InlineData("Game (Review Build)")]
    [InlineData("Game (Internal Build)")]
    [InlineData("Game (Preview Build)")]
    [InlineData("Game (Prototype Build)")]
    [InlineData("Game (Not for Distribution)")]
    [InlineData("Game (Playtest)")]
    [InlineData("Game (Test Build)")]
    public void JunkTagsAggressive_OnlyWithFlag(string name)
    {
        // Without aggressive flag → GAME
        Assert.Equal(FileCategory.Game, FileClassifier.Classify(name, aggressiveJunk: false));
        // With aggressive flag → JUNK
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify(name, aggressiveJunk: true));
    }

    [Theory]
    [InlineData("work in progress game")]
    [InlineData("wip game")]
    [InlineData("playtest version")]
    [InlineData("dev build release")]
    public void JunkWordsAggressive_OnlyWithFlag(string name)
    {
        Assert.Equal(FileCategory.Game, FileClassifier.Classify(name, aggressiveJunk: false));
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify(name, aggressiveJunk: true));
    }

    // ── Normal games (no false positives) ───────────────────────────────

    [Theory]
    [InlineData("Super Mario World (Europe)")]
    [InlineData("Zelda - A Link to the Past (USA)")]
    [InlineData("Final Fantasy VII (Japan)")]
    [InlineData("Crash Bandicoot (Europe) (En,Fr,De,Es,It)")]
    [InlineData("Metal Gear Solid (USA) (Disc 1)")]
    [InlineData("Gran Turismo 2 (Europe) (v1.1)")]
    [InlineData("Tekken 3 (USA) (Rev 1)")]
    [InlineData("Wipeout (Europe) [!]")]
    public void NormalGames_NotFlaggedAsJunk(string name)
    {
        Assert.Equal(FileCategory.Game, FileClassifier.Classify(name));
    }

    [Theory]
    [InlineData("Workbench (System Disk)")]
    [InlineData("Workbench (Operating System)")]
    public void NonGameTags_AreDetected(string name)
    {
        var decision = FileClassifier.Analyze(name);

        Assert.Equal(FileCategory.NonGame, decision.Category);
        Assert.Equal("non-game-tag", decision.ReasonCode);
        Assert.True(decision.Confidence >= 80);
    }

    [Fact]
    public void NonGameWords_AreDetected()
    {
        var decision = FileClassifier.Analyze("Amiga workbench utility pack");

        Assert.Equal(FileCategory.NonGame, decision.Category);
        Assert.Equal("non-game-word", decision.ReasonCode);
        Assert.True(decision.Confidence >= 70);
    }

    [Theory]
    [InlineData("Readme", ".txt")]
    [InlineData("Config", ".json")]
    [InlineData("Screenshot", ".png")]
    [InlineData("Script", ".ps1")]
    public void NonRomExtensions_AreDetectedAsNonGame(string baseName, string extension)
    {
        var decision = FileClassifier.Analyze(baseName, extension, sizeBytes: 1024, aggressiveJunk: false);

        Assert.Equal(FileCategory.NonGame, decision.Category);
        Assert.Equal("non-rom-extension", decision.ReasonCode);
        Assert.True(decision.Confidence >= 95);
    }

    [Fact]
    public void ZeroByteFile_IsDetectedAsNonGame()
    {
        var decision = FileClassifier.Analyze("Some Game", ".bin", sizeBytes: 0, aggressiveJunk: false);

        Assert.Equal(FileCategory.NonGame, decision.Category);
        Assert.Equal("empty-file", decision.ReasonCode);
        Assert.True(decision.Confidence >= 95);
    }

    [Theory]
    [InlineData("data001", ".bin")]
    [InlineData("track01", ".bin")]
    [InlineData("disc1", ".img")]
    public void GenericRawBinaryNames_AreNotPromotedToGame(string baseName, string extension)
    {
        var decision = FileClassifier.Analyze(baseName, extension, sizeBytes: 1024, aggressiveJunk: false);

        Assert.Equal(FileCategory.Unknown, decision.Category);
        Assert.Equal("generic-raw-binary", decision.ReasonCode);
    }

    // ── Edge cases ──────────────────────────────────────────────────────

    [Fact]
    public void EmptyString_ReturnsUnknown()
    {
        Assert.Equal(FileCategory.Unknown, FileClassifier.Classify(""));
    }

    [Fact]
    public void Whitespace_ReturnsUnknown()
    {
        Assert.Equal(FileCategory.Unknown, FileClassifier.Classify("   "));
    }

    [Fact]
    public void BiosBeatsJunk_WhenBothMatch()
    {
        // A BIOS that also contains junk tags → BIOS wins (checked first)
        Assert.Equal(FileCategory.Bios, FileClassifier.Classify("[BIOS] PS2 (Debug)"));
    }

    [Fact]
    public void CaseInsensitive()
    {
        Assert.Equal(FileCategory.Bios, FileClassifier.Classify("(BIOS)"));
        Assert.Equal(FileCategory.Bios, FileClassifier.Classify("(bios)"));
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify("Game (DEMO)"));
        Assert.Equal(FileCategory.Junk, FileClassifier.Classify("Game (demo)"));
    }
}
