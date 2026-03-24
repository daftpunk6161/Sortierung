namespace RomCleanup.Contracts;

/// <summary>
/// Central run-option constants shared across CLI, API, GUI and Orchestration.
/// Single source of truth — prevents hard-coded string/value duplication between entry points.
/// </summary>
public static class RunConstants
{
    /// <summary>Valid conflict policy names (case-insensitive comparison recommended).</summary>
    public static readonly IReadOnlySet<string> ValidConflictPolicies =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Rename", "Skip", "Overwrite" };

    /// <summary>Default conflict policy when none is specified.</summary>
    public const string DefaultConflictPolicy = "Rename";

    /// <summary>Maximum number of PreferRegions entries accepted by CLI and API.</summary>
    public const int MaxPreferRegions = 20;

    /// <summary>Valid hash type names.</summary>
    public static readonly IReadOnlySet<string> ValidHashTypes =
        new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "SHA1", "SHA256", "MD5" };

    /// <summary>Default hash type.</summary>
    public const string DefaultHashType = "SHA1";
}
