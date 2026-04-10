using Romulus.UI.Wpf.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for UiError record: DisplayText formatting and ToString.
/// </summary>
public sealed class UiErrorCoverageTests
{
    [Fact]
    public void DisplayText_Info_HasInfoPrefix()
    {
        var error = new UiError("C1", "test msg", UiErrorSeverity.Info);
        Assert.Equal("[INFO] test msg", error.DisplayText);
    }

    [Fact]
    public void DisplayText_Warning_HasWarnPrefix()
    {
        var error = new UiError("C2", "warn msg", UiErrorSeverity.Warning);
        Assert.Equal("[WARN] warn msg", error.DisplayText);
    }

    [Fact]
    public void DisplayText_Error_HasErrorPrefix()
    {
        var error = new UiError("C3", "err msg", UiErrorSeverity.Error);
        Assert.Equal("[ERROR] err msg", error.DisplayText);
    }

    [Fact]
    public void DisplayText_Blocked_HasBlockedPrefix()
    {
        var error = new UiError("C4", "blocked msg", UiErrorSeverity.Blocked);
        Assert.Equal("[BLOCKED] blocked msg", error.DisplayText);
    }

    [Fact]
    public void ToString_ReturnsSameAsDisplayText()
    {
        var error = new UiError("X", "hello", UiErrorSeverity.Warning);
        Assert.Equal(error.DisplayText, error.ToString());
    }

    [Fact]
    public void FixHint_DefaultsToNull()
    {
        var error = new UiError("X", "msg", UiErrorSeverity.Info);
        Assert.Null(error.FixHint);
    }

    [Fact]
    public void FixHint_CanBeProvided()
    {
        var error = new UiError("X", "msg", UiErrorSeverity.Error, FixHint: "do this");
        Assert.Equal("do this", error.FixHint);
    }
}
