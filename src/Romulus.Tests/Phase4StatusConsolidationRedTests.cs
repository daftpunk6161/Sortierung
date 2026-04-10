using System.Reflection;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

public sealed class Phase4StatusConsolidationRedTests
{
    [Fact]
    public void RunConstants_MustExpose_RunningAndCompletedStatuses()
    {
        var runningField = typeof(RunConstants).GetField("StatusRunning", BindingFlags.Public | BindingFlags.Static);
        var completedField = typeof(RunConstants).GetField("StatusCompleted", BindingFlags.Public | BindingFlags.Static);

        Assert.NotNull(runningField);
        Assert.NotNull(completedField);
        Assert.Equal("running", runningField!.GetValue(null));
        Assert.Equal("completed", completedField!.GetValue(null));
    }

    [Fact]
    public void RunConstants_MustExpose_SortDecisionConstants()
    {
        var nested = typeof(RunConstants).GetNestedType("SortDecisions", BindingFlags.Public);

        Assert.NotNull(nested);
        Assert.Equal("Sort", nested!.GetField("Sort", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.Equal("Review", nested.GetField("Review", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.Equal("Blocked", nested.GetField("Blocked", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
        Assert.Equal("DatVerified", nested.GetField("DatVerified", BindingFlags.Public | BindingFlags.Static)!.GetValue(null));
    }

    [Fact]
    public void ApiAssembly_MustNotContain_LegacyApiRunStatusType()
    {
        var apiAssembly = typeof(RunManager).Assembly;
        var legacyType = apiAssembly.GetType("Romulus.Api.ApiRunStatus", throwOnError: false);

        Assert.Null(legacyType);
    }

    [Fact]
    public void OperationResult_MustNotDuplicate_RunStatusConstants()
    {
        var publicStaticFields = typeof(OperationResult).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Select(field => field.Name)
            .ToHashSet(StringComparer.Ordinal);

        Assert.DoesNotContain("StatusOk", publicStaticFields);
        Assert.DoesNotContain("StatusBlocked", publicStaticFields);
    }
}
