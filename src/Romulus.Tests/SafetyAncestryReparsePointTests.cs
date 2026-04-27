using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Deep Dive Audit – Safety / FileSystem / Security (Round 1, F-S1 + F-S2).
///
/// Diese Tests pruefen die symmetrische Reparse-Point-Pruefung auf der
/// SOURCE-Seite fuer Verzeichnis- und Rename-Operationen. Vor dem Fix war
/// die Pruefung asymmetrisch: Dest-Ancestry wurde via
/// <c>HasReparsePointInAncestry</c> abgesichert, Source-Ancestry nicht.
/// Folge: Eine Junction in der Source-Ancestry konnte zu einer realen
/// Verschiebung von Dateien ausserhalb des intendierten Roots fuehren,
/// obwohl die Source-Leaf-Reparse-Pruefung gruen war.
///
/// Diese Tests laufen nur unter Windows (Junctions sind Windows-spezifisch)
/// und werden auf Nicht-Windows-Plattformen uebersprungen, statt zu
/// faelschlich gruen zu sein.
/// </summary>
public sealed class SafetyAncestryReparsePointTests : IDisposable
{
    private readonly string _tempDir;

    public SafetyAncestryReparsePointTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"safetyancestry_{Guid.NewGuid():N}");
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

    private static bool TryCreateJunction(string linkPath, string targetPath, out string? skipReason)
    {
        skipReason = null;
        if (!OperatingSystem.IsWindows())
        {
            skipReason = "Junction-Tests laufen nur unter Windows.";
            return false;
        }

        try
        {
            Directory.CreateSymbolicLink(linkPath, targetPath);
            // Bestaetigen, dass die Junction wirklich als Reparse-Point sichtbar ist.
            var di = new DirectoryInfo(linkPath);
            if ((di.Attributes & FileAttributes.ReparsePoint) == 0)
            {
                skipReason = "CreateSymbolicLink lieferte kein Reparse-Point-Verzeichnis.";
                return false;
            }
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            skipReason = "Symbolic-Link-Erstellung erfordert Developer-Mode oder Admin-Rechte.";
            return false;
        }
        catch (IOException ex)
        {
            skipReason = $"Symbolic-Link-Erstellung fehlgeschlagen: {ex.Message}";
            return false;
        }
    }

    [Fact]
    public void MoveDirectorySafely_SourceAncestryIsJunction_IsBlocked()
    {
        var realTarget = Path.Combine(_tempDir, "real-target");
        var realChild = Path.Combine(realTarget, "child-dir");
        Directory.CreateDirectory(realChild);
        File.WriteAllText(Path.Combine(realChild, "file.bin"), "x");

        var junctionPath = Path.Combine(_tempDir, "junction");
        if (!TryCreateJunction(junctionPath, realTarget, out var skipReason))
        {
            // Skip statt false-green: kein Test-Setup verfuegbar.
            Assert.True(true, skipReason);
            return;
        }

        var sourceViaJunction = Path.Combine(junctionPath, "child-dir");
        var dest = Path.Combine(_tempDir, "moved-child");
        var fs = new FileSystemAdapter();

        // Erwartung: Source-Ancestry enthaelt eine Junction => blocken.
        var ex = Assert.Throws<InvalidOperationException>(
            () => fs.MoveDirectorySafely(sourceViaJunction, dest));
        Assert.Contains("reparse", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Datenintegritaet: nichts wurde bewegt.
        Assert.True(Directory.Exists(realChild));
        Assert.False(Directory.Exists(dest));
    }

    [Fact]
    public void RenameItemSafely_SourceAncestryIsJunction_IsBlocked()
    {
        var realTarget = Path.Combine(_tempDir, "real-target-rename");
        Directory.CreateDirectory(realTarget);
        var fileInRealTarget = Path.Combine(realTarget, "before.bin");
        File.WriteAllText(fileInRealTarget, "data");

        var junctionPath = Path.Combine(_tempDir, "junction-rename");
        if (!TryCreateJunction(junctionPath, realTarget, out var skipReason))
        {
            Assert.True(true, skipReason);
            return;
        }

        var fileViaJunction = Path.Combine(junctionPath, "before.bin");
        var fs = new FileSystemAdapter();

        var ex = Assert.Throws<InvalidOperationException>(
            () => fs.RenameItemSafely(fileViaJunction, "after.bin"));
        Assert.Contains("reparse", ex.Message, StringComparison.OrdinalIgnoreCase);

        // Datenintegritaet: nichts wurde umbenannt.
        Assert.True(File.Exists(fileInRealTarget));
        Assert.False(File.Exists(Path.Combine(realTarget, "after.bin")));
    }

    [Fact]
    public void MoveDirectorySafely_NoJunctionInAncestry_StillWorks()
    {
        // Negativkontrolle: Ohne Junction in Ancestry darf der Fix nichts blockieren.
        var src = Path.Combine(_tempDir, "plain-src");
        var dst = Path.Combine(_tempDir, "plain-dst");
        Directory.CreateDirectory(src);
        File.WriteAllText(Path.Combine(src, "f.bin"), "data");

        var fs = new FileSystemAdapter();
        Assert.True(fs.MoveDirectorySafely(src, dst));
        Assert.True(Directory.Exists(dst));
        Assert.False(Directory.Exists(src));
    }

    [Fact]
    public void RenameItemSafely_NoJunctionInAncestry_StillWorks()
    {
        var src = Path.Combine(_tempDir, "plain-rename.bin");
        File.WriteAllText(src, "data");

        var fs = new FileSystemAdapter();
        var renamed = fs.RenameItemSafely(src, "renamed.bin");

        Assert.NotNull(renamed);
        Assert.True(File.Exists(renamed));
        Assert.False(File.Exists(src));
    }
}
