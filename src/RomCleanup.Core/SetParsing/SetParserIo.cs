namespace RomCleanup.Core.SetParsing;

internal static class SetParserIo
{
    public static bool Exists(string path) => System.IO.File.Exists(path);

    public static IEnumerable<string> ReadLines(string path) => System.IO.File.ReadLines(path);
}
