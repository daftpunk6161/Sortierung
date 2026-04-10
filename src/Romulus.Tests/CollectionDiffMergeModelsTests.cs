using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

public sealed class CollectionDiffMergeModelsTests
{
    [Fact]
    public void CollectionDiffSummary_FromEntries_CountsAllStatesDeterministically()
    {
        var entries = new[]
        {
            new CollectionDiffEntry { DiffKey = "1", State = CollectionDiffState.OnlyInLeft },
            new CollectionDiffEntry { DiffKey = "2", State = CollectionDiffState.OnlyInRight },
            new CollectionDiffEntry { DiffKey = "3", State = CollectionDiffState.PresentInBothIdentical },
            new CollectionDiffEntry { DiffKey = "4", State = CollectionDiffState.PresentInBothDifferent },
            new CollectionDiffEntry { DiffKey = "5", State = CollectionDiffState.LeftPreferred, PreferredSide = CollectionCompareSide.Left },
            new CollectionDiffEntry { DiffKey = "6", State = CollectionDiffState.RightPreferred, PreferredSide = CollectionCompareSide.Right },
            new CollectionDiffEntry { DiffKey = "7", State = CollectionDiffState.ReviewRequired, ReviewRequired = true }
        };

        var summary = CollectionDiffSummary.FromEntries(entries);

        Assert.Equal(7, summary.TotalEntries);
        Assert.Equal(1, summary.OnlyInLeft);
        Assert.Equal(1, summary.OnlyInRight);
        Assert.Equal(1, summary.PresentInBothIdentical);
        Assert.Equal(1, summary.PresentInBothDifferent);
        Assert.Equal(1, summary.LeftPreferred);
        Assert.Equal(1, summary.RightPreferred);
        Assert.Equal(1, summary.ReviewRequired);
    }

    [Fact]
    public void CollectionMergePlanSummary_FromEntries_CountsMutatingAndNonMutatingDecisions()
    {
        var entries = new[]
        {
            new CollectionMergePlanEntry { PlanEntryId = "1", DiffKey = "a", Decision = CollectionMergeDecision.CopyToTarget },
            new CollectionMergePlanEntry { PlanEntryId = "2", DiffKey = "b", Decision = CollectionMergeDecision.MoveToTarget },
            new CollectionMergePlanEntry { PlanEntryId = "3", DiffKey = "c", Decision = CollectionMergeDecision.KeepExistingTarget },
            new CollectionMergePlanEntry { PlanEntryId = "4", DiffKey = "d", Decision = CollectionMergeDecision.SkipAsDuplicate },
            new CollectionMergePlanEntry { PlanEntryId = "5", DiffKey = "e", Decision = CollectionMergeDecision.ReviewRequired, ReviewRequired = true },
            new CollectionMergePlanEntry { PlanEntryId = "6", DiffKey = "f", Decision = CollectionMergeDecision.Blocked }
        };

        var summary = CollectionMergePlanSummary.FromEntries(entries);

        Assert.Equal(6, summary.TotalEntries);
        Assert.Equal(1, summary.CopyToTarget);
        Assert.Equal(1, summary.MoveToTarget);
        Assert.Equal(1, summary.KeepExistingTarget);
        Assert.Equal(1, summary.SkipAsDuplicate);
        Assert.Equal(1, summary.ReviewRequired);
        Assert.Equal(1, summary.Blocked);
        Assert.Equal(2, summary.MutatingEntries);
    }

    [Theory]
    [InlineData(CollectionMergeDecision.CopyToTarget, true)]
    [InlineData(CollectionMergeDecision.MoveToTarget, true)]
    [InlineData(CollectionMergeDecision.KeepExistingTarget, false)]
    [InlineData(CollectionMergeDecision.SkipAsDuplicate, false)]
    [InlineData(CollectionMergeDecision.ReviewRequired, false)]
    [InlineData(CollectionMergeDecision.Blocked, false)]
    public void CollectionMergeDecision_IsMutating_ReturnsExpectedValue(CollectionMergeDecision decision, bool expected)
    {
        Assert.Equal(expected, decision.IsMutating());
    }

    [Fact]
    public void CollectionMergeApplySummary_FromEntries_CountsOutcomesDeterministically()
    {
        var entries = new[]
        {
            new CollectionMergeApplyEntryResult { PlanEntryId = "1", DiffKey = "a", Decision = CollectionMergeDecision.CopyToTarget, Outcome = CollectionMergeApplyOutcome.Applied },
            new CollectionMergeApplyEntryResult { PlanEntryId = "2", DiffKey = "b", Decision = CollectionMergeDecision.MoveToTarget, Outcome = CollectionMergeApplyOutcome.Applied },
            new CollectionMergeApplyEntryResult { PlanEntryId = "3", DiffKey = "c", Decision = CollectionMergeDecision.KeepExistingTarget, Outcome = CollectionMergeApplyOutcome.KeptExistingTarget },
            new CollectionMergeApplyEntryResult { PlanEntryId = "4", DiffKey = "d", Decision = CollectionMergeDecision.SkipAsDuplicate, Outcome = CollectionMergeApplyOutcome.SkippedAsDuplicate },
            new CollectionMergeApplyEntryResult { PlanEntryId = "5", DiffKey = "e", Decision = CollectionMergeDecision.ReviewRequired, Outcome = CollectionMergeApplyOutcome.ReviewRequired },
            new CollectionMergeApplyEntryResult { PlanEntryId = "6", DiffKey = "f", Decision = CollectionMergeDecision.Blocked, Outcome = CollectionMergeApplyOutcome.Blocked },
            new CollectionMergeApplyEntryResult { PlanEntryId = "7", DiffKey = "g", Decision = CollectionMergeDecision.CopyToTarget, Outcome = CollectionMergeApplyOutcome.Failed }
        };

        var summary = CollectionMergeApplySummary.FromEntries(entries);

        Assert.Equal(7, summary.TotalEntries);
        Assert.Equal(2, summary.Applied);
        Assert.Equal(1, summary.Copied);
        Assert.Equal(1, summary.Moved);
        Assert.Equal(1, summary.KeptExistingTarget);
        Assert.Equal(1, summary.SkippedAsDuplicate);
        Assert.Equal(1, summary.ReviewRequired);
        Assert.Equal(1, summary.Blocked);
        Assert.Equal(1, summary.Failed);
    }
}
