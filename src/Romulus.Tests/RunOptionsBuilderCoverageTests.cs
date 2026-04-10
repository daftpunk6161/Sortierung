using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for RunOptionsBuilder: Validate, Normalize, GetDryRunFeatureWarnings, WithApproveConversionReview.
/// These are pure static methods serving as the central run-options gate for CLI, API, and GUI.
/// </summary>
public sealed class RunOptionsBuilderCoverageTests
{
    private static RunOptions Default() => new()
    {
        Roots = [@"C:\roms"],
        Mode = "DryRun",
        Extensions = [".zip", ".7z"],
        PreferRegions = ["EU", "US"],
    };

    // ═══ Validate ═════════════════════════════════════════════════════

    [Fact]
    public void Validate_DefaultOptions_NoErrors()
    {
        var errors = RunOptionsBuilder.Validate(Default());
        Assert.Empty(errors);
    }

    [Fact]
    public void Validate_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RunOptionsBuilder.Validate(null!));
    }

    [Fact]
    public void Validate_KeepUnknownFalseWithoutOnlyGames_Error()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            OnlyGames = false,
            KeepUnknownWhenOnlyGames = false
        };
        var errors = RunOptionsBuilder.Validate(opts);
        Assert.Contains(errors, e => e.Contains("KeepUnknownWhenOnlyGames"));
    }

    [Fact]
    public void Validate_KeepUnknownFalseWithOnlyGames_NoError()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = false
        };
        var errors = RunOptionsBuilder.Validate(opts);
        Assert.DoesNotContain(errors, e => e.Contains("KeepUnknownWhenOnlyGames"));
    }

    [Fact]
    public void Validate_UncDatRoot_Error()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            DatRoot = @"\\server\dats"
        };
        var errors = RunOptionsBuilder.Validate(opts);
        Assert.Contains(errors, e => e.Contains("datRoot") || e.Contains("UNC"));
    }

    [Fact]
    public void Validate_UncTrashRoot_Error()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            TrashRoot = @"\\server\trash"
        };
        var errors = RunOptionsBuilder.Validate(opts);
        Assert.Contains(errors, e => e.Contains("trashRoot") || e.Contains("UNC"));
    }

    [Fact]
    public void Validate_DriveRootAuditPath_Error()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            AuditPath = @"C:\"
        };
        var errors = RunOptionsBuilder.Validate(opts);
        Assert.Contains(errors, e => e.Contains("auditPath"));
    }

    [Fact]
    public void Validate_DriveRootReportPath_Error()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            ReportPath = @"C:\"
        };
        var errors = RunOptionsBuilder.Validate(opts);
        Assert.Contains(errors, e => e.Contains("reportPath"));
    }

    [Fact]
    public void Validate_SystemPathTrashRoot_Error()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            TrashRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows)
        };
        var errors = RunOptionsBuilder.Validate(opts);
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Validate_TrashRootInsideRoot_Error()
    {
        var root = Path.Combine(Path.GetTempPath(), "RunOptionsBuilderCoverage", Guid.NewGuid().ToString("N"));
        var trash = Path.Combine(root, "_TRASH");
        var opts = new RunOptions
        {
            Roots = [root],
            TrashRoot = trash
        };

        var errors = RunOptionsBuilder.Validate(opts);

        Assert.Contains(errors, e => e.Contains("trashRoot", StringComparison.OrdinalIgnoreCase)
            && e.Contains("inside", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Validate_AuditPathInsideRoot_Error()
    {
        var root = Path.Combine(Path.GetTempPath(), "RunOptionsBuilderCoverage", Guid.NewGuid().ToString("N"));
        var auditPath = Path.Combine(root, "audit", "audit.csv");
        var opts = new RunOptions
        {
            Roots = [root],
            AuditPath = auditPath
        };

        var errors = RunOptionsBuilder.Validate(opts);

        Assert.Contains(errors, e => e.Contains("auditPath", StringComparison.OrdinalIgnoreCase)
            && e.Contains("inside", StringComparison.OrdinalIgnoreCase));
    }

    // ═══ Normalize ═══════════════════════════════════════════════════

    [Fact]
    public void Normalize_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RunOptionsBuilder.Normalize(null!));
    }

    [Fact]
    public void Normalize_EmptyRegions_DefaultsToRunConstants()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            PreferRegions = []
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Equal(RunConstants.DefaultPreferRegions, result.PreferRegions);
    }

    [Fact]
    public void Normalize_DuplicateRegions_Deduplicates()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            PreferRegions = ["EU", "eu", "US", "EU"]
        };
        var result = RunOptionsBuilder.Normalize(opts);
        // Should have only unique (case-insensitive) entries
        Assert.Equal(2, result.PreferRegions.Length);
    }

    [Fact]
    public void Normalize_RegionsTrimmedAndUppercased()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            PreferRegions = [" eu ", " jp "]
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Contains("EU", result.PreferRegions);
        Assert.Contains("JP", result.PreferRegions);
    }

    [Fact]
    public void Normalize_DuplicateExtensions_Deduplicates()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            Extensions = [".zip", ".ZIP", ".7z", ".zip"]
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.True(result.Extensions.Count <= 2, "Duplicate extensions should be removed");
    }

    [Fact]
    public void Normalize_WhitespaceRoots_Removed()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms", "", "  ", @"D:\games"]
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Equal(2, result.Roots.Count);
    }

    [Fact]
    public void Normalize_OverlappingRoots_ChildRootIsRemoved()
    {
        var parent = Path.Combine(Path.GetTempPath(), "RunOptionsBuilderCoverage", "roots", Guid.NewGuid().ToString("N"));
        var child = Path.Combine(parent, "SNES");

        var opts = new RunOptions
        {
            Roots = [child, parent]
        };

        var result = RunOptionsBuilder.Normalize(opts);

        Assert.Single(result.Roots);
        Assert.Equal(Path.GetFullPath(parent), result.Roots[0], ignoreCase: true);
    }

    [Fact]
    public void Normalize_EmptyMode_DefaultsToDryRun()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            Mode = ""
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Equal(RunConstants.ModeDryRun, result.Mode);
    }

    [Fact]
    public void Normalize_MoveMode_NormalizedToCanonical()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            Mode = "move"
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Equal(RunConstants.ModeMove, result.Mode);
    }

    [Fact]
    public void Normalize_UnknownMode_DefaultsToDryRun()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            Mode = "Execute"
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Equal(RunConstants.ModeDryRun, result.Mode);
    }

    [Fact]
    public void Normalize_EmptyConflictPolicy_DefaultsToRename()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            ConflictPolicy = ""
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Equal(RunConstants.DefaultConflictPolicy, result.ConflictPolicy);
    }

    [Fact]
    public void Normalize_EmptyHashType_DefaultsToSHA1()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            HashType = ""
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.Equal("SHA1", result.HashType);
    }

    [Fact]
    public void Normalize_PreservesFlags()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            RemoveJunk = true,
            OnlyGames = true,
            AggressiveJunk = true,
            SortConsole = true,
            EnableDat = true,
            ConvertOnly = true
        };
        var result = RunOptionsBuilder.Normalize(opts);
        Assert.True(result.RemoveJunk);
        Assert.True(result.OnlyGames);
        Assert.True(result.AggressiveJunk);
        Assert.True(result.SortConsole);
        Assert.True(result.EnableDat);
        Assert.True(result.ConvertOnly);
    }

    // ═══ GetDryRunFeatureWarnings ════════════════════════════════════

    [Fact]
    public void GetDryRunFeatureWarnings_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RunOptionsBuilder.GetDryRunFeatureWarnings(null!));
    }

    [Fact]
    public void GetDryRunFeatureWarnings_MoveMode_NoWarnings()
    {
        var opts = new RunOptions { Roots = [@"C:\roms"], Mode = "Move", ConvertFormat = "chd" };
        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(opts);
        Assert.Empty(warnings);
    }

    [Fact]
    public void GetDryRunFeatureWarnings_DryRunWithConvertFormat_Warning()
    {
        var opts = new RunOptions { Roots = [@"C:\roms"], Mode = "DryRun", ConvertFormat = "chd" };
        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(opts);
        Assert.Contains(warnings, w => w.Contains("ConvertFormat"));
    }

    [Fact]
    public void GetDryRunFeatureWarnings_DryRunWithDatRename_Warning()
    {
        var opts = new RunOptions { Roots = [@"C:\roms"], Mode = "DryRun", EnableDatRename = true };
        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(opts);
        Assert.Contains(warnings, w => w.Contains("EnableDatRename"));
    }

    [Fact]
    public void GetDryRunFeatureWarnings_DryRunWithConvertOnly_Warning()
    {
        var opts = new RunOptions { Roots = [@"C:\roms"], Mode = "DryRun", ConvertOnly = true };
        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(opts);
        Assert.Contains(warnings, w => w.Contains("ConvertOnly"));
    }

    [Fact]
    public void GetDryRunFeatureWarnings_DryRunWithAllFlags_ThreeWarnings()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            Mode = "DryRun",
            ConvertFormat = "chd",
            EnableDatRename = true,
            ConvertOnly = true
        };
        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(opts);
        Assert.Equal(3, warnings.Count);
    }

    [Fact]
    public void GetDryRunFeatureWarnings_DryRunNoFlags_NoWarnings()
    {
        var opts = new RunOptions { Roots = [@"C:\roms"], Mode = "DryRun" };
        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(opts);
        Assert.Empty(warnings);
    }

    [Fact]
    public void GetDryRunFeatureWarnings_CaseInsensitiveMode()
    {
        var opts = new RunOptions { Roots = [@"C:\roms"], Mode = "dryrun", ConvertFormat = "chd" };
        var warnings = RunOptionsBuilder.GetDryRunFeatureWarnings(opts);
        Assert.Single(warnings);
    }

    // ═══ WithApproveConversionReview ═════════════════════════════════

    [Fact]
    public void WithApproveConversionReview_SetsFlag()
    {
        var opts = Default();
        var result = RunOptionsBuilder.WithApproveConversionReview(opts, true);
        Assert.True(result.ApproveConversionReview);
    }

    [Fact]
    public void WithApproveConversionReview_ClearsFlag()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            ApproveConversionReview = true
        };
        var result = RunOptionsBuilder.WithApproveConversionReview(opts, false);
        Assert.False(result.ApproveConversionReview);
    }

    [Fact]
    public void WithApproveConversionReview_PreservesOtherFields()
    {
        var opts = new RunOptions
        {
            Roots = [@"C:\roms"],
            Mode = "Move",
            ConvertFormat = "chd",
            SortConsole = true
        };
        var result = RunOptionsBuilder.WithApproveConversionReview(opts, true);
        Assert.Equal("Move", result.Mode);
        Assert.Equal("chd", result.ConvertFormat);
        Assert.True(result.SortConsole);
        Assert.True(result.ApproveConversionReview);
    }

    [Fact]
    public void WithApproveConversionReview_NullThrows()
    {
        Assert.Throws<ArgumentNullException>(() => RunOptionsBuilder.WithApproveConversionReview(null!, true));
    }
}
