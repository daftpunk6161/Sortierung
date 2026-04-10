using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-09): DAT audit metrics must flow through RunResult -> RunProjection.
/// </summary>
public sealed class DatAuditRunProjectionIssue9RedTests
{
    [Fact]
    public void RunProjectionFactory_ShouldExposeDatAuditCounts_FromRunResult_Issue9()
    {
        var result = new RunResult
        {
            Status = "ok",
            ExitCode = 0,
            DatHaveCount = 4,
            DatHaveWrongNameCount = 2,
            DatMissCount = 3,
            DatUnknownCount = 1,
            DatAmbiguousCount = 5
        };

        var projection = RunProjectionFactory.Create(result);

        Assert.Equal(4, projection.DatHaveCount);
        Assert.Equal(2, projection.DatHaveWrongNameCount);
        Assert.Equal(3, projection.DatMissCount);
        Assert.Equal(1, projection.DatUnknownCount);
        Assert.Equal(5, projection.DatAmbiguousCount);
    }

    [Fact]
    public void RunResultBuilder_ShouldCarryDatAuditCounts_ToRunResult_Issue9()
    {
        var builder = new RunResultBuilder
        {
            DatHaveCount = 7,
            DatHaveWrongNameCount = 6,
            DatMissCount = 5,
            DatUnknownCount = 4,
            DatAmbiguousCount = 3
        };

        var result = builder.Build();

        Assert.Equal(7, result.DatHaveCount);
        Assert.Equal(6, result.DatHaveWrongNameCount);
        Assert.Equal(5, result.DatMissCount);
        Assert.Equal(4, result.DatUnknownCount);
        Assert.Equal(3, result.DatAmbiguousCount);
    }
}
