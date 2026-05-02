namespace Romulus.Contracts.Models;

/// <summary>
/// Read model for a per-ROM provenance trail. Built from <see cref="Ports.IProvenanceStore"/>
/// and shared by GUI, CLI and API.
/// </summary>
public sealed record ProvenanceTrail(
    string Fingerprint,
    bool IsValid,
    string? FailureReason,
    int TrustScore,
    IReadOnlyList<ProvenanceEntry> Entries);
