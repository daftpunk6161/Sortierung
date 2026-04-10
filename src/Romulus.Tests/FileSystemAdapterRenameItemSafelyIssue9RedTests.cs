using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-13): Tests define expected behavior for RenameItemSafely.
/// No production implementation is introduced in this step.
/// </summary>
public sealed class FileSystemAdapterRenameItemSafelyIssue9RedTests : IDisposable
{
    private readonly string _tempDir;
    private readonly FileSystemAdapter _fs;

    public FileSystemAdapterRenameItemSafelyIssue9RedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_RenameRed_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
        _fs = new FileSystemAdapter();
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, true);
        }
    }

    [Fact]
    public void RenameItemSafely_ShouldRenameWithinSameRoot_AndPreserveExtension_Issue9()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "old-name.nes");
        File.WriteAllText(sourcePath, "rom-data");

        // Act
        var renamedPath = _fs.RenameItemSafely(sourcePath, "new-name.nes");

        // Assert
        Assert.NotNull(renamedPath);
        Assert.False(File.Exists(sourcePath));
        Assert.True(File.Exists(renamedPath!));
        Assert.Equal(".nes", Path.GetExtension(renamedPath));
    }

    [Fact]
    public void RenameItemSafely_ShouldBlockTraversalInTargetFileName_Issue9()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "old-name.nes");
        File.WriteAllText(sourcePath, "rom-data");

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => _fs.RenameItemSafely(sourcePath, "..\\..\\Windows\\win.ini"));

        Assert.Contains("Blocked", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RenameItemSafely_ShouldBlockReservedWindowsDeviceName_Issue9()
    {
        // Arrange
        var sourcePath = Path.Combine(_tempDir, "old-name.nes");
        File.WriteAllText(sourcePath, "rom-data");

        // Act + Assert
        var ex = Assert.Throws<InvalidOperationException>(
            () => _fs.RenameItemSafely(sourcePath, "CON.nes"));

        Assert.Contains("reserved", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
