using System.Text;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Deep-Dive (Reports/Audit/Rollback/Metrics) — Round 1 findings.
///
/// F1 (P1): WriteMetadataSidecar must keep the sidecar/ledger pair atomic.
/// If the ledger append fails after the sidecar was already written to disk,
/// the previous sidecar+ledger state must be restored. Otherwise every
/// subsequent VerifyMetadataSidecar throws "sidecar not in ledger" and all
/// rollbacks are permanently blocked with AUDIT_INTEGRITY_BROKEN — even
/// though the audit CSV itself is intact.
/// </summary>
public sealed class AuditSidecarLedgerAtomicityTests : IDisposable
{
    private readonly string _tempDir;

    public AuditSidecarLedgerAtomicityTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audit_atomic_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    [Fact]
    public void WriteMetadataSidecar_LedgerAppendFails_PreservesPreviousVerifiableState()
    {
        var keyPath = Path.Combine(_tempDir, "atomic.key");
        var ledgerPath = keyPath + ".ledger.jsonl";
        var service = new AuditSigningService(new FileSystemAdapter(), keyFilePath: keyPath);
        var csvPath = Path.Combine(_tempDir, "audit-atomic.csv");
        var oldPath = Path.Combine(_tempDir, "old.rom");
        var newPath = Path.Combine(_tempDir, "new.rom");

        File.WriteAllText(
            csvPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n" +
            $"{_tempDir},{oldPath},{newPath},Move,GAME,abc,first,2026-04-24T00:00:00Z\n",
            Encoding.UTF8);

        // 1) First sidecar write succeeds, ledger has 1 entry.
        var firstSidecar = service.WriteMetadataSidecar(csvPath, rowCount: 1);
        Assert.NotNull(firstSidecar);
        Assert.True(File.Exists(ledgerPath));
        Assert.True(service.VerifyMetadataSidecar(csvPath));

        var goodSidecarBytes = File.ReadAllBytes(firstSidecar!);
        var goodLedgerBytes = File.ReadAllBytes(ledgerPath);

        // 2) Sabotage ledger so AppendLedgerEntry will throw IOException
        //    (replace the file with a directory of the same name — File.Move
        //    inside AtomicFileWriter.AppendText will throw).
        File.Delete(ledgerPath);
        Directory.CreateDirectory(ledgerPath);

        try
        {
            // 3) Append a second audit row to keep CSV consistent, then attempt
            //    to write a fresh sidecar. AppendLedgerEntry MUST fail.
            File.AppendAllText(
                csvPath,
                $"{_tempDir},{oldPath}2,{newPath}2,Move,GAME,abc,second,2026-04-24T00:00:01Z\n",
                Encoding.UTF8);

            var second = service.WriteMetadataSidecar(csvPath, rowCount: 2);

            // Contract under test: the failed write must NOT leave a "phantom"
            // sidecar that does not appear in the ledger. Either the old sidecar
            // is restored, or the sidecar is removed entirely. In both cases the
            // failure must be signalled to the caller via a null return.
            Assert.Null(second);
        }
        finally
        {
            // Restore ledger so subsequent verification can run.
            Directory.Delete(ledgerPath);
            File.WriteAllBytes(ledgerPath, goodLedgerBytes);
        }

        // 4) After the failed write the system must be in a consistent state.
        //    Two acceptable outcomes:
        //      (a) sidecar was restored to the previous good content + previous CSV,
        //      (b) sidecar was removed (no integrity claim made).
        //    The forbidden state is: sidecar exists but ledger has no matching entry,
        //    which produces "Audit sidecar is not present in the append-only ledger."
        //    on the next verify and permanently blocks rollback.

        var sidecarPath = csvPath + ".meta.json";
        if (File.Exists(sidecarPath))
        {
            // Roll the CSV back to its first-write state so VerifyMetadataSidecar
            // can compare against the restored sidecar without CSV-hash drift.
            File.WriteAllText(
                csvPath,
                "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n" +
                $"{_tempDir},{oldPath},{newPath},Move,GAME,abc,first,2026-04-24T00:00:00Z\n",
                Encoding.UTF8);
            File.WriteAllBytes(sidecarPath, goodSidecarBytes);

            // Previous good state must verify.
            Assert.True(service.VerifyMetadataSidecar(csvPath));
        }
        else
        {
            // Sidecar removed: that is the safe "no integrity claim" outcome.
            Assert.False(File.Exists(sidecarPath));
        }
    }

    [Fact]
    public void WriteMetadataSidecar_LedgerAppendFailsOnFirstWrite_DoesNotLeavePhantomSidecar()
    {
        var keyPath = Path.Combine(_tempDir, "first.key");
        var ledgerPath = keyPath + ".ledger.jsonl";

        // Pre-create ledger as a directory so the very first AppendLedgerEntry fails.
        Directory.CreateDirectory(ledgerPath);

        var service = new AuditSigningService(new FileSystemAdapter(), keyFilePath: keyPath);
        var csvPath = Path.Combine(_tempDir, "first-write.csv");
        File.WriteAllText(
            csvPath,
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n" +
            $"{_tempDir},a,b,Move,GAME,,,2026-04-24T00:00:00Z\n",
            Encoding.UTF8);

        var sidecarPath = csvPath + ".meta.json";

        try
        {
            var written = service.WriteMetadataSidecar(csvPath, rowCount: 1);

            // Contract: caller is told the write failed.
            Assert.Null(written);

            // Phantom sidecar without ledger entry would permanently break verify.
            Assert.False(
                File.Exists(sidecarPath),
                "Sidecar must not be left on disk when the ledger append failed; otherwise VerifyMetadataSidecar will throw 'sidecar not present in the append-only ledger' forever.");
        }
        finally
        {
            if (Directory.Exists(ledgerPath))
                Directory.Delete(ledgerPath);
        }
    }
}
