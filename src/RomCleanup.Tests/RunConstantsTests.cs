using RomCleanup.Contracts;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Tests for central RunConstants to verify consistency and prevent silent drift.
/// </summary>
public sealed class RunConstantsTests
{
    [Fact]
    public void ValidConflictPolicies_ContainsAllThreeValues()
    {
        Assert.Contains("Rename", RunConstants.ValidConflictPolicies);
        Assert.Contains("Skip", RunConstants.ValidConflictPolicies);
        Assert.Contains("Overwrite", RunConstants.ValidConflictPolicies);
        Assert.Equal(3, RunConstants.ValidConflictPolicies.Count);
    }

    [Fact]
    public void ValidConflictPolicies_IsCaseInsensitive()
    {
        Assert.Contains("rename", RunConstants.ValidConflictPolicies);
        Assert.Contains("SKIP", RunConstants.ValidConflictPolicies);
        Assert.Contains("overwrite", RunConstants.ValidConflictPolicies);
    }

    [Fact]
    public void DefaultConflictPolicy_IsRename()
    {
        Assert.Equal("Rename", RunConstants.DefaultConflictPolicy);
        Assert.Contains(RunConstants.DefaultConflictPolicy, RunConstants.ValidConflictPolicies);
    }

    [Fact]
    public void ValidHashTypes_ContainsAllThreeValues()
    {
        Assert.Contains("SHA1", RunConstants.ValidHashTypes);
        Assert.Contains("SHA256", RunConstants.ValidHashTypes);
        Assert.Contains("MD5", RunConstants.ValidHashTypes);
        Assert.Equal(3, RunConstants.ValidHashTypes.Count);
    }

    [Fact]
    public void ValidHashTypes_IsCaseInsensitive()
    {
        Assert.Contains("sha1", RunConstants.ValidHashTypes);
        Assert.Contains("sha256", RunConstants.ValidHashTypes);
        Assert.Contains("md5", RunConstants.ValidHashTypes);
    }

    [Fact]
    public void DefaultHashType_IsInValidSet()
    {
        Assert.Contains(RunConstants.DefaultHashType, RunConstants.ValidHashTypes);
    }

    [Fact]
    public void MaxPreferRegions_IsPositive()
    {
        Assert.True(RunConstants.MaxPreferRegions > 0);
        Assert.Equal(20, RunConstants.MaxPreferRegions);
    }

    [Theory]
    [InlineData("Delete")]
    [InlineData("")]
    [InlineData("merge")]
    public void ValidConflictPolicies_RejectsInvalidValues(string invalid)
    {
        Assert.DoesNotContain(invalid, RunConstants.ValidConflictPolicies);
    }
}
