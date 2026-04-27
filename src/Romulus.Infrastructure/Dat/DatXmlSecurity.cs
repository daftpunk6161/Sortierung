using System.Xml;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// Single source of truth for XmlReaderSettings used by every DAT XML parser
/// (validator, repository adapter, header probe). Replaces the previously
/// divergent <c>CreateSecureXmlSettings</c> / <c>CreateFallbackXmlSettings</c>
/// helpers that were duplicated across files (F-DAT-19).
/// </summary>
/// <remarks>
/// All factories disable XmlResolver, ignore comments and whitespace, and use
/// either <see cref="DtdProcessing.Ignore"/> or <see cref="DtdProcessing.Prohibit"/>.
/// Entity expansion is never enabled, so XXE injection is blocked uniformly
/// for every DAT consumer.
/// </remarks>
public static class DatXmlSecurity
{
    /// <summary>
    /// Settings used to read real-world DAT files (No-Intro, Redump, MAME).
    /// Many of those publish a DOCTYPE declaration but no external entities;
    /// <see cref="DtdProcessing.Ignore"/> tolerates the declaration without
    /// resolving anything. <c>XmlResolver = null</c> additionally blocks any
    /// external resource resolution (SSRF / XXE protection).
    /// </summary>
    public static XmlReaderSettings CreateSecureSettings()
        => new()
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

    /// <summary>
    /// Strict settings that reject any document with a DOCTYPE declaration.
    /// Use for telemetry probes where the caller wants to know whether a
    /// DAT contains DTD content; the caller is expected to fall back to
    /// <see cref="CreateSecureSettings"/> on <see cref="XmlException"/>.
    /// </summary>
    public static XmlReaderSettings CreateProbeSettings()
        => new()
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };
}
