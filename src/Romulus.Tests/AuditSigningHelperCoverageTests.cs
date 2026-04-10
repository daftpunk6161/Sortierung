using Romulus.Contracts;
using Romulus.Infrastructure.Audit;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for AuditSigningService pure static helpers:
/// NormalizeRollbackAction and BuildPendingOperationKey.
/// </summary>
public sealed class AuditSigningHelperCoverageTests
{
    // ── NormalizeRollbackAction: Move group ─────────────────────────

    [Fact]
    public void NormalizeRollbackAction_MovePending_ReturnsMove()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.MovePending);
        Assert.Equal(RunConstants.AuditActions.Move, result);
    }

    [Fact]
    public void NormalizeRollbackAction_Move_ReturnsMove()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.Move);
        Assert.Equal(RunConstants.AuditActions.Move, result);
    }

    [Fact]
    public void NormalizeRollbackAction_Moved_ReturnsMove()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.Moved);
        Assert.Equal(RunConstants.AuditActions.Move, result);
    }

    [Theory]
    [InlineData("move")]
    [InlineData("Move")]
    [InlineData("MOVE")]
    [InlineData("move_pending")]
    [InlineData("moved")]
    public void NormalizeRollbackAction_MoveGroup_CaseInsensitive(string action)
    {
        var result = AuditSigningService.NormalizeRollbackAction(action);
        Assert.Equal(RunConstants.AuditActions.Move, result);
    }

    // ── NormalizeRollbackAction: Copy group ─────────────────────────

    [Fact]
    public void NormalizeRollbackAction_CopyPending_ReturnsCopy()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.CopyPending);
        Assert.Equal(RunConstants.AuditActions.Copy, result);
    }

    [Fact]
    public void NormalizeRollbackAction_Copy_ReturnsCopy()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.Copy);
        Assert.Equal(RunConstants.AuditActions.Copy, result);
    }

    [Theory]
    [InlineData("copy")]
    [InlineData("COPY_PENDING")]
    public void NormalizeRollbackAction_CopyGroup_CaseInsensitive(string action)
    {
        var result = AuditSigningService.NormalizeRollbackAction(action);
        Assert.Equal(RunConstants.AuditActions.Copy, result);
    }

    // ── NormalizeRollbackAction: UpperInvariant group ───────────────

    [Fact]
    public void NormalizeRollbackAction_JunkRemove_ReturnsUpper()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.JunkRemove);
        Assert.Equal("JUNK_REMOVE", result);
    }

    [Fact]
    public void NormalizeRollbackAction_ConsoleSort_ReturnsUpper()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.ConsoleSort);
        Assert.Equal("CONSOLE_SORT", result);
    }

    [Fact]
    public void NormalizeRollbackAction_Convert_ReturnsUpper()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.Convert);
        Assert.Equal("CONVERT", result);
    }

    [Fact]
    public void NormalizeRollbackAction_ConvertSource_ReturnsUpper()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.ConvertSource);
        Assert.Equal("CONVERT_SOURCE", result);
    }

    [Fact]
    public void NormalizeRollbackAction_DatRename_ReturnsUpper()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.DatRename);
        Assert.Equal("DAT_RENAME", result);
    }

    [Fact]
    public void NormalizeRollbackAction_DatRenamePending_ReturnsDatRename()
    {
        var result = AuditSigningService.NormalizeRollbackAction(RunConstants.AuditActions.DatRenamePending);
        Assert.Equal("DAT_RENAME", result);
    }

    [Theory]
    [InlineData("junk_remove", "JUNK_REMOVE")]
    [InlineData("console_sort", "CONSOLE_SORT")]
    [InlineData("convert", "CONVERT")]
    [InlineData("convert_source", "CONVERT_SOURCE")]
    [InlineData("dat_rename", "DAT_RENAME")]
    public void NormalizeRollbackAction_UpperGroup_CaseInsensitive(string action, string expected)
    {
        var result = AuditSigningService.NormalizeRollbackAction(action);
        Assert.Equal(expected, result);
    }

    // ── NormalizeRollbackAction: Unknown / null ─────────────────────

    [Theory]
    [InlineData("UNKNOWN_ACTION")]
    [InlineData("DELETE")]
    [InlineData("")]
    [InlineData("ROLLBACK")]
    public void NormalizeRollbackAction_UnrecognizedAction_ReturnsNull(string action)
    {
        var result = AuditSigningService.NormalizeRollbackAction(action);
        Assert.Null(result);
    }

    // ── BuildPendingOperationKey ─────────────────────────────────────

    [Fact]
    public void BuildPendingOperationKey_FormatsCorrectly()
    {
        var result = AuditSigningService.BuildPendingOperationKey("MOVE", @"C:\old\file.zip", @"C:\new\file.zip");
        Assert.Equal(@"MOVE|C:\old\file.zip|C:\new\file.zip", result);
    }

    [Fact]
    public void BuildPendingOperationKey_EmptyPaths_StillFormats()
    {
        var result = AuditSigningService.BuildPendingOperationKey("COPY", "", "");
        Assert.Equal("COPY||", result);
    }

    [Fact]
    public void BuildPendingOperationKey_SpecialCharsInPaths()
    {
        var result = AuditSigningService.BuildPendingOperationKey("MOVE", @"C:\path with spaces\file (1).zip", @"D:\target\file.zip");
        Assert.Contains("path with spaces", result);
        Assert.Contains("file (1).zip", result);
    }
}
