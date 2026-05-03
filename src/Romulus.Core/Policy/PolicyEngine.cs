using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Core.Policy;

public sealed class PolicyEngine : IPolicyEngine
{
    private const string SeverityError = "error";
    private const string SeverityWarning = "warning";

    public PolicyValidationReport Validate(LibrarySnapshot snapshot, LibraryPolicy policy, string policyFingerprint = "")
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(policy);

        var policyId = string.IsNullOrWhiteSpace(policy.Id) ? "unnamed-policy" : policy.Id.Trim();
        var policyName = string.IsNullOrWhiteSpace(policy.Name) ? policyId : policy.Name.Trim();
        var preferredRegions = NormalizeTokenSet(policy.PreferredRegions);
        var allowedExtensions = NormalizeExtensions(policy.AllowedExtensions);
        var deniedTitleTokens = NormalizeTokens(policy.DeniedTitleTokens);
        var requiredExtensionsByConsole = NormalizeRequiredExtensions(policy.RequiredExtensionsByConsole);

        var violations = new List<PolicyRuleViolation>();
        foreach (var entry in snapshot.Entries.OrderBy(static e => e.Path, StringComparer.OrdinalIgnoreCase))
        {
            ValidatePreferredRegions(entry, preferredRegions, violations);
            ValidateAllowedExtensions(entry, allowedExtensions, violations);
            ValidateDeniedTitleTokens(entry, deniedTitleTokens, violations);
            ValidateConsoleExtension(entry, requiredExtensionsByConsole, violations);
        }

        var sorted = violations
            .OrderBy(static v => SeverityRank(v.Severity))
            .ThenBy(static v => v.RuleId, StringComparer.Ordinal)
            .ThenBy(static v => v.ConsoleKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static v => v.GameKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static v => v.Path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new PolicyValidationReport
        {
            PolicyId = policyId,
            PolicyName = policyName,
            PolicyFingerprint = policyFingerprint,
            GeneratedUtc = snapshot.GeneratedUtc,
            Snapshot = snapshot.Summary,
            Summary = BuildSummary(sorted),
            Violations = sorted
        };
    }

    private static void ValidatePreferredRegions(
        LibrarySnapshotEntry entry,
        IReadOnlySet<string> preferredRegions,
        List<PolicyRuleViolation> violations)
    {
        if (preferredRegions.Count == 0)
            return;

        var region = NormalizeToken(entry.Region);
        if (!string.IsNullOrWhiteSpace(region) && preferredRegions.Contains(region))
            return;

        violations.Add(CreateViolation(
            "preferred-regions",
            SeverityWarning,
            entry,
            $"Region '{Display(entry.Region)}' is outside the preferred policy regions.",
            string.Join("|", preferredRegions.OrderBy(static x => x, StringComparer.Ordinal)),
            Display(entry.Region)));
    }

    private static void ValidateAllowedExtensions(
        LibrarySnapshotEntry entry,
        IReadOnlySet<string> allowedExtensions,
        List<PolicyRuleViolation> violations)
    {
        if (allowedExtensions.Count == 0)
            return;

        var extension = NormalizeExtension(entry.Extension);
        if (!string.IsNullOrWhiteSpace(extension) && allowedExtensions.Contains(extension))
            return;

        violations.Add(CreateViolation(
            "allowed-extensions",
            SeverityError,
            entry,
            $"Extension '{Display(entry.Extension)}' is not allowed by policy.",
            string.Join("|", allowedExtensions.OrderBy(static x => x, StringComparer.Ordinal)),
            Display(entry.Extension)));
    }

    private static void ValidateDeniedTitleTokens(
        LibrarySnapshotEntry entry,
        IReadOnlyList<string> deniedTitleTokens,
        List<PolicyRuleViolation> violations)
    {
        if (deniedTitleTokens.Count == 0)
            return;

        var title = string.Join(" ", entry.FileName, entry.GameKey);
        foreach (var token in deniedTitleTokens)
        {
            if (title.Contains(token, StringComparison.OrdinalIgnoreCase))
            {
                violations.Add(CreateViolation(
                    "denied-title-token",
                    SeverityError,
                    entry,
                    $"Title contains denied policy token '{token}'.",
                    token,
                    entry.FileName));
            }
        }
    }

    private static void ValidateConsoleExtension(
        LibrarySnapshotEntry entry,
        IReadOnlyDictionary<string, IReadOnlySet<string>> requiredExtensionsByConsole,
        List<PolicyRuleViolation> violations)
    {
        if (requiredExtensionsByConsole.Count == 0)
            return;

        if (!requiredExtensionsByConsole.TryGetValue(entry.ConsoleKey, out var requiredExtensions))
            return;

        var extension = NormalizeExtension(entry.Extension);
        if (!string.IsNullOrWhiteSpace(extension) && requiredExtensions.Contains(extension))
            return;

        violations.Add(CreateViolation(
            "required-extension-by-console",
            SeverityError,
            entry,
            $"Console '{Display(entry.ConsoleKey)}' requires a different extension by policy.",
            string.Join("|", requiredExtensions.OrderBy(static x => x, StringComparer.Ordinal)),
            Display(entry.Extension)));
    }

    private static PolicyRuleViolation CreateViolation(
        string ruleId,
        string severity,
        LibrarySnapshotEntry entry,
        string message,
        string expected,
        string actual)
    {
        return new PolicyRuleViolation
        {
            RuleId = ruleId,
            Severity = severity,
            Message = message,
            Path = entry.Path,
            FileName = entry.FileName,
            ConsoleKey = entry.ConsoleKey,
            GameKey = entry.GameKey,
            Region = entry.Region,
            Extension = entry.Extension,
            Expected = expected,
            Actual = actual,
            SortKey = string.Join("|", severity, ruleId, entry.ConsoleKey, entry.GameKey, entry.Path)
        };
    }

    private static PolicyViolationSummary BuildSummary(IReadOnlyList<PolicyRuleViolation> violations)
    {
        return new PolicyViolationSummary
        {
            Total = violations.Count,
            BySeverity = violations
                .GroupBy(static v => v.Severity, StringComparer.Ordinal)
                .OrderBy(static g => g.Key, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.Ordinal),
            ByRule = violations
                .GroupBy(static v => v.RuleId, StringComparer.Ordinal)
                .OrderBy(static g => g.Key, StringComparer.Ordinal)
                .ToDictionary(static g => g.Key, static g => g.Count(), StringComparer.Ordinal)
        };
    }

    private static IReadOnlySet<string> NormalizeExtensions(IEnumerable<string>? extensions)
        => (extensions ?? Array.Empty<string>())
            .Select(NormalizeExtension)
            .Where(static extension => !string.IsNullOrWhiteSpace(extension))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IReadOnlyList<string> NormalizeTokens(IEnumerable<string>? tokens)
        => (tokens ?? Array.Empty<string>())
            .Select(static token => token.Trim())
            .Where(static token => !string.IsNullOrWhiteSpace(token))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static token => token, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static IReadOnlySet<string> NormalizeTokenSet(IEnumerable<string>? tokens)
        => NormalizeTokens(tokens).ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static string NormalizeToken(string? token)
        => (token ?? "").Trim();

    private static string NormalizeExtension(string? extension)
    {
        var trimmed = (extension ?? "").Trim();
        if (trimmed.Length == 0)
            return "";

        return (trimmed[0] == '.' ? trimmed : "." + trimmed).ToLowerInvariant();
    }

    private static IReadOnlyDictionary<string, IReadOnlySet<string>> NormalizeRequiredExtensions(
        IReadOnlyDictionary<string, string[]>? raw)
    {
        var result = new Dictionary<string, IReadOnlySet<string>>(StringComparer.OrdinalIgnoreCase);
        if (raw is null)
            return result;

        foreach (var pair in raw.OrderBy(static p => p.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (string.IsNullOrWhiteSpace(pair.Key))
                continue;

            var extensions = NormalizeExtensions(pair.Value);
            if (extensions.Count == 0)
                continue;

            result[pair.Key.Trim()] = extensions;
        }

        return result;
    }

    private static int SeverityRank(string severity)
        => string.Equals(severity, SeverityError, StringComparison.Ordinal) ? 0 : 1;

    private static string Display(string? value)
        => string.IsNullOrWhiteSpace(value) ? "<empty>" : value.Trim();
}
