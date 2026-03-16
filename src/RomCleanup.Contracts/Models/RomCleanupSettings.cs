using System.Text.Json.Serialization;

namespace RomCleanup.Contracts.Models;

/// <summary>
/// Application settings. Port of settings.json structure from Settings.ps1.
/// Loaded from %APPDATA%\RomCleanupRegionDedupe\settings.json or defaults.json.
/// </summary>
public sealed class RomCleanupSettings
{
    [JsonPropertyName("general")]
    public GeneralSettings General { get; set; } = new();

    [JsonPropertyName("toolPaths")]
    public ToolPathSettings ToolPaths { get; set; } = new();

    [JsonPropertyName("dat")]
    public DatSettings Dat { get; set; } = new();
}

public sealed class GeneralSettings
{
    [JsonPropertyName("logLevel")]
    public string LogLevel { get; set; } = "Info";

    [JsonPropertyName("preferredRegions")]
    public List<string> PreferredRegions { get; set; } = new() { "EU", "US", "JP", "WORLD" };

    [JsonPropertyName("aggressiveJunk")]
    public bool AggressiveJunk { get; set; }

    [JsonPropertyName("aliasEditionKeying")]
    public bool AliasEditionKeying { get; set; }

    [JsonPropertyName("mode")]
    public string Mode { get; set; } = "DryRun";

    [JsonPropertyName("extensions")]
    public string Extensions { get; set; } = ".zip,.7z,.rar,.chd,.iso,.rvz,.cso,.gcz,.wbfs,.nsp,.xci,.3ds,.cia,.nes,.sfc,.smc,.gba,.gb,.gbc,.gen,.sms,.gg,.n64,.z64,.pce,.lnx,.nds,.ngp,.ngc,.a26,.col,.bin,.cue,.gdi,.ccd,.m3u,.pbp";

    [JsonPropertyName("theme")]
    public string Theme { get; set; } = "dark";

    [JsonPropertyName("locale")]
    public string Locale { get; set; } = "de";
}

public sealed class ToolPathSettings
{
    [JsonPropertyName("chdman")]
    public string Chdman { get; set; } = "";

    [JsonPropertyName("7z")]
    public string SevenZip { get; set; } = "";

    [JsonPropertyName("dolphintool")]
    public string DolphinTool { get; set; } = "";
}

public sealed class DatSettings
{
    [JsonPropertyName("useDat")]
    public bool UseDat { get; set; } = true;

    [JsonPropertyName("datRoot")]
    public string DatRoot { get; set; } = "";

    [JsonPropertyName("hashType")]
    public string HashType { get; set; } = "SHA1";

    [JsonPropertyName("datFallback")]
    public bool DatFallback { get; set; } = true;
}
