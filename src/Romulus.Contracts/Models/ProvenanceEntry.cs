using System;
using System.Collections.Generic;
using System.Linq;

namespace Romulus.Contracts.Models;

/// <summary>
/// Wave 7 — T-W7-PROVENANCE-TRAIL. Ein Eintrag im per-ROM Provenance-Trail.
///
/// Architektur: ADR-0024 §1 (Audit vs Provenance), §4 (audit_run_id-Pflicht).
///
/// Der Eintrag wird Append-Only in eine pro-ROM JSONL-Datei geschrieben
/// (Layout siehe ADR §3). Die HMAC-Kette wird ueber
/// <see cref="AuditSigningService.ComputeHmacSha256"/> berechnet, um die
/// Audit-Garantien zu erben (kein zweiter Signing-Pfad – ADR §5).
///
/// Privacy-Kontrakt: <see cref="Detail"/> darf relative oder anonymisierte
/// Hinweise enthalten, aber niemals voll qualifizierte Pfade aus
/// Benutzer-Roots, die ausserhalb des Audit-Sidecars geloggt wuerden.
/// </summary>
public sealed record ProvenanceEntry(
    string Fingerprint,
    string AuditRunId,
    ProvenanceEventKind EventKind,
    string TimestampUtc,
    string? Sha256,
    string? ConsoleKey,
    string? DatMatchId,
    string? Detail,
    string PreviousEntryHmac = "",
    string EntryHmac = "")
{
    public bool Equals(ProvenanceEntry? other)
    {
        if (other is null) return false;
        if (ReferenceEquals(this, other)) return true;
        return string.Equals(Fingerprint, other.Fingerprint, StringComparison.Ordinal)
            && string.Equals(AuditRunId, other.AuditRunId, StringComparison.Ordinal)
            && EventKind == other.EventKind
            && string.Equals(TimestampUtc, other.TimestampUtc, StringComparison.Ordinal)
            && string.Equals(Sha256, other.Sha256, StringComparison.Ordinal)
            && string.Equals(ConsoleKey, other.ConsoleKey, StringComparison.Ordinal)
            && string.Equals(DatMatchId, other.DatMatchId, StringComparison.Ordinal)
            && string.Equals(Detail, other.Detail, StringComparison.Ordinal)
            && string.Equals(PreviousEntryHmac, other.PreviousEntryHmac, StringComparison.Ordinal)
            && string.Equals(EntryHmac, other.EntryHmac, StringComparison.Ordinal);
    }

    public override int GetHashCode()
    {
        var hash = new HashCode();
        hash.Add(Fingerprint);
        hash.Add(AuditRunId);
        hash.Add(EventKind);
        hash.Add(TimestampUtc);
        hash.Add(Sha256);
        hash.Add(ConsoleKey);
        hash.Add(DatMatchId);
        hash.Add(Detail);
        hash.Add(PreviousEntryHmac);
        hash.Add(EntryHmac);
        return hash.ToHashCode();
    }
}

/// <summary>
/// Ergebnis einer <see cref="Romulus.Contracts.Ports.IProvenanceStore.Verify"/>
/// Prueffunktion. <see cref="IsValid"/> = true heisst: Hash-Kette und alle
/// Eintrags-HMACs sind konsistent. <see cref="FailureReason"/> enthaelt einen
/// nicht-lokalisierten Fehlercode-Hinweis (HMAC, Chain, MissingAuditRunId, ...).
/// </summary>
public sealed record ProvenanceVerifyReport(bool IsValid, string? FailureReason, IReadOnlyList<ProvenanceEntry> ValidEntries)
{
    public static ProvenanceVerifyReport Ok(IReadOnlyList<ProvenanceEntry> entries)
        => new(IsValid: true, FailureReason: null, ValidEntries: entries);

    public static ProvenanceVerifyReport Fail(string reason)
        => new(IsValid: false, FailureReason: reason, ValidEntries: Array.Empty<ProvenanceEntry>());
}
