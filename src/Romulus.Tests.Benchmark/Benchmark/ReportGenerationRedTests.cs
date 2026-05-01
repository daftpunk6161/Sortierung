using Xunit;

namespace Romulus.Tests.Benchmark;

public sealed class ReportGenerationRedTests
{
    [Fact]
    [Trait("Category", "RedPhase")]
    public void Should_Provide_HtmlBenchmarkReportWriter_ForEvaluationPipeline_Issue9()
    {
        // Arrange
        var reportWriterTypeName = "Romulus.Tests.Benchmark.BenchmarkHtmlReportWriter";

        // Act
        var reportWriterType = typeof(BenchmarkReportWriter).Assembly.GetType(reportWriterTypeName, throwOnError: false);

        // Assert
        Assert.NotNull(reportWriterType);
    }
}
