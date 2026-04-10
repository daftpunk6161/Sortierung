using Romulus.Contracts.Models;
using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

public class DatCatalogStateServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly string _datRoot;
    private readonly string _statePath;

    public DatCatalogStateServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_DatCat_" + Guid.NewGuid().ToString("N")[..8]);
        _datRoot = Path.Combine(_tempDir, "dats");
        _statePath = Path.Combine(_tempDir, "dat-catalog-state.json");
        Directory.CreateDirectory(_datRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══ DOWNLOAD STRATEGY TESTS ════════════════════════════════════════

    [Fact]
    public void DetermineDownloadStrategy_RawDatWithUrl_ReturnsAuto()
    {
        var entry = new DatCatalogEntry { Format = "raw-dat", Url = "https://example.com/test.dat", Group = "FBNEO" };
        Assert.Equal(DatDownloadStrategy.Auto, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    [Fact]
    public void DetermineDownloadStrategy_ZipDatWithUrl_ReturnsAuto()
    {
        var entry = new DatCatalogEntry { Format = "zip-dat", Url = "https://example.com/test.zip", Group = "Non-Redump" };
        Assert.Equal(DatDownloadStrategy.Auto, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    [Fact]
    public void DetermineDownloadStrategy_NoIntroPack_ReturnsPackImport()
    {
        var entry = new DatCatalogEntry { Format = "nointro-pack", Url = "", Group = "No-Intro" };
        Assert.Equal(DatDownloadStrategy.PackImport, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    [Fact]
    public void DetermineDownloadStrategy_Redump_AlwaysManualLogin()
    {
        var entry = new DatCatalogEntry { Format = "zip-dat", Url = "https://redump.org/datfile/ps2/", Group = "Redump" };
        Assert.Equal(DatDownloadStrategy.ManualLogin, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    [Fact]
    public void DetermineDownloadStrategy_NoUrl_ReturnsManualLogin()
    {
        var entry = new DatCatalogEntry { Format = "raw-dat", Url = "", Group = "Custom" };
        Assert.Equal(DatDownloadStrategy.ManualLogin, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    [Fact]
    public void DetermineDownloadStrategy_7zDatWithUrl_ReturnsAuto()
    {
        var entry = new DatCatalogEntry { Format = "7z-dat", Url = "https://example.com/mame.7z", Group = "MAME" };
        Assert.Equal(DatDownloadStrategy.Auto, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    // ═══ STATE PERSISTENCE TESTS ════════════════════════════════════════

    [Fact]
    public void LoadState_MissingFile_ReturnsEmptyState()
    {
        var state = DatCatalogStateService.LoadState(Path.Combine(_tempDir, "nonexistent.json"));
        Assert.NotNull(state);
        Assert.Empty(state.Entries);
        Assert.Null(state.LastFullScan);
    }

    [Fact]
    public void LoadState_CorruptJsonFile_ReturnsEmptyState()
    {
        File.WriteAllText(_statePath, "{ this is not valid json }}}");
        var state = DatCatalogStateService.LoadState(_statePath);
        Assert.NotNull(state);
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void LoadState_EmptyFile_ReturnsEmptyState()
    {
        File.WriteAllText(_statePath, "");
        var state = DatCatalogStateService.LoadState(_statePath);
        Assert.NotNull(state);
        Assert.Empty(state.Entries);
    }

    [Fact]
    public void SaveAndLoad_StateRoundtrip_PreservesData()
    {
        var state = new DatCatalogState
        {
            LastFullScan = new DateTime(2026, 1, 15, 10, 30, 0, DateTimeKind.Utc),
            Entries = new Dictionary<string, DatLocalInfo>(StringComparer.OrdinalIgnoreCase)
            {
                ["fbneo-arcade"] = new DatLocalInfo
                {
                    InstalledDate = new DateTime(2026, 1, 10, 0, 0, 0, DateTimeKind.Utc),
                    FileSha256 = "abc123",
                    FileSizeBytes = 1234567,
                    LocalPath = @"C:\dat\fbneo-arcade.dat"
                }
            }
        };

        DatCatalogStateService.SaveState(_statePath, state);
        Assert.True(File.Exists(_statePath));

        var loaded = DatCatalogStateService.LoadState(_statePath);
        Assert.NotNull(loaded.LastFullScan);
        Assert.Single(loaded.Entries);
        Assert.True(loaded.Entries.ContainsKey("fbneo-arcade"));
        Assert.Equal("abc123", loaded.Entries["fbneo-arcade"].FileSha256);
        Assert.Equal(1234567, loaded.Entries["fbneo-arcade"].FileSizeBytes);
    }

    [Fact]
    public void SaveState_CreatesDirectoryIfMissing()
    {
        var nestedPath = Path.Combine(_tempDir, "nested", "deep", "state.json");
        var state = new DatCatalogState();
        DatCatalogStateService.SaveState(nestedPath, state);
        Assert.True(File.Exists(nestedPath));
    }

    [Fact]
    public void SaveState_CreatesBackup()
    {
        // Save initial state
        var state = new DatCatalogState();
        state.Entries["test-id"] = new DatLocalInfo { LocalPath = "first.dat" };
        DatCatalogStateService.SaveState(_statePath, state);

        // Save updated state  
        state.Entries["test-id"] = new DatLocalInfo { LocalPath = "second.dat" };
        DatCatalogStateService.SaveState(_statePath, state);

        // Backup should exist
        Assert.True(File.Exists(_statePath + ".bak"));
    }

    // ═══ BUILD CATALOG STATUS TESTS ═════════════════════════════════════

    [Fact]
    public void BuildCatalogStatus_MissingDat_StatusIsMissing()
    {
        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "fbneo-arcade", Group = "FBNEO", System = "FBN Arcade", ConsoleKey = "ARCADE", Format = "raw-dat", Url = "https://example.com" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Missing, result[0].Status);
        Assert.Equal(DatDownloadStrategy.Auto, result[0].DownloadStrategy);
    }

    [Fact]
    public void BuildCatalogStatus_InstalledDat_StatusIsInstalled()
    {
        // Create a local DAT file matching by Id
        var datPath = Path.Combine(_datRoot, "fbneo-arcade.dat");
        File.WriteAllText(datPath, "<xml>test</xml>");
        // Set recent write time
        File.SetLastWriteTime(datPath, DateTime.Now.AddDays(-30));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "fbneo-arcade", Group = "FBNEO", System = "FBN Arcade", ConsoleKey = "ARCADE", Format = "raw-dat", Url = "https://example.com" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Installed, result[0].Status);
        Assert.NotNull(result[0].LocalPath);
        Assert.Contains("fbneo-arcade.dat", result[0].LocalPath);
    }

    [Fact]
    public void BuildCatalogStatus_StaleDat_StatusIsStale()
    {
        var datPath = Path.Combine(_datRoot, "old-dat.dat");
        File.WriteAllText(datPath, "<xml>old</xml>");
        // Set old write time
        File.SetLastWriteTime(datPath, DateTime.Now.AddDays(-400));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "old-dat", Group = "Custom", System = "Old System", ConsoleKey = "OLD", Format = "raw-dat", Url = "https://example.com" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Stale, result[0].Status);
    }

    [Fact]
    public void BuildCatalogStatus_MatchesByConsoleKey()
    {
        // File named by ConsoleKey, not by Id
        var datPath = Path.Combine(_datRoot, "GBA.dat");
        File.WriteAllText(datPath, "<xml>gba</xml>");
        File.SetLastWriteTime(datPath, DateTime.Now.AddDays(-10));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "nointro-gba", Group = "No-Intro", System = "Nintendo - Game Boy Advance", ConsoleKey = "GBA", Format = "nointro-pack" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Installed, result[0].Status);
    }

    [Fact]
    public void BuildCatalogStatus_MatchesBySystemPrefix_Redump()
    {
        // Redump DAT files use full system names like "Acorn - Archimedes - Datfile (77) (2025-10-23 18-11-28).dat"
        var datPath = Path.Combine(_datRoot, "Acorn - Archimedes - Datfile (77) (2025-10-23 18-11-28).dat");
        File.WriteAllText(datPath, "<xml>redump</xml>");
        File.SetLastWriteTime(datPath, DateTime.Now.AddDays(-5));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "redump-acd", Group = "Redump", System = "Acorn - Archimedes", ConsoleKey = "ACD", Format = "zip-dat", Url = "https://redump.org" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Installed, result[0].Status);
        Assert.Contains("Acorn - Archimedes", result[0].LocalPath);
    }

    [Fact]
    public void BuildCatalogStatus_MatchesBySystemPrefix_NoIntro()
    {
        // No-Intro DAT filename starts with system name
        var datPath = Path.Combine(_datRoot, "Nintendo - Switch (Digital) (2026-03-15).dat");
        File.WriteAllText(datPath, "<xml>nointro</xml>");
        File.SetLastWriteTime(datPath, DateTime.Now.AddDays(-3));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "nointro-switch", Group = "No-Intro", System = "Nintendo - Switch", ConsoleKey = "SWITCH", Format = "nointro-pack" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Installed, result[0].Status);
    }

    [Fact]
    public void BuildCatalogStatus_DuplicateStem_PicksAlphabeticallyFirstPath()
    {
        var dirB = Path.Combine(_datRoot, "B");
        var dirA = Path.Combine(_datRoot, "A");
        Directory.CreateDirectory(dirB);
        Directory.CreateDirectory(dirA);

        var pathB = Path.Combine(dirB, "dup.dat");
        var pathA = Path.Combine(dirA, "dup.dat");
        File.WriteAllText(pathB, "b");
        File.WriteAllText(pathA, "a");

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "dup", Group = "Test", System = "Duplicate Stem", ConsoleKey = "DUP", Format = "raw-dat", Url = "https://example.com" }
        };

        var state = new DatCatalogState();
        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Installed, result[0].Status);
        Assert.Equal(pathA, result[0].LocalPath, ignoreCase: true);
    }

    [Fact]
    public void BuildCatalogStatus_SystemPrefixMultiMatch_PicksAlphabeticallyFirstStem()
    {
        var newer = Path.Combine(_datRoot, "Nintendo - Switch (2026-12-31).dat");
        var older = Path.Combine(_datRoot, "Nintendo - Switch (2025-01-01).dat");
        File.WriteAllText(newer, "<xml>new</xml>");
        File.WriteAllText(older, "<xml>old</xml>");

        var catalog = new List<DatCatalogEntry>
        {
            new()
            {
                Id = "nointro-switch-pack",
                Group = "No-Intro",
                System = "Nintendo - Switch",
                ConsoleKey = "NSW_PACK",
                Format = "nointro-pack"
            }
        };

        var state = new DatCatalogState();
        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Installed, result[0].Status);
        Assert.Equal(older, result[0].LocalPath, ignoreCase: true);
    }

    [Fact]
    public void BuildCatalogStatus_NonExistentDatRoot_AllMissing()
    {
        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "test", Group = "Test", System = "Test", ConsoleKey = "T", Format = "raw-dat" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, @"C:\nonexistent\path", state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Missing, result[0].Status);
    }

    [Fact]
    public void BuildCatalogStatus_NullDatRoot_AllMissing()
    {
        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "test", Group = "Test", System = "Test", ConsoleKey = "T", Format = "raw-dat" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, null, state);

        Assert.Single(result);
        Assert.Equal(DatInstallStatus.Missing, result[0].Status);
    }

    [Fact]
    public void BuildCatalogStatus_MultipleCatalogEntries_CorrectCounts()
    {
        // 1 installed, 1 missing, 1 stale
        File.WriteAllText(Path.Combine(_datRoot, "installed.dat"), "<xml/>");
        File.SetLastWriteTime(Path.Combine(_datRoot, "installed.dat"), DateTime.Now.AddDays(-10));

        File.WriteAllText(Path.Combine(_datRoot, "stale.dat"), "<xml/>");
        File.SetLastWriteTime(Path.Combine(_datRoot, "stale.dat"), DateTime.Now.AddDays(-400));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "installed", Group = "A", System = "Installed System", ConsoleKey = "INS", Format = "raw-dat", Url = "https://a" },
            new() { Id = "missing", Group = "B", System = "Missing System", ConsoleKey = "MIS", Format = "raw-dat", Url = "https://b" },
            new() { Id = "stale", Group = "C", System = "Stale System", ConsoleKey = "STL", Format = "raw-dat", Url = "https://c" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Equal(3, result.Count);
        Assert.Equal(1, result.Count(r => r.Status == DatInstallStatus.Installed));
        Assert.Equal(1, result.Count(r => r.Status == DatInstallStatus.Missing));
        Assert.Equal(1, result.Count(r => r.Status == DatInstallStatus.Stale));
    }

    // ═══ UPDATE STATE AFTER DOWNLOAD ════════════════════════════════════

    [Fact]
    public void UpdateStateAfterDownload_TracksEntry()
    {
        var datPath = Path.Combine(_datRoot, "new.dat");
        File.WriteAllText(datPath, "test-content-for-hash");

        var state = new DatCatalogState();
        DatCatalogStateService.UpdateStateAfterDownload(state, "new-dat-id", datPath, 21);

        Assert.True(state.Entries.ContainsKey("new-dat-id"));
        Assert.Equal(datPath, state.Entries["new-dat-id"].LocalPath);
        Assert.Equal(21, state.Entries["new-dat-id"].FileSizeBytes);
        Assert.NotEmpty(state.Entries["new-dat-id"].FileSha256);
    }

    [Fact]
    public void UpdateStateAfterDownload_OverwritesExistingEntry()
    {
        var datPath = Path.Combine(_datRoot, "updated.dat");
        File.WriteAllText(datPath, "v1");

        var state = new DatCatalogState();
        DatCatalogStateService.UpdateStateAfterDownload(state, "upd", datPath, 2);

        File.WriteAllText(datPath, "v2-longer-content");
        DatCatalogStateService.UpdateStateAfterDownload(state, "upd", datPath, 18);

        Assert.Equal(18, state.Entries["upd"].FileSizeBytes);
    }

    // ═══ FULL SCAN ══════════════════════════════════════════════════════

    [Fact]
    public void FullScan_PopulatesStateForInstalledDats()
    {
        File.WriteAllText(Path.Combine(_datRoot, "test-dat.dat"), "<xml/>");
        File.SetLastWriteTime(Path.Combine(_datRoot, "test-dat.dat"), DateTime.Now.AddDays(-5));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "test-dat", Group = "Test", System = "Test System", ConsoleKey = "TST", Format = "raw-dat" }
        };

        var state = DatCatalogStateService.FullScan(catalog, _datRoot, new DatCatalogState());

        Assert.NotNull(state.LastFullScan);
        Assert.True(state.Entries.ContainsKey("test-dat"));
    }

    [Fact]
    public void FullScan_RemovesMissingEntriesFromState()
    {
        var state = new DatCatalogState();
        state.Entries["gone"] = new DatLocalInfo
        {
            LocalPath = Path.Combine(_datRoot, "gone.dat"),
            InstalledDate = DateTime.UtcNow.AddDays(-10)
        };

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "gone", Group = "Test", System = "Gone System", ConsoleKey = "GONE", Format = "raw-dat" }
        };

        state = DatCatalogStateService.FullScan(catalog, _datRoot, state);

        Assert.False(state.Entries.ContainsKey("gone"));
    }

    [Fact]
    public void FullScan_NonExistentDatRoot_ReturnsStateUnchanged()
    {
        var state = new DatCatalogState();
        var result = DatCatalogStateService.FullScan([], @"C:\nonexistent", state);
        Assert.Same(state, result);
    }

    // ═══ DETERMINISM INVARIANTS ═════════════════════════════════════════

    [Fact]
    public void BuildCatalogStatus_IsDeterministic_SameInputsSameOutputs()
    {
        File.WriteAllText(Path.Combine(_datRoot, "det-test.dat"), "<xml/>");
        File.SetLastWriteTime(Path.Combine(_datRoot, "det-test.dat"), DateTime.Now.AddDays(-50));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "det-test", Group = "A", System = "Det", ConsoleKey = "DET", Format = "raw-dat", Url = "https://a" },
            new() { Id = "det-missing", Group = "B", System = "Miss", ConsoleKey = "MIS", Format = "nointro-pack" }
        };
        var state = new DatCatalogState();

        var result1 = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);
        var result2 = DatCatalogStateService.BuildCatalogStatus(catalog, _datRoot, state);

        Assert.Equal(result1.Count, result2.Count);
        for (int i = 0; i < result1.Count; i++)
        {
            Assert.Equal(result1[i].Id, result2[i].Id);
            Assert.Equal(result1[i].Status, result2[i].Status);
            Assert.Equal(result1[i].DownloadStrategy, result2[i].DownloadStrategy);
        }
    }

    [Theory]
    [InlineData("raw-dat", "FBNEO", "https://url", DatDownloadStrategy.Auto)]
    [InlineData("zip-dat", "Non-Redump", "https://url", DatDownloadStrategy.Auto)]
    [InlineData("7z-dat", "MAME", "https://url", DatDownloadStrategy.Auto)]
    [InlineData("nointro-pack", "No-Intro", "", DatDownloadStrategy.PackImport)]
    [InlineData("zip-dat", "Redump", "https://redump.org", DatDownloadStrategy.ManualLogin)]
    [InlineData("raw-dat", "Custom", "", DatDownloadStrategy.ManualLogin)]
    public void DetermineDownloadStrategy_AllCombinations(string format, string group, string url, DatDownloadStrategy expected)
    {
        var entry = new DatCatalogEntry { Format = format, Group = group, Url = url };
        Assert.Equal(expected, DatCatalogStateService.DetermineDownloadStrategy(entry));
    }

    // ═══ MERGE CATALOGS ═════════════════════════════════════════════════

    [Fact]
    public void MergeCatalogs_ReturnsBuiltinWhenNoUserEntries()
    {
        var builtin = new List<DatCatalogEntry>
        {
            new() { Id = "a", System = "A" },
            new() { Id = "b", System = "B" }
        };
        var state = new DatCatalogState();

        var result = DatCatalogStateService.MergeCatalogs(builtin, state);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void MergeCatalogs_UserEntriesAppended()
    {
        var builtin = new List<DatCatalogEntry>
        {
            new() { Id = "builtin-1", System = "Builtin" }
        };
        var state = new DatCatalogState();
        state.UserEntries.Add(new DatUserEntry { Id = "user-ps2", System = "My PS2", ConsoleKey = "PS2" });

        var result = DatCatalogStateService.MergeCatalogs(builtin, state);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, e => e.Id == "user-ps2");
        Assert.Contains(result, e => e.Id == "builtin-1");
    }

    [Fact]
    public void MergeCatalogs_RemovedBuiltinExcluded()
    {
        var builtin = new List<DatCatalogEntry>
        {
            new() { Id = "keep", System = "Keep" },
            new() { Id = "remove-me", System = "Remove" }
        };
        var state = new DatCatalogState();
        state.RemovedBuiltinIds.Add("remove-me");

        var result = DatCatalogStateService.MergeCatalogs(builtin, state);

        Assert.Single(result);
        Assert.Equal("keep", result[0].Id);
    }

    [Fact]
    public void MergeCatalogs_RemovedAndUserCombined()
    {
        var builtin = new List<DatCatalogEntry>
        {
            new() { Id = "a", System = "A" },
            new() { Id = "b", System = "B" },
            new() { Id = "c", System = "C" }
        };
        var state = new DatCatalogState();
        state.RemovedBuiltinIds.Add("b");
        state.UserEntries.Add(new DatUserEntry { Id = "user-x", System = "X", ConsoleKey = "X" });

        var result = DatCatalogStateService.MergeCatalogs(builtin, state);

        Assert.Equal(3, result.Count); // a, c, user-x
        Assert.DoesNotContain(result, e => e.Id == "b");
        Assert.Contains(result, e => e.Id == "user-x");
    }

    [Fact]
    public void MergeCatalogs_RemovedIsCaseInsensitive()
    {
        var builtin = new List<DatCatalogEntry>
        {
            new() { Id = "Redump-PS2", System = "PS2" }
        };
        var state = new DatCatalogState();
        state.RemovedBuiltinIds.Add("redump-ps2");

        var result = DatCatalogStateService.MergeCatalogs(builtin, state);

        Assert.Empty(result);
    }

    [Fact]
    public void SaveAndLoad_PreservesUserEntriesAndRemovedIds()
    {
        var state = new DatCatalogState();
        state.UserEntries.Add(new DatUserEntry
        {
            Id = "user-test",
            System = "Test System",
            ConsoleKey = "TST",
            Url = "https://example.com",
            Format = "raw-dat",
            Group = "Benutzerdefiniert"
        });
        state.RemovedBuiltinIds.Add("removed-builtin");

        DatCatalogStateService.SaveState(_statePath, state);
        var loaded = DatCatalogStateService.LoadState(_statePath);

        Assert.Single(loaded.UserEntries);
        Assert.Equal("user-test", loaded.UserEntries[0].Id);
        Assert.Equal("Test System", loaded.UserEntries[0].System);
        Assert.Contains("removed-builtin", loaded.RemovedBuiltinIds);
    }

    // ═══ FULLSCAN REGRESSION: INSTALLEDDATE STABILITY ════════════════════

    [Fact]
    public void FullScan_ExistingEntry_DoesNotResetInstalledDate()
    {
        var datPath = Path.Combine(_datRoot, "stable.dat");
        File.WriteAllText(datPath, "<xml/>");
        File.SetLastWriteTimeUtc(datPath, DateTime.UtcNow.AddDays(-5));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "stable", Group = "Test", System = "Stable System", ConsoleKey = "STB", Format = "raw-dat" }
        };

        // First scan — establishes InstalledDate
        var state = DatCatalogStateService.FullScan(catalog, _datRoot, new DatCatalogState());
        Assert.True(state.Entries.ContainsKey("stable"));
        var originalDate = state.Entries["stable"].InstalledDate;
        var originalHash = state.Entries["stable"].FileSha256;

        // Second scan — must NOT reset InstalledDate when path is unchanged
        state = DatCatalogStateService.FullScan(catalog, _datRoot, state);
        Assert.Equal(originalDate, state.Entries["stable"].InstalledDate);
        Assert.Equal(originalHash, state.Entries["stable"].FileSha256);
    }

    [Fact]
    public void FullScan_MovedEntry_UpdatesStateWithNewPath()
    {
        var datPath1 = Path.Combine(_datRoot, "moved.dat");
        File.WriteAllText(datPath1, "<xml-moved/>");
        File.SetLastWriteTimeUtc(datPath1, DateTime.UtcNow.AddDays(-3));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "moved", Group = "Test", System = "Moved System", ConsoleKey = "MOV", Format = "raw-dat" }
        };

        // First scan
        var state = DatCatalogStateService.FullScan(catalog, _datRoot, new DatCatalogState());
        var originalPath = state.Entries["moved"].LocalPath;

        // Simulate file being replaced at a different detected path
        // by modifying the state's tracked path to simulate a path mismatch
        state.Entries["moved"].LocalPath = @"C:\old\path\moved.dat";

        // Second scan — path changed, so state should be updated
        state = DatCatalogStateService.FullScan(catalog, _datRoot, state);
        Assert.Equal(originalPath, state.Entries["moved"].LocalPath);
    }

    [Fact]
    public void FullScan_StaleEntry_StaysStale_WithoutResettingInstalledDate()
    {
        var datPath = Path.Combine(_datRoot, "stale-stable.dat");
        File.WriteAllText(datPath, "<xml-stale/>");
        File.SetLastWriteTimeUtc(datPath, DateTime.UtcNow.AddDays(-400));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "stale-stable", Group = "Test", System = "Stale Stable", ConsoleKey = "SSL", Format = "raw-dat" }
        };

        // First scan — picks up stale entry
        var state = DatCatalogStateService.FullScan(catalog, _datRoot, new DatCatalogState());
        Assert.True(state.Entries.ContainsKey("stale-stable"));
        var firstDate = state.Entries["stale-stable"].InstalledDate;

        // Second scan — must preserve InstalledDate even for stale entries
        state = DatCatalogStateService.FullScan(catalog, _datRoot, state);
        Assert.Equal(firstDate, state.Entries["stale-stable"].InstalledDate);
    }

    [Fact]
    public void FullScan_IsDeterministic_StateUnchangedBetweenScans()
    {
        File.WriteAllText(Path.Combine(_datRoot, "det-fs.dat"), "<xml-det/>");
        File.SetLastWriteTimeUtc(Path.Combine(_datRoot, "det-fs.dat"), DateTime.UtcNow.AddDays(-10));

        var catalog = new List<DatCatalogEntry>
        {
            new() { Id = "det-fs", Group = "Test", System = "Det FS", ConsoleKey = "DFS", Format = "raw-dat" }
        };

        var state1 = DatCatalogStateService.FullScan(catalog, _datRoot, new DatCatalogState());
        var hash1 = state1.Entries["det-fs"].FileSha256;
        var date1 = state1.Entries["det-fs"].InstalledDate;
        var path1 = state1.Entries["det-fs"].LocalPath;

        var state2 = DatCatalogStateService.FullScan(catalog, _datRoot, state1);
        Assert.Equal(hash1, state2.Entries["det-fs"].FileSha256);
        Assert.Equal(date1, state2.Entries["det-fs"].InstalledDate);
        Assert.Equal(path1, state2.Entries["det-fs"].LocalPath);
    }
}
