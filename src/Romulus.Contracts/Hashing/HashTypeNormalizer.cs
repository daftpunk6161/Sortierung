namespace Romulus.Contracts.Hashing;

/// <summary>
/// Single source of truth for hash-type token normalization across the DAT and
/// hashing stacks. Replaces the previously divergent local implementations in
/// DatIndex, EnrichmentPipelinePhase and DatRepositoryAdapter (F-DAT-14).
/// </summary>
/// <remarks>
/// The default contract is permissive: unknown / null / whitespace tokens are
/// canonicalised to <c>"SHA1"</c>, which is the dominant DAT/No-Intro/Redump
/// hash. Callers that need fail-closed semantics use <see cref="TryNormalize"/>.
/// </remarks>
public static class HashTypeNormalizer
{
    /// <summary>Canonical token for the project-wide default hash.</summary>
    public const string DefaultHashType = "SHA1";

    /// <summary>
    /// Normalize a hash-type token to its canonical representation.
    /// Unknown / empty tokens fall back to <see cref="DefaultHashType"/> so that
    /// existing call sites remain compatible.
    /// </summary>
    public static string Normalize(string? hashType)
        => TryNormalize(hashType, out var canonical) ? canonical : DefaultHashType;

    /// <summary>
    /// Strict variant: returns <c>false</c> for unknown / empty tokens instead of
    /// silently mapping to the default. Use this where a wrong index key would
    /// hide a hash mismatch.
    /// </summary>
    public static bool TryNormalize(string? hashType, out string canonical)
    {
        if (string.IsNullOrWhiteSpace(hashType))
        {
            canonical = DefaultHashType;
            return false;
        }

        switch (hashType.Trim().ToUpperInvariant())
        {
            case "CRC":
            case "CRC32":
                canonical = "CRC32";
                return true;
            case "MD5":
                canonical = "MD5";
                return true;
            case "SHA1":
                canonical = "SHA1";
                return true;
            case "SHA256":
                canonical = "SHA256";
                return true;
            default:
                canonical = DefaultHashType;
                return false;
        }
    }

    /// <summary>True if the token maps to a known canonical hash type.</summary>
    public static bool IsKnown(string? hashType) => TryNormalize(hashType, out _);
}
