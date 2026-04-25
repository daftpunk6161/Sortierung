using System.Xml;

namespace Romulus.Infrastructure.Dat;

public static class DatXmlValidator
{
    public static void ValidateLogiqxXmlFile(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        using var stream = File.OpenRead(path);
        ValidateLogiqxXml(stream, path);
    }

    public static void ValidateLogiqxXmlContent(string xml, string displayName = "DAT XML")
    {
        ArgumentNullException.ThrowIfNull(xml);

        using var stream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
        ValidateLogiqxXml(stream, displayName);
    }

    private static void ValidateLogiqxXml(Stream stream, string displayName)
    {
        try
        {
            using var reader = XmlReader.Create(stream, CreateSecureXmlSettings());
            var sawRoot = false;
            var sawEntry = false;

            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (!sawRoot)
                {
                    if (!string.Equals(reader.LocalName, "datafile", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException($"{displayName} root element must be datafile.");

                    sawRoot = true;
                    continue;
                }

                if (IsDatEntryElement(reader.LocalName))
                    sawEntry = true;
            }

            if (!sawRoot)
                throw new InvalidOperationException($"{displayName} is empty.");

            if (!sawEntry)
                throw new InvalidOperationException($"{displayName} contains no DAT game entries.");
        }
        catch (XmlException ex)
        {
            throw new InvalidOperationException($"{displayName} is not valid DAT XML: {ex.Message}", ex);
        }
    }

    private static bool IsDatEntryElement(string localName)
        => string.Equals(localName, "game", StringComparison.OrdinalIgnoreCase)
           || string.Equals(localName, "machine", StringComparison.OrdinalIgnoreCase)
           || string.Equals(localName, "software", StringComparison.OrdinalIgnoreCase);

    private static XmlReaderSettings CreateSecureXmlSettings()
        => new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };
}
