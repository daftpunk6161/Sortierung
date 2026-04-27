using Romulus.Core.Safety;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Deep Dive Audit – Safety / FileSystem / Security.
///
/// Konsolidierungs-Tests fuer doppelte safety-kritische Helfer.
///
/// Findings (Audit-Runde):
///   F1 (P2, Hygiene/Duplication): IsWindowsReservedDeviceName war doppelt definiert
///       in FileSystemAdapter (internal) und DatRenamePolicy (private). Doppelte
///       Wahrheit fuer Windows-Reservednames-Regel verstoesst gegen "Single Source
///       of Truth" in project.instructions.md / cleanup.instructions.md.
///   F2 (P2, Hygiene/Duplication): EnumerateDirectoriesWithoutFollowingReparsePoints
///       war identisch dupliziert in ArchiveHashService und DatRepositoryAdapter.
///       Sicherheitsrelevante Helper-Logik darf nur einmal existieren.
///   F3 (P2, Hygiene/Duplication): HasAvailableTempSpace war identisch dupliziert
///       in ArchiveHashService und DatRepositoryAdapter.
///
/// Diese Tests sichern die Konsolidierung in:
///   - Romulus.Core.Safety.WindowsFileNameRules
///   - Romulus.Infrastructure.Safety.FileSystemSafetyHelpers
/// </summary>
public sealed class SafetyHelpersConsolidationTests : IDisposable
{
    private readonly string _tempDir;

    public SafetyHelpersConsolidationTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"safetyhelpers_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
        {
            try { Directory.Delete(_tempDir, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ----- F1: WindowsFileNameRules.IsReservedDeviceName -----

    [Theory]
    [InlineData("CON", true)]
    [InlineData("PRN", true)]
    [InlineData("AUX", true)]
    [InlineData("NUL", true)]
    [InlineData("con", true)]
    [InlineData("Nul.txt", true)]
    [InlineData("COM1", true)]
    [InlineData("COM9", true)]
    [InlineData("COM0", true)]
    [InlineData("LPT1", true)]
    [InlineData("LPT9.dat", true)]
    [InlineData("CONFIG", false)]
    [InlineData("PRINTER", false)]
    [InlineData("COM10", false)]   // nur COM0-COM9 reserviert
    [InlineData("LPT", false)]
    [InlineData("game.rom", false)]
    [InlineData("", false)]
    [InlineData(".hidden", false)]
    public void WindowsFileNameRules_IsReservedDeviceName_MatchesCanonicalRule(string input, bool expected)
    {
        Assert.Equal(expected, WindowsFileNameRules.IsReservedDeviceName(input));
    }

    [Fact]
    public void WindowsFileNameRules_IsReservedDeviceName_HandlesNull()
    {
        Assert.False(WindowsFileNameRules.IsReservedDeviceName(null!));
    }

    // ----- F2: FileSystemSafetyHelpers.EnumerateDirectoriesWithoutFollowingReparsePoints -----

    [Fact]
    public void FileSystemSafetyHelpers_Enumerate_ReturnsAllDirectoriesDeterministically()
    {
        var a = Path.Combine(_tempDir, "a");
        var b = Path.Combine(_tempDir, "b");
        var aSub = Path.Combine(a, "sub");
        Directory.CreateDirectory(aSub);
        Directory.CreateDirectory(b);

        var result = FileSystemSafetyHelpers.EnumerateDirectoriesWithoutFollowingReparsePoints(_tempDir).ToList();

        Assert.Contains(a, result);
        Assert.Contains(b, result);
        Assert.Contains(aSub, result);
    }

    [Fact]
    public void FileSystemSafetyHelpers_Enumerate_OnEmptyDirectory_YieldsNoEntries()
    {
        var result = FileSystemSafetyHelpers.EnumerateDirectoriesWithoutFollowingReparsePoints(_tempDir).ToList();
        Assert.Empty(result);
    }

    [Fact]
    public void FileSystemSafetyHelpers_Enumerate_OnMissingDirectory_YieldsNoEntries()
    {
        var missing = Path.Combine(_tempDir, "does-not-exist");
        var result = FileSystemSafetyHelpers.EnumerateDirectoriesWithoutFollowingReparsePoints(missing).ToList();
        Assert.Empty(result);
    }

    // ----- F3: FileSystemSafetyHelpers.HasAvailableTempSpace -----

    [Fact]
    public void FileSystemSafetyHelpers_HasAvailableTempSpace_ZeroOrNegativeRequirement_ReturnsTrue()
    {
        Assert.True(FileSystemSafetyHelpers.HasAvailableTempSpace(_tempDir, 0));
        Assert.True(FileSystemSafetyHelpers.HasAvailableTempSpace(_tempDir, -1));
    }

    [Fact]
    public void FileSystemSafetyHelpers_HasAvailableTempSpace_SmallRequirement_OnRealVolume_ReturnsTrue()
    {
        Assert.True(FileSystemSafetyHelpers.HasAvailableTempSpace(_tempDir, 1024));
    }

    [Fact]
    public void FileSystemSafetyHelpers_HasAvailableTempSpace_InvalidPath_ReturnsFalse()
    {
        // Pfad ohne erkennbares Volumen-Root => konservativ false
        Assert.False(FileSystemSafetyHelpers.HasAvailableTempSpace(string.Empty, 1024));
    }

    // ----- Cross-check: Konsolidierung respektiert bestehende FileSystemAdapter-Semantik -----

    [Fact]
    public void FileSystemAdapter_IsWindowsReservedDeviceName_DelegatesToWindowsFileNameRules()
    {
        // Beide Helfer muessen identische Antworten liefern (Single Source of Truth).
        string[] cases = ["CON", "nul.txt", "COM1", "LPT9", "game.rom", "", "COM10"];
        foreach (var c in cases)
        {
            Assert.Equal(
                WindowsFileNameRules.IsReservedDeviceName(c),
                Romulus.Infrastructure.FileSystem.FileSystemAdapter.IsWindowsReservedDeviceName(c));
        }
    }
}
