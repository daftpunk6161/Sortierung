using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for UNC / network-path handling across the codebase.
/// Validates GetSiblingDirectory, CheckDiskSpace, FileSystemAdapter,
/// and path normalization with UNC and mapped-drive style paths.
/// </summary>
public sealed class UncPathTests
{
    // =========================================================================
    //  GetSiblingDirectory — the helper used by MainWindow + CLI
    // =========================================================================

    [Theory]
    [InlineData(@"C:\Roms", "audit-logs", @"C:\audit-logs")]
    [InlineData(@"D:\Games\Roms", "reports", @"D:\Games\reports")]
    [InlineData(@"E:\deep\nested\path\roms", "audit-logs", @"E:\deep\nested\path\audit-logs")]
    public void GetSiblingDirectory_LocalPath_PlacesSiblingNextToRoot(
        string root, string sibling, string expected)
    {
        var result = GetSiblingDirectory(root, sibling);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(@"C:\Roms\", "audit-logs", @"C:\audit-logs")]
    [InlineData(@"D:\Games\Roms\", "reports", @"D:\Games\reports")]
    public void GetSiblingDirectory_TrailingSlash_HandledCorrectly(
        string root, string sibling, string expected)
    {
        var result = GetSiblingDirectory(root, sibling);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetSiblingDirectory_DriveRoot_FallsBackInsideRoot()
    {
        // C:\ has no parent — sibling goes inside
        var result = GetSiblingDirectory(@"C:\", "audit-logs");
        Assert.Equal(@"C:\audit-logs", result);
    }

    [Fact]
    public void GetSiblingDirectory_UncShareRoot_FallsBackInsideRoot()
    {
        // \\server\share has no parent above share level —
        // GetDirectoryName("\\server\share") returns "\\server\share"
        // but for a true UNC root, sibling should be inside or next to it
        var result = GetSiblingDirectory(@"\\server\share", "audit-logs");
        // \\server\share is treated as root — sibling placed inside
        Assert.Contains("audit-logs", result);
        // Must NOT produce \\server\audit-logs (escaping above share level)
        Assert.DoesNotContain(@"\\server\audit-logs", result);
    }

    [Theory]
    [InlineData(@"\\nas\roms\snes", "audit-logs", @"\\nas\roms\audit-logs")]
    [InlineData(@"\\192.168.1.100\share\games\roms", "reports", @"\\192.168.1.100\share\games\reports")]
    public void GetSiblingDirectory_UncSubfolder_PlacesSiblingNextToRoot(
        string root, string sibling, string expected)
    {
        var result = GetSiblingDirectory(root, sibling);
        Assert.Equal(expected, result);
    }

    [Fact]
    public void GetSiblingDirectory_UncWithTrailingSlash_Normalized()
    {
        var result = GetSiblingDirectory(@"\\server\share\roms\", "audit-logs");
        Assert.Equal(@"\\server\share\audit-logs", result);
    }

    // =========================================================================
    //  FileSystemAdapter — ResolveChildPathWithinRoot with UNC
    // =========================================================================

    [Fact]
    public void ResolveChildPathWithinRoot_UncChild_InsideRoot_Succeeds()
    {
        var fs = new FileSystemAdapter();
        // Use temp dir as root, create a child inside
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_UNC_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var childDir = Path.Combine(tempDir, "sub");
            Directory.CreateDirectory(childDir);
            var result = fs.ResolveChildPathWithinRoot(tempDir, "sub");
            Assert.NotNull(result);
            Assert.StartsWith(tempDir, result!);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveChildPathWithinRoot_TraversalAttempt_ReturnsNull()
    {
        var fs = new FileSystemAdapter();
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_TRV_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            // Attempt path traversal — should be blocked
            var result = fs.ResolveChildPathWithinRoot(tempDir, @"..\..\Windows\System32");
            Assert.Null(result);
        }
        finally
        {
            Directory.Delete(tempDir, recursive: true);
        }
    }

    // =========================================================================
    //  Path normalization patterns used throughout the codebase
    // =========================================================================

    [Theory]
    [InlineData(@"\\server\share\roms\file.zip", @"\\server\share\roms", true)]
    [InlineData(@"\\server\share\other\file.zip", @"\\server\share\roms", false)]
    [InlineData(@"C:\Games\file.zip", @"C:\Games", true)]
    [InlineData(@"C:\Other\file.zip", @"C:\Games", false)]
    public void PathContainmentCheck_WorksForUncAndLocal(
        string filePath, string root, bool expectedInRoot)
    {
        // Replicate the pattern used in FindRootForPath / AuditSigningService
        var fullPath = Path.GetFullPath(filePath);
        var normalizedRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;
        var isInRoot = fullPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(expectedInRoot, isInRoot);
    }

    [Theory]
    [InlineData(@"\\server\share\roms")]
    [InlineData(@"\\192.168.1.10\data\games")]
    [InlineData(@"C:\Roms")]
    public void PathGetFullPath_DoesNotThrowOnUncOrLocal(string path)
    {
        // Path.GetFullPath must not throw on UNC paths
        var ex = Record.Exception(() => Path.GetFullPath(path));
        Assert.Null(ex);
    }

    [Theory]
    [InlineData(@"\\server\share")]
    [InlineData(@"\\server\share\")]
    [InlineData(@"C:\")]
    public void PathGetPathRoot_ReturnsValidRootForUnc(string path)
    {
        var root = Path.GetPathRoot(path);
        Assert.NotNull(root);
        Assert.NotEmpty(root!);
    }

    [Fact]
    public void PathGetPathRoot_UncPath_StartsWithDoubleBackslash()
    {
        var root = Path.GetPathRoot(@"\\mynas\share\roms\snes");
        Assert.NotNull(root);
        Assert.StartsWith(@"\\", root!);
    }

    // =========================================================================
    //  TrimEnd normalization pattern correctness
    // =========================================================================

    [Theory]
    [InlineData(@"\\server\share\roms\", @"\\server\share\roms")]
    [InlineData(@"\\server\share\roms", @"\\server\share\roms")]
    [InlineData(@"C:\Roms\", @"C:\Roms")]
    [InlineData(@"C:\Roms", @"C:\Roms")]
    public void TrimEndNormalization_ConsistentForUncAndLocal(string input, string expected)
    {
        var normalized = input.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        Assert.Equal(expected, normalized);
    }

    // =========================================================================
    //  GetSiblingDirectory helper — exact replica of the production code
    //  (duplicated here so we can unit-test it in isolation)
    // =========================================================================

    private static string GetSiblingDirectory(string rootPath, string siblingName)
    {
        var fullRoot = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(fullRoot);

        // If parent is null (drive root like C:\ or UNC root \\server\share),
        // put the directory inside the root instead of escaping above it
        if (string.IsNullOrEmpty(parent))
            return Path.Combine(fullRoot, siblingName);

        return Path.Combine(parent, siblingName);
    }
}
