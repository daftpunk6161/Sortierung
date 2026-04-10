using System.Collections.Concurrent;
using System.Globalization;
using System.Text.RegularExpressions;
using Romulus.Contracts.Models;

namespace Romulus.Core.Rules;

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

    /// <summary>Timeout for user-defined regex patterns to prevent ReDoS.</summary>
    private static readonly TimeSpan RegexTimeout = SafeRegex.ShortTimeout;

    /// <summary>Cache for compiled regex patterns from user rules. Bounded to 1024 entries to prevent memory exhaustion.</summary>
    private static readonly ConcurrentDictionary<string, Regex?> _regexCache = new(StringComparer.Ordinal);
    private static readonly int MaxRegexCacheSize = 1024;
    private static readonly object _evictionLock = new();

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
                try { _ = new Regex(cond.Value, RegexOptions.None, RegexTimeout); }
                catch (ArgumentException) { errors.Add($"Invalid regex pattern: {cond.Value}"); }
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
        var op = cond.Op;
        return op.Equals("eq", StringComparison.OrdinalIgnoreCase) ? string.Equals(fieldValue, cond.Value, StringComparison.OrdinalIgnoreCase)
            : op.Equals("neq", StringComparison.OrdinalIgnoreCase) ? !string.Equals(fieldValue, cond.Value, StringComparison.OrdinalIgnoreCase)
            : op.Equals("contains", StringComparison.OrdinalIgnoreCase) ? fieldValue.Contains(cond.Value, StringComparison.OrdinalIgnoreCase)
            : op.Equals("gt", StringComparison.OrdinalIgnoreCase) ? double.TryParse(fieldValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var a)
                      && double.TryParse(cond.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var b) && a > b
            : op.Equals("lt", StringComparison.OrdinalIgnoreCase) ? double.TryParse(fieldValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var c)
                      && double.TryParse(cond.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) && c < d
            : op.Equals("regex", StringComparison.OrdinalIgnoreCase) ? TryRegexMatch(fieldValue, cond.Value)
            : false;
    }

    private static bool TryRegexMatch(string input, string pattern)
    {
        try
        {
            // Evict ~25% of cache entries when capacity exceeded.
            // Lock prevents stampede where multiple threads evict concurrently.
            if (_regexCache.Count >= MaxRegexCacheSize)
            {
                lock (_evictionLock)
                {
                    if (_regexCache.Count >= MaxRegexCacheSize)
                    {
                        var keysToRemove = _regexCache.Keys.Take(MaxRegexCacheSize / 4).ToList();
                        foreach (var key in keysToRemove)
                            _regexCache.TryRemove(key, out _);
                    }
                }
            }

            var rx = _regexCache.GetOrAdd(pattern, p =>
            {
                try { return new Regex(p, RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout); }
                catch (ArgumentException) { return null; }
            });
            return rx is not null && rx.IsMatch(input);
        }
        catch (RegexMatchTimeoutException) { return false; }
    }
}
