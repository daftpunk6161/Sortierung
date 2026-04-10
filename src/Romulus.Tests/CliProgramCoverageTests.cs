using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Profiles;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for RunProfileValidator (ValidateDocument, ValidateSettings, ValidateOptionalSafePath)
/// and ArtifactPathResolver.FindContainingRoot.
/// </summary>
public sealed class ProfileValidatorAndPathCoverageTests
{
    // ═══ RunProfileValidator.ValidateDocument ═════════════════════════

    [Fact]
    public void ValidateDocument_ValidProfile_NoErrors()
    {
        var doc = new RunProfileDocument
        {
            Version = 1,
            Id = "my-profile.01",
            Name = "DryRun Safe",
            Settings = new RunProfileSettings()
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateDocument_InvalidVersion_ReportsError()
    {
        var doc = new RunProfileDocument
        {
            Version = 99,
            Id = "valid-id",
            Name = "Test"
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Contains(errors, e => e.Contains("version"));
    }

    [Fact]
    public void ValidateDocument_EmptyId_ReportsError()
    {
        var doc = new RunProfileDocument
        {
            Version = 1,
            Id = "",
            Name = "Test"
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Contains(errors, e => e.Contains("id"));
    }

    [Fact]
    public void ValidateDocument_IdWithInvalidChars_ReportsError()
    {
        var doc = new RunProfileDocument
        {
            Version = 1,
            Id = "invalid id!@#",
            Name = "Test"
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Contains(errors, e => e.Contains("id"));
    }

    [Fact]
    public void ValidateDocument_IdTooLong_ReportsError()
    {
        var doc = new RunProfileDocument
        {
            Version = 1,
            Id = new string('a', 65),
            Name = "Test"
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Contains(errors, e => e.Contains("id"));
    }

    [Fact]
    public void ValidateDocument_EmptyName_ReportsError()
    {
        var doc = new RunProfileDocument
        {
            Version = 1,
            Id = "valid-id",
            Name = ""
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Contains(errors, e => e.Contains("name"));
    }

    [Fact]
    public void ValidateDocument_NameTooLong_ReportsError()
    {
        var doc = new RunProfileDocument
        {
            Version = 1,
            Id = "valid-id",
            Name = new string('x', 121)
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Contains(errors, e => e.Contains("name"));
    }

    [Fact]
    public void ValidateDocument_DescriptionTooLong_ReportsError()
    {
        var doc = new RunProfileDocument
        {
            Version = 1,
            Id = "valid-id",
            Name = "Test",
            Description = new string('y', 513)
        };
        var errors = RunProfileValidator.ValidateDocument(doc);
        Assert.Contains(errors, e => e.Contains("description"));
    }

    [Fact]
    public void ValidateDocument_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RunProfileValidator.ValidateDocument(null!));
    }

    // ═══ RunProfileValidator.ValidateSettings ═════════════════════════

    [Fact]
    public void ValidateSettings_Default_NoErrors()
    {
        var errors = RunProfileValidator.ValidateSettings(new RunProfileSettings());
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateSettings_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RunProfileValidator.ValidateSettings(null!));
    }

    [Fact]
    public void ValidateSettings_TooManyRegions_Error()
    {
        var regions = Enumerable.Range(0, RunConstants.MaxPreferRegions + 1)
            .Select(i => $"R{i}").ToArray();
        var settings = new RunProfileSettings { PreferRegions = regions };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("preferRegions"));
    }

    [Fact]
    public void ValidateSettings_InvalidRegionChars_Error()
    {
        var settings = new RunProfileSettings { PreferRegions = ["EU", "US!@"] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("US!@"));
    }

    [Fact]
    public void ValidateSettings_EmptyRegion_Error()
    {
        var settings = new RunProfileSettings { PreferRegions = ["EU", ""] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("region"));
    }

    [Fact]
    public void ValidateSettings_RegionTooLong_Error()
    {
        var settings = new RunProfileSettings { PreferRegions = [new string('A', 11)] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateSettings_ValidRegions_NoErrors()
    {
        var settings = new RunProfileSettings { PreferRegions = ["EU", "US", "JP"] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateSettings_InvalidExtension_Error()
    {
        var settings = new RunProfileSettings { Extensions = [".validext", "!!!"] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("extension") || e.Contains("!!!"));
    }

    [Fact]
    public void ValidateSettings_EmptyExtension_Error()
    {
        var settings = new RunProfileSettings { Extensions = [".zip", ""] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("extensions"));
    }

    [Fact]
    public void ValidateSettings_ExtensionWithoutDot_Normalized()
    {
        // "zip" gets normalized to ".zip" which is valid
        var settings = new RunProfileSettings { Extensions = ["zip"] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Empty(errors);
    }

    [Fact]
    public void ValidateSettings_ExtensionTooLong_Error()
    {
        var settings = new RunProfileSettings { Extensions = ["." + new string('a', 20)] };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void ValidateSettings_InvalidHashType_Error()
    {
        var settings = new RunProfileSettings { HashType = "CRC32" };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("hashType") || e.Contains("CRC32"));
    }

    [Theory]
    [InlineData("SHA1")]
    [InlineData("SHA256")]
    [InlineData("MD5")]
    public void ValidateSettings_ValidHashType_NoError(string hashType)
    {
        var settings = new RunProfileSettings { HashType = hashType };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.DoesNotContain(errors, e => e.Contains("hashType"));
    }

    [Fact]
    public void ValidateSettings_InvalidConvertFormat_Error()
    {
        var settings = new RunProfileSettings { ConvertFormat = "wbfs" };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("convertFormat") || e.Contains("wbfs"));
    }

    [Theory]
    [InlineData("auto")]
    [InlineData("chd")]
    [InlineData("rvz")]
    [InlineData("zip")]
    [InlineData("7z")]
    public void ValidateSettings_ValidConvertFormat_NoError(string format)
    {
        var settings = new RunProfileSettings { ConvertFormat = format };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.DoesNotContain(errors, e => e.Contains("convertFormat"));
    }

    [Fact]
    public void ValidateSettings_InvalidConflictPolicy_Error()
    {
        var settings = new RunProfileSettings { ConflictPolicy = "Delete" };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("conflictPolicy") || e.Contains("Delete"));
    }

    [Theory]
    [InlineData("Rename")]
    [InlineData("Skip")]
    [InlineData("Overwrite")]
    public void ValidateSettings_ValidConflictPolicy_NoError(string policy)
    {
        var settings = new RunProfileSettings { ConflictPolicy = policy };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.DoesNotContain(errors, e => e.Contains("conflictPolicy"));
    }

    [Fact]
    public void ValidateSettings_InvalidMode_Error()
    {
        var settings = new RunProfileSettings { Mode = "Execute" };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("mode") || e.Contains("Execute"));
    }

    [Theory]
    [InlineData("DryRun")]
    [InlineData("Move")]
    public void ValidateSettings_ValidMode_NoError(string mode)
    {
        var settings = new RunProfileSettings { Mode = mode };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.DoesNotContain(errors, e => e.Contains("mode"));
    }

    [Fact]
    public void ValidateSettings_DatAuditWithoutDat_Error()
    {
        var settings = new RunProfileSettings { EnableDat = false, EnableDatAudit = true };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("enableDatAudit"));
    }

    [Fact]
    public void ValidateSettings_DatRenameWithoutDat_Error()
    {
        var settings = new RunProfileSettings { EnableDat = false, EnableDatRename = true };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("enableDatRename"));
    }

    [Fact]
    public void ValidateSettings_KeepUnknownFalseWithoutOnlyGames_Error()
    {
        var settings = new RunProfileSettings { OnlyGames = null, KeepUnknownWhenOnlyGames = false };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.Contains(errors, e => e.Contains("keepUnknownWhenOnlyGames"));
    }

    [Fact]
    public void ValidateSettings_KeepUnknownFalseWithOnlyGames_NoError()
    {
        var settings = new RunProfileSettings { OnlyGames = true, KeepUnknownWhenOnlyGames = false };
        var errors = RunProfileValidator.ValidateSettings(settings);
        Assert.DoesNotContain(errors, e => e.Contains("keepUnknownWhenOnlyGames"));
    }

    // ═══ RunProfileValidator.ValidateOptionalSafePath ═════════════════

    [Fact]
    public void ValidateOptionalSafePath_Null_NoError()
    {
        Assert.Null(RunProfileValidator.ValidateOptionalSafePath(null, "test"));
    }

    [Fact]
    public void ValidateOptionalSafePath_Empty_NoError()
    {
        Assert.Null(RunProfileValidator.ValidateOptionalSafePath("", "test"));
    }

    [Fact]
    public void ValidateOptionalSafePath_UncPath_Error()
    {
        var error = RunProfileValidator.ValidateOptionalSafePath(@"\\server\share", "datRoot");
        Assert.NotNull(error);
        Assert.Contains("UNC", error);
    }

    [Fact]
    public void ValidateOptionalSafePath_DriveRoot_Error()
    {
        var error = RunProfileValidator.ValidateOptionalSafePath(@"C:\", "trashRoot");
        Assert.NotNull(error);
        Assert.Contains("drive root", error);
    }

    [Fact]
    public void ValidateOptionalSafePath_SystemPath_Error()
    {
        var error = RunProfileValidator.ValidateOptionalSafePath(
            Environment.GetFolderPath(Environment.SpecialFolder.Windows), "trashRoot");
        Assert.NotNull(error);
        Assert.Contains("protected system path", error);
    }

    // ═══ ArtifactPathResolver.FindContainingRoot ══════════════════════

    [Fact]
    public void FindContainingRoot_MatchingRoot_ReturnsRoot()
    {
        var roots = new[] { @"C:\roms", @"D:\games" };
        var result = ArtifactPathResolver.FindContainingRoot(@"C:\roms\game.zip", roots);
        Assert.Equal(@"C:\roms", result);
    }

    [Fact]
    public void FindContainingRoot_NestedPath_ReturnsRoot()
    {
        var roots = new[] { @"C:\roms" };
        var result = ArtifactPathResolver.FindContainingRoot(@"C:\roms\snes\game.sfc", roots);
        Assert.Equal(@"C:\roms", result);
    }

    [Fact]
    public void FindContainingRoot_NoMatch_ReturnsNull()
    {
        var roots = new[] { @"C:\roms" };
        var result = ArtifactPathResolver.FindContainingRoot(@"D:\other\game.zip", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindContainingRoot_EmptyRoots_ReturnsNull()
    {
        var result = ArtifactPathResolver.FindContainingRoot(@"C:\roms\game.zip", []);
        Assert.Null(result);
    }

    [Fact]
    public void FindContainingRoot_ExactRootPath_ReturnsNull()
    {
        // File path identical to root (no separator after root) → no match
        var roots = new[] { @"C:\roms" };
        var result = ArtifactPathResolver.FindContainingRoot(@"C:\roms", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindContainingRoot_CaseInsensitive_Matches()
    {
        var roots = new[] { @"C:\ROMS" };
        var result = ArtifactPathResolver.FindContainingRoot(@"c:\roms\game.zip", roots);
        Assert.Equal(@"C:\ROMS", result);
    }

    [Fact]
    public void FindContainingRoot_SimilarPrefix_DoesNotFalseMatch()
    {
        // "C:\roms-backup\game.zip" should NOT match root "C:\roms"
        var roots = new[] { @"C:\roms" };
        var result = ArtifactPathResolver.FindContainingRoot(@"C:\roms-backup\game.zip", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindContainingRoot_MultipleRoots_ReturnsFirst()
    {
        var roots = new[] { @"C:\roms", @"C:\roms\snes" };
        var result = ArtifactPathResolver.FindContainingRoot(@"C:\roms\snes\game.sfc", roots);
        // Should return the first matching root
        Assert.Equal(@"C:\roms", result);
    }
}
