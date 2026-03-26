using Xunit;

namespace RomCleanup.Tests.Benchmark;

public sealed class VerdictSeparationRedTests
{
    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_Define_AmbiguousVerdict_SeparateFromUnknownAndWrong_Issue9()
    {
        // Arrange + Act
        var hasAmbiguous = Enum.TryParse<BenchmarkVerdict>("Ambiguous", ignoreCase: true, out _);

        // Assert
        Assert.True(hasAmbiguous,
            "Benchmark verdict model must contain an explicit 'Ambiguous' state to separate conflict cases from Unknown and Wrong.");
    }
}
