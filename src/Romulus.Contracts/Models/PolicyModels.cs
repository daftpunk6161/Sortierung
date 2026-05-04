namespace Romulus.Contracts.Models;

/// <summary>
/// Declarative target-state policy for a ROM library. Policies are not run
/// profiles: they describe what the collection should look like, not which
/// defaults a run should use.
/// </summary>
public sealed record LibraryPolicy
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Description { get; init; } = "";
    public string[] PreferredRegions { get; init; } = Array.Empty<string>();
    public string[] AllowedExtensions { get; init; } = Array.Empty<string>();
    public string[] DeniedTitleTokens { get; init; } = Array.Empty<string>();
    public Dictionary<string, string[]> RequiredExtensionsByConsole { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed record LibrarySnapshot
{
    public DateTime GeneratedUtc { get; init; }
    public string[] Roots { get; init; } = Array.Empty<string>();
    public LibrarySnapshotEntry[] Entries { get; init; } = Array.Empty<LibrarySnapshotEntry>();
    public LibrarySnapshotSummary Summary { get; init; } = new();
}

public sealed record LibrarySnapshotEntry
{
    public string Path { get; init; } = "";
    public string Root { get; init; } = "";
    public string FileName { get; init; } = "";
    public string Extension { get; init; } = "";
    public long SizeBytes { get; init; }
    public string ConsoleKey { get; init; } = "UNKNOWN";
    public string GameKey { get; init; } = "";
    public string Region { get; init; } = "UNKNOWN";
    public string Category { get; init; } = "Game";
    public bool DatMatch { get; init; }
    public string? DatGameName { get; init; }
    public string DecisionClass { get; init; } = "Unknown";
    public string SortDecision { get; init; } = "Blocked";
    public string? PrimaryHash { get; init; }
    public string PrimaryHashType { get; init; } = "SHA1";
}

public sealed record LibrarySnapshotSummary
{
    public int TotalEntries { get; init; }
    public long TotalSizeBytes { get; init; }
    public int DatMatchedEntries { get; init; }
    public int UnknownConsoleEntries { get; init; }
    public Dictionary<string, int> EntriesByConsole { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> EntriesByExtension { get; init; } = new(StringComparer.Ordinal);
}

public sealed record PolicyValidationReport
{
    public string PolicyId { get; init; } = "";
    public string PolicyName { get; init; } = "";
    public string PolicyFingerprint { get; init; } = "";
    public PolicySignatureStatus Signature { get; init; } = new();
    public DateTime GeneratedUtc { get; init; }
    public LibrarySnapshotSummary Snapshot { get; init; } = new();
    public PolicyViolationSummary Summary { get; init; } = new();
    public PolicyRuleViolation[] Violations { get; init; } = Array.Empty<PolicyRuleViolation>();
    public bool IsCompliant => Violations.Length == 0;
}

public sealed record PolicySignatureDocument
{
    public string Version { get; init; } = "romulus-policy-signature-v1";
    public string PolicyFileName { get; init; } = "";
    public string PolicyFingerprint { get; init; } = "";
    public string Signer { get; init; } = "local-audit-key";
    public string KeyId { get; init; } = "";
    public string CreatedUtc { get; init; } = "";
    public string HmacSha256 { get; init; } = "";
}

public sealed record PolicySignatureStatus
{
    public bool IsPresent { get; init; }
    public bool IsValid { get; init; }
    public string SignaturePath { get; init; } = "";
    public string Signer { get; init; } = "";
    public string KeyId { get; init; } = "";
    public string Error { get; init; } = "";
}

public sealed record PolicyViolationSummary
{
    public int Total { get; init; }
    public Dictionary<string, int> BySeverity { get; init; } = new(StringComparer.Ordinal);
    public Dictionary<string, int> ByRule { get; init; } = new(StringComparer.Ordinal);
}

public sealed record PolicyRuleViolation
{
    public string RuleId { get; init; } = "";
    public string Severity { get; init; } = "error";
    public string Message { get; init; } = "";
    public string Path { get; init; } = "";
    public string FileName { get; init; } = "";
    public string ConsoleKey { get; init; } = "UNKNOWN";
    public string GameKey { get; init; } = "";
    public string Region { get; init; } = "UNKNOWN";
    public string Extension { get; init; } = "";
    public string Expected { get; init; } = "";
    public string Actual { get; init; } = "";
    public string SortKey { get; init; } = "";
}

public sealed record PolicyValidationRequest
{
    public string PolicyText { get; init; } = "";
    public string PolicySignatureText { get; init; } = "";
    public string[] Roots { get; init; } = Array.Empty<string>();
    public string[] Extensions { get; init; } = Array.Empty<string>();
}

public sealed record PolicySignRequest
{
    public string PolicyText { get; init; } = "";
    public string PolicyFileName { get; init; } = "policy.yaml";
    public string Signer { get; init; } = "local-audit-key";
}
