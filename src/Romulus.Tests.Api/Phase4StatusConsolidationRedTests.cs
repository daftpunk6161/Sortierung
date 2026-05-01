using System.Reflection;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

public sealed class Phase4StatusConsolidationRedTests
{
    // The two tautological 'field exists with value X' tests for RunConstants.StatusRunning,
    // RunConstants.StatusCompleted and RunConstants.SortDecisions.* were removed in section 3.1
    // of test-suite-remediation-plan-2026-04-25.md. Compile-time references to these constants
    // (e.g. RunOrchestrator/RunResult), the architecture-invariant tests below, and the entry-point
    // parity tests already protect the contract.

    [Fact]
    public void RunConstants_StatusRunning_AndStatusCompleted_HaveExpectedValues()
    {
        // Behavioural anchor (no reflection): regress the literal contract used by GUI/CLI/API/reports.
        Assert.Equal("running", RunConstants.StatusRunning);
        Assert.Equal("completed", RunConstants.StatusCompleted);
        Assert.Equal("Sort", RunConstants.SortDecisions.Sort);
        Assert.Equal("Review", RunConstants.SortDecisions.Review);
        Assert.Equal("Blocked", RunConstants.SortDecisions.Blocked);
        Assert.Equal("DatVerified", RunConstants.SortDecisions.DatVerified);
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
