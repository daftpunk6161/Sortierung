namespace Romulus.Contracts.Models;

/// <summary>
/// A user-defined classification rule with priority and conditions.
/// Port of New-ClassificationRule from RuleEngine.ps1.
/// </summary>
public sealed class ClassificationRule
{
    public string Name { get; init; } = "";
    public int Priority { get; init; } = 10;
    public IReadOnlyList<RuleCondition> Conditions { get; init; } = [];
    public string Action { get; init; } = "keep"; // junk, keep, quarantine, custom
    public string Reason { get; init; } = "";
    public bool Enabled { get; init; } = true;
}

/// <summary>
/// A single condition within a classification rule.
/// </summary>
public sealed class RuleCondition
{
    public string Field { get; init; } = "";
    public string Op { get; init; } = "eq"; // eq, neq, contains, gt, lt, regex
    public string Value { get; init; } = "";
}

/// <summary>
/// Result of evaluating a single item against the rule engine.
/// </summary>
public sealed class RuleMatchResult
{
    public bool Matched { get; init; }
    public ClassificationRule? Rule { get; init; }
    public string? Action { get; init; }
    public string? Reason { get; init; }
    public string? RuleName { get; init; }
}

/// <summary>
/// Result of batch rule evaluation.
/// </summary>
public sealed class BatchRuleResult
{
    public IReadOnlyList<RuleMatchResult> Results { get; init; } = [];
    public IReadOnlyList<string> Warnings { get; init; } = [];
    public int Total { get; init; }
    public int MatchedCount { get; init; }
    public int UnmatchedCount { get; init; }
}

/// <summary>
/// Syntax validation result for a rule.
/// </summary>
public sealed class RuleSyntaxResult
{
    public bool Valid { get; init; }
    public IReadOnlyList<string> Errors { get; init; } = [];
}
