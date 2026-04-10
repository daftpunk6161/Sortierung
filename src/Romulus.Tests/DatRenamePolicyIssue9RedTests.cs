using Romulus.Contracts.Models;
using Romulus.Core.Audit;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-14): specification tests for DatRenamePolicy.
/// Intentionally failing until DatRenamePolicy and DAT models are implemented.
/// </summary>
public sealed class DatRenamePolicyIssue9RedTests
{
    [Fact]
    public void EvaluateRename_ShouldProposeRename_WhenStatusIsHaveWrongName_Issue9()
    {
        // Arrange
        var entry = new DatAuditEntry(
            FilePath: @"C:\roms\NES\wrong_name.nes",
            Hash: "abc123",
            Status: DatAuditStatus.HaveWrongName,
            DatGameName: "Super Mario Bros (World)",
            DatRomFileName: "Super Mario Bros (World).nes",
            ConsoleKey: "NES",
            Confidence: 100);

        // Act
        var proposal = DatRenamePolicy.EvaluateRename(entry, currentFileName: "wrong_name.nes");

        // Assert
        Assert.Equal(@"C:\roms\NES\wrong_name.nes", proposal.SourcePath);
        Assert.Equal("Super Mario Bros (World).nes", proposal.TargetFileName);
        Assert.Equal(DatAuditStatus.HaveWrongName, proposal.Status);
        Assert.Null(proposal.ConflictReason);
    }

    [Fact]
    public void EvaluateRename_ShouldSkip_WhenStatusIsNotHaveWrongName_Issue9()
    {
        // Arrange
        var entry = new DatAuditEntry(
            FilePath: @"C:\roms\NES\already_ok.nes",
            Hash: "def456",
            Status: DatAuditStatus.Have,
            DatGameName: "Already Ok",
            DatRomFileName: "already_ok.nes",
            ConsoleKey: "NES",
            Confidence: 100);

        // Act
        var proposal = DatRenamePolicy.EvaluateRename(entry, currentFileName: "already_ok.nes");

        // Assert
        Assert.Equal(@"C:\roms\NES\already_ok.nes", proposal.SourcePath);
        Assert.Equal("already_ok.nes", proposal.TargetFileName);
        Assert.Equal(DatAuditStatus.Have, proposal.Status);
        Assert.Contains("status", proposal.ConflictReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void EvaluateRename_ShouldPreserveCurrentExtension_WhenDatRomFileNameHasDifferentExtension_Issue9()
    {
        // Arrange
        var entry = new DatAuditEntry(
            FilePath: @"C:\roms\NES\mario.nes",
            Hash: "ghi789",
            Status: DatAuditStatus.HaveWrongName,
            DatGameName: "Mario",
            DatRomFileName: "Mario.zip",
            ConsoleKey: "NES",
            Confidence: 100);

        // Act
        var proposal = DatRenamePolicy.EvaluateRename(entry, currentFileName: "mario.nes");

        // Assert
        Assert.Equal(".nes", Path.GetExtension(proposal.TargetFileName));
        Assert.DoesNotContain(".zip", proposal.TargetFileName, StringComparison.OrdinalIgnoreCase);
        Assert.Null(proposal.ConflictReason);
    }

    [Theory]
    [InlineData("..\\..\\Windows\\win.ini")]
    [InlineData("CON.nes")]
    public void EvaluateRename_ShouldReturnConflict_WhenDatTargetNameIsUnsafe_Issue9(string unsafeDatName)
    {
        // Arrange
        var entry = new DatAuditEntry(
            FilePath: @"C:\roms\NES\source.nes",
            Hash: "jkl012",
            Status: DatAuditStatus.HaveWrongName,
            DatGameName: "Unsafe",
            DatRomFileName: unsafeDatName,
            ConsoleKey: "NES",
            Confidence: 100);

        // Act
        var proposal = DatRenamePolicy.EvaluateRename(entry, currentFileName: "source.nes");

        // Assert
        Assert.Equal("source.nes", proposal.TargetFileName);
        Assert.Equal(DatAuditStatus.HaveWrongName, proposal.Status);
        Assert.Contains("unsafe", proposal.ConflictReason ?? string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
