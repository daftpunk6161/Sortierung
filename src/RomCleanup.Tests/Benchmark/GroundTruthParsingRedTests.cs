using RomCleanup.Tests.Benchmark.Infrastructure;
using Xunit;

namespace RomCleanup.Tests.Benchmark;

public sealed class GroundTruthParsingRedTests
{
    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_Throw_When_GroundTruthContainsDuplicateIds_Issue9()
    {
        // Arrange
        var tempFile = Path.GetTempFileName();
        try
        {
            var duplicateId = "gc-NES-ref-001";
            var line1 =
                "{\"id\":\"gc-NES-ref-001\",\"source\":{\"fileName\":\"Mario.nes\",\"extension\":\".nes\",\"sizeBytes\":40960},\"tags\":[\"clean-reference\"],\"difficulty\":\"easy\",\"expected\":{\"consoleKey\":\"NES\",\"category\":\"Game\"}}";
            var line2 =
                "{\"id\":\"gc-NES-ref-001\",\"source\":{\"fileName\":\"Zelda.nes\",\"extension\":\".nes\",\"sizeBytes\":40960},\"tags\":[\"clean-reference\"],\"difficulty\":\"easy\",\"expected\":{\"consoleKey\":\"NES\",\"category\":\"Game\"}}";

            File.WriteAllLines(tempFile, new[] { line1, line2 });

            // Act
            var act = () => GroundTruthLoader.LoadFile(tempFile);

            // Assert
            var ex = Assert.Throws<InvalidOperationException>(act);
            Assert.Contains(duplicateId, ex.Message);
        }
        finally
        {
            File.Delete(tempFile);
        }
    }
}
