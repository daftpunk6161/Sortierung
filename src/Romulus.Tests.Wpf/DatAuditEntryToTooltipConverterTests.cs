using System.Globalization;
using Romulus.Contracts.Models;
using Romulus.UI.Wpf.Converters;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Unit tests for <see cref="DatAuditEntryToTooltipConverter"/> — Mini-P1
/// (siehe ADR-0025 Aktivierungsbedingung). Liefert pro DAT-Audit-Zeile
/// einen kontextualisierten Tooltip aus den vorhandenen Entry-Feldern,
/// damit der User je Status-Klasse die wahrscheinliche Ursache versteht
/// ohne Schein-Praezision (keine neuen Statuswerte, kein neuer Datenfluss).
/// </summary>
public sealed class DatAuditEntryToTooltipConverterTests
{
    private static readonly DatAuditEntryToTooltipConverter Converter = new();

    private static string Convert(DatAuditEntry entry)
        => (string)Converter.Convert(entry, typeof(string), null!, CultureInfo.InvariantCulture);

    private static DatAuditEntry Entry(
        DatAuditStatus status,
        string consoleKey = "PSP",
        string filePath = @"M:\roms\PSP\game.cso",
        string? datGameName = null,
        string? datRomFileName = null)
        => new(filePath, Hash: "abc", Status: status,
               DatGameName: datGameName, DatRomFileName: datRomFileName,
               ConsoleKey: consoleKey, Confidence: 90);

    [Fact]
    public void Convert_NullEntry_ReturnsEmptyString()
    {
        var result = Converter.Convert(null!, typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Convert_NonEntryValue_ReturnsEmptyString()
    {
        var result = Converter.Convert("not-an-entry", typeof(string), null!, CultureInfo.InvariantCulture);
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Convert_Have_MentionsHashAndNameMatch()
    {
        var text = Convert(Entry(DatAuditStatus.Have, datGameName: "Tiny Claws"));
        Assert.Contains("Hash", text);
        Assert.Contains("Name", text);
    }

    [Fact]
    public void Convert_HaveWrongName_MentionsHashOkAndNameMismatchAndDatName()
    {
        var text = Convert(Entry(DatAuditStatus.HaveWrongName,
            datGameName: "Tiny Claws", datRomFileName: "Tiny Claws (USA).cso"));
        Assert.Contains("Hash", text);
        Assert.Contains("Tiny Claws (USA).cso", text);
    }

    [Fact]
    public void Convert_HaveByName_WarnsThatHashWasNotVerified()
    {
        var text = Convert(Entry(DatAuditStatus.HaveByName,
            datGameName: "Tiny Claws", datRomFileName: "Tiny Claws (USA).cso"));
        Assert.Contains("Name", text);
        Assert.Contains("Hash", text);
        // Must convey that the hash check did NOT happen / was not verified.
        Assert.Contains("nicht verifiziert", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_Miss_MentionsConsoleAndPossibleCauses()
    {
        var text = Convert(Entry(DatAuditStatus.Miss, consoleKey: "PSP"));
        Assert.Contains("PSP", text);
        // Must list at least one plausible cause so the user understands why
        // an extension-detected file misses the DAT (e.g. CSO compression).
        Assert.True(
            text.Contains("komprimier", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("Region", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("Beta", System.StringComparison.OrdinalIgnoreCase)
            || text.Contains("Hack", System.StringComparison.OrdinalIgnoreCase),
            $"Miss-Tooltip soll mindestens eine plausible Ursache nennen: '{text}'");
    }

    [Fact]
    public void Convert_Unknown_DistinguishesNoConsoleVsNoDat()
    {
        // Critic-Vorgabe: Unknown hat zwei Ursachen — der Tooltip muss
        // beide klar benennen, damit der User die richtige Folge-Aktion ableitet.
        var noConsole = Convert(Entry(DatAuditStatus.Unknown, consoleKey: "UNKNOWN"));
        Assert.Contains("Konsole", noConsole, System.StringComparison.OrdinalIgnoreCase);

        var noDat = Convert(Entry(DatAuditStatus.Unknown, consoleKey: "PSP"));
        Assert.Contains("DAT", noDat);
        Assert.Contains("PSP", noDat);
    }

    [Fact]
    public void Convert_Ambiguous_MentionsMultipleDatHits()
    {
        var text = Convert(Entry(DatAuditStatus.Ambiguous));
        Assert.Contains("mehrere", text, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Convert_AllStatuses_ProduceNonEmptyDeterministicText()
    {
        // Pin-Test: jedes Enum-Mitglied muss einen nicht-leeren Tooltip liefern,
        // damit kuenftige neue Statuswerte (siehe ADR-0025 deferred) nicht
        // versehentlich einen leeren Tooltip ausliefern.
        foreach (DatAuditStatus status in System.Enum.GetValues(typeof(DatAuditStatus)))
        {
            var text = Convert(Entry(status));
            Assert.False(string.IsNullOrWhiteSpace(text),
                $"Status {status} liefert leeren Tooltip");
        }
    }

    [Fact]
    public void Convert_IsDeterministic_SameInputSameOutput()
    {
        var entry = Entry(DatAuditStatus.Miss, consoleKey: "PSP");
        Assert.Equal(Convert(entry), Convert(entry));
    }

    [Fact]
    public void ConvertBack_IsNotSupported()
    {
        Assert.Throws<System.NotSupportedException>(
            () => Converter.ConvertBack(null!, typeof(DatAuditEntry), null!, CultureInfo.InvariantCulture));
    }
}
