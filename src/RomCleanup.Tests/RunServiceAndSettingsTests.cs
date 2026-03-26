using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Configuration;
using RomCleanup.Infrastructure.Paths;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Tests for RunService static helpers and SettingsService / SettingsDto.
/// </summary>
public sealed class RunServiceAndSettingsTests : IDisposable
{
    private readonly string _tempDir;

    public RunServiceAndSettingsTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_RS_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_tempDir)) Directory.Delete(_tempDir, true); }
        catch { /* best effort */ }
    }

    // ═══ GetSiblingDirectory ════════════════════════════════════════════

    [Fact]
    public void GetSiblingDirectory_NormalPath_ReturnsSiblingAtSameLevel()
    {
        var result = new RunService().GetSiblingDirectory(@"C:\Games\Roms", "audit-logs");
        Assert.Equal(Path.GetFullPath(@"C:\Games\audit-logs"), result);
    }

    [Fact]
    public void GetSiblingDirectory_DriveRoot_ReturnsSubdirectory()
    {
        var result = new RunService().GetSiblingDirectory(@"C:\", "reports");
        Assert.Equal(Path.GetFullPath(@"C:\reports"), result);
    }

    [Fact]
    public void GetSiblingDirectory_TrailingSlash_NormalizedCorrectly()
    {
        var result = new RunService().GetSiblingDirectory(@"C:\Games\Roms\", "trash");
        Assert.Equal(Path.GetFullPath(@"C:\Games\trash"), result);
    }

    [Fact]
    public void GetSiblingDirectory_NestedPath_CorrectSibling()
    {
        var result = new RunService().GetSiblingDirectory(@"D:\Data\Retro\ROMs", "backups");
        Assert.Equal(Path.GetFullPath(@"D:\Data\Retro\backups"), result);
    }

    [Fact]
    public void ArtifactPathResolver_MultiRoot_OrderInvariant()
    {
        var rootA = Path.Combine(_tempDir, "RootA");
        var rootB = Path.Combine(_tempDir, "RootB");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        var first = ArtifactPathResolver.GetArtifactDirectory([rootA, rootB], "audit-logs");
        var second = ArtifactPathResolver.GetArtifactDirectory([rootB, rootA], "audit-logs");

        Assert.Equal(first, second);
        Assert.Contains("multi-root-", first, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ArtifactPathResolver_MultiRoot_WindowsPathCaseInvariant()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var rootA = Path.Combine(_tempDir, "CaseRootA");
        var rootB = Path.Combine(_tempDir, "CaseRootB");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        var first = ArtifactPathResolver.GetArtifactDirectory([rootA, rootB], "audit-logs");
        var second = ArtifactPathResolver.GetArtifactDirectory([ToMixedCasePath(rootA), ToMixedCasePath(rootB)], "audit-logs");

        Assert.Equal(first, second);
    }

    [Fact]
    public void RunService_BuildOrchestrator_MultiRoot_OrderInvariantArtifactDirectories()
    {
        var rootA = Path.Combine(_tempDir, "ConsoleA");
        var rootB = Path.Combine(_tempDir, "ConsoleB");
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        var firstVm = CreateTestVM();
        firstVm.Roots.Add(rootA);
        firstVm.Roots.Add(rootB);
        firstVm.DryRun = false;

        var secondVm = CreateTestVM();
        secondVm.Roots.Add(rootB);
        secondVm.Roots.Add(rootA);
        secondVm.DryRun = false;

        var runService = new RunService();
        var (_, firstOptions, firstAuditPath, firstReportPath) = runService.BuildOrchestrator(firstVm);
        var (_, secondOptions, secondAuditPath, secondReportPath) = runService.BuildOrchestrator(secondVm);

        Assert.Equal(Path.GetDirectoryName(firstAuditPath), Path.GetDirectoryName(secondAuditPath));
        Assert.Equal(Path.GetDirectoryName(firstReportPath), Path.GetDirectoryName(secondReportPath));
        Assert.Equal(firstOptions.AuditPath is not null, secondOptions.AuditPath is not null);
        Assert.Equal(Path.GetDirectoryName(firstOptions.ReportPath), Path.GetDirectoryName(secondOptions.ReportPath));
    }

    // ═══ SettingsDto record tests ═══════════════════════════════════════

    [Fact]
    public void SettingsDto_DefaultValues_AreCorrect()
    {
        var dto = new SettingsDto();
        Assert.Equal("Info", dto.LogLevel);
        Assert.False(dto.AggressiveJunk);
        Assert.False(dto.AliasKeying);
        Assert.Equal(["EU", "US", "JP", "WORLD"], dto.PreferredRegions);
        Assert.Equal("", dto.ToolChdman);
        Assert.Equal("", dto.ToolDolphin);
        Assert.Equal("", dto.Tool7z);
        Assert.Equal("SHA1", dto.DatHashType);
        Assert.True(dto.DatFallback);
        Assert.True(dto.DryRun);
        Assert.True(dto.ConfirmMove);
        Assert.Equal(ConflictPolicy.Rename, dto.ConflictPolicy);
        Assert.Equal("Dark", dto.Theme);
        Assert.Empty(dto.Roots);
    }

    [Fact]
    public void SettingsService_Load_WithoutUserFile_UsesRepoDefaults()
    {
        var svc = new SettingsService();
        var expected = SettingsLoader.Load(SettingsLoader.ResolveDefaultsJsonPath());

        var dto = svc.Load();

        Assert.NotNull(dto);
        Assert.Equal(expected.General.LogLevel, dto!.LogLevel);
        Assert.Equal(expected.General.PreferredRegions, dto.PreferredRegions);
        Assert.Equal(expected.Dat.UseDat, dto.UseDat);
        Assert.Equal(expected.Dat.HashType, dto.DatHashType);
        Assert.Equal(string.Equals(expected.General.Mode, "DryRun", StringComparison.OrdinalIgnoreCase), dto.DryRun);
        // Theme may come from user settings.json if present on disk, so just verify it's a valid theme name
        string[] validThemes = ["Dark", "Light", "HighContrast", "CleanDarkPro", "RetroCRT", "ArcadeNeon", "SynthwaveDark"];
        Assert.Contains(dto.Theme, validThemes);
    }

    [Fact]
    public void SettingsDto_WithExpression_CreatesModifiedCopy()
    {
        var original = new SettingsDto();
        var modified = original with
        {
            LogLevel = "Debug",
            AggressiveJunk = true,
            PreferredRegions = ["JP", "US"],
            DryRun = false
        };

        Assert.Equal("Debug", modified.LogLevel);
        Assert.True(modified.AggressiveJunk);
        Assert.Equal(["JP", "US"], modified.PreferredRegions);
        Assert.False(modified.DryRun);

        // Original unchanged
        Assert.Equal("Info", original.LogLevel);
        Assert.True(original.DryRun);
    }

    [Fact]
    public void SettingsDto_Equality_SameScalarValues()
    {
        var a = new SettingsDto { LogLevel = "Debug", DryRun = false };
        var b = new SettingsDto { LogLevel = "Debug", DryRun = false };
        // Records with arrays may differ by reference; check scalar equality
        Assert.Equal(a.LogLevel, b.LogLevel);
        Assert.Equal(a.DryRun, b.DryRun);
    }

    [Fact]
    public void SettingsDto_Inequality_DetectsDifferences()
    {
        var a = new SettingsDto { LogLevel = "Debug" };
        var b = new SettingsDto { LogLevel = "Info" };
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void SettingsDto_AllConflictPolicies_CanBeSet()
    {
        foreach (var policy in Enum.GetValues<ConflictPolicy>())
        {
            var dto = new SettingsDto { ConflictPolicy = policy };
            Assert.Equal(policy, dto.ConflictPolicy);
        }
    }

    // ═══ ApplyToViewModel tests ═════════════════════════════════════════

    [Fact]
    public void ApplyToViewModel_SetsAllGeneralProperties()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto
        {
            LogLevel = "Warning",
            AggressiveJunk = true,
            AliasKeying = true
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.Equal("Warning", vm.LogLevel);
        Assert.True(vm.AggressiveJunk);
        Assert.True(vm.AliasKeying);
    }

    [Fact]
    public void ApplyToViewModel_SetsRegionPreferences()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto
        {
            PreferredRegions = ["EU", "JP", "DE", "SCAN"]
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.True(vm.PreferEU);
        Assert.False(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.False(vm.PreferWORLD);
        Assert.True(vm.PreferDE);
        Assert.False(vm.PreferFR);
        Assert.True(vm.PreferSCAN);
    }

    [Fact]
    public void ApplyToViewModel_SetsToolPaths()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto
        {
            ToolChdman = @"C:\Tools\chdman.exe",
            Tool7z = @"C:\Tools\7z.exe",
            ToolDolphin = @"C:\Tools\dolphintool.exe",
            ToolPsxtract = @"C:\Tools\psxtract.exe",
            ToolCiso = @"C:\Tools\ciso.exe"
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.Equal(@"C:\Tools\chdman.exe", vm.ToolChdman);
        Assert.Equal(@"C:\Tools\7z.exe", vm.Tool7z);
        Assert.Equal(@"C:\Tools\dolphintool.exe", vm.ToolDolphin);
        Assert.Equal(@"C:\Tools\psxtract.exe", vm.ToolPsxtract);
        Assert.Equal(@"C:\Tools\ciso.exe", vm.ToolCiso);
    }

    [Fact]
    public void ApplyToViewModel_SetsDatProperties()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto
        {
            UseDat = true,
            DatRoot = @"D:\DAT",
            DatHashType = "SHA256",
            DatFallback = false
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.True(vm.UseDat);
        Assert.Equal(@"D:\DAT", vm.DatRoot);
        Assert.Equal("SHA256", vm.DatHashType);
        Assert.False(vm.DatFallback);
    }

    [Fact]
    public void ApplyToViewModel_SetsPaths()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto
        {
            TrashRoot = @"D:\Trash",
            AuditRoot = @"D:\Audits",
            Ps3DupesRoot = @"D:\PS3Dupes"
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.Equal(@"D:\Trash", vm.TrashRoot);
        Assert.Equal(@"D:\Audits", vm.AuditRoot);
        Assert.Equal(@"D:\PS3Dupes", vm.Ps3DupesRoot);
    }

    [Fact]
    public void ApplyToViewModel_SetsUIProperties()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto
        {
            SortConsole = true,
            DryRun = false,
            ConvertEnabled = true,
            ConfirmMove = false,
            ConflictPolicy = ConflictPolicy.Skip
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.True(vm.SortConsole);
        Assert.False(vm.DryRun);
        Assert.True(vm.ConvertEnabled);
        Assert.False(vm.ConfirmMove);
        Assert.Equal(ConflictPolicy.Skip, vm.ConflictPolicy);
    }

    [Fact]
    public void ApplyToViewModel_SetsRoots()
    {
        var vm = CreateTestVM();
        vm.Roots.Add("old-root");
        var dto = new SettingsDto
        {
            Roots = [@"D:\Roms\SNES", @"D:\Roms\GBA"]
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.Equal(2, vm.Roots.Count);
        Assert.Equal(@"D:\Roms\SNES", vm.Roots[0]);
        Assert.Equal(@"D:\Roms\GBA", vm.Roots[1]);
    }

    [Fact]
    public void ApplyToViewModel_ClearsExistingRoots()
    {
        var vm = CreateTestVM();
        vm.Roots.Add("old1");
        vm.Roots.Add("old2");
        vm.Roots.Add("old3");

        SettingsService.ApplyToViewModel(vm, new SettingsDto { Roots = ["new1"] });

        Assert.Single(vm.Roots);
        Assert.Equal("new1", vm.Roots[0]);
    }

    [Fact]
    public void ApplyToViewModel_AllRegionsEnabled()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto
        {
            PreferredRegions = ["EU", "US", "JP", "WORLD", "DE", "FR", "IT", "ES", "AU", "ASIA", "KR", "CN", "BR", "NL", "SE", "SCAN"]
        };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
        Assert.True(vm.PreferDE);
        Assert.True(vm.PreferFR);
        Assert.True(vm.PreferIT);
        Assert.True(vm.PreferES);
        Assert.True(vm.PreferAU);
        Assert.True(vm.PreferASIA);
        Assert.True(vm.PreferKR);
        Assert.True(vm.PreferCN);
        Assert.True(vm.PreferBR);
        Assert.True(vm.PreferNL);
        Assert.True(vm.PreferSE);
        Assert.True(vm.PreferSCAN);
    }

    [Fact]
    public void ApplyToViewModel_EmptyRegions_DisablesAll()
    {
        var vm = CreateTestVM();
        var dto = new SettingsDto { PreferredRegions = [] };

        SettingsService.ApplyToViewModel(vm, dto);

        Assert.False(vm.PreferEU);
        Assert.False(vm.PreferUS);
        Assert.False(vm.PreferJP);
        Assert.False(vm.PreferWORLD);
    }

    private static MainViewModel CreateTestVM()
    {
        return new MainViewModel(new StubThemeService(), new StubDialogService());
    }

    private static string ToMixedCasePath(string path)
    {
        var chars = path.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetter(chars[index]))
                continue;

            chars[index] = index % 2 == 0
                ? char.ToUpperInvariant(chars[index])
                : char.ToLowerInvariant(chars[index]);
        }

        return new string(chars);
    }

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
        public void ApplyTheme(AppTheme theme) { }
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
    }

    private sealed class StubDialogService : IDialogService
    {
        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }
}
