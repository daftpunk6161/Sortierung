using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Core.Rules;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Gap-coverage tests for FileClassifier and RuleEngine.
/// Targets specific uncovered branches identified by coverage analysis.
/// </summary>
public sealed class ClassifierAndRuleEngineCoverageTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  FileClassifier.IsNonRomExtension — was 0% coverage
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void IsNonRomExtension_NullOrWhitespace_False(string? ext)
    {
        Assert.False(FileClassifier.IsNonRomExtension(ext!));
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".json")]
    [InlineData(".png")]
    [InlineData(".exe")]
    [InlineData(".nfo")]
    [InlineData(".pdf")]
    [InlineData(".ps1")]
    public void IsNonRomExtension_NonRomWithDot_True(string ext)
    {
        Assert.True(FileClassifier.IsNonRomExtension(ext));
    }

    [Theory]
    [InlineData("txt")]
    [InlineData("json")]
    [InlineData("png")]
    public void IsNonRomExtension_NonRomWithoutDot_True(string ext)
    {
        // Extension without leading dot should still be recognized
        Assert.True(FileClassifier.IsNonRomExtension(ext));
    }

    [Theory]
    [InlineData(".zip")]
    [InlineData(".nes")]
    [InlineData(".sfc")]
    [InlineData(".bin")]
    [InlineData(".iso")]
    public void IsNonRomExtension_RomExtension_False(string ext)
    {
        Assert.False(FileClassifier.IsNonRomExtension(ext));
    }

    [Fact]
    public void IsNonRomExtension_CaseInsensitive()
    {
        Assert.True(FileClassifier.IsNonRomExtension(".TXT"));
        Assert.True(FileClassifier.IsNonRomExtension(".Json"));
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FileClassifier.Analyze — extension-based branches
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void Analyze_ExtensionWithoutDot_NormalizesAndDetects()
    {
        // Extension "txt" without leading dot → should still detect as non-rom
        var decision = FileClassifier.Analyze("readme", "txt", sizeBytes: 100, aggressiveJunk: false);
        Assert.Equal(FileCategory.NonGame, decision.Category);
        Assert.Equal("non-rom-extension", decision.ReasonCode);
    }

    [Fact]
    public void Analyze_GenericRawBinary_DiscExt_UpperCase()
    {
        // disc name like "track01" with .BIN extension
        var decision = FileClassifier.Analyze("track01", ".BIN", sizeBytes: 1024, aggressiveJunk: false);
        Assert.Equal(FileCategory.Unknown, decision.Category);
        Assert.Equal("generic-raw-binary", decision.ReasonCode);
    }

    [Theory]
    [InlineData("rom")]
    [InlineData("data")]
    [InlineData("image")]
    [InlineData("backup")]
    [InlineData("dump")]
    [InlineData("unknown")]
    [InlineData("123")]
    public void Analyze_GenericRawBinaryNames_Blocked(string baseName)
    {
        var decision = FileClassifier.Analyze(baseName, ".iso", sizeBytes: 2048, aggressiveJunk: false);
        Assert.Equal(FileCategory.Unknown, decision.Category);
        Assert.Equal("generic-raw-binary", decision.ReasonCode);
    }

    [Fact]
    public void Analyze_NullExtension_SkipsExtensionCheck()
    {
        // Should go through normal classification path
        var decision = FileClassifier.Analyze("Super Mario World (Europe)", null, sizeBytes: 1024, aggressiveJunk: false);
        Assert.Equal(FileCategory.Game, decision.Category);
    }

    [Fact]
    public void Analyze_NullSizeBytes_SkipsEmptyFileCheck()
    {
        var decision = FileClassifier.Analyze("Super Mario World (Europe)", ".zip", sizeBytes: null, aggressiveJunk: false);
        Assert.Equal(FileCategory.Game, decision.Category);
    }

    [Fact]
    public void Analyze_AggressiveJunkWord_ThroughAnalyze()
    {
        // Test aggressive junk WORD detection through the 4-param Analyze overload
        var decision = FileClassifier.Analyze("wip game build", ".zip", sizeBytes: 1024, aggressiveJunk: true);
        Assert.Equal(FileCategory.Junk, decision.Category);
        Assert.Equal("junk-aggressive-word", decision.ReasonCode);
    }

    [Fact]
    public void Analyze_AggressiveJunkWord_NotMatchedWhenDisabled()
    {
        var decision = FileClassifier.Analyze("wip game build", ".zip", sizeBytes: 1024, aggressiveJunk: false);
        Assert.Equal(FileCategory.Game, decision.Category);
    }

    [Fact]
    public void Analyze_AggressiveJunkTag_ThroughAnalyze()
    {
        var decision = FileClassifier.Analyze("Game (Dev Build)", ".zip", sizeBytes: 1024, aggressiveJunk: true);
        Assert.Equal(FileCategory.Junk, decision.Category);
        Assert.Equal("junk-aggressive-tag", decision.ReasonCode);
    }

    [Fact]
    public void Analyze_EmptyBasename_WithValidExtension_ReturnsEmptyBasename()
    {
        // Non-rom extension takes priority over empty-basename
        var decision = FileClassifier.Analyze("", ".txt", sizeBytes: 100, aggressiveJunk: false);
        Assert.Equal(FileCategory.NonGame, decision.Category);
    }

    [Fact]
    public void Analyze_EmptyBasename_WithRomExtension_ReturnsUnknown()
    {
        var decision = FileClassifier.Analyze("", ".zip", sizeBytes: 100, aggressiveJunk: false);
        Assert.Equal(FileCategory.Unknown, decision.Category);
        Assert.Equal("empty-basename", decision.ReasonCode);
    }

    [Fact]
    public void Analyze_LowSignalBinaryExt_NonGenericName_ClassifiesNormally()
    {
        // Real game name with .bin → should not be flagged as generic
        var decision = FileClassifier.Analyze("Super Mario World (Europe)", ".bin", sizeBytes: 1024, aggressiveJunk: false);
        Assert.Equal(FileCategory.Game, decision.Category);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FileClassifier BIOS edge cases via Analyze
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("scph-1001")]
    [InlineData("SCPH 70012")]
    [InlineData("gba_bios")]
    [InlineData("syscard1")]
    [InlineData("sega saturn bios")]
    [InlineData("boot.rom")]
    [InlineData("boot-rom")]
    public void Analyze_BiosEdgeCases_Detected(string baseName)
    {
        var d = FileClassifier.Analyze(baseName);
        Assert.Equal(FileCategory.Bios, d.Category);
        Assert.Equal("bios-tag", d.ReasonCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  FileClassifier Junk word edge cases
    // ═══════════════════════════════════════════════════════════════════

    [Theory]
    [InlineData("demo version")]
    [InlineData("sample version")]
    [InlineData("trial version")]
    [InlineData("pre-release edition")]
    [InlineData("not for resale")]
    [InlineData("gamelist.xml")]
    [InlineData("gamelist.xml.old")]
    [InlineData("gamelist.xml.bak")]
    public void Analyze_JunkWords_Detected(string baseName)
    {
        var d = FileClassifier.Analyze(baseName);
        Assert.Equal(FileCategory.Junk, d.Category);
        Assert.Equal("junk-word", d.ReasonCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  RuleEngine — Uncovered branches
    // ═══════════════════════════════════════════════════════════════════

    private static ClassificationRule MakeRule(
        string name, string action, int priority,
        params RuleCondition[] conditions) => new()
    {
        Name = name,
        Action = action,
        Priority = priority,
        Conditions = conditions,
        Enabled = true
    };

    private static IReadOnlyDictionary<string, string> MakeItem(params (string key, string value)[] pairs)
        => pairs.ToDictionary(p => p.key, p => p.value, StringComparer.OrdinalIgnoreCase);

    // ── ValidateSyntax: empty condition field ──

    [Fact]
    public void ValidateSyntax_EmptyConditionField_ReturnsError()
    {
        var rule = MakeRule("test", "junk", 10,
            new RuleCondition { Field = "", Op = "eq", Value = "x" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("field", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSyntax_WhitespaceConditionField_ReturnsError()
    {
        var rule = MakeRule("test", "junk", 10,
            new RuleCondition { Field = "   ", Op = "eq", Value = "x" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
    }

    [Fact]
    public void ValidateSyntax_ValidRegex_NoError()
    {
        var rule = MakeRule("test", "junk", 10,
            new RuleCondition { Field = "Name", Op = "regex", Value = @"\(Beta\)" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.True(result.Valid);
    }

    // ── TestRule: missing field in item ──

    [Fact]
    public void TestRule_MissingField_TreatsAsEmpty()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "MissingField", Op = "eq", Value = "" });
        // Missing field → empty string → eq "" should match
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Other", "x"))));
    }

    [Fact]
    public void TestRule_MissingField_ContainsNonEmpty_NoMatch()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "MissingField", Op = "contains", Value = "x" });
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Other", "y"))));
    }

    // ── TestRule: gt/lt with non-numeric values ──

    [Fact]
    public void TestRule_GtCondition_NonNumericField_NoMatch()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "gt", Value = "100" });
        // "abc" can't be parsed as double → fails TryParse → false
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Size", "abc"))));
    }

    [Fact]
    public void TestRule_LtCondition_NonNumericField_NoMatch()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "lt", Value = "100" });
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Size", "abc"))));
    }

    [Fact]
    public void TestRule_GtCondition_NonNumericValue_NoMatch()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "gt", Value = "abc" });
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Size", "100"))));
    }

    [Fact]
    public void TestRule_LtCondition_NonNumericValue_NoMatch()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "lt", Value = "abc" });
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Size", "100"))));
    }

    // ── TestRule: unknown operator ──

    [Fact]
    public void TestRule_UnknownOperator_NoMatch()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Name", Op = "startswith", Value = "x" });
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Name", "xgame"))));
    }

    // ── TestRule: regex match ──

    [Fact]
    public void TestRule_RegexCondition_InvalidPattern_NoMatch()
    {
        // Invalid regex should not crash, just return false
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Name", Op = "regex", Value = "[invalid" });
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Name", "game"))));
    }

    [Fact]
    public void TestRule_RegexCondition_CaseInsensitive()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Name", Op = "regex", Value = "beta" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Name", "Game BETA"))));
    }

    // ── EvaluateBatch: warn threshold 0 disables warnings ──

    [Fact]
    public void EvaluateBatch_WarnThreshold0_NoWarnings()
    {
        var rules = new[]
        {
            MakeRule("all", "junk", 10,
                new RuleCondition { Field = "Name", Op = "contains", Value = "" })
        };
        var items = new[] { MakeItem(("Name", "a")), MakeItem(("Name", "b")) };

        var result = RuleEngine.EvaluateBatch(rules, items, warnAllMatchPercent: 0);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void EvaluateBatch_EmptyItems_NoWarnings()
    {
        var rules = new[]
        {
            MakeRule("r", "junk", 10,
                new RuleCondition { Field = "Name", Op = "eq", Value = "x" })
        };

        var result = RuleEngine.EvaluateBatch(rules, [], warnAllMatchPercent: 100);
        Assert.Empty(result.Warnings);
        Assert.Equal(0, result.Total);
    }

    [Fact]
    public void EvaluateBatch_PartialMatch_NoWarningBelow100Pct()
    {
        var rules = new[]
        {
            MakeRule("r", "junk", 10,
                new RuleCondition { Field = "Name", Op = "eq", Value = "x" })
        };
        var items = new[]
        {
            MakeItem(("Name", "x")),
            MakeItem(("Name", "y"))
        };

        var result = RuleEngine.EvaluateBatch(rules, items, warnAllMatchPercent: 100);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void EvaluateBatch_LowerThreshold_WarnsEarlier()
    {
        var rules = new[]
        {
            MakeRule("r", "junk", 10,
                new RuleCondition { Field = "Name", Op = "eq", Value = "x" })
        };
        var items = new[]
        {
            MakeItem(("Name", "x")),
            MakeItem(("Name", "y"))
        };

        // 50% match with 50% threshold → should warn
        var result = RuleEngine.EvaluateBatch(rules, items, warnAllMatchPercent: 50);
        Assert.NotEmpty(result.Warnings);
    }

    // ── Evaluate: priority order + name tiebreak ──

    [Fact]
    public void Evaluate_SamePriorityRules_SortedByNameAlphabetically()
    {
        var rules = new[]
        {
            MakeRule("z-rule", "quarantine", 5,
                new RuleCondition { Field = "Name", Op = "contains", Value = "game" }),
            MakeRule("a-rule", "junk", 5,
                new RuleCondition { Field = "Name", Op = "contains", Value = "game" })
        };
        var result = RuleEngine.Evaluate(rules, MakeItem(("Name", "game x")));
        Assert.True(result.Matched);
        // Same priority → sorted by name ascending → "a-rule" wins
        Assert.Equal("a-rule", result.RuleName);
    }

    // ── Evaluate: valid actions (quarantine, custom) ──

    [Fact]
    public void ValidateSyntax_QuarantineAction_Valid()
    {
        var rule = MakeRule("test", "quarantine", 10,
            new RuleCondition { Field = "Name", Op = "eq", Value = "x" });
        Assert.True(RuleEngine.ValidateSyntax(rule).Valid);
    }

    [Fact]
    public void ValidateSyntax_CustomAction_Valid()
    {
        var rule = MakeRule("test", "custom", 10,
            new RuleCondition { Field = "Name", Op = "eq", Value = "x" });
        Assert.True(RuleEngine.ValidateSyntax(rule).Valid);
    }
}
