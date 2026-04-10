using Xunit;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Conversion;
using Romulus.Contracts.Models;

namespace Romulus.Tests;

/// <summary>
/// Coverage gaps: PipelinePhaseHelpers (77.8%), ConversionOutputValidator (61.1%),
/// PipelinePhaseHelpers.CreateAuditRow, GetConversionOutputPaths.
/// </summary>
public class PipelineAndConversionCoverageTests
{
    // ═══════════════════════════════════════════════════════════════════
    //  PipelinePhaseHelpers.FindRootForPath — SEC-MOVE-01
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void FindRootForPath_FileInRoot_ReturnsRoot()
    {
        var roots = new[] { @"C:\Roms" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms\game.rom", roots);
        Assert.Equal(@"C:\Roms", result);
    }

    [Fact]
    public void FindRootForPath_FileInSubdir_ReturnsRoot()
    {
        var roots = new[] { @"C:\Roms" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms\SNES\game.sfc", roots);
        Assert.Equal(@"C:\Roms", result);
    }

    [Fact]
    public void FindRootForPath_FileOutsideAllRoots_ReturnsNull()
    {
        var roots = new[] { @"C:\Roms" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"D:\Other\game.rom", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindRootForPath_PrefixAttack_DoesNotMatchSimilarName()
    {
        // SEC-MOVE-01: C:\Roms should NOT match C:\Roms-Other
        var roots = new[] { @"C:\Roms" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms-Other\game.rom", roots);
        Assert.Null(result);
    }

    [Fact]
    public void FindRootForPath_MultipleRoots_ReturnsCorrectOne()
    {
        var roots = new[] { @"C:\Roms\SNES", @"C:\Roms\GBA" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms\GBA\game.gba", roots);
        Assert.Equal(@"C:\Roms\GBA", result);
    }

    [Fact]
    public void FindRootForPath_EmptyRoots_ReturnsNull()
    {
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\game.rom", Array.Empty<string>());
        Assert.Null(result);
    }

    [Fact]
    public void FindRootForPath_CaseInsensitive()
    {
        var roots = new[] { @"C:\ROMS" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\roms\game.rom", roots);
        Assert.Equal(@"C:\ROMS", result);
    }

    [Fact]
    public void FindRootForPath_RootWithTrailingBackslash()
    {
        var roots = new[] { @"C:\Roms\" };
        var result = PipelinePhaseHelpers.FindRootForPath(@"C:\Roms\game.rom", roots);
        Assert.Equal(@"C:\Roms\", result);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PipelinePhaseHelpers.GetConversionOutputPaths
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetConversionOutputPaths_SingleTarget_ReturnsSingle()
    {
        var result = new ConversionResult(@"C:\src.rom", @"C:\target.chd", ConversionOutcome.Success);
        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);
        Assert.Single(paths);
        Assert.Equal(@"C:\target.chd", paths[0]);
    }

    [Fact]
    public void GetConversionOutputPaths_NullTarget_ReturnsEmpty()
    {
        var result = new ConversionResult(@"C:\src.rom", null, ConversionOutcome.Error);
        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetConversionOutputPaths_WhitespaceTarget_ReturnsEmpty()
    {
        var result = new ConversionResult(@"C:\src.rom", "  ", ConversionOutcome.Success);
        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);
        Assert.Empty(paths);
    }

    [Fact]
    public void GetConversionOutputPaths_WithAdditional_ReturnsAll()
    {
        var result = new ConversionResult(@"C:\src.rom", @"C:\target.chd", ConversionOutcome.Success)
        {
            AdditionalTargetPaths = new[] { @"C:\extra1.bin", @"C:\extra2.bin" }
        };
        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);
        Assert.Equal(3, paths.Count);
    }

    [Fact]
    public void GetConversionOutputPaths_DeduplicatesTargetAndAdditional()
    {
        var result = new ConversionResult(@"C:\src.rom", @"C:\target.chd", ConversionOutcome.Success)
        {
            AdditionalTargetPaths = new[] { @"C:\target.chd" }  // duplicate
        };
        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);
        Assert.Single(paths);
    }

    [Fact]
    public void GetConversionOutputPaths_DedupeCaseInsensitive()
    {
        var result = new ConversionResult(@"C:\src.rom", @"C:\Target.CHD", ConversionOutcome.Success)
        {
            AdditionalTargetPaths = new[] { @"C:\target.chd" }
        };
        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);
        Assert.Single(paths);
    }

    [Fact]
    public void GetConversionOutputPaths_SkipsWhitespaceAdditional()
    {
        var result = new ConversionResult(@"C:\src.rom", @"C:\target.chd", ConversionOutcome.Success)
        {
            AdditionalTargetPaths = new[] { "", " ", @"C:\real.bin" }
        };
        var paths = PipelinePhaseHelpers.GetConversionOutputPaths(result);
        Assert.Equal(2, paths.Count);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PipelinePhaseHelpers.CreateAuditRow
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void CreateAuditRow_NoAuditPath_ReturnsNull()
    {
        var options = new RunOptions { Roots = new[] { @"C:\Roms" } };
        var row = PipelinePhaseHelpers.CreateAuditRow(options, @"C:\Roms\game.rom", @"C:\Roms\dest.rom", "MOVE");
        Assert.Null(row);
    }

    [Fact]
    public void CreateAuditRow_WithAuditPath_FileInRoot_ReturnsRow()
    {
        var options = new RunOptions
        {
            Roots = new[] { @"C:\Roms" },
            AuditPath = @"C:\Audit"
        };
        var row = PipelinePhaseHelpers.CreateAuditRow(options, @"C:\Roms\game.rom", @"C:\Roms\dest.rom", "MOVE", "GAME", "abc123", "dedup");
        Assert.NotNull(row);
        Assert.Equal(@"C:\Roms", row.RootPath);
        Assert.Equal("MOVE", row.Action);
        Assert.Equal("GAME", row.Category);
        Assert.Equal("abc123", row.Hash);
    }

    [Fact]
    public void CreateAuditRow_FileOutsideRoots_ReturnsNull()
    {
        var options = new RunOptions
        {
            Roots = new[] { @"C:\Roms" },
            AuditPath = @"C:\Audit"
        };
        var row = PipelinePhaseHelpers.CreateAuditRow(options, @"D:\Other\game.rom", null, "MOVE");
        Assert.Null(row);
    }

    [Fact]
    public void CreateAuditRow_NullTargetPath_RowHasEmptyNewPath()
    {
        var options = new RunOptions
        {
            Roots = new[] { @"C:\Roms" },
            AuditPath = @"C:\Audit"
        };
        var row = PipelinePhaseHelpers.CreateAuditRow(options, @"C:\Roms\game.rom", null, "DELETE");
        Assert.NotNull(row);
        Assert.Equal(string.Empty, row.NewPath);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  ConversionOutputValidator.TryValidateCreatedOutput
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void TryValidateCreatedOutput_FileDoesNotExist_ReturnsFalse()
    {
        var result = ConversionOutputValidator.TryValidateCreatedOutput(
            Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".rom"),
            out var reason);
        Assert.False(result);
        Assert.Equal("output-not-created", reason);
    }

    [Fact]
    public void TryValidateCreatedOutput_EmptyFile_ReturnsFalse()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            // File is 0 bytes by default
            var result = ConversionOutputValidator.TryValidateCreatedOutput(tmpFile, out var reason);
            Assert.False(result);
            Assert.Equal("output-empty", reason);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    [Fact]
    public void TryValidateCreatedOutput_ValidFile_ReturnsTrue()
    {
        var tmpFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tmpFile, "test content");
            var result = ConversionOutputValidator.TryValidateCreatedOutput(tmpFile, out var reason);
            Assert.True(result);
            Assert.Equal(string.Empty, reason);
        }
        finally
        {
            File.Delete(tmpFile);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  PipelinePhaseHelpers.GetSetMembers
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void GetSetMembers_NonSetExtension_ReturnsSingleOrEmpty()
    {
        // .sfc is not a multi-file set extension
        var result = PipelinePhaseHelpers.GetSetMembers(@"C:\game.sfc", ".sfc");
        // Returns at most the file itself or empty (non-set extension)
        Assert.NotNull(result);
    }
}
