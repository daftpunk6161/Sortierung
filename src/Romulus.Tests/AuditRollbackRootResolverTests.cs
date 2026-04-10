using System.Text;
using Romulus.Infrastructure.Audit;
using Xunit;

namespace Romulus.Tests;

public sealed class AuditRollbackRootResolverTests : IDisposable
{
    private readonly string _tempDir;

    public AuditRollbackRootResolverTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audit_roots_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public void Resolve_KeepsRestoreAndCurrentRootsSeparate()
    {
        var root = Path.Combine(_tempDir, "root");
        var trash = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trash);

        var auditPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(
            auditPath,
            "RootPath,OldPath,NewPath,Action\n" +
            $"{root},{Path.Combine(root, "game.zip")},{Path.Combine(trash, "game.zip")},MOVE\n",
            Encoding.UTF8);

        var audit = new AuditCsvStore();
        audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object>
        {
            ["AllowedRestoreRoots"] = new[] { root },
            ["AllowedCurrentRoots"] = new[] { trash }
        });

        var result = AuditRollbackRootResolver.Resolve(auditPath);

        Assert.Equal(new[] { Path.GetFullPath(root) }, result.RestoreRoots);
        Assert.Equal(new[] { Path.GetFullPath(trash) }, result.CurrentRoots);
    }

    [Fact]
    public void RollbackService_UsesMetadataCurrentRoots_ForExternalTrashRoot()
    {
        var root = Path.Combine(_tempDir, "restore");
        var trash = Path.Combine(_tempDir, "external-trash");
        var keyPath = Path.Combine(_tempDir, "audit-signing.key");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(trash);

        var oldPath = Path.Combine(root, "game.zip");
        var newPath = Path.Combine(trash, "game.zip");
        File.WriteAllText(newPath, "data");

        var auditPath = Path.Combine(_tempDir, "audit-external-trash.csv");
        File.WriteAllText(
            auditPath,
            "RootPath,OldPath,NewPath,Action\n" +
            $"{root},{oldPath},{newPath},MOVE\n",
            Encoding.UTF8);

        var audit = new AuditCsvStore(keyFilePath: keyPath);
        audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object>
        {
            ["AllowedRestoreRoots"] = new[] { root },
            ["AllowedCurrentRoots"] = new[] { trash }
        });

        var verify = RollbackService.VerifyTrashIntegrity(auditPath, [root], keyFilePath: keyPath);

        Assert.True(verify.DryRun);
        Assert.Equal(1, verify.EligibleRows);
        Assert.Equal(1, verify.DryRunPlanned);
    }
}
