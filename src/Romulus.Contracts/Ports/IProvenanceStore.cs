using System.Collections.Generic;
using Romulus.Contracts.Models;

namespace Romulus.Contracts.Ports;

/// <summary>
/// Wave 7 — T-W7-PROVENANCE-TRAIL. Per-ROM Append-Only Provenance-Trail.
///
/// Architektur: ADR-0024 (Audit vs Provenance).
///   * Ein Eintrag pro Run, in dem der ROM betroffen war.
///   * Layout: <root>/<fp[0..1]>/<fp[2..3]>/<fp>.provenance.jsonl (ADR §3).
///   * audit_run_id Pflichtfeld; ohne wird verworfen (ADR §4).
///   * HMAC-Kette ueber AuditSigningService (kein zweiter Schluessel, ADR §5).
///   * Schreiben ERST nach committetem Audit-Sidecar (ADR §5 Reihenfolge).
/// </summary>
public interface IProvenanceStore
{
    /// <summary>
    /// Append-Only Eintrag in den Trail des angegebenen ROM-Fingerprints.
    /// Berechnet HMAC-Kette automatisch (PreviousEntryHmac aus letzter Zeile,
    /// EntryHmac neu).
    /// </summary>
    /// <exception cref="System.ArgumentException">
    /// Fingerprint ist kein Hex / zu kurz fuer Sharding, oder
    /// AuditRunId ist leer.
    /// </exception>
    void Append(ProvenanceEntry entry);

    /// <summary>
    /// Liefert alle gueltigen Eintraege fuer den ROM-Fingerprint in
    /// Reihenfolge des Schreibens. Eintraege ohne audit_run_id oder mit
    /// kaputter HMAC-Kette werden uebersprungen (verworfen, nicht geworfen).
    /// </summary>
    IReadOnlyList<ProvenanceEntry> Read(string fingerprint);

    /// <summary>
    /// Verifiziert die HMAC-Kette des Trails. Gibt einen Bericht zurueck,
    /// wirft nicht. Fingerprint, der nicht existiert, gilt als gueltig (leerer Trail).
    /// </summary>
    ProvenanceVerifyReport Verify(string fingerprint);
}
