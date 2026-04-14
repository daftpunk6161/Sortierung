using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// R8 verification tests: Confirm that all R8 audit findings are
/// either non-issues (FP), intentional design decisions, or already mitigated.
/// Structural source-scan + unit-level invariant tests.
/// </summary>
public sealed class Phase8RoundVerificationTests
{
    private static string FindSrcDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "Romulus.Core")))
            dir = Directory.GetParent(dir)?.FullName;
        return dir ?? throw new DirectoryNotFoundException("Could not find src/ directory.");
    }

    // ═══════════════════════════════════════════════════════════════════
    // R8-001: ExtensionNormalizer vs ConsoleDetector — different purposes
    // ExtensionNormalizer handles double-extensions (.nkit.iso);
    // ConsoleDetector does dot-prefixing for ext→console map lookup.
    // Both are needed — not a DRY violation. FP-12 documents this.
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("game.nkit.iso", ".nkit.iso")]
    [InlineData("game.nkit.gcz", ".nkit.gcz")]
    [InlineData("game.ecm.bin", ".ecm.bin")]
    [InlineData("game.wia.gcz", ".wia.gcz")]
    public void R8_001_ExtensionNormalizer_HandlesDoubleExtensions(string fileName, string expected)
    {
        // ExtensionNormalizer's purpose: detect composite extensions like .nkit.iso
        var result = ExtensionNormalizer.GetNormalizedExtension(fileName);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("game.zip", ".zip")]
    [InlineData("game.nes", ".nes")]
    [InlineData("game.sfc", ".sfc")]
    public void R8_001_ExtensionNormalizer_SingleExtensions_ReturnLowerDotPrefixed(string fileName, string expected)
    {
        var result = ExtensionNormalizer.GetNormalizedExtension(fileName);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void R8_001_ConsoleDetector_InlineNormalization_DotPrefixForExtMaps()
    {
        // ConsoleDetector purpose: dot-prefix for UniqueExts/AmbigExts map lookup (different from double-ext)
        var path = Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "ConsoleDetector.cs");
        var source = File.ReadAllText(path);

        // Must contain dot-prefix normalization for extension maps
        Assert.Contains("ext.StartsWith(\".\") ? ext : \".\" + ext", source);
    }

    [Fact]
    public void R8_001_ExtensionNormalizer_HasDoubleExtRegex_NotInConsoleDetector()
    {
        var normalizerPath = Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "ExtensionNormalizer.cs");
        var detectorPath = Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "ConsoleDetector.cs");
        var normalizerSrc = File.ReadAllText(normalizerPath);
        var detectorSrc = File.ReadAllText(detectorPath);

        // ExtensionNormalizer owns the double-extension regex
        Assert.Contains("nkit\\.iso", normalizerSrc);
        // ConsoleDetector should NOT duplicate the double-extension regex
        Assert.DoesNotContain("nkit\\.iso", detectorSrc);
    }

    // ═══════════════════════════════════════════════════════════════════
    // R8-002: BIOS detection is name-based by design (fast path, no I/O)
    // Confidence 98 is correct for name-based heuristic.
    // Header-check would be a future enhancement, not a fix.
    // ═══════════════════════════════════════════════════════════════════

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
    public void R8_002_FileClassifier_BiosDetection_IsNameOnly_NoIoDependency()
    {
        // FileClassifier.Analyze is a static method taking only string params — no I/O
        var path = Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "FileClassifier.cs");
        var source = File.ReadAllText(path);

        // The Analyze method must NOT reference FileSystem, File.Open, Stream, etc.
        var analyzeIdx = source.IndexOf("public static ClassificationDecision Analyze(string baseName, string? extension, long? sizeBytes", StringComparison.Ordinal);
        Assert.True(analyzeIdx >= 0, "Analyze method with full signature must exist");

        // Extract method body
        var braceIdx = source.IndexOf('{', analyzeIdx);
        var depth = 0;
        var end = braceIdx;
        for (int i = braceIdx; i < source.Length; i++)
        {
            if (source[i] == '{') depth++;
            else if (source[i] == '}') depth--;
            if (depth == 0) { end = i; break; }
        }
        var body = source[braceIdx..(end + 1)];

        Assert.DoesNotContain("File.Open", body);
        Assert.DoesNotContain("FileStream", body);
        Assert.DoesNotContain("File.Read", body);
    }

    // ═══════════════════════════════════════════════════════════════════
    // R8-003: Magic bytes are immutable system constants — correct in code
    // consoles.json has user-configurable data (extensions, aliases, keywords).
    // Binary header signatures are ROM format standards, not config.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void R8_003_CartridgeHeaderDetector_MagicBytes_AreReadOnlySpanConstants()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Core", "Classification", "CartridgeHeaderDetector.cs");
        var source = File.ReadAllText(path);

        // All magic byte fields must be ReadOnlySpan<byte> static properties (immutable)
        Assert.Contains("private static ReadOnlySpan<byte> INesMagic", source);
        Assert.Contains("private static ReadOnlySpan<byte> GenesisMagic1", source);
        Assert.Contains("private static ReadOnlySpan<byte> GenesisMagic2", source);
        Assert.Contains("private static ReadOnlySpan<byte> N64MagicBE", source);
        Assert.Contains("private static ReadOnlySpan<byte> GbaLogoStart", source);
        Assert.Contains("private static ReadOnlySpan<byte> GbLogoStart", source);
        Assert.Contains("private static ReadOnlySpan<byte> Atari7800Magic", source);
        Assert.Contains("private static ReadOnlySpan<byte> LynxMagic", source);
    }

    [Fact]
    public void R8_003_ConsolesJson_DoesNotContainMagicBytes()
    {
        // consoles.json should contain configurable data, NOT binary header signatures
        var dataDir = FindSrcDir();
        // Navigate up from src/ to project root
        var projectRoot = Directory.GetParent(dataDir)?.FullName
                          ?? throw new DirectoryNotFoundException("Could not find project root.");
        var consolesPath = Path.Combine(projectRoot, "data", "consoles.json");
        var content = File.ReadAllText(consolesPath);

        // Magic bytes like "4E 45 53 1A" or hex arrays should NOT be in the config
        Assert.DoesNotContain("0x4E, 0x45, 0x53, 0x1A", content);
        Assert.DoesNotContain("ATARI7800", content);
        // But it SHOULD contain configurable items
        Assert.Contains("uniqueExts", content);
        Assert.Contains("folderAliases", content);
    }

    // ═══════════════════════════════════════════════════════════════════
    // R8-004: SSE micro-race — already mitigated
    // CompletedUtc is set BEFORE SignalCompletion() in the finally block.
    // Status and CompletedUtc are both lock-protected.
    // WaitForCompletion checks BOTH before returning Completed.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void R8_004_RunLifecycleManager_CompletedUtcBeforeSignalCompletion()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Api", "RunLifecycleManager.cs");
        var source = File.ReadAllText(path);

        // In the finally block: CompletedUtc must be set BEFORE SignalCompletion
        var completedUtcIdx = source.IndexOf("run.CompletedUtc = _timeProvider", StringComparison.Ordinal);
        var signalIdx = source.IndexOf("run.SignalCompletion()", StringComparison.Ordinal);

        Assert.True(completedUtcIdx > 0, "CompletedUtc assignment must exist");
        Assert.True(signalIdx > 0, "SignalCompletion call must exist");
        Assert.True(completedUtcIdx < signalIdx,
            "R8-004: CompletedUtc must be set BEFORE SignalCompletion to prevent SSE micro-race");
    }

    [Fact]
    public void R8_004_RunRecord_StatusAndCompletedUtc_AreLockProtected()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Api", "RunManager.cs");
        var source = File.ReadAllText(path);

        // Both Status and CompletedUtc properties must use lock(_lock)
        Assert.Contains("public string Status", source);
        Assert.Contains("public DateTime? CompletedUtc", source);

        // Verify lock-protected getters exist for both
        // Pattern: "get { lock (_lock) return _status; }"
        Assert.Contains("lock (_lock) return _status", source);
        Assert.Contains("lock (_lock) return _completedUtc", source);
    }

    [Fact]
    public void R8_004_WaitForCompletion_ChecksBothStatusAndCompletedUtc()
    {
        var path = Path.Combine(FindSrcDir(), "Romulus.Api", "RunLifecycleManager.cs");
        var source = File.ReadAllText(path);

        // WaitForCompletion must check BOTH conditions before returning Completed
        // Pattern: "run.Status != ... && run.CompletedUtc is not null"
        Assert.Contains("run.Status != RunConstants.StatusRunning && run.CompletedUtc is not null", source);
    }
}
