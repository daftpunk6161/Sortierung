using System.Globalization;
using System.Windows;
using System.Windows.Media;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Converters;
using Romulus.UI.Wpf.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for all WPF value converters in Converters.cs.
/// Covers BoolToVisibility, InverseBoolToVisibility, InverseBool,
/// StatusLevelToBrush, LogLevelToBrush, StepActiveBrush,
/// StringEqualsToVisibility, StringEqualsToBool, PhaseDetail.
/// </summary>
public sealed class ConverterTests
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    // ═══ BoolToVisibilityConverter ══════════════════════════════════════

    [Fact]
    public void BoolToVisibility_True_ReturnsVisible()
    {
        var c = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(true, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void BoolToVisibility_False_ReturnsCollapsed()
    {
        var c = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert(false, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void BoolToVisibility_Null_ReturnsCollapsed()
    {
        var c = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert(null!, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void BoolToVisibility_NonBool_ReturnsCollapsed()
    {
        var c = new BoolToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert("text", typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void BoolToVisibility_ConvertBack_Visible_ReturnsTrue()
    {
        var c = new BoolToVisibilityConverter();
        Assert.Equal(true, c.ConvertBack(Visibility.Visible, typeof(bool), null!, Culture));
    }

    [Fact]
    public void BoolToVisibility_ConvertBack_Collapsed_ReturnsFalse()
    {
        var c = new BoolToVisibilityConverter();
        Assert.Equal(false, c.ConvertBack(Visibility.Collapsed, typeof(bool), null!, Culture));
    }

    // ═══ InverseBoolToVisibilityConverter ════════════════════════════════

    [Fact]
    public void InverseBoolToVisibility_True_ReturnsCollapsed()
    {
        var c = new InverseBoolToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert(true, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void InverseBoolToVisibility_False_ReturnsVisible()
    {
        var c = new InverseBoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(false, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void InverseBoolToVisibility_Null_ReturnsVisible()
    {
        var c = new InverseBoolToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(null!, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void InverseBoolToVisibility_ConvertBack_Collapsed_ReturnsTrue()
    {
        var c = new InverseBoolToVisibilityConverter();
        Assert.Equal(true, c.ConvertBack(Visibility.Collapsed, typeof(bool), null!, Culture));
    }

    [Fact]
    public void InverseBoolToVisibility_ConvertBack_Visible_ReturnsFalse()
    {
        var c = new InverseBoolToVisibilityConverter();
        Assert.Equal(false, c.ConvertBack(Visibility.Visible, typeof(bool), null!, Culture));
    }

    // ═══ InverseBoolConverter ════════════════════════════════════════════

    [Fact]
    public void InverseBool_True_ReturnsFalse()
    {
        var c = new InverseBoolConverter();
        Assert.Equal(true, c.Convert(false, typeof(bool), null!, Culture));
    }

    [Fact]
    public void InverseBool_False_ReturnsTrue()
    {
        var c = new InverseBoolConverter();
        Assert.Equal(false, c.Convert(true, typeof(bool), null!, Culture));
    }

    [Fact]
    public void InverseBool_Null_ReturnsTrue()
    {
        var c = new InverseBoolConverter();
        Assert.Equal(true, c.Convert(null!, typeof(bool), null!, Culture));
    }

    [Fact]
    public void InverseBool_ConvertBack_False_ReturnsTrue()
    {
        var c = new InverseBoolConverter();
        Assert.Equal(true, c.ConvertBack(false, typeof(bool), null!, Culture));
    }

    // ═══ StatusLevelToBrushConverter ═════════════════════════════════════

    [Theory]
    [InlineData(StatusLevel.Ok)]
    [InlineData(StatusLevel.Warning)]
    [InlineData(StatusLevel.Blocked)]
    [InlineData(StatusLevel.Missing)]
    public void StatusLevelToBrush_AllLevels_ReturnsBrush(StatusLevel level)
    {
        var c = new StatusLevelToBrushConverter();
        var result = c.Convert(level, typeof(Brush), null!, Culture);
        Assert.IsType<SolidColorBrush>(result);
    }

    [Fact]
    public void StatusLevelToBrush_Ok_ReturnsGreen()
    {
        var c = new StatusLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert(StatusLevel.Ok, typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0x00, 0xFF, 0x88), brush.Color);
    }

    [Fact]
    public void StatusLevelToBrush_Warning_ReturnsAmber()
    {
        var c = new StatusLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert(StatusLevel.Warning, typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0xFF, 0xB7, 0x00), brush.Color);
    }

    [Fact]
    public void StatusLevelToBrush_Blocked_ReturnsRed()
    {
        var c = new StatusLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert(StatusLevel.Blocked, typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x44), brush.Color);
    }

    [Fact]
    public void StatusLevelToBrush_NonEnum_ReturnsMissing()
    {
        var c = new StatusLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert("invalid", typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0x99, 0x99, 0xCC), brush.Color);
    }

    [Fact]
    public void StatusLevelToBrush_ConvertBack_Throws()
    {
        var c = new StatusLevelToBrushConverter();
        Assert.Throws<NotSupportedException>(() =>
            c.ConvertBack(null!, typeof(StatusLevel), null!, Culture));
    }

    // ═══ LogLevelToBrushConverter ════════════════════════════════════════

    [Theory]
    [InlineData("ERROR")]
    [InlineData("WARN")]
    [InlineData("WARNING")]
    [InlineData("DEBUG")]
    [InlineData("INFO")]
    public void LogLevelToBrush_ValidLevels_ReturnsBrush(string level)
    {
        var c = new LogLevelToBrushConverter();
        var result = c.Convert(level, typeof(Brush), null!, Culture);
        Assert.IsType<SolidColorBrush>(result);
    }

    [Fact]
    public void LogLevelToBrush_Error_ReturnsRed()
    {
        var c = new LogLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert("ERROR", typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0xFF, 0x00, 0x44), brush.Color);
    }

    [Fact]
    public void LogLevelToBrush_Warn_ReturnsAmber()
    {
        var c = new LogLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert("WARN", typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0xFF, 0xB7, 0x00), brush.Color);
    }

    [Fact]
    public void LogLevelToBrush_Warning_ReturnsAmber()
    {
        var c = new LogLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert("WARNING", typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0xFF, 0xB7, 0x00), brush.Color);
    }

    [Fact]
    public void LogLevelToBrush_Debug_ReturnsMuted()
    {
        var c = new LogLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert("DEBUG", typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0x99, 0x99, 0xCC), brush.Color);
    }

    // Info-Brush wurde bewusst von Neon-Cyan auf einen WCAG-tauglichen Ton (#2C6E95)
    // umgestellt, damit informative Logzeilen auf hellen Surfaces lesbar bleiben.
    [Fact]
    public void LogLevelToBrush_Info_ReturnsReadableInfoTone()
    {
        var c = new LogLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert("INFO", typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0x2C, 0x6E, 0x95), brush.Color);
    }

    [Fact]
    public void LogLevelToBrush_Null_ReturnsReadableInfoTone()
    {
        var c = new LogLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert(null!, typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0x2C, 0x6E, 0x95), brush.Color);
    }

    [Fact]
    public void LogLevelToBrush_NonString_ReturnsReadableInfoTone()
    {
        var c = new LogLevelToBrushConverter();
        var brush = (SolidColorBrush)c.Convert(42, typeof(Brush), null!, Culture);
        Assert.Equal(Color.FromRgb(0x2C, 0x6E, 0x95), brush.Color);
    }

    [Fact]
    public void LogLevelToBrush_ConvertBack_Throws()
    {
        var c = new LogLevelToBrushConverter();
        Assert.Throws<NotSupportedException>(() =>
            c.ConvertBack(null!, typeof(string), null!, Culture));
    }

    // ═══ StepActiveBrushConverter ════════════════════════════════════════

    [Fact]
    public void StepActiveBrush_StepReached_ReturnsActive()
    {
        var c = new StepActiveBrushConverter();
        var brush = (SolidColorBrush)c.Convert(3, typeof(Brush), "2", Culture);
        Assert.Equal(Color.FromRgb(0x00, 0xF5, 0xFF), brush.Color);
    }

    [Fact]
    public void StepActiveBrush_StepNotReached_ReturnsInactive()
    {
        var c = new StepActiveBrushConverter();
        var brush = (SolidColorBrush)c.Convert(1, typeof(Brush), "3", Culture);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    [Fact]
    public void StepActiveBrush_ExactStep_ReturnsActive()
    {
        var c = new StepActiveBrushConverter();
        var brush = (SolidColorBrush)c.Convert(3, typeof(Brush), "3", Culture);
        Assert.Equal(Color.FromRgb(0x00, 0xF5, 0xFF), brush.Color);
    }

    [Fact]
    public void StepActiveBrush_InvalidParameter_ReturnsInactive()
    {
        var c = new StepActiveBrushConverter();
        var brush = (SolidColorBrush)c.Convert(3, typeof(Brush), "abc", Culture);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    [Fact]
    public void StepActiveBrush_NullValue_ReturnsInactive()
    {
        var c = new StepActiveBrushConverter();
        var brush = (SolidColorBrush)c.Convert(null!, typeof(Brush), "1", Culture);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    [Fact]
    public void StepActiveBrush_ConvertBack_Throws()
    {
        var c = new StepActiveBrushConverter();
        Assert.Throws<NotSupportedException>(() =>
            c.ConvertBack(null!, typeof(int), null!, Culture));
    }

    // ═══ PipelinePhaseBrushConverter ═════════════════════════════════════

    [Theory]
    [InlineData(RunState.Scanning, "1", "Done")]     // phase 1 < current phase 2
    [InlineData(RunState.Scanning, "2", "Active")]   // phase 2 == current phase 2
    [InlineData(RunState.Scanning, "3", "Pending")]  // phase 3 > current phase 2
    [InlineData(RunState.Idle, "1", "Idle")]          // idle → all idle
    public void PipelinePhaseBrush_ReturnsCorrectState(RunState state, string param, string expected)
    {
        var c = new PipelinePhaseBrushConverter();
        var brush = (SolidColorBrush)c.Convert(state, typeof(Brush), param, Culture);

        var expectedColor = expected switch
        {
            "Active" => Color.FromRgb(0x00, 0xF5, 0xFF),
            "Done" => Color.FromRgb(0x00, 0xFF, 0x88),
            "Pending" => Color.FromRgb(0x55, 0x55, 0x77),
            "Idle" => Colors.Transparent,
            _ => Colors.Transparent
        };
        Assert.Equal(expectedColor, brush.Color);
    }

    [Theory]
    [InlineData(RunState.Preflight, "1")]
    [InlineData(RunState.Deduplicating, "3")]
    [InlineData(RunState.Moving, "4")]
    [InlineData(RunState.Sorting, "5")]
    [InlineData(RunState.Converting, "6")]
    [InlineData(RunState.Completed, "7")]
    [InlineData(RunState.CompletedDryRun, "7")]
    public void PipelinePhaseBrush_ActivePhase_ReturnsActiveBrush(RunState state, string param)
    {
        var c = new PipelinePhaseBrushConverter();
        var brush = (SolidColorBrush)c.Convert(state, typeof(Brush), param, Culture);
        Assert.Equal(Color.FromRgb(0x00, 0xF5, 0xFF), brush.Color);
    }

    [Fact]
    public void PipelinePhaseBrush_InvalidParam_ReturnsIdle()
    {
        var c = new PipelinePhaseBrushConverter();
        var brush = (SolidColorBrush)c.Convert(RunState.Scanning, typeof(Brush), "notanumber", Culture);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    [Fact]
    public void PipelinePhaseBrush_NullState_ReturnsIdle()
    {
        var c = new PipelinePhaseBrushConverter();
        var brush = (SolidColorBrush)c.Convert(null!, typeof(Brush), "1", Culture);
        Assert.Equal(Colors.Transparent, brush.Color);
    }

    // ═══ StringEqualsToVisibilityConverter ═══════════════════════════════

    [Fact]
    public void StringEqualsToVisibility_Match_ReturnsVisible()
    {
        var c = new StringEqualsToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert("test", typeof(Visibility), "test", Culture));
    }

    [Fact]
    public void StringEqualsToVisibility_NoMatch_ReturnsCollapsed()
    {
        var c = new StringEqualsToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert("test", typeof(Visibility), "other", Culture));
    }

    [Fact]
    public void StringEqualsToVisibility_CaseSensitive_NoMatch()
    {
        var c = new StringEqualsToVisibilityConverter();
        Assert.Equal(Visibility.Collapsed, c.Convert("Test", typeof(Visibility), "test", Culture));
    }

    [Fact]
    public void StringEqualsToVisibility_BothNull_ReturnsVisible()
    {
        var c = new StringEqualsToVisibilityConverter();
        Assert.Equal(Visibility.Visible, c.Convert(null!, typeof(Visibility), null!, Culture));
    }

    [Fact]
    public void StringEqualsToVisibility_ConvertBack_Throws()
    {
        var c = new StringEqualsToVisibilityConverter();
        Assert.Throws<NotSupportedException>(() =>
            c.ConvertBack(Visibility.Visible, typeof(string), null!, Culture));
    }

    // ═══ StringEqualsToBoolConverter ═════════════════════════════════════

    [Fact]
    public void StringEqualsToBool_Match_ReturnsTrue()
    {
        var c = new StringEqualsToBoolConverter();
        Assert.Equal(true, c.Convert("abc", typeof(bool), "abc", Culture));
    }

    [Fact]
    public void StringEqualsToBool_NoMatch_ReturnsFalse()
    {
        var c = new StringEqualsToBoolConverter();
        Assert.Equal(false, c.Convert("abc", typeof(bool), "xyz", Culture));
    }

    [Fact]
    public void StringEqualsToBool_ConvertBack_True_ReturnsParameter()
    {
        var c = new StringEqualsToBoolConverter();
        Assert.Equal("myParam", c.ConvertBack(true, typeof(string), "myParam", Culture));
    }

    [Fact]
    public void StringEqualsToBool_ConvertBack_False_ReturnsUnset()
    {
        var c = new StringEqualsToBoolConverter();
        Assert.Equal(DependencyProperty.UnsetValue, c.ConvertBack(false, typeof(string), "myParam", Culture));
    }

    [Fact]
    public void StringEqualsToBool_ConvertBack_NullParam_ReturnsUnset()
    {
        var c = new StringEqualsToBoolConverter();
        Assert.Equal(DependencyProperty.UnsetValue, c.ConvertBack(true, typeof(string), null!, Culture));
    }

    // ═══ PhaseDetailConverter ════════════════════════════════════════════

    [Fact]
    public void PhaseDetail_ValidPhase_ReturnsDescription()
    {
        var c = new PhaseDetailConverter();
        var result = (string)c.Convert(RunState.Scanning, typeof(string), "2", Culture);
        Assert.Contains("Scan", result);
        Assert.Contains("Aktiv", result);
    }

    [Fact]
    public void PhaseDetail_CompletedPhase_ReturnsAbgeschlossen()
    {
        var c = new PhaseDetailConverter();
        var result = (string)c.Convert(RunState.Sorting, typeof(string), "2", Culture);
        Assert.Contains("Scan", result);
        Assert.Contains("Abgeschlossen", result);
    }

    [Fact]
    public void PhaseDetail_FuturePhase_ReturnsAusstehend()
    {
        var c = new PhaseDetailConverter();
        var result = (string)c.Convert(RunState.Scanning, typeof(string), "5", Culture);
        Assert.Contains("Sort", result);
        Assert.Contains("Ausstehend", result);
    }

    [Fact]
    public void PhaseDetail_InvalidPhase_ReturnsEmpty()
    {
        var c = new PhaseDetailConverter();
        var result = (string)c.Convert(RunState.Scanning, typeof(string), "0", Culture);
        Assert.Equal("", result);
    }

    [Fact]
    public void PhaseDetail_PhaseOutOfRange_ReturnsEmpty()
    {
        var c = new PhaseDetailConverter();
        Assert.Equal("", c.Convert(RunState.Scanning, typeof(string), "999", Culture));
    }

    [Fact]
    public void PhaseDetail_NullState_ReturnsDescriptionOnly()
    {
        var c = new PhaseDetailConverter();
        var result = (string)c.Convert(null!, typeof(string), "1", Culture);
        Assert.Contains("Preflight", result);
    }

    [Fact]
    public void PhaseDetail_AllSevenPhases_HaveDescriptions()
    {
        var c = new PhaseDetailConverter();
        for (int i = 1; i <= 7; i++)
        {
            var result = (string)c.Convert(RunState.Idle, typeof(string), i.ToString(), Culture);
            Assert.False(string.IsNullOrEmpty(result), $"Phase {i} should have description");
        }
    }

    [Fact]
    public void PhaseDetail_ConvertBack_Throws()
    {
        var c = new PhaseDetailConverter();
        Assert.Throws<NotSupportedException>(() =>
            c.ConvertBack("", typeof(RunState), null!, Culture));
    }

    // ═══ DatAuditStatusToGlyphConverter ══════════════════════════════════
    // Triple-encoding badge (icon + color + text) for color-blind accessibility.

    [Theory]
    [InlineData(DatAuditStatus.Have, "\uE73E")]          // CheckMark
    [InlineData(DatAuditStatus.HaveWrongName, "\uE7BA")] // Warning
    [InlineData(DatAuditStatus.Miss, "\uE711")]          // Cancel
    [InlineData(DatAuditStatus.Unknown, "\uE946")]       // Info
    [InlineData(DatAuditStatus.Ambiguous, "\uE783")]     // Important (distinct from Warning)
    public void DatAuditStatusToGlyph_KnownStatus_ReturnsExpectedGlyph(DatAuditStatus status, string expected)
    {
        var c = new DatAuditStatusToGlyphConverter();
        Assert.Equal(expected, c.Convert(status, typeof(string), null!, Culture));
    }

    [Fact]
    public void DatAuditStatusToGlyph_NonStatusValue_ReturnsInfoFallback()
    {
        var c = new DatAuditStatusToGlyphConverter();
        Assert.Equal("\uE946", c.Convert("not-a-status", typeof(string), null!, Culture));
    }

    [Fact]
    public void DatAuditStatusToGlyph_AllGlyphsAreUniquePerStatus()
    {
        var c = new DatAuditStatusToGlyphConverter();
        var glyphs = new[]
        {
            (string)c.Convert(DatAuditStatus.Have, typeof(string), null!, Culture),
            (string)c.Convert(DatAuditStatus.HaveWrongName, typeof(string), null!, Culture),
            (string)c.Convert(DatAuditStatus.Miss, typeof(string), null!, Culture),
            (string)c.Convert(DatAuditStatus.Unknown, typeof(string), null!, Culture),
            (string)c.Convert(DatAuditStatus.Ambiguous, typeof(string), null!, Culture),
        };
        Assert.Equal(glyphs.Length, glyphs.Distinct().Count());
    }

    [Fact]
    public void DatAuditStatusToGlyph_ConvertBack_Throws()
    {
        var c = new DatAuditStatusToGlyphConverter();
        Assert.Throws<NotSupportedException>(() =>
            c.ConvertBack("\uE73E", typeof(DatAuditStatus), null!, Culture));
    }
}
