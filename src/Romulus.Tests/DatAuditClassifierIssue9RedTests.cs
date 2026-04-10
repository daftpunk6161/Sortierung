using Romulus.Contracts.Models;
using Romulus.Core.Audit;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// TDD RED (Issue9/A-06): Pure DAT audit classifier for Have/HaveWrongName/Miss/Unknown/Ambiguous.
/// </summary>
public sealed class DatAuditClassifierIssue9RedTests
{
    [Fact]
    public void Classify_HashAndRomNameMatch_ReturnsHave_Issue9A06()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa111", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "aaa111",
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Have, status);
    }

    [Fact]
    public void Classify_HashMatchButDifferentRomName_ReturnsHaveWrongName_Issue9A06()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa111", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "aaa111",
            actualFileName: "Contra [h1].nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.HaveWrongName, status);
    }

    [Fact]
    public void Classify_EmptyHash_ReturnsUnknown_Issue9A06()
    {
        var status = DatAuditClassifier.Classify(
            hash: null,
            actualFileName: "Contra (USA).nes",
            consoleKey: "NES",
            datIndex: new DatIndex());

        Assert.Equal(DatAuditStatus.Unknown, status);
    }

    [Fact]
    public void Classify_NoHashMatchInConsole_ReturnsMiss_Issue9A06()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa111", "Contra", "Contra (USA).nes");

        var status = DatAuditClassifier.Classify(
            hash: "zzz999",
            actualFileName: "Unknown.nes",
            consoleKey: "NES",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Miss, status);
    }

    [Fact]
    public void Classify_UnknownConsoleAndHashInMultipleConsoles_ReturnsAmbiguous_Issue9A06()
    {
        var index = new DatIndex();
        index.Add("NES", "aaa111", "Contra", "Contra (USA).nes");
        index.Add("FDS", "aaa111", "Contra", "Contra (Disk).fds");

        var status = DatAuditClassifier.Classify(
            hash: "aaa111",
            actualFileName: "Contra.bin",
            consoleKey: "",
            datIndex: index);

        Assert.Equal(DatAuditStatus.Ambiguous, status);
    }
}
