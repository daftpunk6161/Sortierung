using System.Text.RegularExpressions;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Core.Rules;

/// <summary>
/// Priority-based classification rule engine.
/// Port of RuleEngine.ps1 — pure functions, no state.
/// </summary>
public static class RuleEngine
{
    private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
        { "junk", "keep", "quarantine", "custom" };

    private static readonly HashSet<string> ValidOperators = new(StringComparer.OrdinalIgnoreCase)
        { "eq", "neq", "contains", "gt", "lt", "regex" };

    /// <summary>
    /// Validate rule syntax. Returns errors if the rule is misconfigured.
    /// </summary>
    public static RuleSyntaxResult ValidateSyntax(ClassificationRule rule)
    {
        var errors = new List<string>();

        if (string.IsNullOrWhiteSpace(rule.Name))
            errors.Add("Rule name is required");

        if (!ValidActions.Contains(rule.Action))
            errors.Add($"Invalid action '{rule.Action}'. Must be one of: {string.Join(", ", ValidActions)}");

        if (rule.Conditions.Count == 0)
            errors.Add("Rule must have at least one condition");

        foreach (var cond in rule.Conditions)
        {
            if (string.IsNullOrWhiteSpace(cond.Field))
                errors.Add("Condition field is required");
            if (!ValidOperators.Contains(cond.Op))
                errors.Add($"Invalid operator '{cond.Op}'. Must be one of: {string.Join(", ", ValidOperators)}");
            if (cond.Op == "regex")
            {
                try { _ = new Regex(cond.Value); }
                catch { errors.Add($"Invalid regex pattern: {cond.Value}"); }
            }
        }

        return new RuleSyntaxResult { Valid = errors.Count == 0, Errors = errors };
    }

    /// <summary>
    /// Test whether a single rule matches an item.
    /// All conditions must match (AND logic).
    /// </summary>
    public static bool TestRule(ClassificationRule rule, IReadOnlyDictionary<string, string> item)
    {
        if (!rule.Enabled || rule.Conditions.Count == 0)
            return false;

        foreach (var cond in rule.Conditions)
        {
            if (!item.TryGetValue(cond.Field, out var fieldValue))
                fieldValue = "";

            if (!EvaluateCondition(cond, fieldValue))
                return false;
        }
        return true;
    }

    /// <summary>
    /// Evaluate all rules against a single item. First matching rule (by priority) wins.
    /// </summary>
    public static RuleMatchResult Evaluate(
        IReadOnlyList<ClassificationRule> rules,
        IReadOnlyDictionary<string, string> item)
    {
        var sorted = rules
            .Where(r => r.Enabled)
            .OrderByDescending(r => r.Priority)
            .ThenBy(r => r.Name, StringComparer.OrdinalIgnoreCase);

        foreach (var rule in sorted)
        {
            if (TestRule(rule, item))
            {
                return new RuleMatchResult
                {
                    Matched = true,
                    Rule = rule,
                    Action = rule.Action,
                    Reason = rule.Reason,
                    RuleName = rule.Name
                };
            }
        }

        return new RuleMatchResult { Matched = false };
    }

    /// <summary>
    /// Evaluate rules against a batch of items with over-match warnings.
    /// </summary>
    public static BatchRuleResult EvaluateBatch(
        IReadOnlyList<ClassificationRule> rules,
        IReadOnlyList<IReadOnlyDictionary<string, string>> items,
        int warnAllMatchPercent = 100)
    {
        var results = new List<RuleMatchResult>(items.Count);
        int matched = 0;

        foreach (var item in items)
        {
            var result = Evaluate(rules, item);
            results.Add(result);
            if (result.Matched) matched++;
        }

        var warnings = new List<string>();
        if (items.Count > 0 && warnAllMatchPercent > 0)
        {
            double pct = (double)matched / items.Count * 100;
            if (pct >= warnAllMatchPercent)
                warnings.Add($"WARNING: {pct:F1}% of items matched rules (threshold: {warnAllMatchPercent}%). Rules may be too broad.");
        }

        return new BatchRuleResult
        {
            Results = results,
            Warnings = warnings,
            Total = items.Count,
            MatchedCount = matched,
            UnmatchedCount = items.Count - matched
        };
    }

    private static bool EvaluateCondition(RuleCondition cond, string fieldValue)
    {
        return cond.Op.ToLowerInvariant() switch
        {
            "eq" => string.Equals(fieldValue, cond.Value, StringComparison.OrdinalIgnoreCase),
            "neq" => !string.Equals(fieldValue, cond.Value, StringComparison.OrdinalIgnoreCase),
            "contains" => fieldValue.Contains(cond.Value, StringComparison.OrdinalIgnoreCase),
            "gt" => double.TryParse(fieldValue, out var a) && double.TryParse(cond.Value, out var b) && a > b,
            "lt" => double.TryParse(fieldValue, out var c) && double.TryParse(cond.Value, out var d) && c < d,
            "regex" => TryRegexMatch(fieldValue, cond.Value),
            _ => false
        };
    }

    private static bool TryRegexMatch(string input, string pattern)
    {
        try { return Regex.IsMatch(input, pattern, RegexOptions.IgnoreCase); }
        catch { return false; }
    }
}
