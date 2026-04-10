using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for RunEnvironmentBuilder.FindExactStemMatch
/// and ConsoleSorter path helpers (IsInExcludedFolder, IsPathWithinRoot).
/// </summary>
public sealed class PathAndStemHelperCoverageTests
{
    // ── FindExactStemMatch ──────────────────────────────────────────

    [Fact]
    public void FindExactStemMatch_EmptyFiles_ReturnsNull()
    {
        var result = RunEnvironmentBuilder.FindExactStemMatch(Array.Empty<string>(), "Nintendo - NES");
        Assert.Null(result);
    }

    [Fact]
    public void FindExactStemMatch_NoMatch_ReturnsNull()
    {
        var files = new[] { @"C:\dats\SNES.dat", @"C:\dats\GBA.xml" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "NES");
        Assert.Null(result);
    }

    [Fact]
    public void FindExactStemMatch_ExactMatch_ReturnsPath()
    {
        var files = new[] { @"C:\dats\NES.dat", @"C:\dats\SNES.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "NES");
        Assert.Equal(@"C:\dats\NES.dat", result);
    }

    [Fact]
    public void FindExactStemMatch_CaseInsensitive()
    {
        var files = new[] { @"C:\dats\nintendo-nes.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "Nintendo-NES");
        Assert.Equal(@"C:\dats\nintendo-nes.dat", result);
    }

    [Fact]
    public void FindExactStemMatch_MultipleStems_FirstMatchWins()
    {
        var files = new[] { @"C:\dats\alias.dat", @"C:\dats\primary.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "primary", "alias");
        Assert.Equal(@"C:\dats\primary.dat", result);
    }

    [Fact]
    public void FindExactStemMatch_SecondStemMatches_WhenFirstDoesNot()
    {
        var files = new[] { @"C:\dats\alias.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "nonexistent", "alias");
        Assert.Equal(@"C:\dats\alias.dat", result);
    }

    [Fact]
    public void FindExactStemMatch_WhitespaceStems_Skipped()
    {
        var files = new[] { @"C:\dats\real.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "", "  ", "real");
        Assert.Equal(@"C:\dats\real.dat", result);
    }

    [Fact]
    public void FindExactStemMatch_AllStemsEmpty_ReturnsNull()
    {
        var files = new[] { @"C:\dats\test.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "", "  ");
        Assert.Null(result);
    }

    [Fact]
    public void FindExactStemMatch_XmlExtension_AlsoMatches()
    {
        var files = new[] { @"C:\dats\MAME.xml" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "MAME");
        Assert.Equal(@"C:\dats\MAME.xml", result);
    }

    [Fact]
    public void FindExactStemMatch_PartialStemName_DoesNotMatch()
    {
        var files = new[] { @"C:\dats\SNES-Subset.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "SNES");
        Assert.Null(result);
    }

    [Fact]
    public void FindExactStemMatch_DuplicateStems_DedupedMatchStillWorks()
    {
        var files = new[] { @"C:\dats\NES.dat" };
        var result = RunEnvironmentBuilder.FindExactStemMatch(files, "NES", "NES");
        Assert.Equal(@"C:\dats\NES.dat", result);
    }

    // ── IsInExcludedFolder ──────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\roms\_TRASH_REGION_DEDUPE\file.zip", @"C:\roms")]
    [InlineData(@"C:\roms\_TRASH_JUNK\file.zip", @"C:\roms")]
    [InlineData(@"C:\roms\_REVIEW\file.zip", @"C:\roms")]
    [InlineData(@"C:\roms\_BLOCKED\file.zip", @"C:\roms")]
    [InlineData(@"C:\roms\_UNKNOWN\file.zip", @"C:\roms")]
    public void IsInExcludedFolder_KnownExcluded_ReturnsTrue(string filePath, string root)
    {
        Assert.True(ConsoleSorter.IsInExcludedFolder(filePath, root));
    }

    [Theory]
    [InlineData(@"C:\roms\Nintendo\game.zip", @"C:\roms")]
    [InlineData(@"C:\roms\file.zip", @"C:\roms")]
    public void IsInExcludedFolder_NormalFolder_ReturnsFalse(string filePath, string root)
    {
        Assert.False(ConsoleSorter.IsInExcludedFolder(filePath, root));
    }

    [Fact]
    public void IsInExcludedFolder_CaseInsensitive()
    {
        Assert.True(ConsoleSorter.IsInExcludedFolder(@"C:\roms\_trash_region_dedupe\file.zip", @"C:\roms"));
    }

    [Theory]
    [InlineData(@"C:\roms\_BIOS\file.zip", @"C:\roms")]
    [InlineData(@"C:\roms\_JUNK\file.zip", @"C:\roms")]
    public void IsInExcludedFolder_AdditionalExcluded_ReturnsTrue(string filePath, string root)
    {
        Assert.True(ConsoleSorter.IsInExcludedFolder(filePath, root));
    }

    [Fact]
    public void IsInExcludedFolder_TrashConverted_NotExcluded()
    {
        // _TRASH_CONVERTED is NOT in the sort exclusion list
        Assert.False(ConsoleSorter.IsInExcludedFolder(@"C:\roms\_TRASH_CONVERTED\file.zip", @"C:\roms"));
    }

    // ── IsPathWithinRoot ────────────────────────────────────────────

    [Fact]
    public void IsPathWithinRoot_ValidChild_ReturnsTrue()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "PathTest_" + Guid.NewGuid().ToString("N")[..8]);
        var childFile = Path.Combine(tmpDir, "sub", "file.zip");
        Directory.CreateDirectory(Path.Combine(tmpDir, "sub"));
        try
        {
            File.WriteAllBytes(childFile, Array.Empty<byte>());
            Assert.True(ConsoleSorter.IsPathWithinRoot(childFile, tmpDir));
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void IsPathWithinRoot_OutsideRoot_ReturnsFalse()
    {
        Assert.False(ConsoleSorter.IsPathWithinRoot(@"D:\other\file.zip", @"C:\roms"));
    }

    [Fact]
    public void IsPathWithinRoot_NullFilePath_ReturnsFalse()
    {
        Assert.False(ConsoleSorter.IsPathWithinRoot(null!, @"C:\roms"));
    }

    [Fact]
    public void IsPathWithinRoot_EmptyFilePath_ReturnsFalse()
    {
        Assert.False(ConsoleSorter.IsPathWithinRoot("", @"C:\roms"));
    }

    [Fact]
    public void IsPathWithinRoot_EmptyRoot_ReturnsFalse()
    {
        Assert.False(ConsoleSorter.IsPathWithinRoot(@"C:\roms\file.zip", ""));
    }

    [Fact]
    public void IsPathWithinRoot_WhitespaceRoot_ReturnsFalse()
    {
        Assert.False(ConsoleSorter.IsPathWithinRoot(@"C:\roms\file.zip", "   "));
    }

    [Fact]
    public void IsPathWithinRoot_SamePathAsRoot_ReturnsFalse()
    {
        // File is exactly the root, not a child of it
        Assert.False(ConsoleSorter.IsPathWithinRoot(@"C:\roms", @"C:\roms"));
    }
}
