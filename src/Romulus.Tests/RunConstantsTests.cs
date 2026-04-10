using Romulus.Contracts;
using Xunit;

namespace Romulus.Tests;

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

    [Fact]
    public void ArtifactDirectories_Reports_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppIdentity.ArtifactDirectories.Reports));
        Assert.Equal("reports", AppIdentity.ArtifactDirectories.Reports);
    }

    [Fact]
    public void ArtifactDirectories_AuditLogs_IsNotEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(AppIdentity.ArtifactDirectories.AuditLogs));
        Assert.Equal("audit-logs", AppIdentity.ArtifactDirectories.AuditLogs);
    }

    // ── WellKnownFolders regression tests ───────────────────────────

    [Fact]
    public void WellKnownFolders_TrashRegionDedupe_MatchesCanonicalName()
        => Assert.Equal("_TRASH_REGION_DEDUPE", RunConstants.WellKnownFolders.TrashRegionDedupe);

    [Fact]
    public void WellKnownFolders_TrashJunk_MatchesCanonicalName()
        => Assert.Equal("_TRASH_JUNK", RunConstants.WellKnownFolders.TrashJunk);

    [Fact]
    public void WellKnownFolders_TrashConverted_MatchesCanonicalName()
        => Assert.Equal("_TRASH_CONVERTED", RunConstants.WellKnownFolders.TrashConverted);

    [Fact]
    public void WellKnownFolders_TrashGeneric_MatchesCanonicalName()
        => Assert.Equal("_TRASH", RunConstants.WellKnownFolders.TrashGeneric);

    [Fact]
    public void WellKnownFolders_Ps3Dupes_MatchesCanonicalName()
        => Assert.Equal("PS3_DUPES", RunConstants.WellKnownFolders.Ps3Dupes);

    [Fact]
    public void WellKnownFolders_FolderDupes_MatchesCanonicalName()
        => Assert.Equal("_FOLDER_DUPES", RunConstants.WellKnownFolders.FolderDupes);

    [Fact]
    public void WellKnownFolders_SpecialFolders_MatchCanonicalNames()
    {
        Assert.Equal("_BIOS", RunConstants.WellKnownFolders.Bios);
        Assert.Equal("_JUNK", RunConstants.WellKnownFolders.Junk);
        Assert.Equal("_REVIEW", RunConstants.WellKnownFolders.Review);
        Assert.Equal("_QUARANTINE", RunConstants.WellKnownFolders.Quarantine);
        Assert.Equal("_BACKUP", RunConstants.WellKnownFolders.Backup);
    }

    [Fact]
    public void WellKnownFolders_AllNamesStartWithUnderscore_ExceptPs3Dupes()
    {
        // All well-known folders except PS3_DUPES start with underscore —
        // this is a naming invariant that ensures they sort to the top in file managers
        // and are visually distinct from ROM directories.
        Assert.StartsWith("_", RunConstants.WellKnownFolders.TrashRegionDedupe);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.TrashJunk);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.TrashConverted);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.TrashGeneric);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.FolderDupes);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.Bios);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.Junk);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.Review);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.Quarantine);
        Assert.StartsWith("_", RunConstants.WellKnownFolders.Backup);
        // PS3_DUPES is a legacy name that doesn't follow the underscore convention
        Assert.StartsWith("P", RunConstants.WellKnownFolders.Ps3Dupes);
    }
}
