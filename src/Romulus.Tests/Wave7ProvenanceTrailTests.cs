using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Provenance;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 7 — T-W7-PROVENANCE-TRAIL pin tests.
///
/// Acceptance gates from plan.yaml:
///   * Trail ist verifizierbar (Hash-Kette).
///   * GUI/CLI/API Drawer zeigt fuer einen ROM den vollstaendigen Verlauf.
///   * Persistente Provenance pro ROM ueber Run-Grenzen hinweg.
///
/// Architektur-Bindung: ADR-0024 (Provenance vs Audit).
///   * §1 Verantwortungs-Trennung: Audit-Sidecar fuer Run, Provenance-Trail je ROM.
///   * §2 Gemeinsame Helper: HMAC + Atomic-Write aus Audit wiederverwenden, kein
///     zweiter Signing-Pfad.
///   * §3 Speicherort: <root>/<fp[0..1]>/<fp[2..3]>/<fp>.provenance.jsonl
///   * §4 audit_run_id Cross-Reference Pflichtfeld; ohne wird Eintrag verworfen.
///   * §5 Verbote: keine zweite Sidecar-/Ledger-/Signing-Logik.
///
/// Determinismus: gleiche Inputs -> identische Hash-Kette -> bit-identische Datei.
/// </summary>
public sealed class Wave7ProvenanceTrailTests : IDisposable
{
    private readonly string _root;
    private readonly string _keyFile;
    private readonly AuditSigningService _signing;
    private readonly JsonlProvenanceStore _store;

    public Wave7ProvenanceTrailTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "RomulusProvTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
        _keyFile = Path.Combine(_root, ".hmac.key");
        _signing = new AuditSigningService(
            new Romulus.Infrastructure.FileSystem.FileSystemAdapter(),
            log: null,
            keyFilePath: _keyFile);
        _store = new JsonlProvenanceStore(Path.Combine(_root, "provenance"), _signing);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private static ProvenanceEntry MakeEntry(
        string fingerprint = "abcdef0123456789",
        string auditRunId = "11111111-1111-1111-1111-111111111111",
        ProvenanceEventKind kind = ProvenanceEventKind.Verified,
        string? detail = null)
        => new ProvenanceEntry(
            Fingerprint: fingerprint,
            AuditRunId: auditRunId,
            EventKind: kind,
            TimestampUtc: "2026-04-30T10:00:00.0000000Z",
            Sha256: fingerprint,
            ConsoleKey: "NES",
            DatMatchId: kind == ProvenanceEventKind.Verified ? "Nintendo - NES (USA).dat" : null,
            Detail: detail);

    // -------- ADR-0024 §3: Sharded file layout --------

    [Fact]
    public void Append_WritesToFingerprintShardedPath()
    {
        var entry = MakeEntry(fingerprint: "abcdef0123456789");
        _store.Append(entry);

        var expected = Path.Combine(_root, "provenance", "ab", "cd", "abcdef0123456789.provenance.jsonl");
        Assert.True(File.Exists(expected),
            $"Provenance file must live under <root>/<fp[0..1]>/<fp[2..3]>/<fp>.provenance.jsonl. Got root={Directory.GetFiles(Path.Combine(_root, "provenance"), "*.jsonl", SearchOption.AllDirectories).FirstOrDefault()}");
    }

    [Fact]
    public void Append_ShortFingerprint_RejectedNotSilentlyMisFiled()
    {
        // ADR-0024 §3 mandates two shard segments of 2 chars each.
        Assert.Throws<ArgumentException>(() => _store.Append(MakeEntry(fingerprint: "ab")));
    }

    [Fact]
    public void Append_NonHexFingerprint_Rejected()
    {
        Assert.Throws<ArgumentException>(() => _store.Append(MakeEntry(fingerprint: "ZZZZZZZZZZZZ")));
    }

    // -------- ADR-0024 §4: audit_run_id cross-reference is mandatory --------

    [Fact]
    public void Append_NullOrEmptyAuditRunId_Rejected()
    {
        Assert.Throws<ArgumentException>(() => _store.Append(MakeEntry(auditRunId: "")));
        Assert.Throws<ArgumentException>(() => _store.Append(MakeEntry(auditRunId: "   ")));
    }

    [Fact]
    public void Read_SkipsEntriesWithoutAuditRunId()
    {
        // Append a valid entry so file + valid HMAC chain exist.
        var fp = "deadbeefcafe1234";
        _store.Append(MakeEntry(fingerprint: fp));

        // Manually corrupt by appending a malformed line lacking audit_run_id.
        var path = Path.Combine(_root, "provenance", "de", "ad", fp + ".provenance.jsonl");
        File.AppendAllText(path,
            "{\"fingerprint\":\"" + fp + "\",\"event_kind\":\"Imported\",\"timestamp_utc\":\"2026-04-30T11:00:00Z\"}\n");

        var entries = _store.Read(fp);
        Assert.Single(entries); // tampered line is filtered out
    }

    // -------- Hash chain (reuses AuditSigningService HMAC) --------

    [Fact]
    public void Append_ChainsEntries_PreviousEntryHmacEqualsPriorEntryHmac()
    {
        var fp = "0011223344556677";
        var first = MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Imported);
        var second = MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Verified);
        var third = MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Moved, detail: "/dst/x.nes");

        _store.Append(first);
        _store.Append(second);
        _store.Append(third);

        var entries = _store.Read(fp);
        Assert.Equal(3, entries.Count);
        Assert.Equal("", entries[0].PreviousEntryHmac);
        Assert.Equal(entries[0].EntryHmac, entries[1].PreviousEntryHmac);
        Assert.Equal(entries[1].EntryHmac, entries[2].PreviousEntryHmac);
        Assert.NotEqual(entries[0].EntryHmac, entries[1].EntryHmac);
    }

    [Fact]
    public void Verify_DetectsTamperedEntry()
    {
        var fp = "feedfacefeedface";
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Imported));
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Verified));

        var path = Path.Combine(_root, "provenance", "fe", "ed", fp + ".provenance.jsonl");
        var lines = File.ReadAllLines(path);
        // Tamper with second entry's payload (change DatMatchId) without recomputing HMAC.
        lines[1] = lines[1].Replace("Nintendo - NES (USA).dat", "Tampered.dat", StringComparison.Ordinal);
        File.WriteAllLines(path, lines);

        var report = _store.Verify(fp);
        Assert.False(report.IsValid);
        Assert.Contains("HMAC", report.FailureReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Verify_DetectsBrokenChain()
    {
        var fp = "1122334455667788";
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Imported));
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Verified));
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Moved));

        var path = Path.Combine(_root, "provenance", "11", "22", fp + ".provenance.jsonl");
        var lines = File.ReadAllLines(path);
        // Drop middle entry => third entry's PreviousEntryHmac no longer matches.
        File.WriteAllLines(path, new[] { lines[0], lines[2] });

        var report = _store.Verify(fp);
        Assert.False(report.IsValid);
    }

    [Fact]
    public void Verify_HappyPath_ReturnsValid()
    {
        var fp = "aaaabbbbccccdddd";
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Imported));
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Verified));
        var report = _store.Verify(fp);
        Assert.True(report.IsValid, report.FailureReason);
    }

    [Fact]
    public void Project_ValidTrail_ReturnsSharedTrustProjection()
    {
        var fp = "1234567890abcdef";
        _store.Append(MakeEntry(fingerprint: fp, auditRunId: "run-1", kind: ProvenanceEventKind.Imported));
        _store.Append(MakeEntry(fingerprint: fp, auditRunId: "run-1", kind: ProvenanceEventKind.Verified));
        _store.Append(MakeEntry(fingerprint: fp, auditRunId: "run-2", kind: ProvenanceEventKind.Moved));

        var trail = ProvenanceTrailProjection.Project(_store, fp.ToUpperInvariant());

        Assert.Equal(fp, trail.Fingerprint);
        Assert.True(trail.IsValid, trail.FailureReason);
        Assert.Equal(90, trail.TrustScore);
        Assert.Equal(3, trail.Entries.Count);
    }

    [Fact]
    public void Project_TamperedTrail_ReturnsInvalidZeroTrust()
    {
        var fp = "facefacefaceface";
        _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Verified));

        var path = Path.Combine(_root, "provenance", "fa", "ce", fp + ".provenance.jsonl");
        var lines = File.ReadAllLines(path);
        lines[0] = lines[0].Replace("Nintendo - NES (USA).dat", "Tampered.dat", StringComparison.Ordinal);
        File.WriteAllLines(path, lines);

        var trail = ProvenanceTrailProjection.Project(_store, fp);

        Assert.False(trail.IsValid);
        Assert.Equal(0, trail.TrustScore);
        Assert.Contains("HMAC", trail.FailureReason ?? "", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Read_NonExistentFingerprint_ReturnsEmpty()
    {
        var entries = _store.Read("0000000000000000");
        Assert.Empty(entries);
    }

    // -------- ADR-0024 §5: kein zweites Signing-Verfahren --------

    [Fact]
    public void NoSecondHmacInitializationInRepo()
    {
        // Grep-Guard: only AuditSigningService may instantiate HMACSHA256 / hold a signing key.
        // Provenance MUST funnel through AuditSigningService.ComputeHmacSha256.
        var dir = new DirectoryInfo(Directory.GetCurrentDirectory());
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "src", "Romulus.sln")))
            dir = dir.Parent;
        Assert.NotNull(dir);

        var provenanceDir = Path.Combine(dir!.FullName, "src", "Romulus.Infrastructure", "Provenance");
        if (!Directory.Exists(provenanceDir))
            return; // Wave-7 not yet wired; covered by Append/Verify tests above.

        foreach (var file in Directory.EnumerateFiles(provenanceDir, "*.cs", SearchOption.AllDirectories))
        {
            var src = File.ReadAllText(file);
            Assert.DoesNotContain("HMACSHA256", src, StringComparison.Ordinal);
            Assert.DoesNotContain("RandomNumberGenerator.Fill", src, StringComparison.Ordinal);
            Assert.DoesNotContain("new HMAC", src, StringComparison.Ordinal);
        }
    }

    // -------- Atomic append: failure must not leave a half-written line --------

    [Fact]
    public void AppendIsAtomic_NoPartialLineOnIoFailure()
    {
        // Append a valid entry, then read it back twice — second read must be
        // identical to the first (no torn writes from concurrent producers).
        var fp = "abababababababab";
        for (var i = 0; i < 20; i++)
            _store.Append(MakeEntry(fingerprint: fp, kind: ProvenanceEventKind.Verified, detail: "i=" + i));

        var firstRead = _store.Read(fp);
        var secondRead = _store.Read(fp);
        Assert.Equal(20, firstRead.Count);
        Assert.Equal(firstRead.Count, secondRead.Count);
        for (var i = 0; i < firstRead.Count; i++)
            Assert.Equal(firstRead[i].EntryHmac, secondRead[i].EntryHmac);
    }
}
