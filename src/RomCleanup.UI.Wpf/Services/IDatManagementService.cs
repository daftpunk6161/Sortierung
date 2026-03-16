namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-038: DAT file comparison, auto-update, custom DAT editing, arcade merge/split.</summary>
public interface IDatManagementService
{
    DatDiffResult CompareDatFiles(string pathA, string pathB);
    List<string> LoadDatGameNames(string path);
    string GenerateLogiqxEntry(string gameName, string romName, string crc, string sha1, long size);
    void AppendCustomDatEntry(string datRoot, string xmlEntry);
    string FormatDatDiffReport(string fileA, string fileB, DatDiffResult diff);
    (string Report, int LocalCount, int OldCount) BuildDatAutoUpdateReport(string datRoot);
    string BuildArcadeMergeSplitReport(string datPath);
    string ImportDatFileToRoot(string sourcePath, string datRoot);
    (bool Valid, bool IsPlainFtp, string Report) BuildFtpSourceReport(string input);
    bool IsValidHexHash(string hash, int expectedLength);
    string BuildCustomDatXmlEntry(string gameName, string romName, string crc32, string sha1);
}
