using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using Romulus.Core.GameKeys;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

public sealed class Phase11RedTests : IDisposable
{
    private readonly string _tempDir;

    public Phase11RedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Phase11Red_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort.
        }
    }

    [Fact]
    public void TD028_GameKeyNormalizer_UsesSingleAtomicRegisteredStateField()
    {
        var type = typeof(GameKeyNormalizer);
        var flags = BindingFlags.NonPublic | BindingFlags.Static;

        var atomicField = type.GetField("_registeredState", flags);
        var oldPatternsField = type.GetField("_registeredPatterns", flags);
        var oldAliasField = type.GetField("_registeredAliasMap", flags);

        Assert.NotNull(atomicField);
        Assert.Null(oldPatternsField);
        Assert.Null(oldAliasField);
    }

    [Fact]
    public void TD032_AuditSigningService_WhenKeyPersistenceFails_ThrowsInvalidOperation()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var restrictedDir = Path.Combine(_tempDir, "restricted");
        Directory.CreateDirectory(restrictedDir);
        var keyPath = Path.Combine(restrictedDir, "hmac.key");

        var directoryInfo = new DirectoryInfo(restrictedDir);
        var acl = directoryInfo.GetAccessControl();
        var currentUser = WindowsIdentity.GetCurrent().User
                          ?? throw new InvalidOperationException("Could not resolve current user SID.");

        var denyRule = new FileSystemAccessRule(
            currentUser,
            FileSystemRights.CreateFiles | FileSystemRights.WriteData | FileSystemRights.AppendData,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None,
            AccessControlType.Deny);

        acl.AddAccessRule(denyRule);
        directoryInfo.SetAccessControl(acl);

        try
        {
            var sut = new AuditSigningService(new FileSystemAdapter(), keyFilePath: keyPath);

            Assert.Throws<InvalidOperationException>(() => sut.ComputeHmacSha256("payload"));
        }
        finally
        {
            var restoreAcl = directoryInfo.GetAccessControl();
            restoreAcl.RemoveAccessRuleSpecific(denyRule);
            directoryInfo.SetAccessControl(restoreAcl);
        }
    }

    [Fact]
    public void TD033_AuditSigningService_Rollback_MustNotUseFileReadAllLines()
    {
        var sourcePath = ResolveRepoFile("Romulus.Infrastructure", "Audit", "AuditSigningService.cs");
        Assert.True(File.Exists(sourcePath), $"Missing source file: {sourcePath}");

        var source = File.ReadAllText(sourcePath);
        Assert.DoesNotContain("File.ReadAllLines(auditCsvPath", source, StringComparison.Ordinal);
    }

    private static string ResolveRepoFile(params string[] segments)
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null)
        {
            if (File.Exists(Path.Combine(current.FullName, "Romulus.sln")))
            {
                if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Infrastructure"))
                    && Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests")))
                {
                    return Path.Combine([current.FullName, .. segments]);
                }

                return Path.Combine([current.FullName, "src", .. segments]);
            }

            if (Directory.Exists(Path.Combine(current.FullName, "Romulus.Tests"))
                && Directory.Exists(Path.Combine(current.FullName, "Romulus.Infrastructure")))
            {
                return Path.Combine([current.FullName, .. segments]);
            }
            current = current.Parent;
        }

        return Path.Combine(segments);
    }
}
