namespace RomCleanup.UI.Wpf.Services;

/// <summary>GUI-038: Delegates to static FeatureService.Dat methods.</summary>
public sealed class DatManagementService : IDatManagementService
{
    public DatDiffResult CompareDatFiles(string pathA, string pathB)
        => FeatureService.CompareDatFiles(pathA, pathB);

    public List<string> LoadDatGameNames(string path)
        => FeatureService.LoadDatGameNames(path);

    public string GenerateLogiqxEntry(string gameName, string romName, string crc, string sha1, long size)
        => FeatureService.GenerateLogiqxEntry(gameName, romName, crc, sha1, size);

    public void AppendCustomDatEntry(string datRoot, string xmlEntry)
        => FeatureService.AppendCustomDatEntry(datRoot, xmlEntry);

    public string FormatDatDiffReport(string fileA, string fileB, DatDiffResult diff)
        => FeatureService.FormatDatDiffReport(fileA, fileB, diff);

    public (string Report, int LocalCount, int OldCount) BuildDatAutoUpdateReport(string datRoot)
        => FeatureService.BuildDatAutoUpdateReport(datRoot);

    public string BuildArcadeMergeSplitReport(string datPath)
        => FeatureService.BuildArcadeMergeSplitReport(datPath);

    public string ImportDatFileToRoot(string sourcePath, string datRoot)
        => FeatureService.ImportDatFileToRoot(sourcePath, datRoot);

    public (bool Valid, bool IsPlainFtp, string Report) BuildFtpSourceReport(string input)
        => FeatureService.BuildFtpSourceReport(input);

    public bool IsValidHexHash(string hash, int expectedLength)
        => FeatureService.IsValidHexHash(hash, expectedLength);

    public string BuildCustomDatXmlEntry(string gameName, string romName, string crc32, string sha1)
        => FeatureService.BuildCustomDatXmlEntry(gameName, romName, crc32, sha1);
}
