using Romulus.Contracts.Models;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Result of comparing a ground-truth entry against actual detection output.
/// </summary>
public enum BenchmarkVerdict
{
    /// <summary>Detection matches the expected console key exactly.</summary>
    Correct,

    /// <summary>Detection matches an acceptable alternative console key.</summary>
    Acceptable,

    /// <summary>Detection produced a wrong console key.</summary>
    Wrong,

    /// <summary>Detection is inconclusive because multiple conflicting hypotheses remain plausible.</summary>
    Ambiguous,

    /// <summary>Detection produced no result (UNKNOWN) for a known console.</summary>
    Missed,

    /// <summary>Negative control correctly identified as unknown.</summary>
    TrueNegative,

    /// <summary>Junk content identified as its platform family without treating it as a sortable game.</summary>
    JunkClassified,

    /// <summary>Negative control incorrectly assigned a console.</summary>
    FalsePositive
}

/// <summary>
/// Detailed result of evaluating a single ground-truth sample.
/// </summary>
public sealed record BenchmarkSampleResult(
    string Id,
    BenchmarkVerdict Verdict,
    string? ExpectedConsoleKey,
    string? ActualConsoleKey,
    int ActualConfidence,
    bool ActualHasConflict,
    string? Details,
    string ExpectedCategory = "Unknown",
    string ActualCategory = "Unknown",
    SortDecision ActualSortDecision = SortDecision.Blocked);
