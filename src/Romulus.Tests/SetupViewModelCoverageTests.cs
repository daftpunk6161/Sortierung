using System.ComponentModel;
using Romulus.Contracts.Ports;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;
using ConflictPolicy = Romulus.UI.Wpf.Models.ConflictPolicy;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for SetupViewModel:
/// Settings round-trip (ApplySettingsDto/ToSettingsDto), validation (INotifyDataErrorInfo),
/// region preferences, extension/console filters, presets, theme, GameKey preview, config map.
/// </summary>
public sealed class SetupViewModelCoverageTests
{
    private sealed class StubTheme : IThemeService
    {
        public AppTheme Current { get; private set; } = AppTheme.Dark;
        public bool IsDark => Current is AppTheme.Dark or AppTheme.CleanDarkPro or AppTheme.RetroCRT or AppTheme.ArcadeNeon;
        public IReadOnlyList<AppTheme> AvailableThemes { get; } = Enum.GetValues<AppTheme>();
        public void ApplyTheme(AppTheme theme) => Current = theme;
        public void ApplyTheme(bool dark) => Current = dark ? AppTheme.Dark : AppTheme.Light;
        public void Toggle() => Current = Current == AppTheme.Dark ? AppTheme.CleanDarkPro : AppTheme.Dark;
    }

    private sealed class StubDialog : IDialogService
    {
        public string? NextBrowseFolder { get; set; }
        public string? NextBrowseFile { get; set; }
        public string? BrowseFolder(string title) => NextBrowseFolder;
        public string? BrowseFile(string title, string filter) => NextBrowseFile;
        public string? SaveFile(string title, string filter, string? defaultFileName) => null;
        public bool Confirm(string message, string title) => true;
        public void Info(string message, string title) { }
        public void Error(string message, string title) { }
        public ConfirmResult YesNoCancel(string message, string title) => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title, string defaultValue) => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel) => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Contracts.Models.ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<Contracts.Models.DatAuditEntry> renameProposals) => true;
    }

    private sealed class StubSettings : ISettingsService
    {
        public string? LastAuditPath { get; set; }
        public string LastTheme { get; set; } = "Dark";
        public SettingsDto? NextLoad { get; set; }
        public SettingsDto? Load() => NextLoad;
        public void LoadInto(MainViewModel vm) { }
        public bool SaveFrom(MainViewModel vm, string? lastAuditPath) => true;
    }

    private static SetupViewModel Create(
        StubTheme? theme = null,
        StubDialog? dialog = null,
        StubSettings? settings = null)
        => new(
            theme ?? new StubTheme(),
            dialog ?? new StubDialog(),
            settings ?? new StubSettings(),
            new LocalizationService());

    #region Settings DTO Round-Trip

    [Fact]
    public void ToSettingsDto_CapturesAllProperties()
    {
        var vm = Create();
        vm.LogLevel = "Debug";
        vm.AggressiveJunk = true;
        vm.AliasKeying = true;
        vm.ToolChdman = @"C:\tools\chdman.exe";
        vm.Tool7z = @"C:\tools\7z.exe";
        vm.ToolDolphin = @"C:\tools\dolphin.exe";
        vm.ToolPsxtract = @"C:\tools\psxtract.exe";
        vm.ToolCiso = @"C:\tools\ciso.exe";
        vm.UseDat = true;
        vm.DatRoot = @"C:\dats";
        vm.DatHashType = "SHA256";
        vm.DatFallback = true;
        vm.TrashRoot = @"C:\trash";
        vm.AuditRoot = @"C:\audit";
        vm.Ps3DupesRoot = @"C:\ps3";
        vm.SortConsole = true;
        vm.DryRun = false;
        vm.ConvertEnabled = true;
        vm.ConfirmMove = false;
        vm.ConflictPolicy = ConflictPolicy.Skip;

        var dto = vm.ToSettingsDto("last-audit", ["root1", "root2"]);

        Assert.Equal("Debug", dto.LogLevel);
        Assert.True(dto.AggressiveJunk);
        Assert.True(dto.AliasKeying);
        Assert.Equal(@"C:\tools\chdman.exe", dto.ToolChdman);
        Assert.Equal(@"C:\tools\7z.exe", dto.Tool7z);
        Assert.Equal(@"C:\tools\dolphin.exe", dto.ToolDolphin);
        Assert.Equal(@"C:\tools\psxtract.exe", dto.ToolPsxtract);
        Assert.Equal(@"C:\tools\ciso.exe", dto.ToolCiso);
        Assert.True(dto.UseDat);
        Assert.Equal(@"C:\dats", dto.DatRoot);
        Assert.Equal("SHA256", dto.DatHashType);
        Assert.True(dto.DatFallback);
        Assert.Equal(@"C:\trash", dto.TrashRoot);
        Assert.Equal(@"C:\audit", dto.AuditRoot);
        Assert.Equal(@"C:\ps3", dto.Ps3DupesRoot);
        Assert.True(dto.SortConsole);
        Assert.False(dto.DryRun);
        Assert.True(dto.ConvertEnabled);
        Assert.False(dto.ConfirmMove);
        Assert.Equal(ConflictPolicy.Skip, dto.ConflictPolicy);
        Assert.Equal("last-audit", dto.LastAuditPath);
        Assert.Equal(new[] { "root1", "root2" }, dto.Roots);
    }

    [Fact]
    public void ApplySettingsDto_RestoresAllProperties()
    {
        var vm = Create();
        var dto = new SettingsDto
        {
            LogLevel = "Trace",
            AggressiveJunk = true,
            AliasKeying = true,
            PreferredRegions = ["DE", "FR"],
            ToolChdman = @"C:\chdman",
            Tool7z = @"C:\7z",
            ToolDolphin = @"C:\dolphin",
            ToolPsxtract = @"C:\psxtract",
            ToolCiso = @"C:\ciso",
            UseDat = true,
            DatRoot = @"C:\dat",
            DatHashType = "CRC32",
            DatFallback = false,
            TrashRoot = @"C:\t",
            AuditRoot = @"C:\a",
            Ps3DupesRoot = @"C:\p",
            SortConsole = true,
            DryRun = false,
            ConvertEnabled = true,
            ConfirmMove = false,
            ConflictPolicy = ConflictPolicy.Overwrite,
        };

        vm.ApplySettingsDto(dto);

        Assert.Equal("Trace", vm.LogLevel);
        Assert.True(vm.AggressiveJunk);
        Assert.True(vm.AliasKeying);
        Assert.Equal(@"C:\chdman", vm.ToolChdman);
        Assert.Equal(@"C:\7z", vm.Tool7z);
        Assert.Equal(@"C:\dolphin", vm.ToolDolphin);
        Assert.Equal(@"C:\psxtract", vm.ToolPsxtract);
        Assert.Equal(@"C:\ciso", vm.ToolCiso);
        Assert.True(vm.UseDat);
        Assert.Equal(@"C:\dat", vm.DatRoot);
        Assert.Equal("CRC32", vm.DatHashType);
        Assert.False(vm.DatFallback);
        Assert.Equal(@"C:\t", vm.TrashRoot);
        Assert.Equal(@"C:\a", vm.AuditRoot);
        Assert.Equal(@"C:\p", vm.Ps3DupesRoot);
        Assert.True(vm.SortConsole);
        Assert.False(vm.DryRun);
        Assert.True(vm.ConvertEnabled);
        Assert.False(vm.ConfirmMove);
        Assert.Equal(ConflictPolicy.Overwrite, vm.ConflictPolicy);
    }

    [Fact]
    public void ApplySettingsDto_RegionsUpdated()
    {
        var vm = Create();
        vm.ApplySettingsDto(new SettingsDto { PreferredRegions = ["DE", "KR"] });

        var active = vm.GetPreferredRegions();
        Assert.Contains("DE", active);
        Assert.Contains("KR", active);
        Assert.DoesNotContain("EU", active);
        Assert.DoesNotContain("US", active);
    }

    [Fact]
    public void ToSettingsDto_DefaultRoots_EmptyWhenNull()
    {
        var vm = Create();
        var dto = vm.ToSettingsDto();
        Assert.Empty(dto.Roots);
    }

    #endregion

    #region Region Preferences

    [Fact]
    public void RegionItems_InitializedWith16Regions()
    {
        var vm = Create();
        Assert.Equal(16, vm.RegionItems.Count);
    }

    [Fact]
    public void GetPreferredRegions_DefaultsContainTop4()
    {
        var vm = Create();
        var regions = vm.GetPreferredRegions();
        Assert.Contains("EU", regions);
        Assert.Contains("US", regions);
        Assert.Contains("JP", regions);
        Assert.Contains("WORLD", regions);
        Assert.Equal(4, regions.Length);
    }

    [Fact]
    public void GetPreferredRegions_RespectsActiveAndPriority()
    {
        var vm = Create();
        foreach (var r in vm.RegionItems) r.IsActive = false;
        var de = vm.RegionItems.First(r => r.Code == "DE");
        var fr = vm.RegionItems.First(r => r.Code == "FR");
        de.IsActive = true;
        de.Priority = 1;
        fr.IsActive = true;
        fr.Priority = 0;

        var regions = vm.GetPreferredRegions();
        Assert.Equal(2, regions.Length);
        Assert.Equal("FR", regions[0]);
        Assert.Equal("DE", regions[1]);
    }

    #endregion

    #region Extension Filters

    [Fact]
    public void ExtensionFilters_Initialized()
    {
        var vm = Create();
        Assert.True(vm.ExtensionFilters.Count > 0);
        Assert.Contains(vm.ExtensionFilters, e => e.Extension == ".chd");
        Assert.Contains(vm.ExtensionFilters, e => e.Extension == ".zip");
        Assert.Contains(vm.ExtensionFilters, e => e.Extension == ".nes");
    }

    [Fact]
    public void GetSelectedExtensions_ReturnsOnlyChecked()
    {
        var vm = Create();
        foreach (var e in vm.ExtensionFilters) e.IsChecked = false;
        vm.ExtensionFilters.First(e => e.Extension == ".iso").IsChecked = true;
        vm.ExtensionFilters.First(e => e.Extension == ".chd").IsChecked = true;

        var selected = vm.GetSelectedExtensions();
        Assert.Equal(2, selected.Length);
        Assert.Contains(".iso", selected);
        Assert.Contains(".chd", selected);
    }

    [Fact]
    public void GetSelectedExtensions_NoneChecked_Empty()
    {
        var vm = Create();
        foreach (var e in vm.ExtensionFilters) e.IsChecked = false;
        Assert.Empty(vm.GetSelectedExtensions());
    }

    #endregion

    #region Console Filters

    [Fact]
    public void ConsoleFilters_Initialized()
    {
        var vm = Create();
        Assert.True(vm.ConsoleFilters.Count > 0);
        Assert.Contains(vm.ConsoleFilters, c => c.Key == "PS1");
        Assert.Contains(vm.ConsoleFilters, c => c.Key == "PS2");
        Assert.Contains(vm.ConsoleFilters, c => c.Key == "SWITCH");
        Assert.Contains(vm.ConsoleFilters, c => c.Key == "ARCADE");
    }

    [Fact]
    public void GetSelectedConsoles_ReturnsOnlyChecked()
    {
        var vm = Create();
        foreach (var c in vm.ConsoleFilters) c.IsChecked = false;
        vm.ConsoleFilters.First(c => c.Key == "PS2").IsChecked = true;

        var selected = vm.GetSelectedConsoles();
        Assert.Single(selected);
        Assert.Equal("PS2", selected[0]);
    }

    #endregion

    #region Presets

    [Fact]
    public void OnPresetSafeDryRun_SetsCorrectState()
    {
        var vm = Create();
        var refreshed = false;
        vm.StatusRefreshRequested += () => refreshed = true;

        vm.OnPresetSafeDryRun();

        Assert.True(vm.DryRun);
        Assert.False(vm.ConvertEnabled);
        Assert.False(vm.AggressiveJunk);
        Assert.Equal("SafeDryRun", vm.ActivePreset);
        Assert.True(refreshed);

        var activeRegions = vm.GetPreferredRegions();
        Assert.Contains("EU", activeRegions);
        Assert.Contains("US", activeRegions);
        Assert.Contains("JP", activeRegions);
        Assert.Contains("WORLD", activeRegions);
    }

    [Fact]
    public void OnPresetFullSort_SetsCorrectState()
    {
        var vm = Create();
        vm.OnPresetFullSort();

        Assert.True(vm.DryRun);
        Assert.True(vm.SortConsole);
        Assert.Equal("FullSort", vm.ActivePreset);
    }

    [Fact]
    public void OnPresetConvert_SetsCorrectState()
    {
        var vm = Create();
        vm.OnPresetConvert();

        Assert.True(vm.DryRun);
        Assert.True(vm.ConvertEnabled);
        Assert.Equal("Convert", vm.ActivePreset);
    }

    #endregion

    #region Theme

    [Fact]
    public void CurrentThemeName_MatchesThemeState()
    {
        var vm = Create();
        Assert.Equal("Dark", vm.CurrentThemeName);
    }

    [Fact]
    public void OnThemeToggle_ChangesTheme()
    {
        var theme = new StubTheme();
        var vm = new SetupViewModel(theme, new StubDialog(), new StubSettings(), new LocalizationService());

        var nameBefore = vm.CurrentThemeName;
        vm.OnThemeToggle();
        var nameAfter = vm.CurrentThemeName;

        Assert.NotEqual(nameBefore, nameAfter);
    }

    [Fact]
    public void ThemeToggleText_VariesByTheme()
    {
        var theme = new StubTheme();
        var vm = new SetupViewModel(theme, new StubDialog(), new StubSettings(), new LocalizationService());

        theme.ApplyTheme(AppTheme.Dark);
        var darkText = vm.ThemeToggleText;

        theme.ApplyTheme(AppTheme.Light);
        var lightText = vm.ThemeToggleText;

        Assert.NotEqual(darkText, lightText);
    }

    #endregion

    #region GameKey Preview

    [Fact]
    public void OnGameKeyPreview_NormalizesInput()
    {
        var vm = Create();
        vm.GameKeyPreviewInput = "Super Mario World (USA) (Rev 2)";
        vm.OnGameKeyPreview();

        Assert.NotEqual("–", vm.GameKeyPreviewOutput);
        Assert.Contains("supermarioworld", vm.GameKeyPreviewOutput);
    }

    [Fact]
    public void OnGameKeyPreview_EmptyInput_ProducesOutput()
    {
        var vm = Create();
        vm.GameKeyPreviewInput = "";
        vm.OnGameKeyPreview();
        Assert.NotNull(vm.GameKeyPreviewOutput);
    }

    #endregion

    #region Config Map

    [Fact]
    public void GetCurrentConfigMap_ContainsExpectedKeys()
    {
        var vm = Create();
        var map = vm.GetCurrentConfigMap();

        Assert.Contains("sortConsole", map.Keys);
        Assert.Contains("aliasKeying", map.Keys);
        Assert.Contains("aggressiveJunk", map.Keys);
        Assert.Contains("dryRun", map.Keys);
        Assert.Contains("useDat", map.Keys);
        Assert.Contains("datRoot", map.Keys);
        Assert.Contains("datHashType", map.Keys);
        Assert.Contains("convertEnabled", map.Keys);
        Assert.Contains("trashRoot", map.Keys);
        Assert.Contains("auditRoot", map.Keys);
        Assert.Contains("toolChdman", map.Keys);
        Assert.Contains("toolDolphin", map.Keys);
        Assert.Contains("tool7z", map.Keys);
        Assert.Contains("locale", map.Keys);
        Assert.Contains("logLevel", map.Keys);
    }

    [Fact]
    public void GetCurrentConfigMap_ReflectsCurrentValues()
    {
        var vm = Create();
        vm.DryRun = false;
        vm.AggressiveJunk = true;
        vm.Locale = "en";

        var map = vm.GetCurrentConfigMap();
        Assert.Equal("False", map["dryRun"]);
        Assert.Equal("True", map["aggressiveJunk"]);
        Assert.Equal("en", map["locale"]);
    }

    #endregion

    #region Validation (INotifyDataErrorInfo)

    [Fact]
    public void HasErrors_DefaultFalse()
    {
        var vm = Create();
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void ToolPath_InvalidPath_SetsError()
    {
        var vm = Create();
        vm.ToolChdman = @"C:\nonexistent\path\<illegal>.exe";

        Assert.True(vm.HasErrors);
        var errors = vm.GetErrors(nameof(vm.ToolChdman)).Cast<string>().ToList();
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ToolPath_Empty_ClearsError()
    {
        var vm = Create();
        vm.ToolChdman = @"C:\<illegal>.exe";
        Assert.True(vm.HasErrors);

        vm.ToolChdman = "";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void ErrorsChanged_Fires()
    {
        var vm = Create();
        var changedProps = new List<string>();
        vm.ErrorsChanged += (_, e) => changedProps.Add(e.PropertyName!);

        vm.ToolChdman = @"C:\<>|bad.exe";
        Assert.Contains("ToolChdman", changedProps);
    }

    [Fact]
    public void GetErrors_UnknownProperty_ReturnsEmpty()
    {
        var vm = Create();
        var errors = vm.GetErrors("NonExistent").Cast<string>().ToList();
        Assert.Empty(errors);
    }

    #endregion

    #region Conflict Policy

    [Fact]
    public void ConflictPolicy_DefaultRename()
    {
        var vm = Create();
        Assert.Equal(ConflictPolicy.Rename, vm.ConflictPolicy);
        Assert.Equal(0, vm.ConflictPolicyIndex);
    }

    [Fact]
    public void ConflictPolicyIndex_SetUpdatesPolicy()
    {
        var vm = Create();
        vm.ConflictPolicyIndex = 2;
        Assert.Equal(ConflictPolicy.Overwrite, vm.ConflictPolicy);
    }

    [Fact]
    public void ConflictPolicy_SetUpdatesIndex()
    {
        var vm = Create();
        vm.ConflictPolicy = ConflictPolicy.Skip;
        Assert.Equal(1, vm.ConflictPolicyIndex);
    }

    #endregion

    #region Browse Handlers

    [Fact]
    public void OnBrowseToolPath_SetsCorrectToolProperty()
    {
        var dialog = new StubDialog { NextBrowseFile = @"C:\chosen\tool.exe" };
        var vm = new SetupViewModel(new StubTheme(), dialog, new StubSettings(), new LocalizationService());

        vm.OnBrowseToolPath("Chdman");
        Assert.Equal(@"C:\chosen\tool.exe", vm.ToolChdman);

        vm.OnBrowseToolPath("Dolphin");
        Assert.Equal(@"C:\chosen\tool.exe", vm.ToolDolphin);

        vm.OnBrowseToolPath("7z");
        Assert.Equal(@"C:\chosen\tool.exe", vm.Tool7z);

        vm.OnBrowseToolPath("Psxtract");
        Assert.Equal(@"C:\chosen\tool.exe", vm.ToolPsxtract);

        vm.OnBrowseToolPath("Ciso");
        Assert.Equal(@"C:\chosen\tool.exe", vm.ToolCiso);
    }

    [Fact]
    public void OnBrowseToolPath_NullFromDialog_NoChange()
    {
        var dialog = new StubDialog { NextBrowseFile = null };
        var vm = new SetupViewModel(new StubTheme(), dialog, new StubSettings(), new LocalizationService());
        vm.ToolChdman = "original";

        vm.OnBrowseToolPath("Chdman");
        Assert.Equal("original", vm.ToolChdman);
    }

    [Fact]
    public void OnBrowseFolderPath_SetsCorrectProperty()
    {
        var dialog = new StubDialog { NextBrowseFolder = @"C:\chosen\folder" };
        var vm = new SetupViewModel(new StubTheme(), dialog, new StubSettings(), new LocalizationService());

        vm.OnBrowseFolderPath("Dat");
        Assert.Equal(@"C:\chosen\folder", vm.DatRoot);

        vm.OnBrowseFolderPath("Trash");
        Assert.Equal(@"C:\chosen\folder", vm.TrashRoot);

        vm.OnBrowseFolderPath("Audit");
        Assert.Equal(@"C:\chosen\folder", vm.AuditRoot);

        vm.OnBrowseFolderPath("Ps3");
        Assert.Equal(@"C:\chosen\folder", vm.Ps3DupesRoot);
    }

    [Fact]
    public void OnBrowseFolderPath_NullFromDialog_NoChange()
    {
        var dialog = new StubDialog { NextBrowseFolder = null };
        var vm = new SetupViewModel(new StubTheme(), dialog, new StubSettings(), new LocalizationService());

        vm.OnBrowseFolderPath("Dat");
        Assert.Equal("", vm.DatRoot);
    }

    #endregion

    #region StatusRefreshRequested

    [Fact]
    public void StatusRefreshRequested_FiredOnRelevantPropertyChange()
    {
        var vm = Create();
        var count = 0;
        vm.StatusRefreshRequested += () => count++;

        vm.DatRoot = @"C:\some";
        vm.UseDat = true;
        vm.ConvertEnabled = true;
        vm.ToolChdman = @"C:\x";
        vm.ToolDolphin = @"C:\x";
        vm.Tool7z = @"C:\x";
        vm.ToolPsxtract = @"C:\x";
        vm.ToolCiso = @"C:\x";

        Assert.True(count >= 8);
    }

    #endregion

    #region Simple Mode Properties

    [Fact]
    public void SimpleMode_Properties_GetSet()
    {
        var vm = Create();
        vm.SimpleRegionIndex = 2;
        Assert.Equal(2, vm.SimpleRegionIndex);

        vm.SimpleDupes = false;
        Assert.False(vm.SimpleDupes);

        vm.SimpleJunk = false;
        Assert.False(vm.SimpleJunk);

        vm.SimpleSort = false;
        Assert.False(vm.SimpleSort);
    }

    #endregion

    #region Profile Properties

    [Fact]
    public void Profile_GetSet()
    {
        var vm = Create();
        Assert.Equal("Standard", vm.ProfileName);
        vm.ProfileName = "Custom";
        Assert.Equal("Custom", vm.ProfileName);

        vm.QuickProfileIndex = 3;
        Assert.Equal(3, vm.QuickProfileIndex);
    }

    #endregion

    #region Sidebar Navigation

    [Fact]
    public void SidebarNavigation_GetSet()
    {
        var vm = Create();
        Assert.Equal("Sortieroptionen", vm.SelectedSettingsSection);
        vm.SelectedSettingsSection = "Tools";
        Assert.Equal("Tools", vm.SelectedSettingsSection);

        Assert.Equal("Dashboard", vm.SelectedResultSection);
        vm.SelectedResultSection = "KPIs";
        Assert.Equal("KPIs", vm.SelectedResultSection);
    }

    #endregion

    #region LoadInitialSettings

    [Fact]
    public void LoadInitialSettings_WithDto_AppliesValues()
    {
        var settings = new StubSettings
        {
            NextLoad = new SettingsDto { AggressiveJunk = true, LogLevel = "Trace" }
        };
        var vm = new SetupViewModel(new StubTheme(), new StubDialog(), settings, new LocalizationService());

        vm.LoadInitialSettings(null);

        Assert.True(vm.AggressiveJunk);
        Assert.Equal("Trace", vm.LogLevel);
    }

    [Fact]
    public void LoadInitialSettings_NullDto_NoChange()
    {
        var settings = new StubSettings { NextLoad = null };
        var vm = new SetupViewModel(new StubTheme(), new StubDialog(), settings, new LocalizationService());

        vm.LoadInitialSettings(null);
        Assert.Equal("Info", vm.LogLevel);
    }

    [Fact]
    public void LoadInitialSettings_RestoresTheme()
    {
        var theme = new StubTheme();
        var settings = new StubSettings
        {
            NextLoad = new SettingsDto(),
            LastTheme = "Light"
        };
        var vm = new SetupViewModel(theme, new StubDialog(), settings, new LocalizationService());

        vm.LoadInitialSettings(null);

        Assert.Equal(AppTheme.Light, theme.Current);
    }

    #endregion
}
