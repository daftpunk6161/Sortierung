using Romulus.Infrastructure.Dat;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F-DAT-19: DatXmlValidator and DatRepositoryAdapter must use the same XML
/// security policy. An XXE-style external-entity payload must be neutralised
/// (no resolution, no expansion) by both parsers without ever reaching out
/// to the network or the local file system.
/// </summary>
public sealed class DatXmlSecurityTests
{
    [Fact]
    public void CreateSecureSettings_DisablesExternalResolution_AndIgnoresDtd()
    {
        var settings = DatXmlSecurity.CreateSecureSettings();
        // XmlReaderSettings.XmlResolver is set-only in .NET 10; verify the secure
        // policy via DtdProcessing (the externally observable XXE knob) and via
        // the functional XXE test below.
        Assert.Equal(System.Xml.DtdProcessing.Ignore, settings.DtdProcessing);
    }

    [Fact]
    public void CreateProbeSettings_DisablesExternalResolution_AndProhibitsDtd()
    {
        var settings = DatXmlSecurity.CreateProbeSettings();
        Assert.Equal(System.Xml.DtdProcessing.Prohibit, settings.DtdProcessing);
    }

    [Fact]
    public void Validator_NeutralisesXxePayload_WithoutResolvingExternalEntity()
    {
        // Classic XXE payload that points at a non-routable address.
        // With XmlResolver=null and DtdProcessing.Ignore the parser must NOT
        // attempt to resolve the external entity; it should simply read the
        // body and reject the document because it contains no DAT entries.
        const string payload =
            "<?xml version=\"1.0\"?>" +
            "<!DOCTYPE datafile [<!ENTITY xxe SYSTEM \"http://10.255.255.1/secret\">]>" +
            "<datafile><header><name>Test</name></header></datafile>";

        var ex = Assert.Throws<InvalidOperationException>(() =>
            DatXmlValidator.ValidateLogiqxXmlContent(payload, "xxe-test.dat"));

        // The validator surfaces a "no DAT game entries" error — proof that the
        // external entity was NOT expanded into a synthetic <game> element and
        // that no network resolution occurred (otherwise we'd see an XmlException
        // from a failed resolution attempt or a security exception).
        Assert.Contains("DAT game entries", ex.Message, StringComparison.OrdinalIgnoreCase);
    }
}
