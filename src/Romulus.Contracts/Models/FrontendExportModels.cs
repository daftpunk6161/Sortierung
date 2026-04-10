namespace Romulus.Contracts.Models;

public static class FrontendExportTargets
{
    public const string Csv = "csv";
    public const string Json = "json";
    public const string Excel = "excel";
    public const string RetroArch = "retroarch";
    public const string M3u = "m3u";
    public const string LaunchBox = "launchbox";
    public const string EmulationStation = "emulationstation";
    public const string Playnite = "playnite";
    public const string MiSTer = "mister";
    public const string AnaloguePocket = "analoguepocket";
    public const string OnionOs = "onionos";

    public static readonly IReadOnlySet<string> All = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        Csv,
        Json,
        Excel,
        RetroArch,
        M3u,
        LaunchBox,
        EmulationStation,
        Playnite,
        MiSTer,
        AnaloguePocket,
        OnionOs
    };
}

/// <summary>
/// Channel-neutral export row derived once from candidates and reused by frontend-specific exporters.
/// </summary>
public sealed record ExportableGame(
    string SourcePath,
    string FileName,
    string DisplayName,
    string GameKey,
    string ConsoleKey,
    string ConsoleLabel,
    string Region,
    string Extension,
    long SizeBytes,
    bool DatVerified);

/// <summary>
/// Frontend export input shared by CLI, API, and WPF.
/// </summary>
public sealed record FrontendExportRequest(
    string Frontend,
    string OutputPath,
    string CollectionName,
    IReadOnlyList<string> Roots,
    IReadOnlyList<string> Extensions);

public sealed record FrontendExportArtifact(string Path, string Label, int ItemCount);

public sealed record FrontendExportResult(
    string Frontend,
    string Source,
    int GameCount,
    IReadOnlyList<FrontendExportArtifact> Artifacts);
