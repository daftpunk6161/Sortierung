using Romulus.Contracts.Models;
using Romulus.Core.Rules;
using Xunit;

namespace Romulus.Tests;

public sealed class RuleEngineTests
{
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

    // =========================================================================
    //  Syntax Validation
    // =========================================================================

    [Fact]
    public void ValidateSyntax_ValidRule_ReturnsValid()
    {
        var rule = MakeRule("test", "junk", 10,
            new RuleCondition { Field = "Name", Op = "contains", Value = "Beta" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.True(result.Valid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ValidateSyntax_EmptyName_ReturnsError()
    {
        var rule = MakeRule("", "junk", 10,
            new RuleCondition { Field = "Name", Op = "eq", Value = "x" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
        Assert.Contains(result.Errors, e => e.Contains("name", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void ValidateSyntax_InvalidAction_ReturnsError()
    {
        var rule = MakeRule("test", "invalid", 10,
            new RuleCondition { Field = "Name", Op = "eq", Value = "x" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
    }

    [Fact]
    public void ValidateSyntax_InvalidOperator_ReturnsError()
    {
        var rule = MakeRule("test", "junk", 10,
            new RuleCondition { Field = "Name", Op = "invalid", Value = "x" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
    }

    [Fact]
    public void ValidateSyntax_InvalidRegex_ReturnsError()
    {
        var rule = MakeRule("test", "junk", 10,
            new RuleCondition { Field = "Name", Op = "regex", Value = "[invalid" });
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
    }

    [Fact]
    public void ValidateSyntax_NoConditions_ReturnsError()
    {
        var rule = new ClassificationRule { Name = "test", Action = "junk", Conditions = [] };
        var result = RuleEngine.ValidateSyntax(rule);
        Assert.False(result.Valid);
    }

    // =========================================================================
    //  Condition Evaluation
    // =========================================================================

    [Fact]
    public void TestRule_EqCondition_MatchesExact()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Region", Op = "eq", Value = "JP" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Region", "JP"))));
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Region", "EU"))));
    }

    [Fact]
    public void TestRule_NeqCondition_MatchesNotEqual()
    {
        var rule = MakeRule("r", "keep", 1,
            new RuleCondition { Field = "Region", Op = "neq", Value = "JP" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Region", "EU"))));
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Region", "JP"))));
    }

    [Fact]
    public void TestRule_ContainsCondition_MatchesSubstring()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Name", Op = "contains", Value = "Beta" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Name", "Game (Beta)"))));
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Name", "Game (Final)"))));
    }

    [Fact]
    public void TestRule_GtCondition_MatchesGreater()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "gt", Value = "100" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Size", "200"))));
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Size", "50"))));
    }

    [Fact]
    public void TestRule_LtCondition_MatchesLess()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Size", Op = "lt", Value = "100" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Size", "50"))));
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Size", "200"))));
    }

    [Fact]
    public void TestRule_RegexCondition_MatchesPattern()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Name", Op = "regex", Value = @"\(Beta\s*\d*\)" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Name", "Game (Beta 3)"))));
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Name", "Game (Final)"))));
    }

    [Fact]
    public void TestRule_MultipleConditions_AllMustMatch()
    {
        var rule = MakeRule("r", "junk", 1,
            new RuleCondition { Field = "Region", Op = "eq", Value = "JP" },
            new RuleCondition { Field = "Type", Op = "eq", Value = "Beta" });
        Assert.True(RuleEngine.TestRule(rule, MakeItem(("Region", "JP"), ("Type", "Beta"))));
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Region", "JP"), ("Type", "Final"))));
    }

    [Fact]
    public void TestRule_DisabledRule_NeverMatches()
    {
        var rule = new ClassificationRule
        {
            Name = "r", Action = "junk", Priority = 1, Enabled = false,
            Conditions = [new RuleCondition { Field = "Name", Op = "eq", Value = "x" }]
        };
        Assert.False(RuleEngine.TestRule(rule, MakeItem(("Name", "x"))));
    }

    // =========================================================================
    //  Evaluate (first match wins by priority)
    // =========================================================================

    [Fact]
    public void Evaluate_HigherPriorityWins()
    {
        var rules = new[]
        {
            MakeRule("low", "keep", 1, new RuleCondition { Field = "Name", Op = "contains", Value = "x" }),
            MakeRule("high", "junk", 10, new RuleCondition { Field = "Name", Op = "contains", Value = "x" })
        };
        var result = RuleEngine.Evaluate(rules, MakeItem(("Name", "game x")));
        Assert.True(result.Matched);
        Assert.Equal("high", result.RuleName);
        Assert.Equal("junk", result.Action);
    }

    [Fact]
    public void Evaluate_NoMatch_ReturnsFalse()
    {
        var rules = new[]
        {
            MakeRule("r1", "junk", 1, new RuleCondition { Field = "Region", Op = "eq", Value = "JP" })
        };
        var result = RuleEngine.Evaluate(rules, MakeItem(("Region", "EU")));
        Assert.False(result.Matched);
        Assert.Null(result.Rule);
    }

    // =========================================================================
    //  Batch Evaluation
    // =========================================================================

    [Fact]
    public void EvaluateBatch_CountsMatchesCorrectly()
    {
        var rules = new[]
        {
            MakeRule("beta-junk", "junk", 10,
                new RuleCondition { Field = "Name", Op = "contains", Value = "Beta" })
        };
        var items = new[]
        {
            MakeItem(("Name", "Game (Beta)")),
            MakeItem(("Name", "Game (Final)")),
            MakeItem(("Name", "Another (Beta 2)"))
        };

        var result = RuleEngine.EvaluateBatch(rules, items);
        Assert.Equal(3, result.Total);
        Assert.Equal(2, result.MatchedCount);
        Assert.Equal(1, result.UnmatchedCount);
    }

    [Fact]
    public void EvaluateBatch_WarnsOnHighMatchPercent()
    {
        var rules = new[]
        {
            MakeRule("catch-all", "junk", 10,
                new RuleCondition { Field = "Name", Op = "contains", Value = "" }) // matches everything
        };
        var items = new[] { MakeItem(("Name", "a")), MakeItem(("Name", "b")) };

        var result = RuleEngine.EvaluateBatch(rules, items, warnAllMatchPercent: 100);
        Assert.NotEmpty(result.Warnings);
    }
}
