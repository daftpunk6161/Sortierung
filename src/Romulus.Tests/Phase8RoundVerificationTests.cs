using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Behavioural R8 invariant tests retained after Block A cleanup.
/// All source-mirror checks were removed in
/// test-suite-remediation-plan-2026-04-25.md.
/// </summary>
public sealed class Phase8RoundVerificationTests
{
    [Theory]
    [InlineData("game.nkit.iso", ".nkit.iso")]
    [InlineData("game.nkit.gcz", ".nkit.gcz")]
    [InlineData("game.ecm.bin", ".ecm.bin")]
    [InlineData("game.wia.gcz", ".wia.gcz")]
    public void R8_001_ExtensionNormalizer_HandlesDoubleExtensions(string fileName, string expected)
        => Assert.Equal(expected, ExtensionNormalizer.GetNormalizedExtension(fileName));

    [Theory]
    [InlineData("game.zip", ".zip")]
    [InlineData("game.nes", ".nes")]
    [InlineData("game.sfc", ".sfc")]
    public void R8_001_ExtensionNormalizer_SingleExtensions_ReturnLowerDotPrefixed(string fileName, string expected)
        => Assert.Equal(expected, ExtensionNormalizer.GetNormalizedExtension(fileName));

    [Theory]
    [InlineData("[BIOS] PlayStation", FileCategory.Bios)]
    [InlineData("[BIOS] Game Boy Advance", FileCategory.Bios)]
    [InlineData("scph1001", FileCategory.Bios)]
    [InlineData("(firmware) NDS", FileCategory.Bios)]
    [InlineData("syscard3", FileCategory.Bios)]
    public void R8_002_FileClassifier_DetectsBiosPatterns_WithHighConfidence(string baseName, FileCategory expectedCategory)
    {
        var decision = FileClassifier.Analyze(baseName);
        Assert.Equal(expectedCategory, decision.Category);
        Assert.Equal(98, decision.Confidence);
        Assert.Equal("bios-tag", decision.ReasonCode);
    }

    [Theory]
    [InlineData("Super Mario World (Europe)")]
    [InlineData("Zelda - A Link to the Past (USA)")]
    public void R8_002_FileClassifier_NormalGames_AreNotBios(string baseName)
    {
        var decision = FileClassifier.Analyze(baseName);
        Assert.NotEqual(FileCategory.Bios, decision.Category);
    }

    [Fact]
    public void R8_003_ConsolesJson_DoesNotContainMagicBytes()
    {
        var projectRoot = FindRepositoryRoot();
        var consolesPath = Path.Combine(projectRoot, "data", "consoles.json");
        var content = File.ReadAllText(consolesPath);

        Assert.DoesNotContain("0x4E, 0x45, 0x53, 0x1A", content);
        Assert.Contains("uniqueExts", content);
        Assert.Contains("folderAliases", content);
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not resolve repository root from test context.");
    }
}
