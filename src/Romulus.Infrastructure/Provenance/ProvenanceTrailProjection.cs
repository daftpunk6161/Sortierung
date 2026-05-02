using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;

namespace Romulus.Infrastructure.Provenance;

/// <summary>
/// Shared read projection for per-ROM provenance. GUI, CLI and API use this
/// instead of computing trust scores independently.
/// </summary>
public static class ProvenanceTrailProjection
{
    public static ProvenanceTrail Project(IProvenanceStore store, string fingerprint)
    {
        ArgumentNullException.ThrowIfNull(store);
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new ArgumentException("Fingerprint must not be empty.", nameof(fingerprint));

        var normalized = fingerprint.Trim().ToLowerInvariant();
        var verify = store.Verify(normalized);
        var entries = verify.IsValid ? verify.ValidEntries : store.Read(normalized);

        return new ProvenanceTrail(
            Fingerprint: normalized,
            IsValid: verify.IsValid,
            FailureReason: verify.FailureReason,
            TrustScore: CalculateTrustScore(verify.IsValid, entries),
            Entries: entries);
    }

    internal static int CalculateTrustScore(bool isValid, IReadOnlyList<ProvenanceEntry> entries)
    {
        if (!isValid || entries.Count == 0)
            return 0;

        var score = 20;
        if (entries.Any(static entry => entry.EventKind == ProvenanceEventKind.Verified))
            score += 50;
        if (entries.Any(static entry => entry.EventKind == ProvenanceEventKind.Moved))
            score += 10;
        if (entries.Any(static entry => entry.EventKind == ProvenanceEventKind.Converted))
            score += 10;
        if (entries.Select(static entry => entry.AuditRunId).Distinct(StringComparer.Ordinal).Count() > 1)
            score += 10;

        return Math.Clamp(score, 0, 100);
    }
}
