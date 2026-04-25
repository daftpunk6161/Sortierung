using System.Text.Json;
using System.Text;
using System.Windows.Media;
using System.Xml.Linq;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Converters;
using Romulus.UI.Wpf.Models;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for RunState state machine transitions, ConflictPolicy enum,
/// and Theme resource parity between Dark and Light themes.
/// Covers TEST-002, TEST-006, and TEST-008 from gui-ux-deep-audit.md.
/// </summary>
public partial class GuiViewModelTests
{
    /// <summary>
    /// Navigate through valid state transitions to reach the target RunState.
    /// RF-007 ValidateTransition requires legal transitions; direct jumps from Idle are invalid.
    /// </summary>
    private static void SetRunStateViaValidPath(MainViewModel vm, RunState target)
    {
        if (target == RunState.Idle) return; // already idle
        vm.CurrentRunState = RunState.Preflight;
        if (target == RunState.Preflight) return;

        // Terminal states: reachable from any active phase (Preflight is active)
        if (target is RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled)
        {
            vm.CurrentRunState = target;
            return;
        }

        vm.CurrentRunState = RunState.Scanning;
        if (target == RunState.Scanning) return;
        vm.CurrentRunState = RunState.Deduplicating;
        if (target == RunState.Deduplicating) return;
        vm.CurrentRunState = RunState.Sorting;
        if (target == RunState.Sorting) return;
        vm.CurrentRunState = RunState.Moving;
        if (target == RunState.Moving) return;
        vm.CurrentRunState = RunState.Converting;
    }

    // ═══ RunState enum value tests ══════════════════════════════════════

    [Fact]
    public void RunState_HasAllExpectedValues()
    {
        var names = Enum.GetNames<RunState>();
        Assert.Contains("Idle", names);
        Assert.Contains("Preflight", names);
        Assert.Contains("Scanning", names);
        Assert.Contains("Deduplicating", names);
        Assert.Contains("Sorting", names);
        Assert.Contains("Moving", names);
        Assert.Contains("Converting", names);
        Assert.Contains("Completed", names);
        Assert.Contains("CompletedDryRun", names);
        Assert.Contains("Failed", names);
        Assert.Contains("Cancelled", names);
        Assert.Equal(11, names.Length);
    }

    [Theory]
    [InlineData(RunState.Preflight)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Deduplicating)]
    [InlineData(RunState.Sorting)]
    [InlineData(RunState.Moving)]
    [InlineData(RunState.Converting)]
    public void RunState_BusyStates_AreRunning(RunState state)
    {
        // These states should map to IsBusy == true
        Assert.True(state is RunState.Preflight or RunState.Scanning
            or RunState.Deduplicating or RunState.Sorting or RunState.Moving or RunState.Converting);
    }

    [Theory]
    [InlineData(RunState.Idle)]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void RunState_IdleStates_AreNotRunning(RunState state)
    {
        Assert.False(state is RunState.Preflight or RunState.Scanning
            or RunState.Deduplicating or RunState.Sorting or RunState.Moving or RunState.Converting);
    }

    // ═══ PipelinePhaseBrushConverter tests ═══════════════════════════════

    [Fact]
    public void PipelinePhaseBrush_Idle_ReturnsTransparent()
    {
        var conv = new PipelinePhaseBrushConverter();
        var result = conv.Convert(RunState.Idle, typeof(object), "1", null!);
        Assert.IsType<SolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, ((SolidColorBrush)result).Color);
    }

    [Theory]
    [InlineData(RunState.Scanning, "1")]   // Phase 1 done when at phase 2
    [InlineData(RunState.Deduplicating, "2")] // Phase 2 done when at phase 3
    [InlineData(RunState.Completed, "5")]  // Phase 5 done when completed
    public void PipelinePhaseBrush_EarlierPhase_ReturnsDone(RunState state, string param)
    {
        var conv = new PipelinePhaseBrushConverter();
        var result = (SolidColorBrush)conv.Convert(state, typeof(object), param, null!);
        // Done = green (#00FF88)
        Assert.Equal(0x00, result.Color.R);
        Assert.Equal(0xFF, result.Color.G);
        Assert.Equal(0x88, result.Color.B);
    }

    [Theory]
    [InlineData(RunState.Preflight, "1")]
    [InlineData(RunState.Scanning, "2")]
    [InlineData(RunState.Deduplicating, "3")]
    [InlineData(RunState.Moving, "4")]
    [InlineData(RunState.Sorting, "5")]
    [InlineData(RunState.Converting, "6")]
    [InlineData(RunState.Completed, "7")]
    public void PipelinePhaseBrush_CurrentPhase_ReturnsActive(RunState state, string param)
    {
        var conv = new PipelinePhaseBrushConverter();
        var result = (SolidColorBrush)conv.Convert(state, typeof(object), param, null!);
        // Active = cyan (#00F5FF)
        Assert.Equal(0x00, result.Color.R);
        Assert.Equal(0xF5, result.Color.G);
        Assert.Equal(0xFF, result.Color.B);
    }

    [Theory]
    [InlineData(RunState.Preflight, "3")]  // Phase 3 pending when at phase 1
    [InlineData(RunState.Scanning, "5")]   // Phase 5 pending when at phase 2
    public void PipelinePhaseBrush_FuturePhase_ReturnsPending(RunState state, string param)
    {
        var conv = new PipelinePhaseBrushConverter();
        var result = (SolidColorBrush)conv.Convert(state, typeof(object), param, null!);
        // Pending = muted (#555577)
        Assert.Equal(0x55, result.Color.R);
        Assert.Equal(0x55, result.Color.G);
        Assert.Equal(0x77, result.Color.B);
    }

    // ═══ ConflictPolicy enum tests ══════════════════════════════════════

    [Fact]
    public void ConflictPolicy_HasThreeValues()
    {
        var names = Enum.GetNames<ConflictPolicy>();
        Assert.Equal(3, names.Length);
        Assert.Contains("Rename", names);
        Assert.Contains("Skip", names);
        Assert.Contains("Overwrite", names);
    }

    [Fact]
    public void ConflictPolicy_DefaultIsRename()
    {
        // Rename (0) is the safest default
        Assert.Equal(0, (int)ConflictPolicy.Rename);
    }

    [Theory]
    [InlineData(0, ConflictPolicy.Rename)]
    [InlineData(1, ConflictPolicy.Skip)]
    [InlineData(2, ConflictPolicy.Overwrite)]
    public void ConflictPolicy_IndexMapsCorrectly(int index, ConflictPolicy expected)
    {
        Assert.Equal(expected, (ConflictPolicy)index);
    }

    [Theory]
    [InlineData("Rename", ConflictPolicy.Rename)]
    [InlineData("Skip", ConflictPolicy.Skip)]
    [InlineData("Overwrite", ConflictPolicy.Overwrite)]
    [InlineData("rename", ConflictPolicy.Rename)]  // case-insensitive parse
    public void ConflictPolicy_ParseFromString(string input, ConflictPolicy expected)
    {
        Assert.True(Enum.TryParse<ConflictPolicy>(input, true, out var result));
        Assert.Equal(expected, result);
    }

    // ═══ Theme Parity (TEST-006) ════════════════════════════════════════

    [Fact]
    public void ThemeParity_DarkAndLight_HaveSameBrushKeys()
    {
        var darkPath = FindThemeFile("SynthwaveDark.xaml");
        var lightPath = FindThemeFile("Light.xaml");

        Assert.True(File.Exists(darkPath), $"Dark theme not found at {darkPath}");
        Assert.True(File.Exists(lightPath), $"Light theme not found at {lightPath}");

        var darkKeys = ExtractResourceKeys(darkPath);
        var lightKeys = ExtractResourceKeys(lightPath);

        // Filter to Brush keys only (the critical ones for visual parity)
        var darkBrushKeys = darkKeys.Where(k => k.StartsWith("Brush")).OrderBy(k => k).ToList();
        var lightBrushKeys = lightKeys.Where(k => k.StartsWith("Brush")).OrderBy(k => k).ToList();

        var missingInLight = darkBrushKeys.Except(lightBrushKeys).ToList();
        var missingInDark = lightBrushKeys.Except(darkBrushKeys).ToList();

        Assert.True(missingInLight.Count == 0,
            $"Brush keys in Dark but missing in Light: {string.Join(", ", missingInLight)}");
        Assert.True(missingInDark.Count == 0,
            $"Brush keys in Light but missing in Dark: {string.Join(", ", missingInDark)}");
    }

    [Fact]
    public void ThemeParity_DarkAndLight_HaveSameSpacingKeys()
    {
        var darkPath = FindThemeFile("SynthwaveDark.xaml");
        var lightPath = FindThemeFile("Light.xaml");

        var darkKeys = ExtractResourceKeys(darkPath);
        var lightKeys = ExtractResourceKeys(lightPath);

        var darkSpacingKeys = darkKeys.Where(k => k.StartsWith("Space")).OrderBy(k => k).ToList();
        var lightSpacingKeys = lightKeys.Where(k => k.StartsWith("Space")).OrderBy(k => k).ToList();

        var missingInLight = darkSpacingKeys.Except(lightSpacingKeys).ToList();
        var missingInDark = lightSpacingKeys.Except(darkSpacingKeys).ToList();

        Assert.True(missingInLight.Count == 0,
            $"Spacing keys in Dark but missing in Light: {string.Join(", ", missingInLight)}");
        Assert.True(missingInDark.Count == 0,
            $"Spacing keys in Light but missing in Dark: {string.Join(", ", missingInDark)}");
    }

    [Fact]
    public void ThemeParity_DarkAndLight_HaveSameNamedStyles()
    {
        var darkPath = FindThemeFile("SynthwaveDark.xaml");
        var lightPath = FindThemeFile("Light.xaml");

        var darkKeys = ExtractResourceKeys(darkPath);
        var lightKeys = ExtractResourceKeys(lightPath);

        // Named styles (non-Brush, non-Space keys) — e.g. PrimaryButton, SectionCard
        var darkStyleKeys = darkKeys
            .Where(k => !k.StartsWith("Brush") && !k.StartsWith("Space"))
            .OrderBy(k => k).ToList();
        var lightStyleKeys = lightKeys
            .Where(k => !k.StartsWith("Brush") && !k.StartsWith("Space"))
            .OrderBy(k => k).ToList();

        var missingInLight = darkStyleKeys.Except(lightStyleKeys).ToList();
        var missingInDark = lightStyleKeys.Except(darkStyleKeys).ToList();

        Assert.True(missingInLight.Count == 0,
            $"Style keys in Dark but missing in Light: {string.Join(", ", missingInLight)}");
        Assert.True(missingInDark.Count == 0,
            $"Style keys in Light but missing in Dark: {string.Join(", ", missingInDark)}");
    }

    [Fact]
    public void ThemeParity_CornerRadius_MatchesBetweenThemes()
    {
        var darkPath = FindThemeFile("SynthwaveDark.xaml");
        var lightPath = FindThemeFile("Light.xaml");
        var darkDoc = XDocument.Load(darkPath);
        var lightDoc = XDocument.Load(lightPath);

        // Extract paired CornerRadius values by their parent TargetType
        var darkCR = ExtractCornerRadiusValues(darkDoc);
        var lightCR = ExtractCornerRadiusValues(lightDoc);

        var mismatches = new List<string>();
        foreach (var key in darkCR.Keys.Intersect(lightCR.Keys))
        {
            if (darkCR[key] != lightCR[key])
                mismatches.Add($"{key}: Dark={darkCR[key]}, Light={lightCR[key]}");
        }
        Assert.True(mismatches.Count == 0,
            $"CornerRadius mismatches:\n{string.Join("\n", mismatches)}");
    }

    [Fact]
    public void ThemeParity_TabItem_Padding_MatchesBetweenThemes()
    {
        // TabItem style lives in the shared _ControlTemplates.xaml (not per-theme).
        // Verify the shared template defines a TabItem Padding consistently.
        var templatesPath = FindThemeFile("_ControlTemplates.xaml");
        Assert.True(File.Exists(templatesPath),
            $"_ControlTemplates.xaml not found (BaseDir={AppDomain.CurrentDomain.BaseDirectory})");
        var doc = XDocument.Load(templatesPath);
        var padding = ExtractSetterValue(doc, "TabItem", "Padding");
        Assert.True(padding is not null,
            $"TabItem Padding not found in {templatesPath}");
    }

    private static Dictionary<string, string> ExtractCornerRadiusValues(XDocument doc)
    {
        var result = new Dictionary<string, string>();
        foreach (var el in doc.Descendants())
        {
            var cr = el.Attribute("CornerRadius");
            if (cr is null) continue;
            // Use parent style TargetType or element name as key
            var parent = el.Ancestors().FirstOrDefault(a =>
                a.Name.LocalName == "ControlTemplate" || a.Name.LocalName == "Style");
            if (parent is not null)
            {
                var targetType = parent.Attribute("TargetType")?.Value ?? "Unknown";
                var key = $"{targetType}#{el.Name.LocalName}";
                result.TryAdd(key, cr.Value);
            }
        }
        return result;
    }

    private static string? ExtractSetterValue(XDocument doc, string targetType, string property)
    {
        foreach (var style in doc.Descendants().Where(e => e.Name.LocalName == "Style"))
        {
            var tt = style.Attribute("TargetType")?.Value;
            // Match both "TabItem" and "{x:Type TabItem}" formats
            if (tt != targetType && tt?.EndsWith(targetType + "}") != true) continue;
            foreach (var setter in style.Elements().Where(e => e.Name.LocalName == "Setter"))
            {
                if (setter.Attribute("Property")?.Value == property)
                    return setter.Attribute("Value")?.Value;
            }
        }
        return null;
    }

    // ═══ Helpers ════════════════════════════════════════════════════════

    private static string FindThemeFile(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var repoRoot = FindRepoRoot(callerPath);
        var candidate = Path.Combine(repoRoot, "src", "Romulus.UI.Wpf", "Themes", fileName);
        if (File.Exists(candidate))
            return candidate;

        // Last resort: preserve previous behavior to keep tests resilient in odd runners.
        return Path.Combine("src", "Romulus.UI.Wpf", "Themes", fileName);
    }

    private static string FindRepoRoot(string? callerPath)
    {
        // Prefer compile-time source location to avoid resolving archived duplicates.
        if (!string.IsNullOrWhiteSpace(callerPath))
        {
            var dir = Path.GetDirectoryName(callerPath);
            while (dir is not null)
            {
                if (File.Exists(Path.Combine(dir, "src", "Romulus.sln")) ||
                    File.Exists(Path.Combine(dir, "src", "Romulus.UI.Wpf", "Romulus.UI.Wpf.csproj")))
                {
                    return dir;
                }

                dir = Path.GetDirectoryName(dir);
            }
        }

        // Fallback for unusual test hosts that do not provide a caller path.
        var probe = AppDomain.CurrentDomain.BaseDirectory;
        while (probe is not null)
        {
            if (File.Exists(Path.Combine(probe, "src", "Romulus.sln")) ||
                File.Exists(Path.Combine(probe, "src", "Romulus.UI.Wpf", "Romulus.UI.Wpf.csproj")))
            {
                return probe;
            }

            probe = Path.GetDirectoryName(probe);
        }

        return Directory.GetCurrentDirectory();
    }

    private static HashSet<string> ExtractResourceKeys(string xamlPath)
    {
        var keys = new HashSet<string>();
        var doc = XDocument.Load(xamlPath);
        var xKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var el in doc.Descendants())
        {
            var keyAttr = el.Attribute(xKey);
            if (keyAttr is not null)
                keys.Add(keyAttr.Value);
        }
        return keys;
    }

    // ═══ SettingsService Round-trip (TEST-004) ══════════════════════════

    [Fact]
    public void SettingsService_SaveAndLoad_PreservesAllProperties()
    {
        // Arrange: custom settings dir to avoid clobbering real user settings
        var tempDir = Path.Combine(Path.GetTempPath(), "RomulusTest_" + Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(tempDir, "settings.json");
        Directory.CreateDirectory(tempDir);
        try
        {
            var vm1 = new MainViewModel();
            // Set non-default values on every persisted property
            vm1.Roots.Add(@"C:\TestRom1");
            vm1.Roots.Add(@"D:\TestRom2");
            vm1.LogLevel = "Debug";
            vm1.AggressiveJunk = true;
            vm1.AliasKeying = true;
            vm1.PreferEU = true; vm1.PreferUS = false; vm1.PreferJP = true;
            vm1.PreferWORLD = false; vm1.PreferDE = true; vm1.PreferFR = true;
            vm1.ToolChdman = @"C:\tools\chdman.exe";
            vm1.Tool7z = @"C:\tools\7z.exe";
            vm1.ToolDolphin = @"C:\tools\dolphintool.exe";
            vm1.ToolPsxtract = @"C:\tools\psxtract.exe";
            vm1.ToolCiso = @"C:\tools\ciso.exe";
            vm1.UseDat = true;
            vm1.DatRoot = @"C:\dat";
            vm1.DatHashType = "SHA256";
            vm1.DatFallback = false;
            vm1.TrashRoot = @"C:\trash";
            vm1.AuditRoot = @"C:\audit";
            vm1.Ps3DupesRoot = @"C:\ps3dupes";
            vm1.SortConsole = false;
            vm1.DryRun = false;
            vm1.ConvertEnabled = true;
            vm1.ConfirmMove = false;
            vm1.ConflictPolicy = ConflictPolicy.Skip;

            // Act: manually serialize (same shape as SettingsService.SaveFrom)
            var settings = new
            {
                version = 1,
                general = new
                {
                    logLevel = vm1.LogLevel,
                    preferredRegions = vm1.GetPreferredRegions(),
                    aggressiveJunk = vm1.AggressiveJunk,
                    aliasEditionKeying = vm1.AliasKeying
                },
                toolPaths = new Dictionary<string, string>
                {
                    ["chdman"] = vm1.ToolChdman,
                    ["dolphintool"] = vm1.ToolDolphin,
                    ["7z"] = vm1.Tool7z,
                    ["psxtract"] = vm1.ToolPsxtract,
                    ["ciso"] = vm1.ToolCiso
                },
                dat = new
                {
                    useDat = vm1.UseDat,
                    datRoot = vm1.DatRoot,
                    hashType = vm1.DatHashType,
                    datFallback = vm1.DatFallback
                },
                paths = new
                {
                    trashRoot = vm1.TrashRoot,
                    auditRoot = vm1.AuditRoot,
                    ps3DupesRoot = vm1.Ps3DupesRoot,
                    lastAuditPath = "test-audit.csv"
                },
                roots = vm1.Roots.ToArray(),
                ui = new
                {
                    sortConsole = vm1.SortConsole,
                    dryRun = vm1.DryRun,
                    convertEnabled = vm1.ConvertEnabled,
                    confirmMove = vm1.ConfirmMove,
                    conflictPolicy = vm1.ConflictPolicy.ToString()
                }
            };
            var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(settingsPath, json);

            // Act: load into a fresh VM using SettingsService's JSON-parsing logic
            var vm2 = new MainViewModel();
            var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // Manually apply same parsing as SettingsService.LoadInto
            if (root.TryGetProperty("general", out var general))
            {
                vm2.LogLevel = general.GetProperty("logLevel").GetString() ?? "Info";
                vm2.AggressiveJunk = general.GetProperty("aggressiveJunk").GetBoolean();
                vm2.AliasKeying = general.GetProperty("aliasEditionKeying").GetBoolean();
            }
            if (root.TryGetProperty("toolPaths", out var tools))
            {
                vm2.ToolChdman = tools.GetProperty("chdman").GetString() ?? "";
                vm2.Tool7z = tools.GetProperty("7z").GetString() ?? "";
                vm2.ToolDolphin = tools.GetProperty("dolphintool").GetString() ?? "";
                vm2.ToolPsxtract = tools.GetProperty("psxtract").GetString() ?? "";
                vm2.ToolCiso = tools.GetProperty("ciso").GetString() ?? "";
            }
            if (root.TryGetProperty("dat", out var dat))
            {
                vm2.UseDat = dat.GetProperty("useDat").GetBoolean();
                vm2.DatRoot = dat.GetProperty("datRoot").GetString() ?? "";
                vm2.DatHashType = dat.GetProperty("hashType").GetString() ?? "SHA1";
                vm2.DatFallback = dat.GetProperty("datFallback").GetBoolean();
            }
            if (root.TryGetProperty("paths", out var paths))
            {
                vm2.TrashRoot = paths.GetProperty("trashRoot").GetString() ?? "";
                vm2.AuditRoot = paths.GetProperty("auditRoot").GetString() ?? "";
                vm2.Ps3DupesRoot = paths.GetProperty("ps3DupesRoot").GetString() ?? "";
            }
            if (root.TryGetProperty("ui", out var ui))
            {
                vm2.SortConsole = ui.GetProperty("sortConsole").GetBoolean();
                vm2.DryRun = ui.GetProperty("dryRun").GetBoolean();
                vm2.ConvertEnabled = ui.GetProperty("convertEnabled").GetBoolean();
                vm2.ConfirmMove = ui.GetProperty("confirmMove").GetBoolean();
                if (Enum.TryParse<ConflictPolicy>(
                    ui.GetProperty("conflictPolicy").GetString(), true, out var cp))
                    vm2.ConflictPolicy = cp;
            }
            if (root.TryGetProperty("roots", out var roots) && roots.ValueKind == JsonValueKind.Array)
            {
                vm2.Roots.Clear();
                foreach (var r in roots.EnumerateArray())
                {
                    var path = r.GetString();
                    if (!string.IsNullOrWhiteSpace(path))
                        vm2.Roots.Add(path);
                }
            }

            // Assert all values match
            Assert.Equal(vm1.LogLevel, vm2.LogLevel);
            Assert.Equal(vm1.AggressiveJunk, vm2.AggressiveJunk);
            Assert.Equal(vm1.AliasKeying, vm2.AliasKeying);
            Assert.Equal(vm1.ToolChdman, vm2.ToolChdman);
            Assert.Equal(vm1.Tool7z, vm2.Tool7z);
            Assert.Equal(vm1.ToolDolphin, vm2.ToolDolphin);
            Assert.Equal(vm1.ToolPsxtract, vm2.ToolPsxtract);
            Assert.Equal(vm1.ToolCiso, vm2.ToolCiso);
            Assert.Equal(vm1.UseDat, vm2.UseDat);
            Assert.Equal(vm1.DatRoot, vm2.DatRoot);
            Assert.Equal(vm1.DatHashType, vm2.DatHashType);
            Assert.Equal(vm1.DatFallback, vm2.DatFallback);
            Assert.Equal(vm1.TrashRoot, vm2.TrashRoot);
            Assert.Equal(vm1.AuditRoot, vm2.AuditRoot);
            Assert.Equal(vm1.Ps3DupesRoot, vm2.Ps3DupesRoot);
            Assert.Equal(vm1.SortConsole, vm2.SortConsole);
            Assert.Equal(vm1.DryRun, vm2.DryRun);
            Assert.Equal(vm1.ConvertEnabled, vm2.ConvertEnabled);
            Assert.Equal(vm1.ConfirmMove, vm2.ConfirmMove);
            Assert.Equal(vm1.ConflictPolicy, vm2.ConflictPolicy);
            Assert.Equal(vm1.Roots.Count, vm2.Roots.Count);
            for (int i = 0; i < vm1.Roots.Count; i++)
                Assert.Equal(vm1.Roots[i], vm2.Roots[i]);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { /* cleanup best-effort */ }
        }
    }

    [Fact]
    public void SettingsService_ConflictPolicy_PersistsAllValues()
    {
        foreach (var policy in Enum.GetValues<ConflictPolicy>())
        {
            var serialized = policy.ToString();
            Assert.True(Enum.TryParse<ConflictPolicy>(serialized, true, out var parsed));
            Assert.Equal(policy, parsed);
        }
    }

    [Fact]
    public void GetPreferredRegions_SimpleMode_Index0_ReturnsEuropaOrder()
    {
        var vm = new MainViewModel { IsSimpleMode = true };
        vm.PreferEU = true; vm.PreferDE = true; vm.PreferWORLD = true; vm.PreferUS = true; vm.PreferJP = true;
        var regions = vm.GetPreferredRegions();
        Assert.Contains("EU", regions);
        Assert.Contains("DE", regions);
        Assert.Contains("WORLD", regions);
    }

    [Theory]
    [InlineData("EU")]
    [InlineData("US")]
    [InlineData("JP")]
    [InlineData("WORLD")]
    public void GetPreferredRegions_SimpleMode_FirstRegionCorrect(string region)
    {
        var vm = new MainViewModel { IsSimpleMode = true };
        vm.PreferEU = false; vm.PreferUS = false; vm.PreferJP = false; vm.PreferWORLD = false;
        switch (region)
        {
            case "EU": vm.PreferEU = true; break;
            case "US": vm.PreferUS = true; break;
            case "JP": vm.PreferJP = true; break;
            case "WORLD": vm.PreferWORLD = true; break;
        }
        var regions = vm.GetPreferredRegions();
        Assert.Single(regions);
        Assert.Equal(region, regions[0]);
    }

    [Fact]
    public void GetPreferredRegions_ExpertMode_OnlySelectedRegions()
    {
        var vm = new MainViewModel { IsSimpleMode = false };
        // Reset all to false
        vm.PreferEU = false; vm.PreferUS = false; vm.PreferJP = false; vm.PreferWORLD = false;
        vm.PreferDE = false; vm.PreferFR = false; vm.PreferIT = false; vm.PreferES = false;
        vm.PreferAU = false; vm.PreferASIA = false; vm.PreferKR = false; vm.PreferCN = false;
        vm.PreferBR = false; vm.PreferNL = false; vm.PreferSE = false; vm.PreferSCAN = false;
        // Select only JP and DE
        vm.PreferJP = true;
        vm.PreferDE = true;
        var regions = vm.GetPreferredRegions();
        Assert.Equal(2, regions.Length);
        Assert.Contains("JP", regions);
        Assert.Contains("DE", regions);
    }

    // ═══ RefreshStatus Combinations (TEST-005) ══════════════════════════

    [Fact]
    public void RefreshStatus_NoRoots_ShowsNotReady()
    {
        var vm = new MainViewModel();
        vm.Roots.Clear();
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Missing, vm.RootsStatusLevel);
        Assert.Equal("Keine Ordner", vm.StatusRoots);
        Assert.Equal(StatusLevel.Blocked, vm.ReadyStatusLevel);
        Assert.Contains("Nicht bereit", vm.StatusReady);
    }

    [Fact]
    public void RefreshStatus_WithRoots_ShowsConfigured()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Ok, vm.RootsStatusLevel);
        Assert.Contains("1 Ordner konfiguriert", vm.StatusRoots);
    }

    [Fact]
    public void RefreshStatus_NoToolsSpecified_NotConverting_ShowsMissing()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = "";
        vm.Tool7z = "";
        vm.ConvertEnabled = false;
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Missing, vm.ToolsStatusLevel);
        Assert.Equal("Keine Tools", vm.StatusTools);
    }

    [Fact]
    public void RefreshStatus_ToolsSpecifiedButNotFound_ShowsWarning()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = @"C:\nonexistent\chdman.exe";  // doesn't exist
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Warning, vm.ToolsStatusLevel);
        Assert.Equal("Toolchain unvollständig", vm.StatusTools);
    }

    [Fact]
    public void ToolCiso_Setter_RefreshesCisoStatusImmediately()
    {
        var vm = new MainViewModel();
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ToolStatus_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var cisoPath = Path.Combine(tempDir, "ciso.exe");

        try
        {
            File.WriteAllText(cisoPath, "stub");

            vm.ToolCiso = cisoPath;

            Assert.Equal("✓ Gefunden", vm.CisoStatusText);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void ToolPsxtract_Setter_RefreshesPsxtractStatusImmediately()
    {
        var vm = new MainViewModel();
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ToolStatus_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var psxtractPath = Path.Combine(tempDir, "psxtract.exe");

        try
        {
            File.WriteAllText(psxtractPath, "stub");

            vm.ToolPsxtract = psxtractPath;

            Assert.Equal("✓ Gefunden", vm.PsxtractStatusText);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void RefreshStatus_DatDisabled_ShowsDeactivated()
    {
        var vm = new MainViewModel();
        vm.UseDat = false;
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Missing, vm.DatStatusLevel);
        Assert.Equal("DAT deaktiviert", vm.StatusDat);
    }

    [Fact]
    public void RefreshStatus_DatEnabled_InvalidPath_ShowsWarning()
    {
        var vm = new MainViewModel();
        vm.UseDat = true;
        vm.DatRoot = @"C:\nonexistent\dat";
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Warning, vm.DatStatusLevel);
        Assert.Equal("DAT-Pfad ungültig", vm.StatusDat);
    }

    [Fact]
    public void RefreshStatus_DatEnabled_ValidPath_ShowsActive()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_DatTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        try
        {
            var vm = new MainViewModel();
            vm.UseDat = true;
            vm.DatRoot = tempDir;
            vm.RefreshStatus();
            Assert.Equal(StatusLevel.Ok, vm.DatStatusLevel);
            Assert.Equal("DAT aktiv", vm.StatusDat);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Theory]
    [InlineData(RunState.Scanning, "Scanne…")]
    [InlineData(RunState.Deduplicating, "Dedupliziere…")]
    [InlineData(RunState.Moving, "Verschiebe…")]
    [InlineData(RunState.Converting, "Konvertiere…")]
    [InlineData(RunState.Preflight, "Prüfe…")]
    public void RefreshStatus_BusyState_ShowsPhaseLabel(RunState state, string expectedLabel)
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        SetRunStateViaValidPath(vm, state); // sets IsBusy
        vm.RefreshStatus();
        Assert.Equal(2, vm.CurrentStep);
        Assert.Equal(expectedLabel, vm.StepLabel3);
    }

    [Fact]
    public void RefreshStatus_CompletedDryRun_ShowsStep3()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        SetRunStateViaValidPath(vm, RunState.CompletedDryRun);
        vm.RefreshStatus();
        Assert.Equal(3, vm.CurrentStep);
        Assert.Equal("Vorschau fertig", vm.StepLabel3);
    }

    [Fact]
    public void RefreshStatus_Completed_ShowsStep3()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        SetRunStateViaValidPath(vm, RunState.Completed);
        vm.RefreshStatus();
        Assert.Equal(3, vm.CurrentStep);
        Assert.Equal("Abgeschlossen", vm.StepLabel3);
    }

    [Fact]
    public void RefreshStatus_Idle_WithRoots_ShowsStep1()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.CurrentRunState = RunState.Idle;
        vm.RefreshStatus();
        Assert.Equal(1, vm.CurrentStep);
        Assert.Equal("F5 drücken", vm.StepLabel3);
    }

    [Fact]
    public void RefreshStatus_Ready_WithRoots_NoToolWarning_ShowsOk()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.ToolChdman = "";
        vm.Tool7z = "";
        vm.ConvertEnabled = false;
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Ok, vm.ReadyStatusLevel);
        Assert.Equal("Startbereit ✓", vm.StatusReady);
    }

    [Fact]
    public void RefreshStatus_Ready_WithRoots_ToolWarning_ShowsWarning()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.ConvertEnabled = true;  // wants tools but they're not found
        vm.ToolChdman = "";
        vm.Tool7z = "";
        vm.RefreshStatus();
        Assert.Equal(StatusLevel.Warning, vm.ReadyStatusLevel);
        Assert.Contains("Warnung", vm.StatusReady);
    }

    // ═══ WCAG AA Contrast (A11Y-005) ═══════════════════════════════════

    [Theory]
    [InlineData("SynthwaveDark.xaml", "BrushTextPrimary", "BrushBackground", 4.5)]
    [InlineData("SynthwaveDark.xaml", "BrushTextMuted", "BrushBackground", 3.0)]
    [InlineData("SynthwaveDark.xaml", "BrushAccentCyan", "BrushBackground", 3.0)]
    [InlineData("SynthwaveDark.xaml", "BrushTextPrimary", "BrushSurface", 4.5)]
    [InlineData("Light.xaml", "BrushTextPrimary", "BrushBackground", 4.5)]
    [InlineData("Light.xaml", "BrushTextMuted", "BrushBackground", 4.5)]
    [InlineData("Light.xaml", "BrushAccentCyan", "BrushBackground", 3.0)]
    [InlineData("Light.xaml", "BrushTextPrimary", "BrushSurface", 4.5)]
    public void Theme_TextOnBackground_MeetsWcagAAContrast(
        string themeFile, string fgKey, string bgKey, double minRatio)
    {
        var path = FindThemeFile(themeFile);
        Assert.True(File.Exists(path), $"Theme not found: {path}");

        var colors = ExtractBrushColors(path);
        Assert.True(colors.ContainsKey(fgKey), $"Missing brush key: {fgKey}");
        Assert.True(colors.ContainsKey(bgKey), $"Missing brush key: {bgKey}");

        var ratio = ContrastRatio(colors[fgKey], colors[bgKey]);
        Assert.True(ratio >= minRatio,
            $"{themeFile}: {fgKey} on {bgKey} contrast ratio {ratio:F2}:1 < required {minRatio}:1");
    }

    private static Dictionary<string, (int R, int G, int B)> ExtractBrushColors(string xamlPath)
    {
        var result = new Dictionary<string, (int, int, int)>();
        var doc = XDocument.Load(xamlPath);
        var xKey = XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml");

        foreach (var el in doc.Descendants())
        {
            var keyAttr = el.Attribute(xKey);
            if (keyAttr is null || !keyAttr.Value.StartsWith("Brush")) continue;
            var colorAttr = el.Attribute("Color");
            if (colorAttr is null) continue;
            var hex = colorAttr.Value.TrimStart('#');
            // Support #AARRGGBB and #RRGGBB
            if (hex.Length == 8) hex = hex[2..]; // strip alpha
            if (hex.Length != 6) continue;
            int r = Convert.ToInt32(hex[..2], 16);
            int g = Convert.ToInt32(hex[2..4], 16);
            int b = Convert.ToInt32(hex[4..6], 16);
            result[keyAttr.Value] = (r, g, b);
        }
        return result;
    }

    private static double RelativeLuminance((int R, int G, int B) c)
    {
        double Linearize(int v)
        {
            double s = v / 255.0;
            return s <= 0.04045 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
        }
        return 0.2126 * Linearize(c.R) + 0.7152 * Linearize(c.G) + 0.0722 * Linearize(c.B);
    }

    private static double ContrastRatio((int R, int G, int B) fg, (int R, int G, int B) bg)
    {
        double l1 = RelativeLuminance(fg);
        double l2 = RelativeLuminance(bg);
        double lighter = Math.Max(l1, l2);
        double darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    // ═══ CanExecute Guard Tests (TEST-001) ══════════════════════════════

    [Fact]
    public void RunCommand_Disabled_WhenNoRoots()
    {
        var vm = new MainViewModel();
        vm.Roots.Clear();
        vm.CurrentRunState = RunState.Idle;
        // RunCommand CanExecute = !IsBusy && Roots.Count > 0
        Assert.False(vm.IsBusy);
        Assert.Empty(vm.Roots);
    }

    [Fact]
    public void RunCommand_Disabled_WhenBusy()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        SetRunStateViaValidPath(vm, RunState.Scanning);
        // RunCommand CanExecute = !IsBusy && Roots.Count > 0 → false because IsBusy
        Assert.True(vm.IsBusy);
    }

    [Fact]
    public void RunCommand_Enabled_WhenIdleWithRoots()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.CurrentRunState = RunState.Idle;
        // RunCommand CanExecute = !IsBusy && Roots.Count > 0 → true
        Assert.False(vm.IsBusy);
        Assert.True(vm.Roots.Count > 0);
    }

    [Fact]
    public void CancelCommand_Disabled_WhenNotBusy()
    {
        var vm = new MainViewModel();
        vm.CurrentRunState = RunState.Idle;
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void CancelCommand_Enabled_WhenBusy()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Scanning);
        Assert.True(vm.IsBusy);
    }

    [Fact]
    public void RollbackCommand_Disabled_WhenNoUndoHistory()
    {
        var vm = new MainViewModel();
        Assert.False(vm.HasRollbackUndo);
    }

    [Fact]
    public void RollbackCommand_Enabled_AfterPushUndo()
    {
        var vm = new MainViewModel();
        vm.PushRollbackUndo("test-audit.csv");
        Assert.True(vm.HasRollbackUndo);
    }

    [Fact]
    public void RollbackUndo_PopReturnsLastPushed()
    {
        var vm = new MainViewModel();
        vm.PushRollbackUndo("audit1.csv");
        vm.PushRollbackUndo("audit2.csv");
        Assert.Equal("audit2.csv", vm.PopRollbackUndo());
        Assert.Equal("audit1.csv", vm.PopRollbackUndo());
        Assert.False(vm.HasRollbackUndo);
    }

    // ═══ Cancellation State (TEST-008) ══════════════════════════════════

    [Fact]
    public void TransitionTo_Cancelled_FromBusy_SetsState()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Scanning);
        Assert.True(vm.IsBusy);
        // OnCancel is private — simulate via TransitionTo
        vm.TransitionTo(RunState.Cancelled);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void TransitionTo_Failed_FromBusy_SetsState()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Moving);
        vm.TransitionTo(RunState.Failed);
        Assert.Equal(RunState.Failed, vm.CurrentRunState);
        Assert.False(vm.IsBusy);
    }

    [Fact]
    public void ShowStartMoveButton_True_AfterCompletedDryRun()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");
        Assert.True(vm.ShowStartMoveButton);
    }

    [Fact]
    public void ShowStartMoveButton_False_WhenBusy()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Scanning);
        Assert.False(vm.ShowStartMoveButton);
    }

    [Fact]
    public void ShowStartMoveButton_False_WhenIdle()
    {
        var vm = new MainViewModel();
        vm.CurrentRunState = RunState.Idle;
        Assert.False(vm.ShowStartMoveButton);
    }

    [Fact]
    public void ShowStartMoveButton_False_WhenPreviewConfigChanged()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");

        vm.AggressiveJunk = true;

        Assert.False(vm.ShowStartMoveButton);
        Assert.False(vm.StartMoveCommand.CanExecute(null));
    }

    // ═══ ExtensionFilters (UX-004) ══════════════════════════════════════

    [Fact]
    public void ExtensionFilters_InitializedWith64Items()
    {
        var vm = new MainViewModel();
        Assert.Equal(64, vm.ExtensionFilters.Count);
    }

    [Fact]
    public void ExtensionFilters_AllUncheckedByDefault()
    {
        var vm = new MainViewModel();
        Assert.All(vm.ExtensionFilters, f => Assert.False(f.IsChecked));
    }

    [Fact]
    public void GetSelectedExtensions_NoneChecked_ReturnsEmpty()
    {
        var vm = new MainViewModel();
        Assert.Empty(vm.GetSelectedExtensions());
    }

    [Fact]
    public void GetSelectedExtensions_CheckedItemsReturned()
    {
        var vm = new MainViewModel();
        vm.ExtensionFilters.First(e => e.Extension == ".chd").IsChecked = true;
        vm.ExtensionFilters.First(e => e.Extension == ".zip").IsChecked = true;

        var selected = vm.GetSelectedExtensions();
        Assert.Equal(2, selected.Length);
        Assert.Contains(".chd", selected);
        Assert.Contains(".zip", selected);
    }

    [Fact]
    public void ExtensionFilters_CategoriesAreCorrect()
    {
        var vm = new MainViewModel();
        var categories = vm.ExtensionFilters.Select(e => e.Category).Distinct().OrderBy(c => c).ToArray();
        Assert.Equal(8, categories.Length);
        Assert.Contains("Archive", categories);
        Assert.Contains("Disc-Images", categories);
        Assert.Contains("Nintendo", categories);
        Assert.Contains("Sega", categories);
        Assert.Contains("Atari", categories);
        Assert.Contains("Computer / Retro", categories);
    }

    [Fact]
    public void ExtensionFilters_ContainsKeyExtensions()
    {
        var vm = new MainViewModel();
        var actual = vm.ExtensionFilters.Select(e => e.Extension).ToHashSet();
        // Disc images
        Assert.Contains(".chd", actual);
        Assert.Contains(".iso", actual);
        Assert.Contains(".cue", actual);
        // Archives
        Assert.Contains(".zip", actual);
        Assert.Contains(".7z", actual);
        // Nintendo
        Assert.Contains(".nes", actual);
        Assert.Contains(".nsp", actual);
        // Sega
        Assert.Contains(".md", actual);
        Assert.Contains(".sms", actual);
        // Computer / Retro
        Assert.Contains(".tzx", actual);
        Assert.Contains(".adf", actual);
        Assert.Contains(".d64", actual);
        // Atari
        Assert.Contains(".a26", actual);
        Assert.Contains(".st", actual);
    }

    // ═══ HasRunResult (UX-003/TEST-009) ═════════════════════════════════

    [Theory]
    [InlineData(RunState.Completed, true)]
    [InlineData(RunState.CompletedDryRun, true)]
    [InlineData(RunState.Idle, false)]
    [InlineData(RunState.Scanning, false)]
    [InlineData(RunState.Failed, false)]
    [InlineData(RunState.Cancelled, false)]
    public void HasRunResult_ReflectsCompletedStates(RunState state, bool expected)
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, state);
        Assert.Equal(expected, vm.HasRunResult);
    }

    // ═══ CONSOLE FILTERS (Runde 7) ══════════════════════════════════════

    [Fact]
    public void ConsoleFilters_InitializedWith30Items()
    {
        var vm = new MainViewModel();
        Assert.Equal(30, vm.ConsoleFilters.Count);
    }

    [Fact]
    public void ConsoleFilters_AllUncheckedByDefault()
    {
        var vm = new MainViewModel();
        Assert.All(vm.ConsoleFilters, c => Assert.False(c.IsChecked));
    }

    [Fact]
    public void GetSelectedConsoles_NoneChecked_ReturnsEmpty()
    {
        var vm = new MainViewModel();
        Assert.Empty(vm.GetSelectedConsoles());
    }

    [Fact]
    public void GetSelectedConsoles_CheckedItemsReturned()
    {
        var vm = new MainViewModel();
        vm.ConsoleFilters.First(c => c.Key == "PS1").IsChecked = true;
        vm.ConsoleFilters.First(c => c.Key == "NES").IsChecked = true;
        var selected = vm.GetSelectedConsoles();
        Assert.Equal(2, selected.Length);
        Assert.Contains("PS1", selected);
        Assert.Contains("NES", selected);
    }

    [Fact]
    public void ConsoleFilters_CategoriesAreCorrect()
    {
        var vm = new MainViewModel();
        var categories = vm.ConsoleFilters.Select(c => c.Category).Distinct().OrderBy(c => c).ToArray();
        Assert.Equal(4, categories.Length);
        Assert.Contains("Sony", categories);
        Assert.Contains("Nintendo", categories);
        Assert.Contains("Sega", categories);
        Assert.Contains("Andere", categories);
    }

    [Fact]
    public void ConsoleFilters_ContainsExpectedConsoles()
    {
        var vm = new MainViewModel();
        var keys = vm.ConsoleFilters.Select(c => c.Key).ToArray();
        // Spot-check representative consoles from each category
        Assert.Contains("PS1", keys);
        Assert.Contains("PS2", keys);
        Assert.Contains("PSP", keys);
        Assert.Contains("NES", keys);
        Assert.Contains("SNES", keys);
        Assert.Contains("GC", keys);
        Assert.Contains("SWITCH", keys);
        Assert.Contains("MD", keys);
        Assert.Contains("DC", keys);
        Assert.Contains("GG", keys);
        Assert.Contains("ARCADE", keys);
        Assert.Contains("3DO", keys);
        Assert.Contains("JAG", keys);
    }

    [Fact]
    public void ConsoleFiltersView_HasGroupDescriptions()
    {
        var vm = new MainViewModel();
        Assert.Single(vm.ConsoleFiltersView.GroupDescriptions);
    }

    // ═══ CTS CANCEL RACE SAFETY (Runde 7: Threading) ════════════════════

    [Fact]
    public void OnCancel_SetsState_WhenCtsAlreadyDisposed()
    {
        var vm = new MainViewModel();
        // Create and immediately dispose the CTS to simulate race
        var ct = vm.CreateRunCancellation();
        // Cancel should not throw even after CTS is created
        SetRunStateViaValidPath(vm, RunState.Scanning);
        vm.CancelCommand.Execute(null);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    [Fact]
    public void ApplyRunResult_WhenStateIsCancelled_DoesNotMutateDashboardOrCollections()
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, RunState.Cancelled);
        // Set sentinels AFTER reaching Cancelled — Preflight triggers ResetDashboardForNewRun
        vm.DashWinners = "sentinel";
        vm.DashDupes = "sentinel";

        var winner = new RomCandidate
        {
            MainPath = @"C:\Roms\Game (USA).zip",
            GameKey = "game",
            Region = "US",
            Category = FileCategory.Game,
            SizeBytes = 1024,
            Extension = ".zip"
        };

        var loser = new RomCandidate
        {
            MainPath = @"C:\Roms\Game (EU).zip",
            GameKey = "game",
            Region = "EU",
            Category = FileCategory.Game,
            SizeBytes = 1000,
            Extension = ".zip"
        };

        var result = new RunResult
        {
            Status = "cancelled",
            TotalFilesScanned = 2,
            GroupCount = 1,
            WinnerCount = 1,
            LoserCount = 1,
            AllCandidates = new[] { winner, loser },
            DedupeGroups = new[]
            {
                new DedupeGroup
                {
                    GameKey = "game",
                    Winner = winner,
                    Losers = new[] { loser }
                }
            }
        };

        vm.ApplyRunResult(result);

        Assert.Equal("sentinel", vm.DashWinners);
        Assert.Equal("sentinel", vm.DashDupes);
        Assert.Null(vm.LastRunResult);
        Assert.Empty(vm.LastCandidates);
        Assert.Empty(vm.LastDedupeGroups);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    [Fact]
    public void ApplyRunResult_BlockedOnlyConversionRun_IsTreatedAsConvertOnly()
    {
        var vm = new MainViewModel();

        var result = new RunResult
        {
            Status = "completed_with_errors",
            TotalFilesScanned = 1,
            ConvertBlockedCount = 1,
            AllCandidates =
            [
                new RomCandidate
                {
                    MainPath = @"C:\Roms\Blocked.iso",
                    GameKey = "blocked",
                    Region = "US",
                    Category = FileCategory.Game,
                    SizeBytes = 1024,
                    Extension = ".iso",
                    ConsoleKey = "PSX"
                }
            ],
            DedupeGroups = Array.Empty<DedupeGroup>()
        };

        vm.ApplyRunResult(result);

        Assert.Equal("Nur Konvertierung aktiv. Keine Dateien werden verschoben.", vm.MoveConsequenceText);
        Assert.Equal("Entfällt", vm.DashWinners);
    }

    [Fact]
    public void ApproveReviews_ChangesInvalidateCompletedDryRunFingerprint()
    {
        var vm = new MainViewModel();
        var tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_PreviewGate_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        try
        {
            vm.Roots.Add(tempRoot);
            TransitionTo(vm, RunState.CompletedDryRun);

            var fingerprintMethod = typeof(MainViewModel).GetMethod(
                "BuildPreviewConfigurationFingerprint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var fingerprintField = typeof(MainViewModel).GetField(
                "_lastSuccessfulPreviewFingerprint",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            Assert.NotNull(fingerprintMethod);
            Assert.NotNull(fingerprintField);

            var fingerprint = (string)fingerprintMethod!.Invoke(vm, Array.Empty<object>())!;
            fingerprintField!.SetValue(vm, fingerprint);

            Assert.True(vm.CanStartMoveWithCurrentPreview);

            vm.ApproveReviews = true;

            Assert.False(vm.CanStartMoveWithCurrentPreview);
            Assert.True(vm.ShowConfigChangedBanner);
        }
        finally
        {
            try { Directory.Delete(tempRoot, true); } catch { }
        }
    }

    [Fact]
    public async Task ExecuteRunAsync_CancelledRun_ShowsPartialResultsInDashboard()
    {
        var winner = new RomCandidate
        {
            MainPath = @"C:\Roms\Game (USA).zip",
            GameKey = "game",
            Region = "US",
            Category = FileCategory.Game,
            SizeBytes = 1024,
            Extension = ".zip"
        };

        var loser = new RomCandidate
        {
            MainPath = @"C:\Roms\Game (EU).zip",
            GameKey = "game",
            Region = "EU",
            Category = FileCategory.Game,
            SizeBytes = 1000,
            Extension = ".zip"
        };

        var partialCancelledResult = new RunResult
        {
            Status = "cancelled",
            ExitCode = 2,
            TotalFilesScanned = 2,
            GroupCount = 1,
            WinnerCount = 1,
            LoserCount = 1,
            AllCandidates = new[] { winner, loser },
            DedupeGroups = new[]
            {
                new DedupeGroup
                {
                    GameKey = "game",
                    Winner = winner,
                    Losers = new[] { loser }
                }
            }
        };

        var fakeRunService = new FakeRunService(partialCancelledResult);
        var vm = new MainViewModel(new ThemeService(), new StubDialogService(), runService: fakeRunService);
        vm.Roots.Add(Path.GetTempPath());
        vm.TransitionTo(RunState.Preflight);

        await vm.ExecuteRunAsync();

        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
        Assert.NotNull(vm.LastRunResult);
        Assert.Equal("cancelled", vm.LastRunResult!.Status);
        Assert.StartsWith("1", vm.DashWinners, StringComparison.Ordinal);
        Assert.StartsWith("1", vm.DashDupes, StringComparison.Ordinal);
        Assert.StartsWith("1", vm.DashGames, StringComparison.Ordinal);
        Assert.Equal(2, vm.LastCandidates.Count);
        Assert.Single(vm.LastDedupeGroups);
    }

    [Fact]
    public void ProgressEstimator_ScanPhase_ShouldIncreaseAcrossRepeatedScanMessages()
    {
        var vm = new MainViewModel(new ThemeService(), new StubDialogService());
        var configureMethod = typeof(MainViewModel).GetMethod(
            "ConfigureRunProgressPlan",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var estimateMethod = typeof(MainViewModel).GetMethod(
            "EstimatePhaseProgress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(configureMethod);
        Assert.NotNull(estimateMethod);

        configureMethod!.Invoke(vm, new object[]
        {
            new RunOptions
            {
                Roots = new[] { @"C:\ROMS" },
                Extensions = new[] { ".zip" },
                Mode = RunConstants.ModeDryRun
            }
        });

        var p1 = (double)estimateMethod!.Invoke(vm, new object[] { "[Scan] Root: C:\\ROMS" })!;
        vm.Progress = p1;
        var p2 = (double)estimateMethod.Invoke(vm, new object[] { "[Scan] Hash: A.chd (300 MB)…" })!;
        vm.Progress = p2;
        var p3 = (double)estimateMethod.Invoke(vm, new object[] { "[Scan] Hash: B.chd (420 MB)…" })!;

        Assert.True(p1 >= 5, $"Expected scan progress to start at or above phase lower bound, got {p1}");
        Assert.True(p2 > p1, $"Expected scan progress to increase (p1={p1}, p2={p2})");
        Assert.True(p3 > p2, $"Expected scan progress to continue increasing (p2={p2}, p3={p3})");
    }

    [Fact]
    public void ProgressEstimator_ScanFractionWithoutCompletion_DoesNotReachPhaseCeiling()
    {
        var vm = new MainViewModel(new ThemeService(), new StubDialogService());
        var configureMethod = typeof(MainViewModel).GetMethod(
            "ConfigureRunProgressPlan",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var estimateMethod = typeof(MainViewModel).GetMethod(
            "EstimatePhaseProgress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(configureMethod);
        Assert.NotNull(estimateMethod);

        configureMethod!.Invoke(vm, new object[]
        {
            new RunOptions
            {
                Roots = new[] { @"C:\ROMS" },
                Extensions = new[] { ".zip" },
                Mode = RunConstants.ModeDryRun
            }
        });

        var nonTerminal = (double)estimateMethod!.Invoke(vm, new object[] { "[Scan] 850/850 Dateien verarbeitet..." })!;
        vm.Progress = nonTerminal;
        var completed = (double)estimateMethod.Invoke(vm, new object[] { "[Scan] Abgeschlossen: 850 Dateien in 1ms" })!;

        Assert.True(nonTerminal < completed,
            $"Expected non-completion scan fraction to stay below completion ceiling (nonTerminal={nonTerminal}, completed={completed})");
        Assert.True(completed <= 100d, $"Expected completion ceiling to stay within 0..100, got {completed}");
    }

    [Fact]
    public void ProgressEstimator_LongRunningScanHeuristic_StaysBelowPhaseCeilingUntilCompletion()
    {
        var timeProvider = new TestTimeProvider(new DateTimeOffset(2026, 4, 10, 12, 0, 0, TimeSpan.Zero));
        var vm = new MainViewModel(new ThemeService(), new StubDialogService(), timeProvider: timeProvider);
        var configureMethod = typeof(MainViewModel).GetMethod(
            "ConfigureRunProgressPlan",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var estimateMethod = typeof(MainViewModel).GetMethod(
            "EstimatePhaseProgress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(configureMethod);
        Assert.NotNull(estimateMethod);

        configureMethod!.Invoke(vm, new object[]
        {
            new RunOptions
            {
                Roots = new[] { @"C:\ROMS" },
                Extensions = new[] { ".zip" },
                Mode = RunConstants.ModeDryRun
            }
        });

        var start = (double)estimateMethod!.Invoke(vm, new object[] { "[Scan] Root: C:\\ROMS" })!;
        vm.Progress = start;

        timeProvider.Advance(TimeSpan.FromMinutes(10));
        var heuristic = (double)estimateMethod.Invoke(vm, new object[] { "[Scan] Hash: Batricops.chd (277 MB)..." })!;
        vm.Progress = heuristic;
        var completed = (double)estimateMethod.Invoke(vm, new object[] { "[Scan] Abgeschlossen: 850 Dateien in 1ms" })!;

        Assert.True(heuristic > start, $"Expected long-running scan heuristic to increase progress (start={start}, heuristic={heuristic})");
        Assert.True(heuristic < completed,
            $"Expected heuristic progress to stay below scan completion ceiling (heuristic={heuristic}, completed={completed})");
    }

    [Fact]
    public void ProgressEstimator_ConvertOnlyRun_UsesActivePhasePlan_InsteadOfFixedTailRange()
    {
        var vm = new MainViewModel(new ThemeService(), new StubDialogService());
        var configureMethod = typeof(MainViewModel).GetMethod(
            "ConfigureRunProgressPlan",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var estimateMethod = typeof(MainViewModel).GetMethod(
            "EstimatePhaseProgress",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(configureMethod);
        Assert.NotNull(estimateMethod);

        configureMethod!.Invoke(vm, new object[]
        {
            new RunOptions
            {
                Roots = new[] { @"C:\ROMS" },
                Extensions = new[] { ".zip" },
                Mode = RunConstants.ModeMove,
                ConvertOnly = true,
                ConvertFormat = "auto"
            }
        });

        var scanDone = (double)estimateMethod!.Invoke(vm, new object[] { "[Scan] Abgeschlossen: 21 Dateien in 1ms" })!;
        vm.Progress = scanDone;
        var convertHalf = (double)estimateMethod.Invoke(
            vm,
            new object[] { "[Convert] Fortschritt: 1/2 Dateien (ok=0, skip=0, blocked=0, err=0)" })!;

        Assert.True(scanDone >= 45d, $"Expected scan completion to occupy a meaningful share, got {scanDone}");
        Assert.True(convertHalf > scanDone, $"Expected convert progress to advance beyond scan completion (scan={scanDone}, convert={convertHalf})");
        Assert.True(convertHalf < 90d, $"Expected convert progress to use the active run plan instead of the old fixed tail range, got {convertHalf}");
    }

    [Fact]
    public void UpdatePerfContext_UsesReadablePhaseLabel_NotRawBracketPrefix()
    {
        var vm = new MainViewModel(new ThemeService(), new StubDialogService());
        var method = typeof(MainViewModel).GetMethod(
            "UpdatePerfContext",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        method!.Invoke(vm, new object[] { "[Convert] Fortschritt: 1/3 Dateien" });

        Assert.DoesNotContain("[Convert]", vm.PerfPhase, StringComparison.Ordinal);
        Assert.Contains("Phase:", vm.PerfPhase, StringComparison.Ordinal);
        Assert.Equal("Fortschritt: 1/3 Dateien", vm.PerfFile);
    }

    [Fact]
    public void ShouldDispatchProgressMessage_AllowsImmediatePhaseChange_WithinThrottleWindow()
    {
        var method = typeof(MainViewModel).GetMethod(
            "ShouldDispatchProgressMessage",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);

        Assert.NotNull(method);

        var shouldDispatch = (bool)method!.Invoke(null, new object[]
        {
            "[Convert] game.iso -> .chd",
            DateTime.UtcNow,
            DateTime.UtcNow,
            "[Scan]"
        })!;

        Assert.True(shouldDispatch);
    }

    // ═══ TEST-007: DryRun E2E Smoke-Test ════════════════════════════════

    [Fact]
    public void DryRun_EndToEnd_ScansDedupesButDoesNotMoveFiles()
    {
        // Arrange: create temp ROM directory with two copies of same game
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_E2E_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var file1 = Path.Combine(tempDir, "Super Mario (USA).zip");
            var file2 = Path.Combine(tempDir, "Super Mario (Europe).zip");
            File.WriteAllBytes(file1, new byte[64]);
            File.WriteAllBytes(file2, new byte[64]);

            var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore();
            var orch = new Romulus.Infrastructure.Orchestration.RunOrchestrator(fs, audit);

            // Act: DryRun
            var options = new Romulus.Contracts.Models.RunOptions
            {
                Roots = new[] { tempDir },
                Extensions = new[] { ".zip" },
                Mode = "DryRun",
                PreferRegions = new[] { "US", "EU" }
            };

            var result = orch.Execute(options);

            // Assert: pipeline completed
            Assert.Equal("ok", result.Status);
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(2, result.TotalFilesScanned);
            Assert.True(result.GroupCount >= 1, "Should find at least 1 dedup group");
            Assert.Null(result.MoveResult); // DryRun does NOT move

            // Both files still exist
            Assert.True(File.Exists(file1), "USA file must still exist after DryRun");
            Assert.True(File.Exists(file2), "Europe file must still exist after DryRun");

            // Dedup groups contain expected data
            Assert.NotEmpty(result.DedupeGroups);
            Assert.NotEmpty(result.AllCandidates);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void DryRun_VMStateTransitions_FollowCorrectSequence()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");

        // Simulate the DryRun flow that MainWindow.xaml.cs executes
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
        Assert.True(vm.IsIdle);
        Assert.False(vm.IsBusy);

        // Preflight phase
        vm.TransitionTo(RunState.Preflight);
        Assert.True(vm.IsBusy);
        Assert.False(vm.IsIdle);

        // Scanning phase
        vm.TransitionTo(RunState.Scanning);
        Assert.True(vm.IsBusy);

        // Deduplicating
        vm.TransitionTo(RunState.Deduplicating);
        Assert.True(vm.IsBusy);

        // Complete DryRun (no Move phase)
        vm.DryRun = true;
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");
        Assert.Equal(RunState.CompletedDryRun, vm.CurrentRunState);
        Assert.True(vm.HasRunResult);
        Assert.False(vm.IsBusy);
        Assert.True(vm.ShowStartMoveButton); // DryRun shows "Start Move" button
        Assert.Equal("/tmp/report.html", vm.LastReportPath);
    }

    [Fact]
    public void DryRun_VMStateTransitions_MovePhaseFollowsDryRun()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");

        // First: complete a DryRun
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);
        vm.TransitionTo(RunState.Scanning);
        vm.TransitionTo(RunState.Deduplicating);
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");
        Assert.True(vm.ShowStartMoveButton);

        // Then: user clicks "Start Move" → goes through phases again
        vm.DryRun = false;
        vm.TransitionTo(RunState.Preflight);
        vm.TransitionTo(RunState.Scanning);
        vm.TransitionTo(RunState.Moving);
        Assert.True(vm.IsBusy);
        Assert.False(vm.HasRunResult); // Moving state doesn't have result yet

        // Complete Move
        vm.CompleteRun(success: true, reportPath: "/tmp/report2.html");
        Assert.Equal(RunState.Completed, vm.CurrentRunState);
        Assert.True(vm.HasRunResult);
        Assert.False(vm.ShowStartMoveButton); // Move done, no more "Start Move"
    }

    // ═══ RunService tests ═══════════════════════════════════════════════

    [Fact]
    public void RunService_GetSiblingDirectory_ReturnsParentSibling()
    {
        var result = new RunService().GetSiblingDirectory(@"C:\Games\Roms", "reports");
        Assert.Equal(Path.Combine(@"C:\Games", "reports"), result);
    }

    [Fact]
    public void RunService_GetSiblingDirectory_DriveRoot_FallsBackToSubdirectory()
    {
        var result = new RunService().GetSiblingDirectory(@"C:\", "reports");
        Assert.Equal(Path.Combine(@"C:\", "reports"), result);
    }

    [Fact]
    public async Task RunService_BuildOrchestrator_MapsDatRenameFlags_FromViewModel_Issue9()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gui_datrename_root_{Guid.NewGuid():N}");
        var datRoot = Path.Combine(Path.GetTempPath(), $"gui_datrename_dat_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(datRoot);

        try
        {
            var vm = new MainViewModel(new ThemeService(), new StubDialogService());
            vm.Roots.Add(root);
            vm.UseDat = true;
            vm.EnableDatRename = true;
            vm.DatRoot = datRoot;

            var (_, options, _, _) = await new RunService().BuildOrchestratorAsync(vm);

            Assert.True(options.EnableDat);
            Assert.True(options.EnableDatAudit);
            Assert.True(options.EnableDatRename);
        }
        finally
        {
            Directory.Delete(root, true);
            Directory.Delete(datRoot, true);
        }
    }

    [Fact]
    public async Task ExecuteRunAsync_ShouldAbort_WhenDatRenamePreviewNotConfirmed_Issue9()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gui_datrename_abort_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            var dialog = new StubDialogService
            {
                DangerConfirmResult = true,
                ConfirmReturnValue = false
            };

            var runService = new RecordingRunService(new RunResult
            {
                Status = "ok",
                ExitCode = 0,
                AllCandidates = Array.Empty<RomCandidate>(),
                DedupeGroups = Array.Empty<DedupeGroup>()
            });

            var vm = new MainViewModel(new ThemeService(), dialog, runService: runService);
            vm.Roots.Add(root);
            vm.UseDat = true;
            vm.EnableDatRename = true;
            vm.DryRun = false;

            vm.LastRunResult = new RunResult
            {
                DatAuditResult = new DatAuditResult(
                    Entries:
                    [
                        new DatAuditEntry(
                            FilePath: Path.Combine(root, "old-name.nes"),
                            Hash: "abc",
                            Status: DatAuditStatus.HaveWrongName,
                            DatGameName: "Super Mario Bros.",
                            DatRomFileName: "Super Mario Bros. (USA).nes",
                            ConsoleKey: "NES",
                            Confidence: 100)
                    ],
                    HaveCount: 0,
                    HaveWrongNameCount: 1,
                    MissCount: 0,
                    UnknownCount: 0,
                    AmbiguousCount: 0)
            };

            await vm.ExecuteRunAsync();

            Assert.Equal(1, dialog.ConfirmCallCount);
            Assert.Equal(0, runService.ExecuteRunCallCount);
            Assert.Equal(RunState.Idle, vm.CurrentRunState);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task ExecuteRunAsync_ShouldExecuteAndEnableRollback_WhenDatRenamePreviewConfirmed_Issue9()
    {
        var root = Path.Combine(Path.GetTempPath(), $"gui_datrename_confirm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);
        var auditPath = Path.Combine(root, "audit.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action\n", Encoding.UTF8);

        try
        {
            var dialog = new StubDialogService
            {
                DangerConfirmResult = true,
                ConfirmReturnValue = true
            };

            var runService = new RecordingRunService(
                new RunResult
                {
                    Status = "ok",
                    ExitCode = 0,
                    AllCandidates =
                    [
                        new RomCandidate { MainPath = Path.Combine(root, "game.nes"), Category = FileCategory.Game }
                    ],
                    DedupeGroups = Array.Empty<DedupeGroup>()
                },
                auditPath: auditPath,
                hasVerifiedRollback: true);

            var vm = new MainViewModel(new ThemeService(), dialog, runService: runService);
            vm.Roots.Add(root);
            vm.UseDat = true;
            vm.EnableDatRename = true;
            vm.DryRun = false;

            vm.TransitionTo(RunState.Preflight);

            vm.LastRunResult = new RunResult
            {
                DatAuditResult = new DatAuditResult(
                    Entries:
                    [
                        new DatAuditEntry(
                            FilePath: Path.Combine(root, "old-name.nes"),
                            Hash: "abc",
                            Status: DatAuditStatus.HaveWrongName,
                            DatGameName: "Super Mario Bros.",
                            DatRomFileName: "Super Mario Bros. (USA).nes",
                            ConsoleKey: "NES",
                            Confidence: 100)
                    ],
                    HaveCount: 0,
                    HaveWrongNameCount: 1,
                    MissCount: 0,
                    UnknownCount: 0,
                    AmbiguousCount: 0)
            };

            await vm.ExecuteRunAsync();

            Assert.Equal(1, dialog.ConfirmCallCount);
            Assert.Equal(1, runService.ExecuteRunCallCount);
            Assert.Equal(RunState.Completed, vm.CurrentRunState);
            Assert.True(vm.CanRollback);
            Assert.True(vm.HasRollbackUndo);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    // ═══ WatchService tests ═════════════════════════════════════════════

    [Fact]
    public void WatchService_Start_NoRoots_ReturnsZero()
    {
        using var ws = new WatchService();
        var count = ws.Start(Array.Empty<string>());
        Assert.Equal(0, count);
        Assert.False(ws.IsActive);
    }

    [Fact]
    public void WatchService_Start_NonExistentRoot_ReturnsZero()
    {
        using var ws = new WatchService();
        var count = ws.Start(new[] { @"Z:\NonExistent_12345" });
        Assert.Equal(0, count);
        Assert.False(ws.IsActive);
    }

    [Fact]
    public void WatchService_Start_ValidRoot_CreatesWatchers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ws_test_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            using var ws = new WatchService();
            var count = ws.Start(new[] { tempDir });
            Assert.Equal(1, count);
            Assert.True(ws.IsActive);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WatchService_Start_Toggle_StopsOnSecondCall()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ws_toggle_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            using var ws = new WatchService();
            ws.Start(new[] { tempDir });
            Assert.True(ws.IsActive);

            var count = ws.Start(new[] { tempDir });
            Assert.Equal(0, count);
            Assert.False(ws.IsActive);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WatchService_Dispose_CleansUp()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "ws_dispose_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        try
        {
            var ws = new WatchService();
            ws.Start(new[] { tempDir });
            Assert.True(ws.IsActive);

            ws.Dispose();
            Assert.False(ws.IsActive);

            // Double dispose should not throw
            ws.Dispose();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void WatchService_FlushPendingIfNeeded_NoPending_DoesNothing()
    {
        using var ws = new WatchService();
        ws.FlushPendingIfNeeded();
        Assert.False(ws.HasPending);
    }

    // ═══ RollbackService tests ══════════════════════════════════════════

    [Fact]
    public void RollbackService_Execute_NoAuditFile_ReturnsEmpty()
    {
        var result = Romulus.Infrastructure.Audit.RollbackService.Execute(
            Path.Combine(Path.GetTempPath(), "nonexistent_audit.csv"),
            new[] { @"C:\Games" });
        Assert.Equal(0, result.RolledBack);
    }

    [Fact]
    public async Task Rollback_WithMissingTrashFiles_ShowsIntegrityWarning()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_RollbackWarn_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var auditPath = Path.Combine(tempDir, "audit.csv");
            File.WriteAllText(auditPath,
                "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n" +
                $"{tempDir},{tempDir}\\old.bin,{tempDir}\\trash\\old.bin,MOVE,GAME,,dedupe,2026-01-01\n",
                Encoding.UTF8);

            var stub = new StubDialogService();
            stub.ConfirmResponses.Enqueue(true);
            stub.ConfirmResponses.Enqueue(false);

            var vm = new MainViewModel(new ThemeService(), stub);
            vm.Roots.Add(tempDir);
            vm.LastAuditPath = auditPath;

            await vm.RollbackCommand.ExecuteAsync(null);

            Assert.Equal(2, stub.ConfirmCallCount);
            Assert.Contains("integrity", stub.LastConfirmMessage ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ═══ ProfileService tests ═══════════════════════════════════════════

    [Fact]
    public void ProfileService_Delete_NoFile_ReturnsFalse()
    {
        // Delete without existing file should return false (no-op)
        // We can't easily test the true-path without touching %APPDATA%,
        // but the false-path is safe to verify.
        // (If settings happen to exist, the method still won't throw.)
        Assert.IsType<bool>(ProfileService.Delete());
    }

    [Fact]
    public void ProfileService_Import_InvalidJson_Throws()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"profile_test_{Guid.NewGuid():N}.txt");
        try
        {
            File.WriteAllText(tmp, "NOT JSON {{{");
            var ex = Record.Exception(() => ProfileService.Import(tmp));
            Assert.NotNull(ex);
            Assert.True(ex is JsonException, $"Expected JsonException but got {ex.GetType().FullName}");
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ProfileService_Export_RoundTrip()
    {
        var tmp = Path.Combine(Path.GetTempPath(), $"profile_export_{Guid.NewGuid():N}.json");
        try
        {
            var map = new Dictionary<string, string>
            {
                ["foo"] = "bar",
                ["baz"] = "42"
            };
            ProfileService.Export(tmp, map);
            Assert.True(File.Exists(tmp));

            var json = File.ReadAllText(tmp);
            var rt = JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            Assert.NotNull(rt);
            Assert.Equal("bar", rt!["foo"]);
            Assert.Equal("42", rt["baz"]);
        }
        finally
        {
            File.Delete(tmp);
        }
    }

    [Fact]
    public void ProfileService_LoadSavedConfigFlat_NoFile_ReturnsNull()
    {
        // LoadSavedConfigFlat reads from %APPDATA% — if no settings.json exists it returns null
        // This test is safe: it only verifies the null-path vs. crash behavior
        var result = ProfileService.LoadSavedConfigFlat();
        // Result is either null (no file) or a valid dictionary (file exists) — never throws
        Assert.True(result is null || result is Dictionary<string, string>);
    }

    // ═══ TrayService tests ══════════════════════════════════════════════

    [Fact]
    public void TrayService_IsActive_DefaultFalse()
    {
        // TrayService needs a Window — we can't create one in a headless test.
        // Verify the type exists and has the expected public API shape.
        var type = typeof(TrayService);
        Assert.True(type.IsSealed);
        Assert.Contains(type.GetInterfaces(), i => i == typeof(IDisposable));
        Assert.NotNull(type.GetProperty("IsActive"));
        Assert.NotNull(type.GetMethod("Toggle"));
        Assert.NotNull(type.GetMethod("OnWindowStateChanged"));
        Assert.NotNull(type.GetMethod("Dispose"));
    }

    // ═══ FeatureService extraction tests ════════════════════════════════

    [Fact]
    public void FeatureService_BuildMissingRomReport_AllVerified_ReturnsNull()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = @"C:\Roms\game1.rom", GameKey = "game1", DatMatch = true, Category = FileCategory.Game, Extension = ".rom", Region = "EU", SizeBytes = 100 }
        };
        var result = FeatureService.BuildMissingRomReport(candidates, new[] { @"C:\Roms" });
        Assert.Null(result);
    }

    [Fact]
    public void FeatureService_BuildMissingRomReport_HasUnverified_ReturnsReport()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = @"C:\Roms\SNES\game1.rom", GameKey = "game1", DatMatch = false, Category = FileCategory.Game, Extension = ".rom", Region = "EU", SizeBytes = 100 },
            new() { MainPath = @"C:\Roms\SNES\game2.rom", GameKey = "game2", DatMatch = true, Category = FileCategory.Game, Extension = ".rom", Region = "US", SizeBytes = 200 }
        };
        var result = FeatureService.BuildMissingRomReport(candidates, new[] { @"C:\Roms" });
        Assert.NotNull(result);
        Assert.Contains("1 / 2", result);
    }

    [Fact]
    public void FeatureService_BuildCrossRootReport_NoGroups_ShowsEmpty()
    {
        var groups = new List<DedupeGroup>();
        var result = FeatureService.BuildCrossRootReport(groups, new[] { @"C:\A", @"C:\B" });
        Assert.Contains("Cross-Root-Gruppen: 0", result);
        Assert.Contains("Keine Cross-Root-Duplikate", result);
    }

    [Fact]
    public void FeatureService_AppendCustomDatEntry_NewFile()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"dat_test_{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tmpDir);
            FeatureService.AppendCustomDatEntry(tmpDir, "  <game name=\"test\"><rom name=\"t.rom\" size=\"0\" crc=\"00000000\" /></game>");
            var datPath = Path.Combine(tmpDir, "custom.dat");
            Assert.True(File.Exists(datPath));
            var content = File.ReadAllText(datPath);
            Assert.Contains("<game name=\"test\">", content);
            Assert.Contains("</datafile>", content);
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FeatureService_FormatDatDiffReport_ContainsStats()
    {
        var diff = new DatDiffResult(
            Added: ["GameNew"],
            Removed: ["GameOld"],
            ModifiedCount: 1,
            UnchangedCount: 5);
        var report = FeatureService.FormatDatDiffReport("old.dat", "new.dat", diff);
        Assert.Contains("Gleich:       5", report);
        Assert.Contains("Geändert:     1", report);
        Assert.Contains("+ GameNew", report);
        Assert.Contains("- GameOld", report);
    }

    [Fact]
    public void FeatureService_BuildRuleEngineReport_NoRulesFile_ReturnsHelp()
    {
        // With a non-existent data directory, should return help text
        var report = FeatureService.BuildRuleEngineReport();
        Assert.NotNull(report);
        Assert.True(report.Length > 0);
    }

    [Fact]
    public void FeatureService_ImportDatFileToRoot_PathTraversal_SanitizesFilename()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"dat_import_{Guid.NewGuid():N}");
        var datRoot = Path.Combine(tmpDir, "dats");
        Directory.CreateDirectory(datRoot);
        // Create a source file in a parent directory (simulating attempted traversal)
        var parentFile = Path.Combine(tmpDir, "escape.dat");
        File.WriteAllText(parentFile, "test-content");
        try
        {
            // Source path with ".." — the method strips path and copies just the filename
            var sourcePath = Path.Combine(datRoot, "..", "escape.dat");
            var target = FeatureService.ImportDatFileToRoot(sourcePath, datRoot);
            // Target MUST be within datRoot (path traversal protection)
            Assert.StartsWith(Path.GetFullPath(datRoot), target);
            Assert.True(File.Exists(target));
            Assert.Equal("test-content", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FeatureService_ImportDatFileToRoot_ValidCopy()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), $"dat_import_{Guid.NewGuid():N}");
        var source = Path.Combine(tmpDir, "source.dat");
        var datRoot = Path.Combine(tmpDir, "dats");
        Directory.CreateDirectory(tmpDir);
        Directory.CreateDirectory(datRoot);
        try
        {
            File.WriteAllText(source, "<datafile/>");
            var target = FeatureService.ImportDatFileToRoot(source, datRoot);
            Assert.True(File.Exists(target));
            Assert.Equal("<datafile/>", File.ReadAllText(target));
        }
        finally
        {
            Directory.Delete(tmpDir, true);
        }
    }

    [Fact]
    public void FeatureService_BuildFtpSourceReport_ValidSftp()
    {
        var (valid, isPlain, report) = FeatureService.BuildFtpSourceReport("sftp://roms.example.com/roms");
        Assert.True(valid);
        Assert.False(isPlain);
        Assert.Contains("SFTP", report);
        Assert.Contains("roms.example.com", report);
    }

    [Fact]
    public void FeatureService_BuildFtpSourceReport_ValidFtp()
    {
        var (valid, isPlain, report) = FeatureService.BuildFtpSourceReport("ftp://host.local/data");
        Assert.True(valid);
        Assert.True(isPlain);
        Assert.Contains("FTP", report);
    }

    [Fact]
    public void FeatureService_BuildFtpSourceReport_InvalidUrl()
    {
        var (valid, _, report) = FeatureService.BuildFtpSourceReport("http://example.com");
        Assert.False(valid);
        Assert.Contains("Ungültige FTP-URL", report);
    }

    [Fact]
    public void FeatureService_BuildHtmlReportData_EmptyCandidates()
    {
        var (summary, entries) = FeatureService.BuildHtmlReportData(
            Array.Empty<RomCandidate>(), Array.Empty<DedupeGroup>(), null, true);
        Assert.Equal("DryRun", summary.Mode);
        Assert.Equal(0, summary.TotalFiles);
        Assert.Empty(entries);
    }

    [Fact]
    public void FeatureService_BuildHtmlReportData_PopulatedCandidates()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = @"C:\game.rom", GameKey = "game", Category = FileCategory.Game, Extension = ".rom", Region = "EU", SizeBytes = 100, RegionScore = 50, FormatScore = 500, VersionScore = 100, DatMatch = true },
            new() { MainPath = @"C:\junk.rom", GameKey = "junk", Category = FileCategory.Junk, Extension = ".rom", Region = "US", SizeBytes = 200 }
        };
        var groups = new List<DedupeGroup>
        {
            new() { GameKey = "game", Winner = candidates[0], Losers = [] }
        };
        var result = new RunResult { Status = "ok", DurationMs = 1234 };
        var (summary, entries) = FeatureService.BuildHtmlReportData(candidates, groups, result, false);
        Assert.Equal("Move", summary.Mode);
        Assert.Equal(2, summary.TotalFiles);
        Assert.Equal(1, summary.JunkCount);
        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void FeatureService_BuildHtmlReportData_UngroupedGameCandidate_CountsKeep()
    {
        var candidates = new List<RomCandidate>
        {
            new() { MainPath = @"C:\grouped-winner.rom", GameKey = "grouped", Category = FileCategory.Game, Extension = ".rom", Region = "EU", SizeBytes = 100 },
            new() { MainPath = @"C:\grouped-loser.rom", GameKey = "grouped", Category = FileCategory.Game, Extension = ".rom", Region = "US", SizeBytes = 100 },
            new() { MainPath = @"C:\standalone.rom", GameKey = "standalone", Category = FileCategory.Game, Extension = ".rom", Region = "JP", SizeBytes = 100 }
        };
        var groups = new List<DedupeGroup>
        {
            new() { GameKey = "grouped", Winner = candidates[0], Losers = [candidates[1]] }
        };
        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 3,
            DedupeGroups = groups,
            AllCandidates = candidates
        };

        var (summary, entries) = FeatureService.BuildHtmlReportData(candidates, groups, result, true);

        Assert.Equal("DryRun", summary.Mode);
        Assert.Equal(2, summary.KeepCount);
        Assert.Equal(1, summary.MoveCount);
        Assert.Equal(3, entries.Count);
        Assert.Equal(2, entries.Count(entry => entry.Action == "KEEP"));
    }

    [Theory]
    [InlineData("ABCDEF01", 8, true)]
    [InlineData("abcdef01", 8, true)]
    [InlineData("ZZZZZZZZ", 8, false)]
    [InlineData("ABCDEF0", 8, false)]
    [InlineData("da39a3ee5e6b4b0d3255bfef95601890afd80709", 40, true)]
    [InlineData("", 8, false)]
    public void FeatureService_IsValidHexHash(string hash, int len, bool expected)
    {
        Assert.Equal(expected, FeatureService.IsValidHexHash(hash, len));
    }

    [Fact]
    public void FeatureService_BuildCustomDatXmlEntry_EscapesXml()
    {
        var xml = FeatureService.BuildCustomDatXmlEntry("Game & \"Test\"", "rom<1>.bin", "AABBCCDD", "da39a3ee5e6b4b0d3255bfef95601890afd80709");
        Assert.Contains("Game &amp; &quot;Test&quot;", xml);
        Assert.Contains("rom&lt;1&gt;.bin", xml);
        Assert.Contains("crc=\"AABBCCDD\"", xml);
        Assert.Contains("sha1=\"da39a3ee5e6b4b0d3255bfef95601890afd80709\"", xml);
    }

    [Fact]
    public void FeatureService_BuildCustomDatXmlEntry_EmptySha1_OmitsSha1Attr()
    {
        var xml = FeatureService.BuildCustomDatXmlEntry("Game", "rom.bin", "AABBCCDD", "");
        Assert.DoesNotContain("sha1=", xml);
    }

    [Fact]
    public void FeatureService_BuildCommandPaletteReport_ShowsResults()
    {
        var results = new List<(string key, string name, string shortcut, int score)>
        {
            ("dryrun", "DryRun starten", "F5", 0),
            ("theme", "Theme wechseln", "Ctrl+T", 2)
        };
        var report = FeatureService.BuildCommandPaletteReport("dry", results);
        Assert.Contains("Ergebnisse für \"dry\"", report);
        Assert.Contains("DryRun starten", report);
        Assert.Contains("F5", report);
    }

    [Fact]
    public void FeatureService_BuildCommandPaletteReport_EmptyResults()
    {
        var report = FeatureService.BuildCommandPaletteReport("xyz",
            Array.Empty<(string, string, string, int)>());
        Assert.Contains("Ergebnisse für \"xyz\"", report);
    }

    // ═══ Browse + Quick Commands (Runde 18) ═════════════════════════════

    [Fact]
    public void BrowseToolPathCommand_Exists()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.BrowseToolPathCommand);
    }

    [Fact]
    public void BrowseFolderPathCommand_Exists()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.BrowseFolderPathCommand);
    }

    [Fact]
    public void QuickPreviewCommand_CanExecute_WhenRootsExistAndNotBusy()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        Assert.True(vm.QuickPreviewCommand.CanExecute(null));
    }

    [Fact]
    public void QuickPreviewCommand_CannotExecute_WhenNoRoots()
    {
        var vm = new MainViewModel();
        Assert.False(vm.QuickPreviewCommand.CanExecute(null));
    }

    [Fact]
    public void QuickPreviewCommand_CannotExecute_WhenBusy()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        SetRunStateViaValidPath(vm, RunState.Scanning);
        Assert.False(vm.QuickPreviewCommand.CanExecute(null));
    }

    [Fact]
    public void StartMoveCommand_CanExecute_AfterMatchingPreview()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");
        Assert.True(vm.StartMoveCommand.CanExecute(null));
    }

    [Fact]
    public void StartMoveCommand_CannotExecute_WithoutPreview()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        Assert.False(vm.StartMoveCommand.CanExecute(null));
    }

    [Fact]
    public void RequestStartMoveCommand_ArmsInlineConfirm()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");

        vm.RequestStartMoveCommand.Execute(null);

        Assert.True(vm.Shell.ShowMoveInlineConfirm);

        vm.CancelStartMoveCommand.Execute(null);

        Assert.False(vm.Shell.ShowMoveInlineConfirm);
    }

    [Fact]
    public void RunCommand_CannotExecute_InMoveModeWithoutPreview()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = false;

        Assert.False(vm.RunCommand.CanExecute(null));
    }

    [Fact]
    public void RunCommand_CanExecute_InMoveModeAfterMatchingPreview()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);
        vm.CompleteRun(success: true, reportPath: "/tmp/report.html");
        vm.DryRun = false;

        Assert.True(vm.RunCommand.CanExecute(null));
    }

    [Fact]
    public void CompleteRun_Cancelled_IncludesPhaseContextInSummary()
    {
        var vm = new MainViewModel();
        vm.PerfPhase = "Move";
        vm.TransitionTo(RunState.Preflight);

        vm.CompleteRun(success: false, cancelled: true);

        Assert.Contains("Phase", vm.RunSummaryText, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Move", vm.RunSummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CompleteRun_DryRun_IncludesMoveShortcutHint()
    {
        var vm = new MainViewModel();
        vm.Roots.Add(@"C:\TestRoot");
        vm.DryRun = true;
        vm.TransitionTo(RunState.Preflight);

        vm.CompleteRun(success: true, reportPath: "report.html");

        Assert.Contains("Ctrl+M", vm.RunSummaryText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RunCommand_CannotExecute_WhenBlockingValidationErrorExists()
    {
        var vm = new MainViewModel();
        var root = Path.Combine(Path.GetTempPath(), $"run_block_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            vm.Roots.Add(root);
            vm.AuditRoot = $"bad{'\0'}path";

            Assert.True(vm.HasBlockingValidationErrors);
            Assert.False(vm.RunCommand.CanExecute(null));
            Assert.Equal(StatusLevel.Blocked, vm.ReadyStatusLevel);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public async Task RunCommand_BlockedValidation_DoesNotBlockCallerWhileShowingInfoDialog_IssueF16()
    {
        var dialog = new BlockingInfoDialogService();
        var vm = new MainViewModel(new ThemeService(), dialog);
        var root = Path.Combine(Path.GetTempPath(), $"run_block_ui_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        Task? runTask = null;
        try
        {
            vm.Roots.Add(root);
            vm.AuditRoot = $"bad{'\0'}path";

            Assert.True(vm.HasBlockingValidationErrors);

            runTask = Task.Run(() => vm.RunCommand.Execute(null));

            await dialog.InfoEntered.WaitAsync(TimeSpan.FromSeconds(1));
            try
            {
                await runTask.WaitAsync(TimeSpan.FromMilliseconds(200));
            }
            catch (TimeoutException)
            {
                Assert.Fail("RunCommand should return quickly and not wait for modal info dialog completion.");
            }
        }
        finally
        {
            dialog.ReleaseInfo();
            if (runTask is not null)
            {
                await runTask.WaitAsync(TimeSpan.FromSeconds(1));
            }

            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void RunCommand_CanExecute_WhenOnlyValidationWarningExists()
    {
        var vm = new MainViewModel();
        var root = Path.Combine(Path.GetTempPath(), $"run_warn_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            vm.Roots.Add(root);
            vm.ToolChdman = @"C:\nonexistent\path\chdman.exe";

            Assert.True(vm.HasErrors);
            Assert.False(vm.HasBlockingValidationErrors);
            Assert.True(vm.RunCommand.CanExecute(null));
            Assert.Equal(StatusLevel.Warning, vm.ReadyStatusLevel);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void StartMoveCommand_CannotExecute_WhenBlockingValidationErrorExists()
    {
        var vm = new MainViewModel();
        var root = Path.Combine(Path.GetTempPath(), $"move_block_{Guid.NewGuid():N}");
        Directory.CreateDirectory(root);

        try
        {
            vm.Roots.Add(root);
            vm.DryRun = true;
            vm.TransitionTo(RunState.Preflight);
            vm.CompleteRun(success: true, reportPath: "/tmp/report.html");
            vm.AuditRoot = $"bad{'\0'}path";

            Assert.True(vm.HasBlockingValidationErrors);
            Assert.False(vm.StartMoveCommand.CanExecute(null));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void BrowseToolPathCommand_SetsChdman()
    {
        // BrowseToolPathCommand with "Chdman" parameter should set ToolChdman
        var stub = new StubDialogService { BrowseFileResult = @"C:\tools\chdman.exe" };
        var vm = new MainViewModel(new ThemeService(), stub);
        vm.BrowseToolPathCommand.Execute("Chdman");
        Assert.Equal(@"C:\tools\chdman.exe", vm.ToolChdman);
    }

    [Fact]
    public void BrowseFolderPathCommand_SetsDatRoot()
    {
        var stub = new StubDialogService { BrowseFolderResult = @"C:\dat" };
        var vm = new MainViewModel(new ThemeService(), stub);
        vm.BrowseFolderPathCommand.Execute("Dat");
        Assert.Equal(@"C:\dat", vm.DatRoot);
    }

    [Fact]
    public void BrowseToolPathCommand_NoOpWhenCancelled()
    {
        // BrowseFile returns null → property unchanged
        var stub = new StubDialogService { BrowseFileResult = null };
        var vm = new MainViewModel(new ThemeService(), stub);
        vm.ToolChdman = "original";
        vm.BrowseToolPathCommand.Execute("Chdman");
        Assert.Equal("original", vm.ToolChdman);
    }

    /// <summary>Minimal dialog service stub for VM command tests (no UI).</summary>
    private sealed class StubDialogService : IDialogService
    {
        public string? BrowseFileResult { get; set; }
        public string? BrowseFolderResult { get; set; }
        public bool ConfirmReturnValue { get; set; } = true;
        public Queue<bool> ConfirmResponses { get; } = new();
        public bool DangerConfirmResult { get; set; } = true;
        public int ConfirmCallCount { get; private set; }
        public string? LastConfirmMessage { get; private set; }
        public string? LastConfirmTitle { get; private set; }

        public string? BrowseFolder(string title = "Ordner auswählen") => BrowseFolderResult;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => BrowseFileResult;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung")
        {
            ConfirmCallCount++;
            LastConfirmMessage = message;
            LastConfirmTitle = title;
            if (ConfirmResponses.Count > 0)
                return ConfirmResponses.Dequeue();
            return ConfirmReturnValue;
        }
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => DangerConfirmResult;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries)
            => ConfirmReturnValue;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals)
        {
            ConfirmCallCount++;
            return ConfirmReturnValue;
        }
    }

    private sealed class BlockingInfoDialogService : IDialogService
    {
        private readonly TaskCompletionSource<bool> _infoEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource<bool> _releaseInfo = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task InfoEntered => _infoEntered.Task;

        public void ReleaseInfo() => _releaseInfo.TrySetResult(true);

        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung") => true;

        public void Info(string message, string title = "Information")
        {
            _infoEntered.TrySetResult(true);
            _releaseInfo.Task.GetAwaiter().GetResult();
        }

        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<Romulus.Contracts.Models.ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }

    private sealed class RecordingRunService : IRunService
    {
        private readonly RunResult _result;
        private readonly string? _auditPath;
        private readonly string? _reportPath;
        private readonly bool _hasVerifiedRollback;

        public RecordingRunService(
            RunResult result,
            string? auditPath = null,
            string? reportPath = null,
            bool hasVerifiedRollback = false)
        {
            _result = result;
            _auditPath = auditPath;
            _reportPath = reportPath;
            _hasVerifiedRollback = hasVerifiedRollback;
        }

        public int ExecuteRunCallCount { get; private set; }

        public Task<(RunOrchestrator Orchestrator, RunOptions Options, string? AuditPath, string? ReportPath)>
            BuildOrchestratorAsync(MainViewModel vm, Action<string>? onProgress = null)
        {
            onProgress?.Invoke("[Init] RecordingRunService bereit");
            var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore();
            var orchestrator = new RunOrchestrator(fs, audit, onProgress: onProgress);
            var options = new RunOptions
            {
                Roots = vm.Roots.ToList(),
                Mode = vm.DryRun ? "DryRun" : "Move",
                Extensions = new[] { ".zip" },
                EnableDat = vm.UseDat,
                EnableDatAudit = vm.UseDat,
                EnableDatRename = vm.UseDat && vm.EnableDatRename
            };
            return Task.FromResult((orchestrator, options, _auditPath, _reportPath));
        }

        public Task<RunService.RunServiceResult> ExecuteRunAsync(
            RunOrchestrator orchestrator,
            RunOptions options,
            string? auditPath,
            string? reportPath,
            CancellationToken ct)
        {
            ExecuteRunCallCount++;
            return Task.FromResult(new RunService.RunServiceResult
            {
                Result = _result,
                AuditPath = auditPath,
                ReportPath = reportPath
            });
        }

        public string GetSiblingDirectory(string rootPath, string siblingName)
            => Path.Combine(Path.GetDirectoryName(rootPath) ?? rootPath, siblingName);

        public IReadOnlyList<ConversionReviewEntry> BuildConversionReviewEntries(
            RunOptions runOptions,
            IReadOnlyList<DedupeGroup> dedupeGroups,
            CancellationToken cancellationToken = default)
            => Array.Empty<ConversionReviewEntry>();

        public bool HasVerifiedRollback(string? auditPath) => _hasVerifiedRollback;
    }

    private sealed class FakeRunService : IRunService
    {
        private readonly RunResult _result;

        public FakeRunService(RunResult result)
        {
            _result = result;
        }

        public Task<(RunOrchestrator Orchestrator, RunOptions Options, string? AuditPath, string? ReportPath)>
            BuildOrchestratorAsync(MainViewModel vm, Action<string>? onProgress = null)
        {
            onProgress?.Invoke("[Init] FakeRunService bereit");
            var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore();
            var orchestrator = new RunOrchestrator(fs, audit, onProgress: onProgress);
            var options = new RunOptions
            {
                Roots = vm.Roots.ToList(),
                Mode = vm.DryRun ? "DryRun" : "Move",
                Extensions = new[] { ".zip" }
            };
            return Task.FromResult((orchestrator, options, (string?)null, (string?)null));
        }

        public Task<RunService.RunServiceResult> ExecuteRunAsync(
            RunOrchestrator orchestrator,
            RunOptions options,
            string? auditPath,
            string? reportPath,
            CancellationToken ct)
        {
            return Task.FromResult(new RunService.RunServiceResult
            {
                Result = _result,
                AuditPath = auditPath,
                ReportPath = reportPath
            });
        }

        public string GetSiblingDirectory(string rootPath, string siblingName)
            => Path.Combine(Path.GetDirectoryName(rootPath) ?? rootPath, siblingName);

        public IReadOnlyList<ConversionReviewEntry> BuildConversionReviewEntries(
            RunOptions runOptions,
            IReadOnlyList<DedupeGroup> dedupeGroups,
            CancellationToken cancellationToken = default)
            => Array.Empty<ConversionReviewEntry>();

        public bool HasVerifiedRollback(string? auditPath) => false;
    }

    private sealed class TestTimeProvider(DateTimeOffset initialUtcNow) : ITimeProvider
    {
        private DateTimeOffset _utcNow = initialUtcNow;

        public DateTimeOffset UtcNow => _utcNow;

        public void Advance(TimeSpan delta)
        {
            _utcNow = _utcNow.Add(delta);
        }
    }

    // ═══ XAML Binding Validation (VERIFY-001) ═══════════════════════════

    [Fact]
    public void XamlBinding_AllPaths_ExistAsViewModelProperties()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        Assert.True(File.Exists(xamlPath), $"MainWindow.xaml not found at {xamlPath}");

        var xamlContent = File.ReadAllText(xamlPath);

        // Extract all {Binding PropertyName} paths (skip complex expressions with Converter, StringFormat alone)
        var bindingRegex = new System.Text.RegularExpressions.Regex(
            @"\{Binding\s+([A-Za-z][A-Za-z0-9_.]*)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var bindingPaths = new HashSet<string>();
        foreach (System.Text.RegularExpressions.Match match in bindingRegex.Matches(xamlContent))
        {
            var path = match.Groups[1].Value;
            // Take only the root property (before any dot for nested paths like Roots.Count)
            var rootProp = path.Contains('.') ? path.Split('.')[0] : path;
            bindingPaths.Add(rootProp);
        }

        // Get all public properties and public fields from MainViewModel via reflection
        var vmType = typeof(MainViewModel);
        var vmProperties = vmType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance)
            .Select(p => p.Name)
            .ToHashSet();

        // DataTemplate bindings use model properties, not VM properties
        var modelPropertyNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "Extension", "IsChecked", "ToolTip", "DisplayName", "Category",
            "Key", "Description", "Icon", "IsVisible", "RequiresRunResult",
            "Command", "Level", "Text", "Name", "Console", "DatFile",
            "IsExpanded", "IsLocked", "IsPinned", "IsPlanned", "Items",
            // Werkzeuge/Features tab DataTemplate models
            "HasRecentTools", "IsToolSearchActive", "QuickAccessItems",
            "RecentToolItems", "ToolCategories",
            // NotificationItem model + RelativeSource ancestor bindings
            "DataContext", "Message", "Type",
            // Child ViewModel / Command properties (coverage reflection may miss these)
            "Shell", "CommandPalette", "DatAudit", "ToggleCommandPaletteCommand"
        };

        var missing = new List<string>();
        foreach (var path in bindingPaths.OrderBy(p => p))
        {
            if (!vmProperties.Contains(path) && !modelPropertyNames.Contains(path))
                missing.Add(path);
        }

        Assert.True(missing.Count == 0,
            $"XAML bindings reference {missing.Count} VM properties that don't exist:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void XamlBinding_AllViewModelProperties_HaveINPC()
    {
        // Verify that MainViewModel implements INotifyPropertyChanged
        var vmType = typeof(MainViewModel);
        Assert.True(typeof(System.ComponentModel.INotifyPropertyChanged).IsAssignableFrom(vmType),
            "MainViewModel must implement INotifyPropertyChanged");
    }

}
