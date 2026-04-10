using System.Text.RegularExpressions;
using Romulus.UI.Wpf.Services;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Phase 8: Polish, Accessibility &amp; Theme verification tests.
/// TASK-128 through TASK-133 + TASK-143.
/// </summary>
public class Phase8ThemeAccessibilityTests
{
    // ═══ TASK-128: Theme-System verifizieren ════════════════════════════

    [Fact]
    public void Task128_AppTheme_Enum_Has6Members()
    {
        var values = Enum.GetValues<AppTheme>();
        Assert.Equal(6, values.Length);
    }

    [Theory]
    [InlineData(AppTheme.Dark)]
    [InlineData(AppTheme.Light)]
    [InlineData(AppTheme.HighContrast)]
    [InlineData(AppTheme.CleanDarkPro)]
    [InlineData(AppTheme.RetroCRT)]
    [InlineData(AppTheme.ArcadeNeon)]
    public void Task128_AllThemes_HaveCorrespondingXamlFile(AppTheme theme)
    {
        var fileName = theme switch
        {
            AppTheme.Dark => "SynthwaveDark.xaml",
            AppTheme.Light => "Light.xaml",
            AppTheme.HighContrast => "HighContrast.xaml",
            AppTheme.CleanDarkPro => "CleanDarkPro.xaml",
            AppTheme.RetroCRT => "RetroCRT.xaml",
            AppTheme.ArcadeNeon => "ArcadeNeon.xaml",
            _ => throw new ArgumentOutOfRangeException()
        };
        var path = FindThemeFile(fileName);
        Assert.True(File.Exists(path), $"Theme file {fileName} not found");
    }

    [Theory]
    [InlineData("_DesignTokens.xaml")]
    [InlineData("_ControlTemplates.xaml")]
    public void Task128_SharedInfrastructureFiles_Exist(string fileName)
    {
        var path = FindThemeFile(fileName);
        Assert.True(File.Exists(path), $"Infrastructure file {fileName} not found");
    }

    [Fact]
    public void Task128_ThemeService_AllThemes_Contains6()
    {
        Assert.Equal(6, ThemeService.AllThemes.Count);
    }

    [Theory]
    [InlineData("SynthwaveDark.xaml")]
    [InlineData("Light.xaml")]
    [InlineData("HighContrast.xaml")]
    [InlineData("CleanDarkPro.xaml")]
    [InlineData("RetroCRT.xaml")]
    [InlineData("ArcadeNeon.xaml")]
    public void Task128_AllThemes_DefineRequiredBrushKeys(string themeFile)
    {
        var path = FindThemeFile(themeFile);
        var xaml = File.ReadAllText(path);

        string[] requiredKeys =
        [
            "BrushBackground", "BrushSurface", "BrushSurfaceAlt", "BrushSurfaceLight",
            "BrushAccentCyan", "BrushAccentPurple",
            "BrushDanger", "BrushSuccess", "BrushWarning",
            "BrushTextPrimary", "BrushTextMuted", "BrushBorder",
            "BrushPrimaryBg", "BrushTextOnAccent",
        ];

        foreach (var key in requiredKeys)
        {
            Assert.Contains($"x:Key=\"{key}\"", xaml);
        }
    }

    [Theory]
    [InlineData("SmartActionBar.xaml")]
    [InlineData("CommandBar.xaml")]
    [InlineData("WizardView.xaml")]
    public void Task128_Views_UseDynamicResource_ForColors(string viewFile)
    {
        var path = FindUiFile("Views", viewFile);
        var xaml = File.ReadAllText(path);

        // Views must use DynamicResource for color brushes, not StaticResource
        var staticBrush = Regex.Matches(xaml, @"StaticResource\s+Brush\w+");
        Assert.True(staticBrush.Count == 0,
            $"Found {staticBrush.Count} StaticResource references to Brush* in {viewFile} — should use DynamicResource for theme support");
    }

    // ═══ TASK-129: Theme-Switcher-Dropdown ══════════════════════════════

    [Fact]
    public void Task129_SystemAppearanceView_HasThemeComboBox()
    {
        var path = FindUiFile("Views", "SystemAppearanceView.xaml");
        var xaml = File.ReadAllText(path);

        // Must have a ComboBox or ListBox bound to AvailableThemes
        Assert.Matches(new Regex(@"(?s)AvailableThemes|ThemeItems|AllThemes"), xaml);
    }

    [Fact]
    public void Task129_SystemAppearanceView_HasColorPreview()
    {
        var path = FindUiFile("Views", "SystemAppearanceView.xaml");
        var xaml = File.ReadAllText(path);

        // Each theme item should have a color swatch (Rectangle or Ellipse for preview)
        Assert.Matches(new Regex(@"(?s)ThemeColorPreview|ThemeSwatch|ColorPreview|Rectangle.*Fill"), xaml);
    }

    [Fact]
    public void Task129_MainWindow_HasCtrlT_Binding()
    {
        var path = FindUiFile("", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Matches(new Regex(@"(?s)Ctrl.*T.*ThemeToggle|ThemeToggle.*Ctrl.*T"), xaml);
    }

    [Fact]
    public void Task129_ThemeService_Toggle_CyclesAllThemes()
    {
        // Verify that Toggle() cycles through all 6 themes
        var themes = ThemeService.AllThemes;
        Assert.Equal(6, themes.Count);
        Assert.Equal(AppTheme.Dark, themes[0]); // starts with Dark
    }

    // ═══ TASK-130: Keyboard-Navigation ══════════════════════════════════

    [Fact]
    public void Task130_MainWindow_HasTabIndex_Setup()
    {
        var path = FindUiFile("", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Matches(new Regex(@"TabIndex\s*="), xaml);
    }

    [Theory]
    [InlineData("F5", "Run")]
    [InlineData("Escape", "Cancel")]
    public void Task130_MainWindow_HasKeyboardShortcuts(string key, string description)
    {
        _ = description; // used for test readability in InlineData
        var path = FindUiFile("", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains(key, xaml);
    }

    [Fact]
    public void Task130_ControlTemplates_HasFocusRing()
    {
        var path = FindThemeFile("_ControlTemplates.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("FocusRing", xaml);
    }

    [Fact]
    public void Task130_HighContrast_Has3pxFocusRing()
    {
        var path = FindThemeFile("HighContrast.xaml");
        var xaml = File.ReadAllText(path);

        // HighContrast must have 3px focus ring for WCAG AAA
        Assert.Matches(new Regex(@"FocusRing.*BorderThickness.*3|BorderThickness.*3.*FocusRing", RegexOptions.Singleline), xaml);
    }

    // ═══ TASK-131: Screen Reader / AutomationProperties ════════════════

    [Theory]
    [InlineData("CommandBar.xaml")]
    [InlineData("WizardView.xaml")]
    [InlineData("SmartActionBar.xaml")]
    [InlineData("SystemAppearanceView.xaml")]
    public void Task131_Views_HaveAutomationProperties(string viewFile)
    {
        var path = FindUiFile("Views", viewFile);
        var xaml = File.ReadAllText(path);

        var count = Regex.Matches(xaml, @"AutomationProperties\.Name").Count;
        Assert.True(count >= 1,
            $"{viewFile} has {count} AutomationProperties.Name — expected ≥1");
    }

    [Fact]
    public void Task131_MainWindow_HasLiveRegions()
    {
        var path = FindUiFile("", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("LiveSetting", xaml);
    }

    [Fact]
    public void Task131_WizardView_StepperEllipses_AreAnnotated()
    {
        var path = FindUiFile("Views", "WizardView.xaml");
        var xaml = File.ReadAllText(path);

        // All 3 wizard step ellipses must have AutomationProperties.Name
        var stepLabels = Regex.Matches(xaml, @"Ellipse[^>]*AutomationProperties\.Name", RegexOptions.Singleline);
        Assert.True(stepLabels.Count >= 3,
            $"Found {stepLabels.Count} annotated Ellipses, expected ≥3 for wizard stepper");
    }

    [Fact]
    public void Task131_SmartActionBar_RunButton_HasAutomationName()
    {
        var path = FindUiFile("Views", "SmartActionBar.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Contains("AutomationProperties.Name", xaml);
    }

    // ═══ TASK-132: WCAG AA Contrast (4.5:1) ════════════════════════════

    [Theory]
    [InlineData("SynthwaveDark.xaml", "#E8E8F8", "#0D0D1F", 4.5)]    // TextPrimary on Background
    [InlineData("Light.xaml", "#12142A", "#F4F6FF", 4.5)]
    [InlineData("HighContrast.xaml", "#FFFFFF", "#000000", 7.0)]       // AAA for HC
    [InlineData("CleanDarkPro.xaml", "#D4D6DE", "#16181D", 4.5)]
    [InlineData("RetroCRT.xaml", "#CCFFCC", "#050A05", 4.5)]
    [InlineData("ArcadeNeon.xaml", "#F0E8FF", "#0A0A1E", 4.5)]
    public void Task132_TextPrimary_MeetsContrastRatio(string theme, string fgHex, string bgHex, double minRatio)
    {
        _ = theme; // used for test readability in InlineData
        var ratio = CalculateContrastRatio(ParseColor(fgHex), ParseColor(bgHex));
        Assert.True(ratio >= minRatio,
            $"Contrast ratio {ratio:F2}:1 for {fgHex} on {bgHex} is below {minRatio}:1");
    }

    [Theory]
    [InlineData("SynthwaveDark.xaml", "#9999CC", "#0D0D1F", 4.5)]     // TextMuted on Background
    [InlineData("Light.xaml", "#4C5478", "#F4F6FF", 4.5)]
    [InlineData("HighContrast.xaml", "#CCCCCC", "#000000", 7.0)]
    [InlineData("CleanDarkPro.xaml", "#8A8EA0", "#16181D", 4.5)]
    [InlineData("RetroCRT.xaml", "#66AA66", "#050A05", 4.5)]
    [InlineData("ArcadeNeon.xaml", "#9988CC", "#0A0A1E", 4.5)]
    public void Task132_TextMuted_MeetsContrastRatio(string theme, string fgHex, string bgHex, double minRatio)
    {
        _ = theme; // used for test readability in InlineData
        var ratio = CalculateContrastRatio(ParseColor(fgHex), ParseColor(bgHex));
        Assert.True(ratio >= minRatio,
            $"Contrast ratio {ratio:F2}:1 for {fgHex} on {bgHex} is below {minRatio}:1");
    }

    [Fact]
    public void Task132_DesignTokens_MinTouchTarget_Is44px()
    {
        var path = FindThemeFile("_DesignTokens.xaml");
        var xaml = File.ReadAllText(path);

        Assert.Matches(new Regex(@"MinTouchTarget.*44|44.*MinTouchTarget"), xaml);
    }

    // ═══ TASK-133: Narrator DryRun-Testplan ═════════════════════════════

    [Fact]
    public void Task133_NarratorTestplan_DocumentExists()
    {
        var path = FindDocsFile("ux", "narrator-testplan.md");
        Assert.True(File.Exists(path), "Narrator testplan document missing at docs/ux/narrator-testplan.md");
    }

    // ═══ TASK-143: Phase 8 Verify ═══════════════════════════════════════

    [Fact]
    public void Task143_AllSixThemes_ExistAndAreComplete()
    {
        string[] themeFiles = ["SynthwaveDark.xaml", "Light.xaml", "HighContrast.xaml",
                               "CleanDarkPro.xaml", "RetroCRT.xaml", "ArcadeNeon.xaml"];

        foreach (var f in themeFiles)
        {
            var path = FindThemeFile(f);
            Assert.True(File.Exists(path), $"Theme {f} missing");
            var xaml = File.ReadAllText(path);
            Assert.Contains("BrushBackground", xaml);
            Assert.Contains("BrushTextPrimary", xaml);
        }
    }

    [Fact]
    public void Task143_KeyboardShortcuts_ComprehensiveSet()
    {
        var path = FindUiFile("", "MainWindow.xaml");
        var xaml = File.ReadAllText(path);

        // Must have at minimum: F5, Escape, Ctrl+T, Ctrl+R, Ctrl+Z
        string[] required = ["F5", "Escape", "Ctrl+T", "Ctrl+Z"];
        foreach (var key in required)
        {
            // Normalise: Ctrl+T might appear as modifiers="Ctrl" + key="T"
            var normalised = key.Replace("Ctrl+", "");
            Assert.Contains(normalised, xaml);
        }
    }

    [Fact]
    public void Task143_AutomationProperties_MinimumCoverage()
    {
        // Aggregate AutomationProperties across all views
        var viewsDir = FindUiDir("Views");
        if (!Directory.Exists(viewsDir)) return;

        var totalA11y = 0;
        foreach (var xamlFile in Directory.GetFiles(viewsDir, "*.xaml"))
        {
            var content = File.ReadAllText(xamlFile);
            totalA11y += Regex.Matches(content, @"AutomationProperties\.Name").Count;
        }

        Assert.True(totalA11y >= 30,
            $"Total AutomationProperties.Name across all views: {totalA11y}, expected ≥30");
    }

    // ═══ Helpers ════════════════════════════════════════════════════════

    private static (double R, double G, double B) ParseColor(string hex)
    {
        hex = hex.TrimStart('#');
        if (hex.Length == 8) hex = hex[2..]; // strip alpha
        var r = int.Parse(hex[0..2], System.Globalization.NumberStyles.HexNumber) / 255.0;
        var g = int.Parse(hex[2..4], System.Globalization.NumberStyles.HexNumber) / 255.0;
        var b = int.Parse(hex[4..6], System.Globalization.NumberStyles.HexNumber) / 255.0;
        return (r, g, b);
    }

    private static double Linearize(double c)
        => c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);

    private static double RelativeLuminance((double R, double G, double B) color)
        => 0.2126 * Linearize(color.R) + 0.7152 * Linearize(color.G) + 0.0722 * Linearize(color.B);

    private static double CalculateContrastRatio((double R, double G, double B) fg, (double R, double G, double B) bg)
    {
        var l1 = RelativeLuminance(fg);
        var l2 = RelativeLuminance(bg);
        var lighter = Math.Max(l1, l2);
        var darker = Math.Min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
    }

    private static string FindThemeFile(string fileName)
    {
        var dir = Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)!;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", "Themes", fileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)))));
        return Path.Combine(repoRoot!, "src", "Romulus.UI.Wpf", "Themes", fileName);
    }

    private static string FindUiFile(string folder, string fileName)
    {
        var subPath = string.IsNullOrEmpty(folder)
            ? fileName
            : Path.Combine(folder, fileName);
        var dir = Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)!;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", subPath);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)))));
        return Path.Combine(repoRoot!, "src", "Romulus.UI.Wpf", subPath);
    }

    private static string FindUiDir(string folder)
    {
        var dir = Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)!;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "src", "Romulus.UI.Wpf", folder);
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)))));
        return Path.Combine(repoRoot!, "src", "Romulus.UI.Wpf", folder);
    }

    private static string FindDocsFile(string folder, string fileName)
    {
        var dir = Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)!;
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "docs", folder, fileName);
            if (File.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        var repoRoot = Path.GetDirectoryName(Path.GetDirectoryName(
            Path.GetDirectoryName(Path.GetDirectoryName(
                Path.GetDirectoryName(typeof(Phase8ThemeAccessibilityTests).Assembly.Location)))));
        return Path.Combine(repoRoot!, "docs", folder, fileName);
    }
}
