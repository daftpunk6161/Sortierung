using Romulus.Contracts.Ports;

namespace Romulus.Core.SetParsing;

/// <summary>
/// Abstracts I/O for set parsers so Core stays testable.
/// The runtime adapter must be provided by Infrastructure via <see cref="Use"/>.
/// Tests can override delegates via <see cref="Configure"/>.
/// </summary>
public static class SetParserIo
{
    private static ISetParserIo _default = CreateDefaultAdapter();
    private static Func<string, bool>? _exists;
    private static Func<string, IEnumerable<string>>? _readLines;

    public static void Use(ISetParserIo io)
    {
        ArgumentNullException.ThrowIfNull(io);
        _default = io;
    }

    public static bool Exists(string path) => (_exists ?? _default.Exists)(path);

    public static IEnumerable<string> ReadLines(string path) => (_readLines ?? _default.ReadLines)(path);

    /// <summary>
    /// Replace I/O delegates (for Infrastructure wiring or testing).
    /// Pass null to use the currently configured infrastructure adapter.
    /// </summary>
    public static void Configure(
        Func<string, bool>? exists = null,
        Func<string, IEnumerable<string>>? readLines = null)
    {
        _exists = exists;
        _readLines = readLines;
    }

    /// <summary>
    /// Reset per-context delegate overrides.
    /// </summary>
    public static void ResetDefaults()
    {
        _exists = null;
        _readLines = null;
    }

    private sealed class UnconfiguredSetParserIo : ISetParserIo
    {
        public bool Exists(string path)
            => throw new InvalidOperationException("Set parser I/O is not configured. Register ISetParserIo from Infrastructure before invoking parser logic.");

        public IEnumerable<string> ReadLines(string path)
            => throw new InvalidOperationException("Set parser I/O is not configured. Register ISetParserIo from Infrastructure before invoking parser logic.");
    }

    private static ISetParserIo CreateDefaultAdapter()
    {
        try
        {
            var adapterType = Type.GetType("Romulus.Infrastructure.IO.SetParserIo, Romulus.Infrastructure", throwOnError: false);
            if (adapterType is not null && Activator.CreateInstance(adapterType) is ISetParserIo adapter)
                return adapter;
        }
        catch
        {
            // Fall back to explicit configuration path.
        }

        return new UnconfiguredSetParserIo();
    }
}
