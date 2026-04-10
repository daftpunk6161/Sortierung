using Romulus.Core.Classification;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-18): HeaderAnalyzer must exist in Core and detect NES headers from raw bytes.
/// </summary>
public sealed class HeaderAnalyzerIssue9RedTests
{
    [Fact]
    public void AnalyzeHeader_ShouldDetectNesINes_FromRawHeaderBytes_Issue9A18()
    {
        // Arrange
        var headerBytes = new byte[16];
        headerBytes[0] = 0x4E; // N
        headerBytes[1] = 0x45; // E
        headerBytes[2] = 0x53; // S
        headerBytes[3] = 0x1A;
        headerBytes[4] = 0x02; // PRG
        headerBytes[5] = 0x01; // CHR

        // Act
        var result = HeaderAnalyzer.AnalyzeHeader(headerBytes, fileSize: 40_960);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("NES", result!.Platform);
        Assert.Equal("iNES", result.Format);
    }
}
