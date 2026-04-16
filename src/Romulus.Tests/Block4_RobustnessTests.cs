using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Block 4 – Robustheit + Ressourcen-Sicherheit
/// R6-07: ArchiveHashService muss Cache invalidieren wenn Datei geändert wurde
/// R6-09: TrayService.Toggle muss _isCreating in try/finally zurücksetzen
/// RED tests: fail now, GREEN after implementing fixes.
/// </summary>
public sealed class Block4_RobustnessTests : IDisposable
{
    private readonly string _tempDir;

    public Block4_RobustnessTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Block4_" + Guid.NewGuid().ToString("N"));
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
            // best-effort cleanup
        }
    }

    // ═══ Helper ═════════════════════════════════════════════════════════

    private static string FindRepoFile(params string[] parts)
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var repoRoot = Directory.GetParent(dataDir)?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved from data directory.");
        return Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    }

    private static void WriteZip(string path, string entryName, byte[] content)
    {
        using var fs = new FileStream(path, FileMode.Create, FileAccess.Write);
        using var zip = new ZipArchive(fs, ZipArchiveMode.Create, leaveOpen: false);
        var entry = zip.CreateEntry(entryName, CompressionLevel.Fastest);
        using var entryStream = entry.Open();
        entryStream.Write(content);
    }

    private static string Sha1Hex(byte[] data)
    {
        var hash = SHA1.HashData(data);
        return Convert.ToHexString(hash);
    }

    // ═══ R6-07 ══════════════════════════════════════════════════════════

    [Fact]
    public void R6_07_ArchiveHashService_Cache_MustInvalidate_WhenZipFileIsModified()
    {
        // ARRANGE – create version-1 ZIP
        var zipPath = Path.Combine(_tempDir, "game.zip");
        var v1Content = "content-version-one"u8.ToArray();
        var v2Content = "content-version-two-longer"u8.ToArray(); // different size → staleness detectable
        WriteZip(zipPath, "game.rom", v1Content);

        var sut = new ArchiveHashService(toolRunner: null, maxEntries: 100);

        // ACT – populate the cache with version-1 hashes
        var firstResult = sut.GetArchiveHashes(zipPath, "SHA1");
        Assert.NotEmpty(firstResult); // sanity: ZIP was hashed successfully

        var firstWriteUtc = File.GetLastWriteTimeUtc(zipPath);

        // Replace the ZIP with version-2 content (different data, different size)
        WriteZip(zipPath, "game.rom", v2Content);
        // Force a distinct timestamp without relying on wall-clock sleeps.
        File.SetLastWriteTimeUtc(zipPath, firstWriteUtc.AddSeconds(1));

        // ACT – request hashes again for the same path
        var secondResult = sut.GetArchiveHashes(zipPath, "SHA1");

        // ASSERT – the result must reflect the new file content, not the cached version-1 hashes
        Assert.NotEmpty(secondResult);
        Assert.NotEqual(firstResult[0], secondResult[0]);
    }

    [Fact]
    public void R6_07_ArchiveHashService_Cache_MustReturn_SameResult_WhenFileIsUnchanged()
    {
        // ARRANGE
        var zipPath = Path.Combine(_tempDir, "stable.zip");
        WriteZip(zipPath, "rom.bin", "stable-content"u8.ToArray());
        var sut = new ArchiveHashService(toolRunner: null, maxEntries: 100);

        // ACT – two calls, file not modified
        var first = sut.GetArchiveHashes(zipPath, "SHA1");
        var second = sut.GetArchiveHashes(zipPath, "SHA1");

        // ASSERT – cache must return the same value when file is unchanged
        Assert.NotEmpty(first);
        Assert.Equal(first[0], second[0]);
        Assert.Equal(1, sut.CacheCount);
    }

    // ═══ R6-09 ══════════════════════════════════════════════════════════

    [Fact]
    public void R6_09_TrayService_Toggle_MustHaveTryFinally_ToResetIsCreatingFlag()
    {
        // ARRANGE – read TrayService source
        var path = FindRepoFile("src", "Romulus.UI.Wpf", "Services", "TrayService.cs");
        var source = File.ReadAllText(path);

        // ACT – locate the positions of the guard flag assignments
        int setTrueIdx  = source.IndexOf("_isCreating = true;",  StringComparison.Ordinal);
        int setFalseIdx = source.IndexOf("_isCreating = false;", StringComparison.Ordinal);

        Assert.True(setTrueIdx  >= 0, "Expected '_isCreating = true;'  in TrayService.cs");
        Assert.True(setFalseIdx >  0, "Expected '_isCreating = false;' in TrayService.cs");

        // ASSERT – a 'finally' block must appear between the two assignments
        // so that the flag is reset even when an exception is thrown during tray-icon creation.
        int finallyIdx = source.IndexOf("finally", setTrueIdx, StringComparison.Ordinal);
        Assert.True(
            finallyIdx > setTrueIdx && finallyIdx < setFalseIdx,
            "Expected a 'finally' block between '_isCreating = true' and '_isCreating = false' in TrayService.Toggle(). " +
            "Without it, an exception during icon creation leaves _isCreating permanently true, blocking future tray creation.");
    }
}
