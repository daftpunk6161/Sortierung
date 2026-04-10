using Romulus.UI.Wpf.Views;
using Xunit;

namespace Romulus.Tests;

public sealed class LibraryReportViewPathTests
{
    [Fact]
    public void TryNormalizeReportPath_NullOrWhitespace_ReturnsFalse()
    {
        Assert.False(LibraryReportView.TryNormalizeReportPath(null, out _));
        Assert.False(LibraryReportView.TryNormalizeReportPath(string.Empty, out _));
        Assert.False(LibraryReportView.TryNormalizeReportPath("   ", out _));
    }

    [Fact]
    public void TryNormalizeReportPath_InvalidPath_ReturnsFalse()
    {
        Assert.False(LibraryReportView.TryNormalizeReportPath("C:\\temp\\bad\0path\\report.html", out _));
    }

    [Fact]
    public void TryNormalizeReportPath_ValidRelativePath_ReturnsFullPath()
    {
        var ok = LibraryReportView.TryNormalizeReportPath("reports\\sample.html", out var normalized);

        Assert.True(ok);
        Assert.True(System.IO.Path.IsPathRooted(normalized));
    }
}
