using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;

namespace Romulus.Infrastructure.Provenance;

/// <summary>
/// Wave 7 — T-W7-PROVENANCE-TRAIL.
/// Per-ROM Append-Only JSONL Provenance-Trail mit HMAC-Kette.
///
/// ADR-0024:
///   §1 Trennung: Audit-Sidecar (Run) vs Provenance-Trail (ROM). Hier ist nur Provenance.
///   §2 Reuse: <see cref="AuditSigningService.ComputeHmacSha256"/> als einziger Signing-Pfad.
///   §3 Layout: <root>/<fp[0..1]>/<fp[2..3]>/<fp>.provenance.jsonl
///   §4 Cross-Reference: audit_run_id Pflichtfeld; ohne wird verworfen.
///   §5 Verbot: kein zweites HMAC-Init, kein eigenes Ledger, kein Schreiben vor Audit-Commit.
///
/// Atomic-Append: schreibt eine Zeile in eine .tmp-Datei + File.Move (rename), damit
/// kein halb geschriebener Eintrag bei IO-Failure zurueckbleibt. Der Datei-Lock
/// pro Pfad serialisiert konkurrente Writer im selben Prozess.
/// </summary>
public sealed class JsonlProvenanceStore : IProvenanceStore
{
    private static readonly object FileLock = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _root;
    private readonly AuditSigningService _signing;

    public JsonlProvenanceStore(string root, AuditSigningService signing)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Provenance-Root darf nicht leer sein.", nameof(root));

        _root = Path.GetFullPath(root);
        _signing = signing ?? throw new ArgumentNullException(nameof(signing));
    }

    /// <inheritdoc />
    public void Append(ProvenanceEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        ValidateFingerprint(entry.Fingerprint);

        // ADR-0024 §4: audit_run_id Pflicht.
        if (string.IsNullOrWhiteSpace(entry.AuditRunId))
            throw new ArgumentException("AuditRunId ist Pflicht (ADR-0024 §4).", nameof(entry));

        var path = ResolvePathForFingerprint(entry.Fingerprint);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        lock (FileLock)
        {
            var previousHmac = ReadLastEntryHmac(path);
            var payload = BuildSignaturePayload(entry, previousHmac);
            var entryHmac = _signing.ComputeHmacSha256(payload);

            var stamped = entry with { PreviousEntryHmac = previousHmac, EntryHmac = entryHmac };
            var line = JsonSerializer.Serialize(StorageDto.From(stamped), JsonOptions) + "\n";

            // Atomic append: write next-state to tmp, rename into place.
            var existing = File.Exists(path) ? File.ReadAllBytes(path) : Array.Empty<byte>();
            var tmpPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var fs = new FileStream(tmpPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    fs.Write(existing);
                    var lineBytes = Encoding.UTF8.GetBytes(line);
                    fs.Write(lineBytes);
                    fs.Flush(flushToDisk: true);
                }

                File.Move(tmpPath, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmpPath)) File.Delete(tmpPath); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<ProvenanceEntry> Read(string fingerprint)
    {
        ValidateFingerprint(fingerprint);
        var path = ResolvePathForFingerprint(fingerprint);
        if (!File.Exists(path))
            return Array.Empty<ProvenanceEntry>();

        var output = new List<ProvenanceEntry>();
        string previousHmac = "";
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            ProvenanceEntry? parsed;
            try { parsed = StorageDto.TryToEntry(line, JsonOptions); }
            catch (JsonException) { continue; }
            if (parsed is null) continue;

            // ADR-0024 §4: ohne audit_run_id verwerfen.
            if (string.IsNullOrWhiteSpace(parsed.AuditRunId)) continue;

            // Chain check (lenient: invalid entry is dropped, but valid trailing entries stay).
            if (!string.Equals(parsed.PreviousEntryHmac, previousHmac, StringComparison.Ordinal))
                continue;

            var payload = BuildSignaturePayload(parsed, previousHmac);
            var expectedHmac = _signing.ComputeHmacSha256(payload);
            if (!string.Equals(expectedHmac, parsed.EntryHmac, StringComparison.Ordinal))
                continue;

            output.Add(parsed);
            previousHmac = parsed.EntryHmac;
        }
        return output;
    }

    /// <inheritdoc />
    public ProvenanceVerifyReport Verify(string fingerprint)
    {
        ValidateFingerprint(fingerprint);
        var path = ResolvePathForFingerprint(fingerprint);
        if (!File.Exists(path))
            return ProvenanceVerifyReport.Ok(Array.Empty<ProvenanceEntry>());

        var validated = new List<ProvenanceEntry>();
        string previousHmac = "";
        var lineNumber = 0;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            lineNumber++;
            if (string.IsNullOrWhiteSpace(line)) continue;

            ProvenanceEntry? parsed;
            try { parsed = StorageDto.TryToEntry(line, JsonOptions); }
            catch (JsonException ex) { return ProvenanceVerifyReport.Fail($"Line {lineNumber}: malformed JSON ({ex.Message})"); }
            if (parsed is null)
                return ProvenanceVerifyReport.Fail($"Line {lineNumber}: missing required fields");

            if (string.IsNullOrWhiteSpace(parsed.AuditRunId))
                return ProvenanceVerifyReport.Fail($"Line {lineNumber}: missing audit_run_id");

            if (!string.Equals(parsed.PreviousEntryHmac, previousHmac, StringComparison.Ordinal))
                return ProvenanceVerifyReport.Fail($"Line {lineNumber}: chain broken (previous_entry_hmac mismatch)");

            var payload = BuildSignaturePayload(parsed, previousHmac);
            var expectedHmac = _signing.ComputeHmacSha256(payload);
            if (!string.Equals(expectedHmac, parsed.EntryHmac, StringComparison.Ordinal))
                return ProvenanceVerifyReport.Fail($"Line {lineNumber}: HMAC verification failed");

            validated.Add(parsed);
            previousHmac = parsed.EntryHmac;
        }

        return ProvenanceVerifyReport.Ok(validated);
    }

    // ---- Helpers ----

    private string ResolvePathForFingerprint(string fingerprint)
    {
        // ADR-0024 §3
        var seg1 = fingerprint.Substring(0, 2).ToLowerInvariant();
        var seg2 = fingerprint.Substring(2, 2).ToLowerInvariant();
        var fileName = fingerprint.ToLowerInvariant() + ".provenance.jsonl";
        return Path.Combine(_root, seg1, seg2, fileName);
    }

    private static void ValidateFingerprint(string fingerprint)
    {
        if (string.IsNullOrWhiteSpace(fingerprint))
            throw new ArgumentException("Fingerprint darf nicht leer sein.", nameof(fingerprint));
        if (fingerprint.Length < 4)
            throw new ArgumentException("Fingerprint muss mindestens 4 hex Zeichen fuer Sharding haben.", nameof(fingerprint));
        for (var i = 0; i < fingerprint.Length; i++)
        {
            var c = fingerprint[i];
            var isHex = (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F');
            if (!isHex)
                throw new ArgumentException("Fingerprint muss hex sein (0-9 a-f).", nameof(fingerprint));
        }
    }

    private string? ReadLastEntryHmacOrNull(string path)
    {
        if (!File.Exists(path)) return null;
        string? lastHmac = null;
        foreach (var line in File.ReadLines(path, Encoding.UTF8))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            try
            {
                var parsed = StorageDto.TryToEntry(line, JsonOptions);
                if (parsed is not null && !string.IsNullOrEmpty(parsed.EntryHmac))
                    lastHmac = parsed.EntryHmac;
            }
            catch (JsonException) { /* skip torn line */ }
        }
        return lastHmac;
    }

    private string ReadLastEntryHmac(string path) => ReadLastEntryHmacOrNull(path) ?? "";

    private static string BuildSignaturePayload(ProvenanceEntry entry, string previousHmac)
    {
        // Canonical pipe-delimited payload. Renaming any field, reordering, or
        // changing the delimiter is a Hash-chain breaking change and must be
        // accompanied by a schema-version bump.
        var sb = new StringBuilder(256);
        sb.Append("v1|");
        sb.Append(entry.Fingerprint.ToLowerInvariant()).Append('|');
        sb.Append(entry.AuditRunId).Append('|');
        sb.Append(entry.EventKind.ToString()).Append('|');
        sb.Append(entry.TimestampUtc).Append('|');
        sb.Append(entry.Sha256 ?? "").Append('|');
        sb.Append(entry.ConsoleKey ?? "").Append('|');
        sb.Append(entry.DatMatchId ?? "").Append('|');
        sb.Append(entry.Detail ?? "").Append('|');
        sb.Append(previousHmac);
        return sb.ToString();
    }

    /// <summary>
    /// Wire-format DTO. Snake-case keys keep the JSONL file readable and
    /// friendly to ad-hoc tooling. Schema is the source of truth for the
    /// hash payload — any change here must bump the "v1" version tag in
    /// <see cref="BuildSignaturePayload"/>.
    /// </summary>
    private sealed record StorageDto
    {
        [JsonPropertyName("fingerprint")] public string Fingerprint { get; init; } = "";
        [JsonPropertyName("audit_run_id")] public string AuditRunId { get; init; } = "";
        [JsonPropertyName("event_kind")] public ProvenanceEventKind EventKind { get; init; }
        [JsonPropertyName("timestamp_utc")] public string TimestampUtc { get; init; } = "";
        [JsonPropertyName("sha256")] public string? Sha256 { get; init; }
        [JsonPropertyName("console_key")] public string? ConsoleKey { get; init; }
        [JsonPropertyName("dat_match_id")] public string? DatMatchId { get; init; }
        [JsonPropertyName("detail")] public string? Detail { get; init; }
        [JsonPropertyName("previous_entry_hmac")] public string PreviousEntryHmac { get; init; } = "";
        [JsonPropertyName("entry_hmac")] public string EntryHmac { get; init; } = "";

        public static StorageDto From(ProvenanceEntry e) => new()
        {
            Fingerprint = e.Fingerprint,
            AuditRunId = e.AuditRunId,
            EventKind = e.EventKind,
            TimestampUtc = e.TimestampUtc,
            Sha256 = e.Sha256,
            ConsoleKey = e.ConsoleKey,
            DatMatchId = e.DatMatchId,
            Detail = e.Detail,
            PreviousEntryHmac = e.PreviousEntryHmac,
            EntryHmac = e.EntryHmac
        };

        public static ProvenanceEntry? TryToEntry(string line, JsonSerializerOptions options)
        {
            var dto = JsonSerializer.Deserialize<StorageDto>(line, options);
            if (dto is null) return null;
            if (string.IsNullOrEmpty(dto.EntryHmac)) return null;
            return new ProvenanceEntry(
                Fingerprint: dto.Fingerprint,
                AuditRunId: dto.AuditRunId,
                EventKind: dto.EventKind,
                TimestampUtc: dto.TimestampUtc,
                Sha256: dto.Sha256,
                ConsoleKey: dto.ConsoleKey,
                DatMatchId: dto.DatMatchId,
                Detail: dto.Detail,
                PreviousEntryHmac: dto.PreviousEntryHmac,
                EntryHmac: dto.EntryHmac);
        }
    }
}
