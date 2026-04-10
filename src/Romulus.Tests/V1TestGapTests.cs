// ══════════════════════════════════════════════════════════════════════════════
// LEGACY: Phase-3/4 gap-fill tests. Still active — do not delete.
// These tests cover real scenarios but may overlap with Phase5D determinism suite.
// Migrate to domain-specific test classes when revisiting test organization.
// ══════════════════════════════════════════════════════════════════════════════
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.GameKeys;
using Romulus.Core.Regions;
using Romulus.Core.SetParsing;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Deduplication;
using Romulus.Infrastructure.Sorting;
using System.Text;
using Xunit;

namespace Romulus.Tests;

// ═══════════════════════════════════════════════════════════════
// TEST-01: FolderDeduplicator Move-Mode Tests
// ═══════════════════════════════════════════════════════════════

public sealed class FolderDeduplicatorMoveTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FolderDeduplicator _dedup;
    private readonly List<string> _log = new();

    public FolderDeduplicatorMoveTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "fdedup_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        var fs = new RealFileSystem();
        _dedup = new FolderDeduplicator(fs, msg => _log.Add(msg));
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetFolderBaseKey_RemovesParenthesizedTags()
    {
        var key = FolderDeduplicator.GetFolderBaseKey("My Game (USA) (v1.0)");
        Assert.DoesNotContain("(", key);
        Assert.DoesNotContain(")", key);
    }

    [Fact]
    public void GetFolderBaseKey_PreservesBaseName()
    {
        var key1 = FolderDeduplicator.GetFolderBaseKey("Street Fighter II (USA)");
        var key2 = FolderDeduplicator.GetFolderBaseKey("Street Fighter II (Japan)");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void NeedsFolderDedupe_DOS_ReturnsTrue()
    {
        Assert.True(FolderDeduplicator.NeedsFolderDedupe("DOS"));
    }

    [Fact]
    public void NeedsFolderDedupe_NES_ReturnsFalse()
    {
        Assert.False(FolderDeduplicator.NeedsFolderDedupe("NES"));
    }

    [Fact]
    public void NeedsPs3Dedupe_PS3_ReturnsTrue()
    {
        Assert.True(FolderDeduplicator.NeedsPs3Dedupe("PS3"));
    }

    [Fact]
    public void IsPs3MultidiscFolder_MatchesExpected()
    {
        Assert.True(FolderDeduplicator.IsPs3MultidiscFolder("Game Disc 2"));
        Assert.False(FolderDeduplicator.IsPs3MultidiscFolder("My Game"));
    }

    // Minimal IFileSystem for FolderDeduplicator tests
    private sealed class RealFileSystem : IFileSystem
    {
        public bool TestPath(string path, string pathType = "Any") => Directory.Exists(path) || File.Exists(path);
        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? exts = null) => Array.Empty<string>();
        public string? MoveItemSafely(string src, string dst)
        {
            if (File.Exists(src)) { File.Move(src, dst); return dst; }
            if (Directory.Exists(src)) { Directory.Move(src, dst); return dst; }
            return null;
        }
        public string? ResolveChildPathWithinRoot(string root, string rel)
        {
            var full = Path.GetFullPath(Path.Combine(root, rel));
            return full.StartsWith(Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }
        public void CopyFile(string src, string dst, bool overwrite = false) => File.Copy(src, dst, overwrite);
    }
}

// ═══════════════════════════════════════════════════════════════
// TEST-03: AuditSigningService Negative Tests
// ═══════════════════════════════════════════════════════════════

public sealed class AuditSigningNegativeTests : IDisposable
{
    private readonly string _tempDir;

    public AuditSigningNegativeTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audit_neg_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void VerifyMetadataSidecar_TamperedCsv_Throws()
    {
        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);

        // Write audit CSV
        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(csvPath, "RootPath,OldPath,NewPath,Action\nroot,old,new,Move\n");

        // Write sidecar
        var metaPath = service.WriteMetadataSidecar(csvPath, 1);
        Assert.NotNull(metaPath);
        Assert.True(File.Exists(metaPath));

        // Tamper with the CSV
        File.AppendAllText(csvPath, "root,old2,new2,Move\n");

        // Verify should fail with InvalidDataException (hash mismatch)
        Assert.ThrowsAny<Exception>(() => service.VerifyMetadataSidecar(csvPath));
    }

    [Fact]
    public void VerifyMetadataSidecar_MissingSidecar_ThrowsFileNotFound()
    {
        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);

        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(csvPath, "RootPath,OldPath,NewPath,Action\n");

        // No sidecar file created
        Assert.Throws<FileNotFoundException>(() => service.VerifyMetadataSidecar(csvPath));
    }

    [Fact]
    public void VerifyMetadataSidecar_CorruptSidecarJson_Throws()
    {
        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);

        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(csvPath, "RootPath,OldPath,NewPath,Action\n");

        var metaPath = csvPath + ".meta.json";
        File.WriteAllText(metaPath, "{ invalid json }}}}");

        Assert.ThrowsAny<Exception>(() => service.VerifyMetadataSidecar(csvPath));
    }

    [Fact]
    public void WriteMetadataSidecar_NonexistentCsv_ReturnsNull()
    {
        var fs = new MinimalFs();
        var service = new AuditSigningService(fs);

        var result = service.WriteMetadataSidecar(Path.Combine(_tempDir, "nonexistent.csv"), 0);
        Assert.Null(result);
    }

    private sealed class MinimalFs : IFileSystem
    {
        public bool TestPath(string path, string type = "Any") => true;
        public string EnsureDirectory(string path) => path;
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? exts = null) => Array.Empty<string>();
        public string? MoveItemSafely(string src, string dst) => dst;
        public string? ResolveChildPathWithinRoot(string root, string rel) => Path.Combine(root, rel);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string src, string dst, bool overwrite = false) { }
    }
}

// ═══════════════════════════════════════════════════════════════
// TEST-04: DatSourceService Download Tests (schema validation)
// ═══════════════════════════════════════════════════════════════

public sealed class SettingsSchemaValidationTests
{
    [Fact]
    public void ValidateSettingsStructure_ValidJson_NoErrors()
    {
        var json = """
        {
          "general": { "logLevel": "Info", "preferredRegions": ["EU", "US"] },
          "toolPaths": { "chdman": "" },
          "dat": { "useDat": true, "hashType": "SHA1" }
        }
        """;
        var errors = SettingsLoader.ValidateSettingsStructure(json);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateSettingsStructure_UnknownKey_ReportsError()
    {
        var json = """{ "general": {}, "toolPaths": {}, "dat": {}, "unknownStuff": 42 }""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);
        Assert.Contains(errors, e => e.Contains("Unknown top-level key"));
    }

    [Fact]
    public void ValidateSettingsStructure_WrongSectionType_ReportsError()
    {
        var json = """{ "general": "not-an-object", "toolPaths": {}, "dat": {} }""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);
        Assert.Contains(errors, e => e.Contains("general") && e.Contains("Object"));
    }

    [Fact]
    public void ValidateSettingsStructure_WrongFieldType_ReportsError()
    {
        var json = """{ "general": { "logLevel": 123 }, "toolPaths": {}, "dat": {} }""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);
        Assert.Contains(errors, e => e.Contains("logLevel"));
    }

    [Fact]
    public void ValidateSettingsStructure_InvalidJson_ReportsParseError()
    {
        var errors = SettingsLoader.ValidateSettingsStructure("{ invalid }}}");
        Assert.NotEmpty(errors);
        Assert.Contains(errors, e => e.Contains("Invalid JSON"));
    }

    [Fact]
    public void ValidateSettingsStructure_BooleanFieldWithString_ReportsError()
    {
        var json = """{ "general": { "aggressiveJunk": "yes" }, "toolPaths": {}, "dat": {} }""";
        var errors = SettingsLoader.ValidateSettingsStructure(json);
        Assert.Contains(errors, e => e.Contains("aggressiveJunk"));
    }
}

// ═══════════════════════════════════════════════════════════════
// TEST-05: GameKeyNormalizer DOS Metadata Deep Nesting
// ═══════════════════════════════════════════════════════════════

public class GameKeyNormalizerDosTests
{
    [Fact]
    public void RemoveMsDosMetadataTags_DeepNesting_DoesNotHang()
    {
        // Build deeply nested tags — loop limit should protect against DoS
        var input = "Game";
        for (int i = 0; i < 30; i++)
            input += " (tag" + i + ")";

        var result = GameKeyNormalizer.RemoveMsDosMetadataTags(input);
        Assert.NotNull(result);
        // Should have removed most tags (up to loop limit of 20)
        Assert.True(result.Length < input.Length);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_BracketNesting_Handled()
    {
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game [v1.0] [Demo] [Alt]");
        Assert.NotNull(result);
        Assert.DoesNotContain("[", result);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_EmptyInput_ReturnsEmpty()
    {
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("");
        Assert.Equal("", result);
    }

    [Fact]
    public void RemoveMsDosMetadataTags_TrailingParens_Removed()
    {
        // Only trailing parenthesized tags are removed (not mid-string ones)
        var result = GameKeyNormalizer.RemoveMsDosMetadataTags("Game (v1.4) (1993)");
        Assert.NotNull(result);
        Assert.DoesNotContain("(1993)", result);
    }
}

// ═══════════════════════════════════════════════════════════════
// TEST-06: RegionDetector Two-Letter-Code Disambiguation
// ═══════════════════════════════════════════════════════════════

public class RegionDetectorDisambiguationTests
{
    [Theory]
    [InlineData("Game (de)", "EU")]
    [InlineData("Game (De)", "EU")]
    public void TwoLetterCode_De_MapsToEU(string input, string expected)
    {
        // (de) as token resolves to EU region (Germany is part of Europe)
        var result = RegionDetector.GetRegionTag(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Game (fr)", "EU")]
    [InlineData("Game (Fr)", "EU")]
    public void TwoLetterCode_Fr_MapsToEU(string input, string expected)
    {
        // (fr) as token resolves to EU region (France is part of Europe)
        var result = RegionDetector.GetRegionTag(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("Game (it)", "EU")]
    [InlineData("Game (It)", "EU")]
    public void TwoLetterCode_It_MapsToEU(string input, string expected)
    {
        // (it) as token resolves to EU region (Italy is part of Europe)
        var result = RegionDetector.GetRegionTag(input);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void TwoLetterCode_NotRegion_ReturnsUnknown()
    {
        // "xy" is not a known region code
        var result = RegionDetector.GetRegionTag("Game (xy)");
        Assert.Equal("UNKNOWN", result);
    }

    [Theory]
    [InlineData("Game (en)")]
    [InlineData("Game (ja)")]
    public void LanguageCodes_DoNotCrash(string input)
    {
        var result = RegionDetector.GetRegionTag(input);
        // Language codes may or may not map to regions — at minimum should not crash
        Assert.NotNull(result);
    }

    [Fact]
    public void MultipleRegionTags_FirstWins()
    {
        var result = RegionDetector.GetRegionTag("Game (Europe) (Japan)");
        Assert.Equal("EU", result);
    }
}

// ═══════════════════════════════════════════════════════════════
// TEST-07: MDS Set-Parsing Tests
// ═══════════════════════════════════════════════════════════════

public sealed class MdsSetParserTests : IDisposable
{
    private readonly string _tempDir;

    public MdsSetParserTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "mds_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void GetRelatedFiles_WithMdf_ReturnsMdfPath()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        var mdfPath = Path.Combine(_tempDir, "game.mdf");
        File.WriteAllText(mdsPath, "dummy mds content");
        File.WriteAllText(mdfPath, "dummy mdf content");

        var related = MdsSetParser.GetRelatedFiles(mdsPath);
        Assert.Single(related);
        Assert.Equal(Path.GetFullPath(mdfPath), related[0]);
    }

    [Fact]
    public void GetRelatedFiles_MissingMdf_ReturnsEmpty()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        File.WriteAllText(mdsPath, "dummy mds content");

        var related = MdsSetParser.GetRelatedFiles(mdsPath);
        Assert.Empty(related);
    }

    [Fact]
    public void GetMissingFiles_MissingMdf_ReturnsMdfPath()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        File.WriteAllText(mdsPath, "dummy mds content");

        var missing = MdsSetParser.GetMissingFiles(mdsPath);
        Assert.Single(missing);
        Assert.EndsWith(".mdf", missing[0]);
    }

    [Fact]
    public void GetMissingFiles_ExistingMdf_ReturnsEmpty()
    {
        var mdsPath = Path.Combine(_tempDir, "game.mds");
        var mdfPath = Path.Combine(_tempDir, "game.mdf");
        File.WriteAllText(mdsPath, "dummy");
        File.WriteAllText(mdfPath, "dummy");

        var missing = MdsSetParser.GetMissingFiles(mdsPath);
        Assert.Empty(missing);
    }

    [Fact]
    public void GetRelatedFiles_NullInput_ReturnsEmpty()
    {
        var related = MdsSetParser.GetRelatedFiles(null!);
        Assert.Empty(related);
    }

    [Fact]
    public void GetRelatedFiles_EmptyInput_ReturnsEmpty()
    {
        var related = MdsSetParser.GetRelatedFiles("");
        Assert.Empty(related);
    }

    [Fact]
    public void GetRelatedFiles_NonexistentFile_ReturnsEmpty()
    {
        var related = MdsSetParser.GetRelatedFiles(Path.Combine(_tempDir, "nonexistent.mds"));
        Assert.Empty(related);
    }
}
