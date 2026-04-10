namespace Romulus.Core.SetParsing;

/// <summary>
/// Abstracts I/O for set parsers so Core stays testable.
/// The default delegates to System.IO. Infrastructure or tests can override via <see cref="Configure"/>.
/// </summary>
internal static class SetParserIo
{
    private static Func<string, bool> _exists = System.IO.File.Exists;
    private static Func<string, IEnumerable<string>> _readLines = System.IO.File.ReadLines;

    public static bool Exists(string path) => _exists(path);

    public static IEnumerable<string> ReadLines(string path) => _readLines(path);

    /// <summary>
    /// Replace I/O delegates (for Infrastructure wiring or testing).
    /// Pass null to reset to default System.IO implementations.
    /// </summary>
    public static void Configure(
        Func<string, bool>? exists = null,
        Func<string, IEnumerable<string>>? readLines = null)
    {
        _exists = exists ?? System.IO.File.Exists;
        _readLines = readLines ?? System.IO.File.ReadLines;
    }

    /// <summary>
    /// Reset to default System.IO delegates.
    /// </summary>
    public static void ResetDefaults()
    {
        _exists = System.IO.File.Exists;
        _readLines = System.IO.File.ReadLines;
    }
}
