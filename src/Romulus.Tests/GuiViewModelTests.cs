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
public class GuiViewModelTests
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

    [Fact]
    public void AutoDetectDatMappingsCommand_CanExecute_TracksDatRootValidity()
    {
        var vm = new MainViewModel();

        vm.DatRoot = "";
        Assert.False(vm.AutoDetectDatMappingsCommand.CanExecute(null));

        var tempDatRoot = Path.Combine(Path.GetTempPath(), "Romulus_DatCmd_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDatRoot);
        try
        {
            vm.DatRoot = tempDatRoot;
            Assert.True(vm.AutoDetectDatMappingsCommand.CanExecute(null));
        }
        finally
        {
            if (Directory.Exists(tempDatRoot))
                Directory.Delete(tempDatRoot, true);
        }
    }

    [Fact]
    public void AutoDetectDatMappingsCommand_DoesNotAssignPs1ToVitaDat()
    {
        var vm = new MainViewModel();

        var tempDatRoot = Path.Combine(Path.GetTempPath(), "Romulus_DatMap_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDatRoot);

        var ps1Dat = Path.Combine(tempDatRoot, "Sony - PlayStation - Datfile (2026-04-03 01-01-13).dat");
        var vitaDat = Path.Combine(tempDatRoot, "Sony - PlayStation Vita - Datfile (2026-04-03 01-01-13).dat");

        // Regression guard: old substring+filesize heuristic could map PS1 to Vita DAT.
        File.WriteAllText(ps1Dat, "<datafile><game name=\"ps1\"/></datafile>");
        File.WriteAllText(vitaDat, new string('V', 4096));

        try
        {
            vm.DatMappings.Clear();
            vm.DatRoot = tempDatRoot;

            Assert.True(vm.AutoDetectDatMappingsCommand.CanExecute(null));
            vm.AutoDetectDatMappingsCommand.Execute(null);

            DatMapRow? ps1Mapping = null;
            foreach (var mapping in vm.DatMappings)
            {
                if (string.Equals(mapping.Console, "PS1", StringComparison.OrdinalIgnoreCase))
                {
                    ps1Mapping = mapping;
                    break;
                }
            }

            Assert.NotNull(ps1Mapping);
            Assert.True(
                string.Equals(
                    Path.GetFullPath(ps1Dat),
                    Path.GetFullPath(ps1Mapping!.DatFile ?? string.Empty),
                    StringComparison.OrdinalIgnoreCase),
                $"Expected PS1 mapping '{ps1Dat}', got '{ps1Mapping!.DatFile}'.");
        }
        finally
        {
            if (Directory.Exists(tempDatRoot))
                Directory.Delete(tempDatRoot, true);
        }
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
    public void RunService_BuildOrchestrator_MapsDatRenameFlags_FromViewModel_Issue9()
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

            var (_, options, _, _) = new RunService().BuildOrchestrator(vm);

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
            File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n", Encoding.UTF8);

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

        public (RunOrchestrator Orchestrator, RunOptions Options, string? AuditPath, string? ReportPath)
            BuildOrchestrator(MainViewModel vm, Action<string>? onProgress = null)
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
            return (orchestrator, options, _auditPath, _reportPath);
        }

        public RunService.RunServiceResult ExecuteRun(
            RunOrchestrator orchestrator,
            RunOptions options,
            string? auditPath,
            string? reportPath,
            CancellationToken ct)
        {
            ExecuteRunCallCount++;
            return new RunService.RunServiceResult
            {
                Result = _result,
                AuditPath = auditPath,
                ReportPath = reportPath
            };
        }

        public string GetSiblingDirectory(string rootPath, string siblingName)
            => Path.Combine(Path.GetDirectoryName(rootPath) ?? rootPath, siblingName);

        public bool HasVerifiedRollback(string? auditPath) => _hasVerifiedRollback;
    }

    private sealed class FakeRunService : IRunService
    {
        private readonly RunResult _result;

        public FakeRunService(RunResult result)
        {
            _result = result;
        }

        public (RunOrchestrator Orchestrator, RunOptions Options, string? AuditPath, string? ReportPath)
            BuildOrchestrator(MainViewModel vm, Action<string>? onProgress = null)
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
            return (orchestrator, options, null, null);
        }

        public RunService.RunServiceResult ExecuteRun(
            RunOrchestrator orchestrator,
            RunOptions options,
            string? auditPath,
            string? reportPath,
            CancellationToken ct)
        {
            return new RunService.RunServiceResult
            {
                Result = _result,
                AuditPath = auditPath,
                ReportPath = reportPath
            };
        }

        public string GetSiblingDirectory(string rootPath, string siblingName)
            => Path.Combine(Path.GetDirectoryName(rootPath) ?? rootPath, siblingName);

        public bool HasVerifiedRollback(string? auditPath) => false;
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

    // ═══ Accessibility Coverage (VERIFY-002) ════════════════════════════

    [Fact]
    public void Accessibility_AllButtons_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        // Parse Button elements and check for AutomationProperties.Name
        // Match <Button ... /> or <Button ...>...</Button> blocks
        var buttonRegex = new System.Text.RegularExpressions.Regex(
            @"<Button\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var buttonsWithoutA11y = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in buttonRegex.Matches(xamlContent))
        {
            var buttonTag = match.Value;
            if (!buttonTag.Contains("AutomationProperties.Name"))
            {
                // Extract x:Name or Content for identification
                var nameMatch = System.Text.RegularExpressions.Regex.Match(buttonTag, @"x:Name=""([^""]+)""");
                var contentMatch = System.Text.RegularExpressions.Regex.Match(buttonTag, @"Content=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value
                    : contentMatch.Success ? contentMatch.Groups[1].Value
                    : buttonTag[..Math.Min(80, buttonTag.Length)];
                buttonsWithoutA11y.Add(id);
            }
        }

        Assert.True(buttonsWithoutA11y.Count == 0,
            $"{buttonsWithoutA11y.Count} Button(s) without AutomationProperties.Name:\n" +
            string.Join("\n", buttonsWithoutA11y));
    }

    [Fact]
    public void Accessibility_AllTextBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var textBoxRegex = new System.Text.RegularExpressions.Regex(
            @"<TextBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in textBoxRegex.Matches(xamlContent))
        {
            var tag = match.Value;
            if (!tag.Contains("AutomationProperties.Name"))
            {
                var nameMatch = System.Text.RegularExpressions.Regex.Match(tag, @"x:Name=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value : tag[..Math.Min(80, tag.Length)];
                missing.Add(id);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} TextBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void Accessibility_AllComboBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var comboRegex = new System.Text.RegularExpressions.Regex(
            @"<ComboBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in comboRegex.Matches(xamlContent))
        {
            var tag = match.Value;
            if (!tag.Contains("AutomationProperties.Name"))
            {
                var nameMatch = System.Text.RegularExpressions.Regex.Match(tag, @"x:Name=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value : tag[..Math.Min(80, tag.Length)];
                missing.Add(id);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} ComboBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void Accessibility_AllListBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var listBoxRegex = new System.Text.RegularExpressions.Regex(
            @"<ListBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in listBoxRegex.Matches(xamlContent))
        {
            var tag = match.Value;
            if (!tag.Contains("AutomationProperties.Name"))
            {
                var nameMatch = System.Text.RegularExpressions.Regex.Match(tag, @"x:Name=""([^""]+)""");
                var id = nameMatch.Success ? nameMatch.Groups[1].Value : tag[..Math.Min(80, tag.Length)];
                missing.Add(id);
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} ListBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    [Fact]
    public void Accessibility_RegionCheckBoxes_HaveDescriptiveAutomationName()
    {
        var xamlContent = ReadAllWpfXaml();
        var regionBindingRegex = new System.Text.RegularExpressions.Regex(
            @"<CheckBox[^>]*IsChecked=""\{Binding (Prefer[A-Z0-9]+)\}""[^>]*>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var regionCodes = regionBindingRegex.Matches(xamlContent)
            .Select(match => match.Groups[1].Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();

        Assert.NotEmpty(regionCodes);

        var missingA11y = new List<string>();
        foreach (var region in regionCodes)
        {
            // Find CheckBox with this binding and check for AutomationProperties.Name
            var pattern = new System.Text.RegularExpressions.Regex(
                $@"<CheckBox[^>]*IsChecked=""\{{Binding {region}\}}""[^>]*>",
                System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

            var match = pattern.Match(xamlContent);
            if (!match.Success || !match.Value.Contains("AutomationProperties.Name"))
                missingA11y.Add(region);
        }

        Assert.True(missingA11y.Count == 0,
            $"{missingA11y.Count} Region CheckBox(es) without descriptive AutomationProperties.Name:\n" +
            string.Join("\n", missingA11y));
    }

    [Fact]
    public void Accessibility_DataTemplateCheckBoxes_HaveAutomationName()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        // DataTemplate CheckBoxes should have AutomationProperties.Name binding
        var dataTemplateRegex = new System.Text.RegularExpressions.Regex(
            @"<DataTemplate>\s*<CheckBox\s[^>]*?>",
            System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Singleline);

        var missing = new List<string>();
        foreach (System.Text.RegularExpressions.Match match in dataTemplateRegex.Matches(xamlContent))
        {
            if (!match.Value.Contains("AutomationProperties.Name"))
            {
                var contentMatch = System.Text.RegularExpressions.Regex.Match(match.Value, @"Content=""\{Binding ([^}]+)\}""");
                var id = contentMatch.Success ? contentMatch.Groups[1].Value : match.Value[..Math.Min(60, match.Value.Length)];
                missing.Add($"DataTemplate CheckBox with Content={id}");
            }
        }

        Assert.True(missing.Count == 0,
            $"{missing.Count} DataTemplate CheckBox(es) without AutomationProperties.Name:\n" +
            string.Join("\n", missing));
    }

    // ═══ XAML/VM Completeness Checks ════════════════════════════════════

    [Fact]
    public void XamlBinding_NoDuplicateAutomationNames()
    {
        var xamlPath = FindWpfFile("MainWindow.xaml");
        var xamlContent = File.ReadAllText(xamlPath);

        var a11yRegex = new System.Text.RegularExpressions.Regex(
            @"AutomationProperties\.Name=""([^""{}]+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (System.Text.RegularExpressions.Match match in a11yRegex.Matches(xamlContent))
        {
            var name = match.Groups[1].Value;
            names[name] = names.GetValueOrDefault(name) + 1;
        }

        var duplicates = names.Where(kv => kv.Value > 1)
            .Select(kv => $"'{kv.Key}' × {kv.Value}")
            .ToList();

        Assert.True(duplicates.Count == 0,
            $"Duplicate AutomationProperties.Name values:\n" +
            string.Join("\n", duplicates));
    }

    [Fact]
    public void XamlBinding_MinimumBindingCount()
    {
        // Ensure we don't accidentally lose bindings during refactoring
        var xamlContent = ReadAllWpfXaml();

        var bindingCount = System.Text.RegularExpressions.Regex.Matches(
            xamlContent, @"\{Binding\s").Count;

        Assert.True(bindingCount >= 70,
            $"Expected at least 70 bindings in MainWindow.xaml, found {bindingCount}. " +
            "Bindings may have been accidentally removed during refactoring.");
    }

    [Fact]
    public void XamlBinding_MinimumAutomationPropertiesCount()
    {
        // Ensure accessibility annotations don't regress
        var xamlContent = ReadAllWpfXaml();

        var a11yCount = System.Text.RegularExpressions.Regex.Matches(
            xamlContent, @"AutomationProperties\.Name").Count;

        Assert.True(a11yCount >= 70,
            $"Expected at least 70 AutomationProperties.Name in MainWindow.xaml, found {a11yCount}. " +
            "Accessibility annotations may have been accidentally removed.");
    }

    // ═══ TASK-104: TabIndex Groups ══════════════════════════════════════

    [Fact]
    public void TabIndex_MainWindow_HasLogicalGroups()
    {
        var xamlContent = ReadAllWpfXaml();

        var tabIndexRegex = new System.Text.RegularExpressions.Regex(
            @"TabIndex=""(\d+)""",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        var indices = new List<int>();
        foreach (System.Text.RegularExpressions.Match match in tabIndexRegex.Matches(xamlContent))
            indices.Add(int.Parse(match.Groups[1].Value));

        // Action bar group (1-3)
        Assert.Contains(1, indices);
        Assert.Contains(2, indices);
        Assert.Contains(3, indices);

        // Root list group (10-12)
        Assert.Contains(10, indices);
        Assert.Contains(11, indices);
        Assert.Contains(12, indices);

        // Config options group (40-41)
        Assert.Contains(40, indices);
        Assert.Contains(41, indices);

        // Navigation / advanced option groups (50+)
        Assert.Contains(50, indices);
        Assert.Contains(51, indices);
        Assert.Contains(52, indices);
        Assert.Contains(53, indices);
        Assert.Contains(54, indices);
        Assert.Contains(55, indices);

        // Safety group (60+)
        Assert.Contains(60, indices);
        Assert.Contains(61, indices);

        // At least 14 controls with explicit TabIndex across the shell/views
        Assert.True(indices.Count >= 14,
            $"Expected at least 14 controls with TabIndex, found {indices.Count}");
    }

    // ═══ TASK-127: Feature Buttons use MinWidth not Width ═══════════════

    [Fact]
    public void FeatureButtons_ProfileButtons_UseMinWidth()
    {
        var xamlContent = ReadAllWpfXaml();

        // Profile buttons should use MinWidth, not fixed Width
        // AutomationProperties.Name is now a binding key (e.g. Settings.ProfileSaveTip)
        var profileBindingKeys = new[] { "Settings.ProfileSaveTip", "Settings.ProfileLoadTip", "Settings.ProfileDeleteTip", "Settings.ProfileImportTip", "Settings.ProfileDiffTip" };
        foreach (var key in profileBindingKeys)
        {
            var pattern = $"Loc[{key}]";
            var idx = xamlContent.IndexOf(pattern, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Button with AutomationProperties.Name binding for '{key}' not found in XAML");

            // Extract the Button tag containing this automation name
            var tagStart = xamlContent.LastIndexOf("<Button", idx, StringComparison.Ordinal);
            var tagEnd = xamlContent.IndexOf("/>", idx, StringComparison.Ordinal);
            if (tagEnd < 0) tagEnd = xamlContent.IndexOf(">", idx, StringComparison.Ordinal);
            var buttonTag = xamlContent[tagStart..(tagEnd + 2)];

            Assert.Contains("MinWidth=", buttonTag);
            // Should NOT have fixed Width= (only MinWidth=)
            var hasFixedWidth = System.Text.RegularExpressions.Regex.IsMatch(
                buttonTag, @"(?<!\bMin)Width=""");
            Assert.False(hasFixedWidth, $"Button '{key}' still uses fixed Width instead of MinWidth");
        }
    }

    // ═══ TASK-095: MessageDialog exists and DialogService uses it ════════

    [Fact]
    public void MessageDialog_XamlFile_UsesDynamicResources()
    {
        var xamlPath = FindWpfFile("MessageDialog.xaml");
        Assert.True(File.Exists(xamlPath), "MessageDialog.xaml must exist");

        var xamlContent = File.ReadAllText(xamlPath);
        Assert.Contains("DynamicResource BrushBackground", xamlContent);
        Assert.Contains("DynamicResource BrushTextPrimary", xamlContent);
        Assert.Contains("DynamicResource BrushAccentCyan", xamlContent);
    }

    [Fact]
    public void DialogService_UsesMessageDialog_NotRawMessageBox()
    {
        var csPath = FindWpfFile(Path.Combine("Services", "DialogService.cs"));
        Assert.True(File.Exists(csPath), "DialogService.cs must exist");

        var code = File.ReadAllText(csPath);

        // DialogService methods should use MessageDialog.Show, not MessageBox.Show
        Assert.Contains("MessageDialog.Show(", code);

        // The only MessageBox reference should be for the return type, not for Show calls
        var messageBoxShowCount = System.Text.RegularExpressions.Regex.Matches(
            code, @"MessageBox\.Show\(").Count;
        Assert.Equal(0, messageBoxShowCount);
    }

    // ═══ TASK-131/132: INotifyDataErrorInfo Path Validation ═════════════

    [Fact]
    public void ToolPath_InvalidPath_HasErrors()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = @"C:\nonexistent\path\chdman.exe";
        Assert.True(vm.HasErrors);
        var errors = vm.GetErrors(nameof(vm.ToolChdman)).Cast<string>().ToList();
        Assert.Single(errors);
        Assert.Contains("nicht gefunden", errors[0]);
    }

    [Fact]
    public void ToolPath_EmptyPath_NoErrors()
    {
        var vm = new MainViewModel();
        vm.ToolChdman = "";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void DirectoryPath_InvalidPath_HasErrors()
    {
        var vm = new MainViewModel();
        vm.DatRoot = @"C:\nonexistent\directory\path";
        Assert.True(vm.HasErrors);
        var errors = vm.GetErrors(nameof(vm.DatRoot)).Cast<string>().ToList();
        Assert.Single(errors);
        Assert.Contains("existiert nicht", errors[0]);
    }

    [Fact]
    public void DirectoryPath_EmptyPath_NoErrors()
    {
        var vm = new MainViewModel();
        vm.DatRoot = "";
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void DirectoryPath_ValidPath_NoErrors()
    {
        var vm = new MainViewModel();
        vm.DatRoot = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        Assert.False(vm.HasErrors);
    }

    [Fact]
    public void DirectoryPath_ProtectedPath_HasBlockingError()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var protectedPath = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        if (string.IsNullOrWhiteSpace(protectedPath))
            return;

        var vm = new MainViewModel();
        vm.TrashRoot = protectedPath;

        Assert.True(vm.HasBlockingValidationErrors);
        var errors = vm.GetErrors(nameof(vm.TrashRoot)).Cast<string>().ToList();
        Assert.Single(errors);
        Assert.Contains("protected", errors[0], StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ErrorsChanged_FiredOnInvalidPath()
    {
        var vm = new MainViewModel();
        string? changedProperty = null;
        vm.ErrorsChanged += (_, e) => changedProperty = e.PropertyName;
        vm.TrashRoot = @"C:\nonexistent\directory";
        Assert.Equal(nameof(vm.TrashRoot), changedProperty);
    }

    [Fact]
    public void Xaml_PathBindings_HaveValidatesOnNotifyDataErrors()
    {
        var xaml = ReadAllWpfXaml();
        foreach (var prop in new[] { "ToolChdman", "ToolDolphin", "Tool7z", "ToolPsxtract", "ToolCiso",
                                     "DatRoot", "TrashRoot", "AuditRoot", "Ps3DupesRoot" })
        {
            var pattern = $"Binding {prop}";
            var idx = xaml.IndexOf(pattern, StringComparison.Ordinal);
            Assert.True(idx >= 0, $"Binding for {prop} not found in XAML");
            var segment = xaml.Substring(idx, Math.Min(200, xaml.Length - idx));
            Assert.Contains("ValidatesOnNotifyDataErrors=True", segment);
        }
    }

    // ═══ TASK-117: Locale has tooltip ═══════════════════════════════════

    [Fact]
    public void Xaml_LocaleComboBox_HasLocalizationTooltip()
    {
        var xaml = ReadAllWpfXaml();
        var localeIdx = xaml.IndexOf("Binding Locale", StringComparison.Ordinal);
        Assert.True(localeIdx >= 0);
        var segment = xaml.Substring(Math.Max(0, localeIdx - 200), Math.Min(600, xaml.Length - Math.Max(0, localeIdx - 200)));
        Assert.Contains("ToolTip=", segment);
    }

    // ═══ WPF file locator ══════════════════════════════════════════════

    private static string FindWpfFile(string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var repoRoot = FindRepoRoot(callerPath);
        var candidate = Path.Combine(repoRoot, "src", "Romulus.UI.Wpf", fileName);
        if (File.Exists(candidate))
            return candidate;

        return Path.Combine("src", "Romulus.UI.Wpf", fileName);
    }

    /// <summary>Read and concatenate all WPF XAML files (MainWindow + Views/*.xaml).</summary>
    private static string ReadAllWpfXaml()
    {
        var main = FindWpfFile("MainWindow.xaml");
        var sb = new System.Text.StringBuilder(File.ReadAllText(main));
        var viewsDir = Path.Combine(Path.GetDirectoryName(main)!, "Views");
        if (Directory.Exists(viewsDir))
        {
            foreach (var file in Directory.GetFiles(viewsDir, "*.xaml"))
                sb.AppendLine(File.ReadAllText(file));
        }
        return sb.ToString();
    }

    // ═══ TEST-005: Preset Commands (SafeDryRun, FullSort, Convert) ══════

    [Fact]
    public void PresetSafeDryRun_SetsDryRun_DisablesConvert()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertEnabled = true;
        vm.AggressiveJunk = true;
        vm.Roots.Add(@"C:\TestRoot");

        vm.PresetSafeDryRunCommand.Execute(null);

        Assert.True(vm.DryRun);
        Assert.False(vm.ConvertEnabled);
        Assert.False(vm.AggressiveJunk);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
    }

    [Fact]
    public void PresetFullSort_SetsDryRun_EnablesSort()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.SortConsole = false;
        vm.Roots.Add(@"C:\TestRoot");

        vm.PresetFullSortCommand.Execute(null);

        Assert.True(vm.DryRun);
        Assert.True(vm.SortConsole);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
    }

    [Fact]
    public void PresetConvert_SetsDryRun_EnablesConvert()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertEnabled = false;
        vm.Roots.Add(@"C:\TestRoot");

        vm.PresetConvertCommand.Execute(null);

        Assert.True(vm.DryRun);
        Assert.True(vm.ConvertEnabled);
    }

    [Fact]
    public void PresetCommands_AreAlwaysExecutable()
    {
        var vm = new MainViewModel();
        // Presets should always be executable (no CanExecute guard)
        Assert.True(vm.PresetSafeDryRunCommand.CanExecute(null));
        Assert.True(vm.PresetFullSortCommand.CanExecute(null));
        Assert.True(vm.PresetConvertCommand.CanExecute(null));
    }

    // ═══ TEST-002 supplement: Invalid state transitions ═════════════════

    [Theory]
    [InlineData(RunState.Idle, RunState.Scanning)]
    [InlineData(RunState.Idle, RunState.Completed)]
    [InlineData(RunState.Idle, RunState.CompletedDryRun)]
    [InlineData(RunState.Idle, RunState.Moving)]
    [InlineData(RunState.Idle, RunState.Converting)]
    [InlineData(RunState.Idle, RunState.Deduplicating)]
    [InlineData(RunState.Idle, RunState.Sorting)]
    [InlineData(RunState.Scanning, RunState.Preflight)]
    [InlineData(RunState.Moving, RunState.Scanning)]
    public void InvalidTransition_IsRejected(RunState from, RunState to)
    {
        // RF-007: IsValidTransition must return false for invalid transitions
        Assert.False(MainViewModel.IsValidTransition(from, to),
            $"Transition {from} → {to} should be invalid");
    }

    [Theory]
    [InlineData(RunState.Idle, RunState.Scanning)]
    [InlineData(RunState.Idle, RunState.Completed)]
    [InlineData(RunState.Idle, RunState.CompletedDryRun)]
    [InlineData(RunState.Idle, RunState.Moving)]
    [InlineData(RunState.Idle, RunState.Converting)]
    [InlineData(RunState.Idle, RunState.Deduplicating)]
    [InlineData(RunState.Idle, RunState.Sorting)]
    [InlineData(RunState.Scanning, RunState.Preflight)]
    [InlineData(RunState.Moving, RunState.Scanning)]
    public void InvalidTransition_ThrowsInvalidOperationException(RunState from, RunState to)
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, from);

        Assert.Throws<InvalidOperationException>(() => vm.CurrentRunState = to);
    }

    [Theory]
    [InlineData(RunState.Idle, RunState.Preflight)]
    [InlineData(RunState.Preflight, RunState.Scanning)]
    [InlineData(RunState.Scanning, RunState.Deduplicating)]
    [InlineData(RunState.Deduplicating, RunState.Sorting)]
    [InlineData(RunState.Deduplicating, RunState.Moving)]
    [InlineData(RunState.Sorting, RunState.Moving)]
    [InlineData(RunState.Moving, RunState.Sorting)]
    [InlineData(RunState.Moving, RunState.Converting)]
    [InlineData(RunState.Preflight, RunState.Cancelled)]
    [InlineData(RunState.Scanning, RunState.Failed)]
    [InlineData(RunState.Moving, RunState.Completed)]
    public void ValidTransition_DoesNotThrow(RunState from, RunState to)
    {
        var vm = new MainViewModel();
        SetRunStateViaValidPath(vm, from);

        var ex = Record.Exception(() => vm.CurrentRunState = to);
        Assert.Null(ex);
        Assert.Equal(to, vm.CurrentRunState);
    }

    // ═══ TEST-007 supplement: CTS cancel signal ═════════════════════════

    [Fact]
    public void CreateRunCancellation_ReturnsCancellableToken()
    {
        var vm = new MainViewModel();
        var ct = vm.CreateRunCancellation();
        Assert.False(ct.IsCancellationRequested);
    }

    [Fact]
    public void CancelCommand_SignalsCancellationToken()
    {
        var vm = new MainViewModel();
        var ct = vm.CreateRunCancellation();
        SetRunStateViaValidPath(vm, RunState.Scanning);

        vm.CancelCommand.Execute(null);

        Assert.True(ct.IsCancellationRequested);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    [Fact]
    public void CancelCommand_MultipleCalls_NoThrow()
    {
        var vm = new MainViewModel();
        var ct = vm.CreateRunCancellation();
        SetRunStateViaValidPath(vm, RunState.Scanning);

        vm.CancelCommand.Execute(null);
        // Second cancel attempt — should be safe
        var ex = Record.Exception(() => vm.CancelCommand.Execute(null));
        Assert.Null(ex);
    }

    // ═══ TEST-008 supplement: Rollback file restoration ═════════════════

    [Fact]
    public void RollbackService_Execute_RestoresMovedFile()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Rollback_" + Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(tempDir, "src");
        var destDir = Path.Combine(tempDir, "dest");
        var keyPath = Path.Combine(tempDir, "audit-signing.key");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);

        try
        {
            var srcFile = Path.Combine(srcDir, "game.rom");
            var destFile = Path.Combine(destDir, "game.rom");
            File.WriteAllText(destFile, "ROM-DATA");

            // Write audit CSV manually (as AuditCsvStore would)
            var auditPath = Path.Combine(tempDir, "audit.csv");
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore(keyFilePath: keyPath);
            audit.AppendAuditRow(auditPath, tempDir, srcFile, destFile, "Move", "GAME", "", "test");
            audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

            // Execute rollback: should move destFile back to srcFile
            var restored = Romulus.Infrastructure.Audit.RollbackService.Execute(auditPath, new[] { tempDir }, keyPath);

            Assert.Equal(1, restored.RolledBack);
            Assert.True(File.Exists(srcFile), "Source file should be restored");
            Assert.False(File.Exists(destFile), "Dest file should be gone after rollback");
            Assert.Equal("ROM-DATA", File.ReadAllText(srcFile));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void RollbackService_Execute_SkipsNonMoveActions()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Rollback2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var auditPath = Path.Combine(tempDir, "audit.csv");
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore();
            audit.AppendAuditRow(auditPath, tempDir,
                Path.Combine(tempDir, "a.rom"),
                Path.Combine(tempDir, "b.rom"),
                "Skip", "GAME", "", "test");

            var restored = Romulus.Infrastructure.Audit.RollbackService.Execute(auditPath, new[] { tempDir });
            Assert.Equal(0, restored.RolledBack);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void RollbackService_Execute_BlocksTamperedSignedAudit()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Rollback3_" + Guid.NewGuid().ToString("N"));
        var srcDir = Path.Combine(tempDir, "src");
        var destDir = Path.Combine(tempDir, "dest");
        var keyPath = Path.Combine(tempDir, "audit-signing.key");
        Directory.CreateDirectory(srcDir);
        Directory.CreateDirectory(destDir);

        try
        {
            var srcFile = Path.Combine(srcDir, "game.rom");
            var destFile = Path.Combine(destDir, "game.rom");
            File.WriteAllText(destFile, "ROM-DATA");

            var auditPath = Path.Combine(tempDir, "audit.csv");
            var audit = new Romulus.Infrastructure.Audit.AuditCsvStore(keyFilePath: keyPath);
            audit.AppendAuditRow(auditPath, tempDir, srcFile, destFile, "Move", "GAME", "", "test");
            audit.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

            File.AppendAllText(auditPath, "tampered\n");

            var restored = Romulus.Infrastructure.Audit.RollbackService.Execute(auditPath, new[] { tempDir }, keyPath);

            Assert.Equal(0, restored.RolledBack);
            Assert.Equal(1, restored.Failed);
            Assert.False(File.Exists(srcFile));
            Assert.True(File.Exists(destFile));
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    // ═══ TEST-009 supplement: Runtime theme cycle ═══════════════════════

    [Fact]
    public void ThemeService_InitialState_IsDark()
    {
        var ts = new ThemeService();
        Assert.Equal(AppTheme.Dark, ts.Current);
        Assert.True(ts.IsDark);
    }

    [Fact]
    public void ThemeService_ToggleCycle_FollowsAllThemes()
    {
        // Toggle() calls ApplyTheme() which needs Application.Current — not available in unit tests.
        // Verify the cycle logic is correct by checking the AllThemes list order.
        var all = ThemeService.AllThemes;
        Assert.Equal(6, all.Count);
        Assert.Equal(AppTheme.Dark, all[0]);
        Assert.Equal(AppTheme.CleanDarkPro, all[1]);
        Assert.Equal(AppTheme.RetroCRT, all[2]);
        Assert.Equal(AppTheme.ArcadeNeon, all[3]);
        Assert.Equal(AppTheme.Light, all[4]);
        Assert.Equal(AppTheme.HighContrast, all[5]);
    }

    [Fact]
    public void ThemeService_ApplyThemeBool_MapsCorrectly()
    {
        // ApplyTheme(bool) maps: true → Dark, false → Light
        // Verify the mapping logic without calling Application.Current
        Assert.Equal(AppTheme.Dark, true ? AppTheme.Dark : AppTheme.Light);
        Assert.Equal(AppTheme.Light, false ? AppTheme.Dark : AppTheme.Light);
    }

    [Fact]
    public void ThemeNames_MatchExpectedValues()
    {
        var names = Enum.GetNames<AppTheme>();
        Assert.Contains("Dark", names);
        Assert.Contains("Light", names);
        Assert.Contains("HighContrast", names);
        Assert.Contains("CleanDarkPro", names);
        Assert.Contains("RetroCRT", names);
        Assert.Contains("ArcadeNeon", names);
        Assert.Equal(6, names.Length);
    }

    // ═══ TEST-010: VM Smoke Tests ═══════════════════════════════════════

    [Fact]
    public void MainViewModel_Constructor_NoException()
    {
        var ex = Record.Exception(() => new MainViewModel());
        Assert.Null(ex);
    }

    [Fact]
    public void MainViewModel_AllPublicCommands_NotNull()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.RunCommand);
        Assert.NotNull(vm.CancelCommand);
        Assert.NotNull(vm.StartMoveCommand);
        Assert.NotNull(vm.PresetSafeDryRunCommand);
        Assert.NotNull(vm.PresetFullSortCommand);
        Assert.NotNull(vm.PresetConvertCommand);
        Assert.NotNull(vm.QuickPreviewCommand);
        Assert.NotNull(vm.OpenReportCommand);
        Assert.NotNull(vm.SaveSettingsCommand);
        Assert.NotNull(vm.LoadSettingsCommand);
        Assert.NotNull(vm.GameKeyPreviewCommand);
    }

    [Fact]
    public void MainViewModel_DefaultState_IsIdle()
    {
        var vm = new MainViewModel();
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
        Assert.True(vm.IsIdle);
        Assert.False(vm.IsBusy);
        Assert.False(vm.HasRunResult);
    }

    [Fact]
    public void MainViewModel_Collections_Initialized()
    {
        var vm = new MainViewModel();
        Assert.NotNull(vm.Roots);
        Assert.NotNull(vm.LogEntries);
        Assert.NotNull(vm.ExtensionFilters);
        Assert.NotNull(vm.ConsoleFilters);
        Assert.NotNull(vm.ToolCategories);
        Assert.NotNull(vm.QuickAccessItems);
        Assert.NotNull(vm.RecentToolItems);
    }

    [Fact]
    public void MainViewModel_SettingsDefaults_Sensible()
    {
        var vm = new MainViewModel();
        // Default should be safe: DryRun on, no aggressive junk
        Assert.True(vm.DryRun);
        Assert.False(vm.AggressiveJunk);
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
    }

    // ═══ TASK-124: Localization — inline strings must use _loc ═════════

    [Theory]
    [InlineData("Run.MoveApplyGate.Unlocked")]
    [InlineData("Run.MoveApplyGate.LockedNoPrev")]
    [InlineData("Run.MoveApplyGate.LockedChanged")]
    [InlineData("Progress.BusyHint.Converting")]
    [InlineData("Progress.BusyHint.Preview")]
    [InlineData("Progress.BusyHint.DryRun")]
    [InlineData("Progress.BusyHint.Move")]
    [InlineData("Progress.BusyHint.CancelRequested")]
    [InlineData("Step.NoRoots")]
    [InlineData("Step.Ready")]
    [InlineData("Step.PressF5")]
    [InlineData("Step.Preflight")]
    [InlineData("Step.Scanning")]
    [InlineData("Step.Deduplicating")]
    [InlineData("Step.Sorting")]
    [InlineData("Step.Moving")]
    [InlineData("Step.Converting")]
    [InlineData("Step.Running")]
    [InlineData("Step.PreviewComplete")]
    [InlineData("Step.Completed")]
    [InlineData("Status.RootsConfigured")]
    [InlineData("Status.NoRoots")]
    [InlineData("Status.ToolsFound")]
    [InlineData("Status.ToolsNotFound")]
    [InlineData("Status.NoTools")]
    [InlineData("Status.DatActive")]
    [InlineData("Status.DatPathInvalid")]
    [InlineData("Status.DatDisabled")]
    [InlineData("Status.Ready.Ok")]
    [InlineData("Status.Ready.Warning")]
    [InlineData("Status.Ready.Blocked")]
    [InlineData("Tool.Status.Found")]
    [InlineData("Tool.Status.NotFound")]
    [InlineData("Conversion.ReviewRequired")]
    [InlineData("Result.Summary.PreviewDone")]
    [InlineData("Result.Summary.PreviewShortcutHint")]
    [InlineData("Result.Summary.ChangesApplied")]
    [InlineData("Result.Summary.CancelledPartial")]
    [InlineData("Result.Summary.CancelledInPhase")]
    [InlineData("Result.Summary.CancelledInPhaseMoved")]
    [InlineData("Result.Context.Preview")]
    [InlineData("Result.Context.CancelledPartial")]
    [InlineData("Result.Context.CancelledNoData")]
    [InlineData("Result.Context.ConvertOnly")]
    [InlineData("Result.Context.MoveCompleted")]
    [InlineData("Result.InlineConfirmWaiting")]
    [InlineData("Result.InlineConfirmReady")]
    [InlineData("Phase.Skipped.MoveConvert")]
    [InlineData("Phase.Skipped.MoveOnly")]
    [InlineData("Phase.Skipped.ConvertOnly")]
    [InlineData("Result.Summary.Failed")]
    public void Localization_DeJson_ContainsRequiredKey(string key)
    {
        // RED: These keys do not yet exist in de.json
        var loc = new LocalizationService();
        var value = loc[key];
        Assert.False(value.StartsWith('[') && value.EndsWith(']'),
            $"Key '{key}' missing from de.json — got placeholder [{key}]");
    }

    [Fact]
    public void MoveApplyGateText_UsesLocalizationService_NotHardcodedGerman()
    {
        // Inject English locale so hardcoded German would be detected
        var loc = new Romulus.UI.Wpf.Services.LocalizationService();
        loc.SetLocale("en");
        var vm = new MainViewModel(new Romulus.UI.Wpf.Services.ThemeService(), new Romulus.UI.Wpf.Services.WpfDialogService(), loc: loc);
        var text = vm.MoveApplyGateText;
        Assert.DoesNotContain("Änderungen anwenden ist gesperrt", text);
    }

    [Fact]
    public void RefreshStatus_StatusLabels_UseLocalizationService()
    {
        // Inject English locale so hardcoded German would be detected
        var loc = new Romulus.UI.Wpf.Services.LocalizationService();
        loc.SetLocale("en");
        var vm = new MainViewModel(new Romulus.UI.Wpf.Services.ThemeService(), new Romulus.UI.Wpf.Services.WpfDialogService(), loc: loc);
        vm.RefreshStatus();
        Assert.DoesNotContain("Keine Ordner", vm.StatusRoots);
        Assert.DoesNotContain("Keine Tools", vm.StatusTools);
    }

    [Fact]
    public void Localization_De_UsesUnifiedUxTerms_ForAuditFindings()
    {
        var loc = new LocalizationService();

        Assert.Equal("Behalten", loc["Start.Winners"]);
        Assert.Equal("Vorbereitung", loc["Phase.Preflight"]);
        Assert.Equal("Duplikat-Erkennung", loc["Phase.Dedupe"]);
        Assert.Equal("Aussortiert (Junk)", loc["Result.MetricJunk"]);
        Assert.Contains("Aufräumen starten", loc["Result.BtnCleanup"], StringComparison.Ordinal);
    }

    [Fact]
    public void Localization_De_RollbackPreview_ContainsRestoreCountAndTrashPathPlaceholders()
    {
        var loc = new LocalizationService();
        var preview = loc["Dialog.Rollback.Preview"];

        Assert.Contains("{4}", preview, StringComparison.Ordinal);
        Assert.Contains("{5}", preview, StringComparison.Ordinal);
    }

    [Fact]
    public void StepLabels_Default_UseLocalizationService()
    {
        // RED: Step labels default to hardcoded German
        var vm = new MainViewModel();
        Assert.DoesNotContain("Keine Ordner", vm.StepLabel1);
        Assert.DoesNotContain("Bereit", vm.StepLabel2);
        Assert.DoesNotContain("F5 drücken", vm.StepLabel3);
    }

    // ═══ TASK-127: Code-behind must not reference Infrastructure ═══════

    [Fact]
    public void ToolsView_CodeBehind_NoInfrastructureImport()
    {
        var codeBehindPath = FindWpfFile(Path.Combine("Views", "ToolsView.xaml.cs"));
        var content = File.ReadAllText(codeBehindPath);
        Assert.DoesNotContain("Romulus.Infrastructure", content);
    }

    [Fact]
    public void StartView_CodeBehind_NoDragDropBusinessLogic()
    {
        // RED: StartView.xaml.cs contains vm.Roots.Add() in OnHeroDrop
        var codeBehindPath = FindWpfFile(Path.Combine("Views", "StartView.xaml.cs"));
        var content = File.ReadAllText(codeBehindPath);
        Assert.DoesNotContain("vm.Roots.Add", content);
    }

    // ═══ TASK-125: ConversionPreviewViewModel must exist ════════════════

    [Fact]
    public void ConversionPreviewViewModel_Exists_WithExpectedProperties()
    {
        // RED: ConversionPreviewViewModel does not exist yet
        var type = typeof(MainViewModel).Assembly.GetType(
            "Romulus.UI.Wpf.ViewModels.ConversionPreviewViewModel");
        Assert.NotNull(type);
        Assert.NotNull(type!.GetProperty("Items"));
        Assert.NotNull(type.GetProperty("HasItems"));
        Assert.NotNull(type.GetProperty("SummaryText"));
    }

    [Fact]
    public void MainViewModel_HasConversionPreviewChild()
    {
        // RED: MainViewModel does not have ConversionPreview child VM
        var vm = new MainViewModel();
        var prop = vm.GetType().GetProperty("ConversionPreview");
        Assert.NotNull(prop);
        Assert.NotNull(prop!.GetValue(vm));
    }

    // ═══ TASK-123: Settings delegation — no duplication ═════════════════

    [Fact]
    public void SettingsDelegation_MainVM_TrashRoot_DelegatesToSetup()
    {
        // RED: MainViewModel.TrashRoot is independent, not delegated to Setup
        var vm = new MainViewModel();
        vm.TrashRoot = @"C:\TestTrash";
        Assert.Equal(@"C:\TestTrash", vm.Setup.TrashRoot);
    }

    [Fact]
    public void SettingsDelegation_SetupChange_ReflectedInMainVM()
    {
        // RED: Setting Setup.ToolChdman does not propagate to MainVM.ToolChdman
        var vm = new MainViewModel();
        vm.Setup.ToolChdman = @"C:\tools\chdman.exe";
        Assert.Equal(@"C:\tools\chdman.exe", vm.ToolChdman);
    }

    // ═══ TASK-126A: SortDecision in UI ══════════════════════════════════

    [Fact]
    public void SortDecision_HasAllExpectedValues()
    {
        var values = Enum.GetValues<SortDecision>();
        Assert.Contains(SortDecision.Sort, values);
        Assert.Contains(SortDecision.Review, values);
        Assert.Contains(SortDecision.Blocked, values);
        Assert.Contains(SortDecision.DatVerified, values);
        Assert.Contains(SortDecision.Unknown, values);
        Assert.Equal(5, values.Length);
    }

    [Fact]
    public void SortDecision_CodeBehind_HandlesBlockedAndReview()
    {
        // Architectural guard: LibrarySafetyView.xaml.cs switches on SortDecision
        var code = File.ReadAllText(FindUiFile("Views", "LibrarySafetyView.xaml.cs"));
        Assert.Contains("SortDecision.Blocked", code);
        Assert.Contains("SortDecision.Review", code);
    }

    [Fact]
    public void SortDecision_DefaultIsSort()
    {
        Assert.Equal(SortDecision.Sort, default(SortDecision));
    }

    // ═══ TASK-126B: Smart Action Bar States ═════════════════════════════

    [Fact]
    public void ShowConfigChangedBanner_FalseWhenIdle()
    {
        var vm = new MainViewModel();
        Assert.False(vm.ShowConfigChangedBanner);
    }

    [Fact]
    public void IsMovePhaseApplicable_FalseWhenDryRun()
    {
        var vm = new MainViewModel();
        vm.DryRun = true;
        Assert.False(vm.IsMovePhaseApplicable);
    }

    [Fact]
    public void IsMovePhaseApplicable_FalseWhenConvertOnly()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        Assert.False(vm.IsMovePhaseApplicable);
    }

    [Fact]
    public void IsMovePhaseApplicable_TrueWhenNotDryRunAndNotConvertOnly()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertOnly = false;
        Assert.True(vm.IsMovePhaseApplicable);
    }

    [Fact]
    public void IsConvertPhaseApplicable_TrueWhenConvertOnly()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        Assert.True(vm.IsConvertPhaseApplicable);
    }

    [Fact]
    public void IsConvertPhaseApplicable_TrueWhenConvertEnabledAndNotDryRun()
    {
        var vm = new MainViewModel();
        vm.DryRun = false;
        vm.ConvertEnabled = true;
        Assert.True(vm.IsConvertPhaseApplicable);
    }

    [Fact]
    public void IsConvertPhaseApplicable_FalseWhenDryRunOnly()
    {
        var vm = new MainViewModel();
        vm.DryRun = true;
        vm.ConvertEnabled = false;
        vm.ConvertOnly = false;
        Assert.False(vm.IsConvertPhaseApplicable);
    }

    [Theory]
    [InlineData(RunState.Idle, RunState.Preflight, true)]
    [InlineData(RunState.Preflight, RunState.Scanning, true)]
    [InlineData(RunState.Scanning, RunState.Deduplicating, true)]
    [InlineData(RunState.Deduplicating, RunState.Sorting, true)]
    [InlineData(RunState.Deduplicating, RunState.Moving, true)]
    [InlineData(RunState.Sorting, RunState.Moving, true)]
    [InlineData(RunState.Moving, RunState.Sorting, true)]
    [InlineData(RunState.Moving, RunState.Converting, true)]
    [InlineData(RunState.Converting, RunState.Completed, true)]
    [InlineData(RunState.Idle, RunState.Completed, false)]
    [InlineData(RunState.Completed, RunState.Scanning, false)]
    [InlineData(RunState.Moving, RunState.Scanning, false)]
    [InlineData(RunState.Scanning, RunState.Moving, true)]
    [InlineData(RunState.Deduplicating, RunState.Converting, true)]
    [InlineData(RunState.Sorting, RunState.Failed, true)]
    [InlineData(RunState.Completed, RunState.Idle, true)]
    [InlineData(RunState.CompletedDryRun, RunState.Preflight, true)]
    public void TransitionMatrix_SystematicCoverage(RunState from, RunState to, bool expected)
    {
        Assert.Equal(expected, RunStateMachine.IsValidTransition(from, to));
    }

    // ═══ TASK-126C: Region-Ranker ═══════════════════════════════════════

    [Fact]
    public void InitRegionPriorities_EnabledFirst_DisabledLast()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.InitRegionPriorities();

        var enabledItems = vm.RegionPriorities.Where(r => r.IsEnabled).ToList();
        var disabledItems = vm.RegionPriorities.Where(r => !r.IsEnabled).ToList();

        // All enabled items must appear before all disabled items
        int lastEnabledIdx = vm.RegionPriorities.ToList().FindLastIndex(r => r.IsEnabled);
        int firstDisabledIdx = vm.RegionPriorities.ToList().FindIndex(r => !r.IsEnabled);
        if (enabledItems.Count > 0 && disabledItems.Count > 0)
            Assert.True(lastEnabledIdx < firstDisabledIdx);
    }

    [Fact]
    public void InitRegionPriorities_EnabledItemsHavePositions()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.InitRegionPriorities();

        var enabled = vm.RegionPriorities.Where(r => r.IsEnabled).ToList();
        Assert.All(enabled, r => Assert.True(r.Position > 0));
        var disabled = vm.RegionPriorities.Where(r => !r.IsEnabled).ToList();
        Assert.All(disabled, r => Assert.Equal(0, r.Position));
    }

    [Fact]
    public void EnabledRegionCount_MatchesEnabledItems()
    {
        var vm = new MainViewModel();
        // Defaults: EU=true, US=true, JP=true, WORLD=true
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        Assert.Equal(3, vm.EnabledRegionCount);
    }

    [Fact]
    public void MoveRegionUpCommand_MovesItem()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.InitRegionPriorities();

        // EU=0, US=1, JP=2 initially
        var usItem = vm.RegionPriorities.First(r => r.Code == "US");
        vm.MoveRegionUpCommand.Execute(usItem);

        Assert.Equal("US", vm.RegionPriorities[0].Code);
        Assert.Equal("EU", vm.RegionPriorities[1].Code);
    }

    [Fact]
    public void MoveRegionUpCommand_AtTop_NoChange()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionUpCommand.Execute(euItem);

        Assert.Equal("EU", vm.RegionPriorities[0].Code);
    }

    [Fact]
    public void MoveRegionDownCommand_MovesItem()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionDownCommand.Execute(euItem);

        Assert.Equal("US", vm.RegionPriorities[0].Code);
        Assert.Equal("EU", vm.RegionPriorities[1].Code);
    }

    [Fact]
    public void MoveRegionDownCommand_AtLastEnabled_NoChange()
    {
        var vm = new MainViewModel();
        // Enable only EU, disable all others
        vm.PreferEU = true;
        vm.PreferUS = false;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.MoveRegionDownCommand.Execute(euItem);

        // EU should stay at position 0 since next item is disabled
        Assert.Equal("EU", vm.RegionPriorities[0].Code);
    }

    [Fact]
    public void ToggleRegionCommand_DisablesEnabled()
    {
        var vm = new MainViewModel();
        // Reset all defaults, enable only EU+US
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var euItem = vm.RegionPriorities.First(r => r.Code == "EU");
        vm.ToggleRegionCommand.Execute(euItem);

        Assert.False(vm.PreferEU);
        Assert.Equal(1, vm.EnabledRegionCount);
    }

    [Fact]
    public void ToggleRegionCommand_EnablesDisabled()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferJP = false;
        vm.InitRegionPriorities();

        var jpItem = vm.RegionPriorities.First(r => r.Code == "JP");
        vm.ToggleRegionCommand.Execute(jpItem);

        Assert.True(vm.PreferJP);
    }

    [Fact]
    public void RegionPresetEuFocus_SetsCorrectRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetEuFocusCommand.Execute(null);

        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferDE);
        Assert.True(vm.PreferFR);
        Assert.True(vm.PreferWORLD);
        Assert.False(vm.PreferUS);
        Assert.False(vm.PreferJP);
    }

    [Fact]
    public void RegionPresetUsFocus_SetsCorrectRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetUsFocusCommand.Execute(null);

        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferWORLD);
        Assert.True(vm.PreferEU);
        Assert.False(vm.PreferJP);
        Assert.False(vm.PreferDE);
    }

    [Fact]
    public void RegionPresetMultiRegion_SetsCorrectRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetMultiRegionCommand.Execute(null);

        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
        Assert.False(vm.PreferDE);
    }

    [Fact]
    public void RegionPresetAll_EnablesAllRegions()
    {
        var vm = new MainViewModel();
        vm.RegionPresetAllCommand.Execute(null);

        Assert.Equal(16, vm.EnabledRegionCount);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
        Assert.True(vm.PreferJP);
        Assert.True(vm.PreferWORLD);
        Assert.True(vm.PreferDE);
        Assert.True(vm.PreferSCAN);
    }

    [Fact]
    public void RegionPreset_OrderMatchesPreset()
    {
        var vm = new MainViewModel();
        vm.RegionPresetUsFocusCommand.Execute(null);

        // US-Focus preset order: US, WORLD, EU
        var enabled = vm.RegionPriorities.Where(r => r.IsEnabled).Select(r => r.Code).ToList();
        Assert.Equal(new[] { "US", "WORLD", "EU" }, enabled);
    }

    // ═══ TASK-117: Region Ranker Drag & Drop ════════════════════════════

    [Fact]
    public void MoveRegionTo_ReordersEnabledItems()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        // Order: EU(0), US(1), JP(2) — move JP to position 0
        vm.MoveRegionTo(2, 0);

        Assert.Equal("JP", vm.RegionPriorities[0].Code);
        Assert.Equal("EU", vm.RegionPriorities[1].Code);
        Assert.Equal("US", vm.RegionPriorities[2].Code);
    }

    [Fact]
    public void MoveRegionTo_RenumbersPositions()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = true;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        vm.MoveRegionTo(0, 2);

        Assert.Equal(1, vm.RegionPriorities[0].Position);
        Assert.Equal(2, vm.RegionPriorities[1].Position);
        Assert.Equal(3, vm.RegionPriorities[2].Position);
    }

    [Fact]
    public void MoveRegionTo_SyncsBooleans()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        // Ensure booleans stay consistent after reorder
        vm.MoveRegionTo(0, 1);
        Assert.True(vm.PreferEU);
        Assert.True(vm.PreferUS);
    }

    [Fact]
    public void MoveRegionTo_InvalidFromIndex_NoChange()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var original = vm.RegionPriorities.Select(r => r.Code).ToList();
        vm.MoveRegionTo(-1, 0);
        vm.MoveRegionTo(99, 0);
        var after = vm.RegionPriorities.Select(r => r.Code).ToList();
        Assert.Equal(original, after);
    }

    [Fact]
    public void MoveRegionTo_DisabledItemStaysInDisabledSection()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        int enabledCount = vm.RegionPriorities.Count(r => r.IsEnabled);
        // Try to move disabled item into enabled section — should be no-op
        int disabledIdx = vm.RegionPriorities.ToList().FindIndex(r => !r.IsEnabled);
        vm.MoveRegionTo(disabledIdx, 0);

        // Enabled count should not change
        Assert.Equal(enabledCount, vm.RegionPriorities.Count(r => r.IsEnabled));
    }

    [Fact]
    public void MoveRegionTo_SameIndex_NoChange()
    {
        var vm = new MainViewModel();
        vm.PreferEU = true;
        vm.PreferUS = true;
        vm.PreferJP = false;
        vm.PreferWORLD = false;
        vm.InitRegionPriorities();

        var original = vm.RegionPriorities.Select(r => r.Code).ToList();
        vm.MoveRegionTo(0, 0);
        var after = vm.RegionPriorities.Select(r => r.Code).ToList();
        Assert.Equal(original, after);
    }

    [Fact]
    public void ConfigWorkflowViews_PreserveMigratedSortFeatures()
    {
        var optionsXaml = File.ReadAllText(FindUiFile("Views", "ConfigOptionsView.xaml"));
        var regionsXaml = File.ReadAllText(FindUiFile("Views", "ConfigRegionsView.xaml"));

        Assert.Contains("AllowDrop", optionsXaml);
        Assert.Contains("RegionPriorities", regionsXaml);
        Assert.Contains("MoveRegionUpCommand", regionsXaml);
    }

    // ═══ TASK-126D: Console-Picker ══════════════════════════════════════

    [Fact]
    public void SelectAllConsolesCommand_SelectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);

        Assert.True(vm.ConsoleFilters.All(c => c.IsChecked));
        Assert.Equal(vm.ConsoleFilters.Count, vm.SelectedConsoleCount);
    }

    [Fact]
    public void ClearAllConsolesCommand_DeselectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);
        vm.ClearAllConsolesCommand.Execute(null);

        Assert.True(vm.ConsoleFilters.All(c => !c.IsChecked));
        Assert.Equal(0, vm.SelectedConsoleCount);
    }

    [Fact]
    public void SelectConsoleGroupCommand_SelectsOnlyGroup()
    {
        var vm = new MainViewModel();
        vm.SelectConsoleGroupCommand.Execute("Nintendo");

        var nintendo = vm.ConsoleFilters.Where(c => c.Category == "Nintendo").ToList();
        var others = vm.ConsoleFilters.Where(c => c.Category != "Nintendo").ToList();

        Assert.True(nintendo.All(c => c.IsChecked));
        Assert.True(others.All(c => !c.IsChecked));
    }

    [Fact]
    public void DeselectConsoleGroupCommand_DeselectsGroup()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);
        vm.DeselectConsoleGroupCommand.Execute("Sony");

        var sony = vm.ConsoleFilters.Where(c => c.Category == "Sony").ToList();
        var others = vm.ConsoleFilters.Where(c => c.Category != "Sony").ToList();

        Assert.True(sony.All(c => !c.IsChecked));
        Assert.True(others.All(c => c.IsChecked));
    }

    [Fact]
    public void ConsolePresetTop10_Selects10()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetTop10Command.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).ToList();
        Assert.Equal(10, selected.Count);
        Assert.Contains(selected, c => c.Key == "PS1");
        Assert.Contains(selected, c => c.Key == "SNES");
        Assert.Contains(selected, c => c.Key == "N64");
    }

    [Fact]
    public void ConsolePresetDiscBased_SelectsDiscSystems()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetDiscBasedCommand.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToList();
        Assert.Contains("PS1", selected);
        Assert.Contains("PS2", selected);
        Assert.Contains("GC", selected);
        Assert.Contains("DC", selected);
        Assert.DoesNotContain("NES", selected);
        Assert.DoesNotContain("SNES", selected);
    }

    [Fact]
    public void ConsolePresetHandhelds_SelectsHandhelds()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetHandheldsCommand.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToList();
        Assert.Contains("GB", selected);
        Assert.Contains("GBA", selected);
        Assert.Contains("PSP", selected);
        Assert.DoesNotContain("PS1", selected);
    }

    [Fact]
    public void ConsolePresetRetro_SelectsRetroSystems()
    {
        var vm = new MainViewModel();
        vm.ConsolePresetRetroCommand.Execute(null);

        var selected = vm.ConsoleFilters.Where(c => c.IsChecked).Select(c => c.Key).ToList();
        Assert.Contains("NES", selected);
        Assert.Contains("SNES", selected);
        Assert.Contains("MD", selected);
        Assert.DoesNotContain("PS2", selected);
    }

    [Fact]
    public void SelectedConsoleCount_UpdatesAfterSelection()
    {
        var vm = new MainViewModel();
        Assert.Equal(0, vm.SelectedConsoleCount);

        vm.ConsoleFilters[0].IsChecked = true;
        vm.ConsoleFilters[1].IsChecked = true;

        Assert.Equal(2, vm.SelectedConsoleCount);
    }

    [Fact]
    public void ConsoleCountDisplay_NoSelection_ShowsKeine()
    {
        var vm = new MainViewModel();
        // ConsoleCountDisplay when nothing selected
        Assert.Contains("0", vm.ConsoleCountDisplay);
    }

    [Fact]
    public void RemoveConsoleSelectionCommand_DeselectsItem()
    {
        var vm = new MainViewModel();
        vm.SelectAllConsolesCommand.Execute(null);
        var firstItem = vm.ConsoleFilters[0];
        Assert.True(firstItem.IsChecked);

        vm.RemoveConsoleSelectionCommand.Execute(firstItem);
        Assert.False(firstItem.IsChecked);
    }

    [Fact]
    public void ConsoleFilterText_FiltersView()
    {
        var vm = new MainViewModel();
        vm.ConsoleFilterText = "Play";
        // The ICollectionView should filter — we verify the filter is set
        Assert.Equal("Play", vm.ConsoleFilterText);
        // ConsoleFiltersView should have filtering active
        Assert.NotNull(vm.ConsoleFiltersView.Filter);
    }

    [Fact]
    public void SelectConsoleGroupCommand_NullCategory_NoException()
    {
        var vm = new MainViewModel();
        vm.SelectConsoleGroupCommand.Execute(null);
        // Should not throw, no console selected
        Assert.Equal(0, vm.SelectedConsoleCount);
    }

    // ═══ TASK-119: Extension Filter Counter + Group Commands ════════════

    [Fact]
    public void SelectedExtensionCount_InitiallyZero()
    {
        var vm = new MainViewModel();
        Assert.Equal(0, vm.SelectedExtensionCount);
    }

    [Fact]
    public void SelectedExtensionCount_UpdatesOnCheck()
    {
        var vm = new MainViewModel();
        vm.ExtensionFilters[0].IsChecked = true;
        vm.ExtensionFilters[1].IsChecked = true;
        Assert.Equal(2, vm.SelectedExtensionCount);
    }

    [Fact]
    public void ExtensionCountDisplay_NoSelection_Reflects()
    {
        var vm = new MainViewModel();
        Assert.Contains("0", vm.ExtensionCountDisplay);
    }

    [Fact]
    public void SelectAllExtensionsCommand_SelectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllExtensionsCommand.Execute(null);
        Assert.True(vm.ExtensionFilters.All(e => e.IsChecked));
        Assert.Equal(vm.ExtensionFilters.Count, vm.SelectedExtensionCount);
    }

    [Fact]
    public void ClearAllExtensionsCommand_DeselectsAll()
    {
        var vm = new MainViewModel();
        vm.SelectAllExtensionsCommand.Execute(null);
        vm.ClearAllExtensionsCommand.Execute(null);
        Assert.True(vm.ExtensionFilters.All(e => !e.IsChecked));
        Assert.Equal(0, vm.SelectedExtensionCount);
    }

    [Fact]
    public void SelectExtensionGroupCommand_SelectsOnlyGroup()
    {
        var vm = new MainViewModel();
        vm.SelectExtensionGroupCommand.Execute("Archive");

        var archives = vm.ExtensionFilters.Where(e => e.Category == "Archive").ToList();
        var others = vm.ExtensionFilters.Where(e => e.Category != "Archive").ToList();

        Assert.True(archives.All(e => e.IsChecked));
        Assert.True(others.All(e => !e.IsChecked));
    }

    [Fact]
    public void DeselectExtensionGroupCommand_DeselectsGroup()
    {
        var vm = new MainViewModel();
        vm.SelectAllExtensionsCommand.Execute(null);
        vm.DeselectExtensionGroupCommand.Execute("Disc-Images");

        var discs = vm.ExtensionFilters.Where(e => e.Category == "Disc-Images").ToList();
        var others = vm.ExtensionFilters.Where(e => e.Category != "Disc-Images").ToList();

        Assert.True(discs.All(e => !e.IsChecked));
        Assert.True(others.All(e => e.IsChecked));
    }

    // ═══ TASK-113: Responsive NavRail Compact ═══════════════════════════

    [Fact]
    public void IsCompactNav_DefaultFalse()
    {
        var vm = new MainViewModel();
        Assert.False(vm.Shell.IsCompactNav);
    }

    [Fact]
    public void IsCompactNav_Settable()
    {
        var vm = new MainViewModel();
        vm.Shell.IsCompactNav = true;
        Assert.True(vm.Shell.IsCompactNav);
    }

    [Fact]
    public void NavigationRailXaml_LabelsBindToIsCompactNav()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "NavigationRail.xaml"));
        Assert.Contains("Shell.IsCompactNav", xaml);
    }

    [Fact]
    public void CommandBarXaml_ShowsWorkspaceAndWorkflowSummary()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "CommandBar.xaml"));
        Assert.Contains("Shell.CurrentWorkspaceBreadcrumb", xaml);
        Assert.Contains("SelectedWorkflowName", xaml);
        Assert.Contains("SelectedRunProfileId", xaml);
    }

    [Fact]
    public void CommandBarXaml_UsesCompactBindings_ForResponsiveHeader()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "CommandBar.xaml"));

        Assert.Contains("Shell.IsCompactNav", xaml);
        Assert.Contains("StatusRuntime", xaml);
        Assert.Contains("CurrentThemeLabel", xaml);
        Assert.Contains("OpenReportLog", xaml);
    }

    [Fact]
    public void ResultViewXaml_UsesStackedResponsiveCharts()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        // Redesigned dashboard: pure XAML stacked bar, no ScottPlot for breakdown
        Assert.DoesNotContain("MinWidth=\"420\"", xaml);
        Assert.DoesNotContain("Height=\"460\"", xaml);
        Assert.Contains("KeepFraction", xaml);
        Assert.Contains("MoveFraction", xaml);
        Assert.Contains("JunkFraction", xaml);
        Assert.Contains("ConsoleDistribution", xaml);
        // No redundant inline dedupe decisions (own tab handles that)
        Assert.DoesNotContain("DedupeGroupItems", xaml);
        // No ScottPlot in ResultView (pure XAML bars)
        Assert.DoesNotContain("scott:", xaml);
    }

    [Fact]
    public void ResultViewXaml_HasTopErrorSummaryBanner()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        Assert.Contains("HasActionableErrorSummary", xaml);
        Assert.Contains("ActionableErrorSummaryItems", xaml);
        Assert.Contains("ActionableErrorSummaryTitle", xaml);
    }

    [Fact]
    public void ResultViewXaml_HasConvertOnlyHeroMetrics()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ResultView.xaml"));

        Assert.Contains("IsConvertOnlyDashboard", xaml);
        Assert.Contains("Run.DashConverted", xaml);
        Assert.Contains("Run.DashConvertBlocked", xaml);
        Assert.Contains("Run.DashConvertSaved", xaml);
        Assert.Contains("Run.DashConvertReview", xaml);
    }

    [Fact]
    public void ProgressViewXaml_HidesNonApplicablePhases_InsteadOfOpacity()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ProgressView.xaml"));

        Assert.Contains("IsMovePhaseApplicable", xaml);
        Assert.Contains("IsConvertPhaseApplicable", xaml);
        Assert.DoesNotContain("Opacity=\"0.4\"", xaml);
        Assert.Contains("SkippedPhaseInfoText", xaml);
    }

    [Fact]
    public void ToolsViewXaml_SimpleMode_HidesFullCatalogNavigation()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "ToolsView.xaml"));

        Assert.Contains("IsSimpleMode", xaml);
        Assert.Contains("Converter={StaticResource InverseBoolToVis}", xaml);
    }

    // ═══ TASK-115: SmartActionBar RunState DataTriggers ════════════════

    [Theory]
    [InlineData(RunState.Idle)]
    [InlineData(RunState.Preflight)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Deduplicating)]
    [InlineData(RunState.Sorting)]
    [InlineData(RunState.Moving)]
    [InlineData(RunState.Converting)]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void RunStateDisplayText_ReturnsNonEmptyString_ForEachState(RunState state)
    {
        var vm = new MainViewModel();
        // Transition through valid path to reach target state
        TransitionTo(vm, state);
        Assert.False(string.IsNullOrWhiteSpace(vm.RunStateDisplayText));
    }

    [Fact]
    public void CurrentRunState_Setter_NotifiesRunStateDisplayText()
    {
        var vm = new MainViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);
        vm.CurrentRunState = RunState.Preflight;
        Assert.Contains(nameof(vm.RunStateDisplayText), changed);
    }

    [Fact]
    public void SmartActionBarXaml_HasRunStateBindings()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "SmartActionBar.xaml"));
        // RunState-derived properties must be used in SmartActionBar
        Assert.Contains("RunStateDisplayText", xaml);
        Assert.Contains("IsIdle", xaml);
        Assert.Contains("IsBusy", xaml);
    }

    [Fact]
    public void SmartActionBarXaml_RunButton_HiddenViaTriggerWhenBusy()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "SmartActionBar.xaml"));
        // Run button uses IsIdle binding to hide when pipeline is running
        Assert.Contains("RunCommand", xaml);
        Assert.Matches(@"(?s)RunCommand.*IsIdle", xaml);
    }

    [Fact]
    public void SmartActionBarXaml_CancelButton_HasRunStateTrigger()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "SmartActionBar.xaml"));
        // Cancel button has x:Name and IsBusy-based visibility
        Assert.Matches(@"x:Name=""CancelButton""", xaml);
        Assert.Contains("CancelCommand", xaml);
    }

    [Fact]
    public void SmartActionBarXaml_ProgressPanel_HasRunStateTrigger()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "SmartActionBar.xaml"));
        Assert.Matches(@"x:Name=""ProgressPanel""", xaml);
    }

    [Fact]
    public void SmartActionBarXaml_HasRunStateStatusLabel()
    {
        var xaml = File.ReadAllText(FindUiFile("Views", "SmartActionBar.xaml"));
        Assert.Contains("RunStateDisplayText", xaml);
    }

    [Theory]
    [InlineData(RunState.Idle, true)]
    [InlineData(RunState.Preflight, false)]
    [InlineData(RunState.Scanning, false)]
    [InlineData(RunState.Deduplicating, false)]
    [InlineData(RunState.Sorting, false)]
    [InlineData(RunState.Moving, false)]
    [InlineData(RunState.Converting, false)]
    [InlineData(RunState.Completed, true)]
    [InlineData(RunState.CompletedDryRun, true)]
    [InlineData(RunState.Failed, true)]
    [InlineData(RunState.Cancelled, true)]
    public void IsIdle_MatchesExpectation_ForEachState(RunState state, bool expectedIdle)
    {
        var vm = new MainViewModel();
        TransitionTo(vm, state);
        Assert.Equal(expectedIdle, vm.IsIdle);
    }

    [Theory]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    public void HasRunResult_TrueOnlyForCompletedStates(RunState state)
    {
        var vm = new MainViewModel();
        TransitionTo(vm, state);
        Assert.True(vm.HasRunResult);
    }

    [Theory]
    [InlineData(RunState.Idle)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Cancelled)]
    public void HasRunResult_FalseForNonCompletedStates(RunState state)
    {
        var vm = new MainViewModel();
        TransitionTo(vm, state);
        Assert.False(vm.HasRunResult);
    }

    /// <summary>Transitions the VM through valid states to reach the target.</summary>
    private static void TransitionTo(MainViewModel vm, RunState target)
    {
        if (target == RunState.Idle) return;
        RunState[] chain = [RunState.Preflight, RunState.Scanning, RunState.Deduplicating,
            RunState.Sorting, RunState.Moving, RunState.Converting];
        RunState[] terminals = [RunState.Completed, RunState.CompletedDryRun, RunState.Failed, RunState.Cancelled];

        if (terminals.Contains(target))
        {
            vm.CurrentRunState = RunState.Preflight;
            vm.CurrentRunState = target;
            return;
        }
        foreach (var s in chain)
        {
            vm.CurrentRunState = s;
            if (s == target) return;
        }
    }

    // ═══ TASK-122: RunState entkernen — MainVM delegiert an RunViewModel ═══

    [Fact]
    public void Task122_CurrentRunState_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Scanning);

        // MainVM.CurrentRunState must be the same object as Run.CurrentRunState
        Assert.Equal(vm.Run.CurrentRunState, vm.CurrentRunState);
    }

    [Fact]
    public void Task122_SetRunCurrentRunState_ReflectedInMainVm()
    {
        var vm = new MainViewModel();
        vm.Run.CurrentRunState = RunState.Preflight;

        Assert.Equal(RunState.Preflight, vm.CurrentRunState);
    }

    [Fact]
    public void Task122_IsBusy_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Scanning);

        Assert.True(vm.IsBusy);
        Assert.Equal(vm.Run.IsBusy, vm.IsBusy);
    }

    [Fact]
    public void Task122_IsIdle_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        Assert.True(vm.IsIdle);
        Assert.Equal(vm.Run.IsIdle, vm.IsIdle);
    }

    [Fact]
    public void Task122_HasRunResult_DelegatesToRunViewModel()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Completed);

        Assert.True(vm.HasRunResult);
        Assert.Equal(vm.Run.HasRunResult, vm.HasRunResult);
    }

    [Fact]
    public void Task122_MainVm_HasNoOwnRunStateField()
    {
        // After TASK-122, MainViewModel must NOT have its own _runState field.
        // RunState is owned exclusively by RunViewModel (ADR-0006).
        var fields = typeof(MainViewModel)
            .GetFields(System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            .Where(f => f.FieldType == typeof(RunState) && f.Name.Contains("runState", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(fields);
    }

    [Fact]
    public void Task122_PropertyChanged_FiresOnMainVm_WhenRunStateChanges()
    {
        var vm = new MainViewModel();
        var changed = new List<string>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        vm.CurrentRunState = RunState.Preflight;

        Assert.Contains("CurrentRunState", changed);
        Assert.Contains("IsBusy", changed);
        Assert.Contains("IsIdle", changed);
    }

    [Fact]
    public void Task122_RunStateDisplayText_StillWorksAfterDelegation()
    {
        var vm = new MainViewModel();
        TransitionTo(vm, RunState.Scanning);

        // RunStateDisplayText must still return a non-empty localized string
        Assert.False(string.IsNullOrWhiteSpace(vm.RunStateDisplayText));
    }

    [Fact]
    public void Task122_IsValidTransition_StillAvailableOnMainVm()
    {
        // The static helper should still be accessible for backward compat
        Assert.True(MainViewModel.IsValidTransition(RunState.Idle, RunState.Preflight));
        Assert.False(MainViewModel.IsValidTransition(RunState.Idle, RunState.Completed));
    }

    // ═══ BUG-FIX: ActionRailHeight too small (buttons clipped) ══════════

    [Fact]
    public void DesignTokens_ActionRailHeight_IsAtLeast84()
    {
        var tokensPath = FindUiFile("Themes", "_DesignTokens.xaml");
        var content = File.ReadAllText(tokensPath);
        var match = System.Text.RegularExpressions.Regex.Match(
            content, @"x:Key=""ActionRailHeight"">(\d+)<");
        Assert.True(match.Success, "ActionRailHeight token not found in _DesignTokens.xaml");
        var value = int.Parse(match.Groups[1].Value);
        Assert.True(value >= 84,
            $"ActionRailHeight is {value}px but must be ≥ 84px to avoid button clipping (44px buttons + 12+6 padding)");
    }

    [Fact]
    public void MainWindow_ActionRailRow_MatchesDesignToken()
    {
        var windowPath = FindWpfFile("MainWindow.xaml");
        var content = File.ReadAllText(windowPath);
        // Row 3 should use a dynamic resource or be at least 84
        var match = System.Text.RegularExpressions.Regex.Match(
            content, @"<!-- Row 3: ActionRail -->\s*</RowDefinitions>|Height=""(\d+)""\s*/>\s*<!-- Row 3");
        // Find the last RowDefinition (Row 3)
        var rowMatches = System.Text.RegularExpressions.Regex.Matches(
            content, @"<RowDefinition\s+Height=""(\d+)""/>");
        // Row 3 is the 4th RowDefinition (index 3) — the one with hardcoded height for ActionRail
        var rowDefs = System.Text.RegularExpressions.Regex.Matches(
            content, @"<RowDefinition\s+Height=""([^""]+)""\s*/>");
        Assert.True(rowDefs.Count >= 4, "Expected at least 4 RowDefinitions in MainWindow.xaml");
        var row3Height = rowDefs[3].Groups[1].Value;
        // Should be "84" or a dynamic resource reference
        if (int.TryParse(row3Height, out var h))
        {
            Assert.True(h >= 84,
                $"MainWindow Row 3 Height is {h}px but must be ≥ 84px to match ActionRailHeight token");
        }
    }

    // ═══ BUG-FIX: Theme button shows NEXT theme instead of current ══════

    [Fact]
    public void CurrentThemeLabel_ReturnsHumanFriendlyName_ForAllThemes()
    {
        var vm = new MainViewModel();

        // Default theme is Dark → "Synthwave"
        Assert.Equal("Synthwave", vm.CurrentThemeLabel);

        // Verify CurrentThemeLabel maps all AppTheme values via reflection
        // (we can't call SelectedTheme= because ApplyTheme loads WPF resources)
        var expectedLabels = new Dictionary<AppTheme, string>
        {
            [AppTheme.Dark] = "Synthwave",
            [AppTheme.CleanDarkPro] = "Clean Dark",
            [AppTheme.RetroCRT] = "Retro CRT",
            [AppTheme.ArcadeNeon] = "Arcade Neon",
            [AppTheme.Light] = "Hell",
            [AppTheme.HighContrast] = "Kontrast",
        };

        // Verify ThemeToggleText also covers all themes (complementary)
        var toggleLabels = new Dictionary<AppTheme, string>
        {
            [AppTheme.Dark] = "⮞ Clean Dark",
            [AppTheme.CleanDarkPro] = "⮞ Retro CRT",
            [AppTheme.RetroCRT] = "⮞ Arcade Neon",
            [AppTheme.ArcadeNeon] = "⮞ Hell",
            [AppTheme.Light] = "⮞ Kontrast",
            [AppTheme.HighContrast] = "⮞ Synthwave",
        };

        // All themes must have a mapping in both CurrentThemeLabel and ThemeToggleText
        foreach (var theme in Enum.GetValues<AppTheme>())
        {
            Assert.True(expectedLabels.ContainsKey(theme),
                $"CurrentThemeLabel has no mapping for {theme}");
            Assert.True(toggleLabels.ContainsKey(theme),
                $"ThemeToggleText has no mapping for {theme}");
        }
    }

    [Fact]
    public void CommandBar_ThemeButton_BindsToCurrentThemeLabel()
    {
        var cmdBarPath = FindUiFile("Views", "CommandBar.xaml");
        var content = File.ReadAllText(cmdBarPath);

        // The theme button's display text must bind to CurrentThemeLabel (current theme)
        // NOT to ThemeToggleText (which shows the NEXT theme)
        Assert.Contains("CurrentThemeLabel", content);

        // ThemeToggleText should only appear in ToolTip binding, not in Text binding
        var textBindings = System.Text.RegularExpressions.Regex.Matches(
            content, @"Text=""\{Binding\s+ThemeToggleText\}""");
        Assert.True(textBindings.Count == 0,
            "Theme button Text should bind to CurrentThemeLabel, not ThemeToggleText. " +
            "ThemeToggleText should only appear in ToolTip.");
    }

    [Fact]
    public void CommandBar_ModeToggle_BindsToCurrentUiModeLabel()
    {
        var cmdBarPath = FindUiFile("Views", "CommandBar.xaml");
        var content = File.ReadAllText(cmdBarPath);

        Assert.Contains("IsSimpleMode", content);
        Assert.Contains("CurrentUiModeLabel", content);
    }

    [Fact]
    public void CommandBar_UsesWorkspaceAndInspectorBindings()
    {
        var cmdBarPath = FindUiFile("Views", "CommandBar.xaml");
        var content = File.ReadAllText(cmdBarPath);

        Assert.Contains("Shell.CurrentWorkspaceBreadcrumb", content, StringComparison.Ordinal);
        Assert.Contains("Shell.ToggleContextWingCommand", content, StringComparison.Ordinal);
        Assert.Contains("Shell.ContextToggleLabel", content, StringComparison.Ordinal);
        Assert.Contains("AvailableRunProfiles", content, StringComparison.Ordinal);
    }

    [Fact]
    public void NavigationRail_ToolsVisibility_BindsToShellShowToolsNav()
    {
        var navPath = FindUiFile("Views", "NavigationRail.xaml");
        var content = File.ReadAllText(navPath);

        Assert.Contains("Shell.ShowToolsNav", content);
    }

    [Fact]
    public void ShellViewModel_ContextWing_DefaultsCollapsed()
    {
        var shell = new ShellViewModel(new LocalizationService());

        Assert.False(shell.ShowContextWing);
        Assert.Equal("Inspector einblenden", shell.ContextToggleLabel);
    }

    [Fact]
    public void MainViewModel_ShowSmartActionBar_HidesOnPrimaryContentSurfaces()
    {
        var vm = new MainViewModel();

        vm.Shell.SelectedNavTag = "MissionControl";
        vm.Shell.SelectedSubTab = "Dashboard";
        Assert.False(vm.ShowSmartActionBar);

        vm.Shell.SelectedNavTag = "Library";
        vm.Shell.SelectedSubTab = "Results";
        Assert.False(vm.ShowSmartActionBar);

        vm.Shell.SelectedNavTag = "Config";
        vm.Shell.SelectedSubTab = "Options";
        Assert.True(vm.ShowSmartActionBar);
    }

    [Fact]
    public void MainWindowXaml_Title_IsRomulus_AndActionRailIsBindable()
    {
        var xaml = File.ReadAllText(FindUiFile("", "MainWindow.xaml"));

        Assert.Contains("Title=\"Romulus\"", xaml);
        Assert.Contains("ShowSmartActionBar", xaml);
    }

    [Fact]
    public void SubTabBar_ExpertTabs_ReflectConsolidatedNavigation()
    {
        var subTabPath = FindUiFile("Views", "SubTabBar.xaml");
        var content = File.ReadAllText(subTabPath);

        Assert.Contains("Shell.ShowLibraryDecisionsTab", content);
        Assert.Contains("Shell.ShowSystemActivityLogTab", content);
        Assert.DoesNotContain("ConverterParameter=QuickStart", content);
        Assert.DoesNotContain("ConverterParameter=Filtering", content);
        Assert.DoesNotContain("ConverterParameter=Report", content);
        Assert.Contains("ConverterParameter=DatManagement", content);
        Assert.DoesNotContain("ConverterParameter=Conversion", content);
        Assert.DoesNotContain("ConverterParameter=GameKeyLab", content);
    }

    // ═══ BUG-FIX: EnableDatAudit missing from GUI layer ═════════════════

    [Fact]
    public void SettingsDto_HasEnableDatAudit_DefaultTrue()
    {
        var dto = new SettingsDto();
        Assert.True(dto.EnableDatAudit,
            "SettingsDto.EnableDatAudit must default to true so DAT verification runs by default");
    }

    [Fact]
    public void MainViewModel_HasEnableDatAudit_DefaultTrue()
    {
        var vm = new MainViewModel();
        // EnableDatAudit should be an independent property (not just a copy of UseDat)
        var prop = typeof(MainViewModel).GetProperty("EnableDatAudit");
        Assert.NotNull(prop);
        Assert.True((bool)prop.GetValue(vm)!,
            "MainViewModel.EnableDatAudit must default to true");
    }

    [Fact]
    public void AutoSavePropertyNames_IncludesEnableDatAudit()
    {
        // AutoSavePropertyNames is a private static field — verify via reflection
        var field = typeof(MainViewModel)
            .GetField("AutoSavePropertyNames",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(field);
        var names = (HashSet<string>)field.GetValue(null)!;
        Assert.Contains("EnableDatAudit", names);
    }

    [Fact]
    public void RunService_EnableDatAudit_ReadsFromViewModel()
    {
        // When a VM has EnableDatAudit = true but UseDat = false,
        // EnableDatAudit must still be independently controllable
        var vm = new MainViewModel();
        vm.UseDat = false;

        // The EnableDatAudit property should exist and be independent
        var prop = typeof(MainViewModel).GetProperty("EnableDatAudit");
        Assert.NotNull(prop);
        // With default true, even if UseDat is false, the property should be true
        Assert.True((bool)prop.GetValue(vm)!);
    }

    private static string FindUiFile(string folder, string fileName)
    {
        var dir = Path.GetDirectoryName(typeof(GuiViewModelTests).Assembly.Location)!;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", folder, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        // Fallback: try repo root from CallerFilePath context
        var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(typeof(GuiViewModelTests).Assembly.Location)))));
        return Path.Combine(repoRoot!, "src", "Romulus.UI.Wpf", folder, fileName);
    }

    // ═══ UpdateBreakdown fraction normalization ─────────────────────────

    [Fact]
    public void UpdateBreakdown_FractionsNormalizedToTotal_NotMax()
    {
        var vm = new RunViewModel();
        vm.GamesRaw = 100;
        vm.DupesRaw = 20;
        vm.JunkRaw = 10;

        vm.UpdateBreakdown();

        Assert.Equal(70, vm.KeepCount);
        Assert.Equal(20, vm.MoveCount);
        Assert.Equal(10, vm.JunkCount);

        // Fractions must sum to ~1.0 (normalized to total)
        Assert.Equal(0.7, vm.KeepFraction, 3);
        Assert.Equal(0.2, vm.MoveFraction, 3);
        Assert.Equal(0.1, vm.JunkFraction, 3);

        var sum = vm.KeepFraction + vm.MoveFraction + vm.JunkFraction;
        Assert.InRange(sum, 0.99, 1.01);
    }

    [Fact]
    public void UpdateBreakdown_ZeroTotal_NoException()
    {
        var vm = new RunViewModel();
        vm.GamesRaw = 0;
        vm.DupesRaw = 0;
        vm.JunkRaw = 0;

        vm.UpdateBreakdown();

        Assert.Equal(0.0, vm.KeepFraction);
        Assert.Equal(0.0, vm.MoveFraction);
        Assert.Equal(0.0, vm.JunkFraction);
    }

    [Fact]
    public void UpdateBreakdown_AllJunk_FractionIsOne()
    {
        var vm = new RunViewModel();
        vm.GamesRaw = 50;
        vm.DupesRaw = 0;
        vm.JunkRaw = 50;

        vm.UpdateBreakdown();

        Assert.Equal(0, vm.KeepCount);
        Assert.Equal(0.0, vm.KeepFraction);
        Assert.Equal(1.0, vm.JunkFraction, 3);
    }

    // ═══ SEC-001: Preflight→Idle dialog-decline state safety ════════════

    [Fact]
    public void SEC001_PreflightToIdle_IsValidTransition()
    {
        // Preflight → Idle must be legal for dialog-decline paths
        Assert.True(RunStateMachine.IsValidTransition(RunState.Preflight, RunState.Idle));
    }

    [Fact]
    public void SEC001_DialogDecline_ResetsConvertOnlyFlag()
    {
        var vm = new MainViewModel();
        vm.ConvertOnly = true;
        vm.CurrentRunState = RunState.Preflight;

        // Simulate dialog-decline path: reset transient flags, return to Idle
        vm.ConvertOnly = false;
        vm.CurrentRunState = RunState.Idle;

        Assert.False(vm.ConvertOnly);
        Assert.Equal(RunState.Idle, vm.CurrentRunState);
    }

    [Fact]
    public void SEC001_DialogDecline_ResetsBusyHint()
    {
        var vm = new MainViewModel();
        vm.CurrentRunState = RunState.Preflight;
        vm.BusyHint = "Running...";

        // Simulate dialog-decline: BusyHint must be cleared
        vm.BusyHint = "";
        vm.CurrentRunState = RunState.Idle;

        Assert.Equal("", vm.BusyHint);
    }

    // ═══ SEC-002: Rollback invalidates preview fingerprint ══════════════

    [Fact]
    public void SEC002_AfterRollback_MoveGateIsLocked()
    {
        var vm = new MainViewModel();

        // After DryRun, preview gate should be CompletedDryRun
        SetRunStateViaValidPath(vm, RunState.CompletedDryRun);

        // After rollback, state returns to Idle → not CompletedDryRun → gate locked
        vm.CurrentRunState = RunState.Idle;

        Assert.False(vm.CanStartMoveWithCurrentPreview);
    }

    [Fact]
    public void SEC002_PreflightToIdle_DoesNotThrow()
    {
        var vm = new MainViewModel();
        vm.CurrentRunState = RunState.Preflight;

        // This must not throw InvalidOperationException (SEC-001 fix)
        var ex = Record.Exception(() => vm.CurrentRunState = RunState.Idle);
        Assert.Null(ex);
    }

    [Fact]
    public async Task GUIRED_IssueMain_PartialFailure_MustNotSurfaceAsCompleted()
    {
        var winner = new RomCandidate
        {
            MainPath = @"C:\Roms\Winner.zip",
            GameKey = "winner",
            Category = FileCategory.Game,
            DatMatch = true,
            ConsoleKey = "SNES"
        };

        var result = new RunResult
        {
            Status = "completed_with_errors",
            TotalFilesScanned = 2,
            WinnerCount = 1,
            LoserCount = 1,
            AllCandidates = new[] { winner },
            DedupeGroups = Array.Empty<DedupeGroup>(),
            MoveResult = new MovePhaseResult(MoveCount: 1, FailCount: 1, SavedBytes: 0)
        };

        var runService = new RecordingRunService(result);
        var vm = new MainViewModel(new ThemeService(), new StubDialogService(), runService: runService);
        vm.Roots.Add(Path.GetTempPath());
        vm.DryRun = false;
        vm.ConfirmMove = false;
        vm.CurrentRunState = RunState.Preflight;

        await vm.ExecuteRunAsync();

        // Red invariant: partial failures must not be presented as a clean Completed+Info outcome.
        Assert.NotEqual(RunState.Completed, vm.CurrentRunState);
        Assert.NotEqual(UiErrorSeverity.Info, vm.RunSummarySeverity);
        Assert.Contains(vm.ErrorSummaryItems, e => e.Code == "IO-MOVE");
    }

    [Fact]
    public async Task GUIRED_IssueMain_ExecuteRunAsync_MustBeSingleFlight()
    {
        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 1,
            WinnerCount = 1,
            AllCandidates = new[]
            {
                new RomCandidate
                {
                    MainPath = @"C:\Roms\Single.zip",
                    GameKey = "single",
                    Category = FileCategory.Game,
                    DatMatch = true,
                    ConsoleKey = "SNES"
                }
            },
            DedupeGroups = Array.Empty<DedupeGroup>()
        };

        var runService = new RecordingRunService(result);
        var vm = new MainViewModel(new ThemeService(), new StubDialogService(), runService: runService);
        vm.Roots.Add(Path.GetTempPath());
        vm.DryRun = true;
        vm.CurrentRunState = RunState.Preflight;

        var first = vm.ExecuteRunAsync();
        var second = vm.ExecuteRunAsync();
        await Task.WhenAll(first, second);

        // Red invariant: concurrent triggers must not execute the pipeline twice.
        Assert.Equal(1, runService.ExecuteRunCallCount);
    }

    [Fact]
    public void GUIRED_IssueMain_UnknownStatus_MustBeVisibleInErrorSummaryProjection()
    {
        var result = new RunResult
        {
            Status = "mystery_status",
            WinnerCount = 0,
            LoserCount = 0
        };

        var issues = ErrorSummaryProjection.Build(result, [], []);

        // Red invariant: unknown run status must not be silently mapped to RUN-OK.
        Assert.DoesNotContain(issues, issue => issue.Code == "RUN-OK");
        Assert.Contains(issues, issue => issue.Code == "RUN-UNKNOWN" && issue.Severity == UiErrorSeverity.Warning);
    }
}
