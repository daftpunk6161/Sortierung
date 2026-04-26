using System.Security.Cryptography;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests.Audit;

/// <summary>
/// Move + Rollback hash round-trip invariants.
///
/// Invariant: A file moved into the trash via the safe move + audit pipeline,
/// then rolled back from the audit, must reappear at its original path with
/// byte-identical content (hash equality) and identical filename.
///
/// This guards against silent file corruption during the move/rollback cycle
/// for the data-loss prevention contract.
/// </summary>
public sealed class MoveRollbackRoundTripTests : IDisposable
{
    private readonly string _tempDir;

    public MoveRollbackRoundTripTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_B2_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, recursive: true); }
        catch { /* best-effort */ }
    }

    [Fact]
    public void AuditRollback_MoveToTrashThenRollback_RestoresIdenticalBytesAtOriginalPath()
    {
        var rootDir = Path.Combine(_tempDir, "root");
        var trashDir = Path.Combine(rootDir, "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(trashDir);

        // Deterministic non-trivial payload (multiple chunks).
        var payload = new byte[1024 * 32];
        new Random(4242).NextBytes(payload);

        var sourcePath = Path.Combine(rootDir, "Game (USA).bin");
        File.WriteAllBytes(sourcePath, payload);
        var originalHash = Sha256(sourcePath);

        var trashPath = Path.Combine(trashDir, "Game (USA).bin");
        var fs = new FileSystemAdapter();
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var store = new AuditCsvStore(fs);

        // 1) Move source -> trash.
        var movedPath = fs.MoveItemSafely(sourcePath, trashPath);
        Assert.NotNull(movedPath);
        Assert.False(File.Exists(sourcePath), "Source must no longer exist at original path after move.");
        Assert.True(File.Exists(trashPath));
        Assert.Equal(originalHash, Sha256(trashPath));

        // 2) Append audit row reflecting the move.
        store.AppendAuditRow(
            auditPath,
            rootPath: rootDir,
            oldPath: sourcePath,
            newPath: trashPath,
            action: "MOVE",
            category: "Game",
            hash: originalHash,
            reason: "B2-roundtrip");

        // 3) Rollback live.
        var restored = store.Rollback(
            auditPath,
            allowedRestoreRoots: [rootDir],
            allowedCurrentRoots: [rootDir],
            dryRun: false);

        Assert.NotEmpty(restored);
        Assert.True(File.Exists(sourcePath), "Source must exist at original path after rollback.");
        Assert.False(File.Exists(trashPath), "Trash copy must not remain after live rollback.");

        // 4) Hash + bytes equal.
        Assert.Equal(originalHash, Sha256(sourcePath));
        Assert.Equal(payload, File.ReadAllBytes(sourcePath));
    }

    [Fact]
    public void AuditRollback_DryRun_DoesNotMutateAnyBytes()
    {
        var rootDir = Path.Combine(_tempDir, "root");
        var trashDir = Path.Combine(rootDir, "_TRASH_JUNK");
        Directory.CreateDirectory(rootDir);
        Directory.CreateDirectory(trashDir);

        var payload = new byte[2048];
        new Random(99).NextBytes(payload);

        var sourcePath = Path.Combine(rootDir, "Junk.bin");
        File.WriteAllBytes(sourcePath, payload);
        var originalHash = Sha256(sourcePath);

        var trashPath = Path.Combine(trashDir, "Junk.bin");
        var fs = new FileSystemAdapter();
        var auditPath = Path.Combine(_tempDir, "audit.csv");
        var store = new AuditCsvStore(fs);

        Assert.NotNull(fs.MoveItemSafely(sourcePath, trashPath));
        store.AppendAuditRow(auditPath, rootDir, sourcePath, trashPath, "MOVE", "Junk", originalHash, "junk");

        var planned = store.Rollback(auditPath, [rootDir], [rootDir], dryRun: true);
        Assert.Single(planned);

        // DryRun must not mutate either side.
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(trashPath));
        Assert.Equal(originalHash, Sha256(trashPath));
    }

    private static string Sha256(string path)
    {
        using var s = File.OpenRead(path);
        var h = SHA256.HashData(s);
        return Convert.ToHexString(h);
    }
}
