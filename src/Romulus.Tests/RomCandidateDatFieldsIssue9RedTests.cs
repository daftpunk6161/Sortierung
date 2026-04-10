using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-05): RomCandidate must expose DAT enrichment fields.
/// These tests are expected to fail until RomCandidate is extended.
/// </summary>
public sealed class RomCandidateDatFieldsIssue9RedTests
{
    [Fact]
    public void RomCandidate_ShouldExposeDatFieldsWithSafeDefaults_Issue9()
    {
        var candidate = new RomCandidate();

        Assert.Null(candidate.Hash);
        Assert.Null(candidate.DatGameName);
        Assert.Equal(DatAuditStatus.Unknown, candidate.DatAuditStatus);
    }

    [Fact]
    public void RomCandidate_ShouldAllowInitOfDatFields_Issue9()
    {
        var candidate = new RomCandidate
        {
            MainPath = @"C:\\roms\\mario.nes",
            Hash = "ABC123",
            DatGameName = "Super Mario Bros. (World)",
            DatAuditStatus = DatAuditStatus.HaveWrongName
        };

        Assert.Equal("ABC123", candidate.Hash);
        Assert.Equal("Super Mario Bros. (World)", candidate.DatGameName);
        Assert.Equal(DatAuditStatus.HaveWrongName, candidate.DatAuditStatus);
    }
}
