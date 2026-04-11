using System.Collections.Concurrent;
using System.Reflection;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.RegularExpressions;
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
    public async Task TD028_GameKeyNormalizer_ConcurrentRegistration_DoesNotProduceHybridResults()
    {
        var flags = BindingFlags.NonPublic | BindingFlags.Static;
        var stateField = typeof(GameKeyNormalizer).GetField("_registeredState", flags)
                         ?? throw new InvalidOperationException("_registeredState field not found.");
        var factoryField = typeof(GameKeyNormalizer).GetField("_patternFactory", flags)
                           ?? throw new InvalidOperationException("_patternFactory field not found.");

        var originalState = stateField.GetValue(null);
        var originalFactory = factoryField.GetValue(null);

        var patternsA = new[]
        {
            new Regex(@"\s*\(A\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250))
        };
        var aliasesA = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["game"] = "alpha"
        };

        var patternsB = new[]
        {
            new Regex(@"\s*\(B\)$", RegexOptions.IgnoreCase | RegexOptions.Compiled, TimeSpan.FromMilliseconds(250))
        };
        var aliasesB = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["game"] = "beta"
        };

        try
        {
            var invalidResults = new ConcurrentBag<string>();
            var workerErrors = new ConcurrentQueue<Exception>();
            var iteration = 0;
            const int writerIterations = 10_000;
            const int readerIterations = 3_000;

            var writer = Task.Run(() =>
            {
                var toggle = 0;
                for (var i = 0; i < writerIterations; i++)
                {
                    if ((toggle++ & 1) == 0)
                        GameKeyNormalizer.RegisterDefaultPatterns(patternsA, aliasesA);
                    else
                        GameKeyNormalizer.RegisterDefaultPatterns(patternsB, aliasesB);
                }
            });

            var readers = Enumerable.Range(0, Math.Max(2, Environment.ProcessorCount / 2))
                .Select(_ => Task.Run(() =>
                {
                    try
                    {
                        for (var i = 0; i < readerIterations; i++)
                        {
                            var current = Interlocked.Increment(ref iteration);
                            var input = (current & 1) == 0 ? "Game (A)" : "Game (B)";
                            var normalized = GameKeyNormalizer.Normalize(input);

                            var valid = input.EndsWith("(A)", StringComparison.Ordinal)
                                ? normalized is "alpha" or "game(a)"
                                : normalized is "beta" or "game(b)";

                            if (!valid)
                                invalidResults.Add($"{input}->{normalized}");
                        }
                    }
                    catch (Exception ex)
                    {
                        workerErrors.Enqueue(ex);
                    }
                }))
                .ToArray();

            await Task.WhenAll(readers.Prepend(writer));

            Assert.Empty(workerErrors);
            Assert.Empty(invalidResults);
        }
        finally
        {
            stateField.SetValue(null, originalState);
            factoryField.SetValue(null, originalFactory);
        }
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

            Assert.False(File.Exists(keyPath), "Unsafely persisted key file must be deleted on security failure.");
            Assert.False(File.Exists(keyPath + ".tmp"), "Temporary key file must be cleaned up on security failure.");
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

    [Fact]
    public void TD033_AuditSigningService_ReadAuditRowsReverse_HandlesLargeAuditInput()
    {
        var auditPath = Path.Combine(_tempDir, "large-audit.csv");
        const int rowCount = 20_000;

        using (var writer = new StreamWriter(auditPath, append: false, Encoding.UTF8))
        {
            writer.WriteLine("RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");
            for (var i = 0; i < rowCount; i++)
            {
                writer.WriteLine($"C:\\Roms,C:\\Roms\\old-{i}.zip,C:\\Trash\\new-{i}.zip,MOVE,GAME,,,2026-01-01T00:00:00.0000000Z");
            }
        }

        var method = typeof(AuditSigningService).GetMethod("ReadAuditRowsReverse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var rows = method!.Invoke(null, new object[] { auditPath }) as IEnumerable<string>;
        Assert.NotNull(rows);

        var seen = 0;
        string? first = null;
        string? last = null;
        foreach (var row in rows!)
        {
            if (seen == 0)
                first = row;

            last = row;
            seen++;
        }

        Assert.Equal(rowCount, seen);
        Assert.Contains($"old-{rowCount - 1}.zip", first);
        Assert.Contains("old-0.zip", last);
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
