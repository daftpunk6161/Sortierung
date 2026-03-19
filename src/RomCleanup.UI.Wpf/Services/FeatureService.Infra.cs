using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using RomCleanup.Contracts.Models;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.Infrastructure.Reporting;

namespace RomCleanup.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ CONFIG DIFF ════════════════════════════════════════════════════
    // Port of ConfigMerge.ps1

    public static List<ConfigDiffEntry> GetConfigDiff(Dictionary<string, string> current, Dictionary<string, string> saved)
    {
        var result = new List<ConfigDiffEntry>();
        var allKeys = current.Keys.Union(saved.Keys).Distinct();
        foreach (var key in allKeys)
        {
            current.TryGetValue(key, out var curVal);
            saved.TryGetValue(key, out var savedVal);
            if (curVal != savedVal)
                result.Add(new ConfigDiffEntry(key, savedVal ?? "(fehlt)", curVal ?? "(fehlt)"));
        }
        return result;
    }


    // ═══ LOCALIZATION ═══════════════════════════════════════════════════
    // Port of Localization.ps1

    public static Dictionary<string, string> LoadLocale(string locale)
    {
        var dataDir = ResolveDataDirectory("i18n");
        if (dataDir is null)
            return new Dictionary<string, string>();

        var path = Path.Combine(dataDir, $"{locale}.json");
        if (!File.Exists(path))
            path = Path.Combine(dataDir, "de.json");
        if (!File.Exists(path))
            return new Dictionary<string, string>();

        try
        {
            var json = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(path));
            if (json is null) return new Dictionary<string, string>();
            return json.Where(kv => kv.Value.ValueKind == JsonValueKind.String)
                       .ToDictionary(kv => kv.Key, kv => kv.Value.GetString() ?? "");
        }
        catch { return new Dictionary<string, string>(); }
    }


    /// <summary>Resolve the data/ subdirectory, probing from BaseDirectory upward (max 5 levels).</summary>
    internal static string? ResolveDataDirectory(string? subFolder = null)
    {
        var baseDataDir = RunEnvironmentBuilder.TryResolveDataDir();
        if (string.IsNullOrWhiteSpace(baseDataDir))
            return null;

        if (subFolder is null)
            return baseDataDir;

        var resolvedSubFolder = Path.Combine(baseDataDir, subFolder);
        return Directory.Exists(resolvedSubFolder) ? resolvedSubFolder : null;
    }


    // ═══ PORTABLE MODE CHECK ════════════════════════════════════════════
    // Port of PortableMode.ps1
    // NOTE: The marker file ".portable" is checked relative to AppContext.BaseDirectory,
    // which is the directory containing the executable (e.g. bin/Debug/net10.0-windows/).
    // Place ".portable" next to the .exe, NOT the workspace root.

    public static bool IsPortableMode()
    {
        var marker = Path.Combine(AppContext.BaseDirectory, ".portable");
        return File.Exists(marker);
    }


    // ═══ DOCKER CONFIG ══════════════════════════════════════════════════
    // Port of DockerContainer.ps1

    public static string GenerateDockerfile()
    {
        return """
            FROM mcr.microsoft.com/dotnet/aspnet:10.0
            LABEL maintainer="RomCleanup" description="ROM Cleanup REST API"
            WORKDIR /app
            COPY publish/ .
            VOLUME ["/data/roms", "/data/config"]
            EXPOSE 5000 5001
            ENV ASPNETCORE_URLS=http://+:5000;https://+:5001
            ENV ASPNETCORE_Kestrel__Certificates__Default__Path=/app/certs/cert.pfx
            ENTRYPOINT ["dotnet", "RomCleanup.Api.dll"]
            """;
    }


    public static string GenerateDockerCompose()
    {
        return """
            # HINWEIS: ROM_CLEANUP_API_KEY NICHT in docker-compose.yml hartcodieren!
            # Verwende eine .env-Datei oder Docker Secrets.
            services:
              romcleanup:
                build: .
                ports:
                  - "5000:5000"
                volumes:
                  - ./roms:/data/roms
                  - ./config:/data/config
                environment:
                  - ROM_CLEANUP_API_KEY=${ROM_CLEANUP_API_KEY}
                restart: unless-stopped
                healthcheck:
                  test: ["CMD", "curl", "-f", "http://localhost:5000/health"]
                  interval: 30s
                  retries: 3
            """;
    }


    // ═══ WINDOWS CONTEXT MENU ═══════════════════════════════════════════
    // Port of WindowsContextMenu.ps1

    public static string GetContextMenuRegistryScript()
    {
        var exePath = Environment.ProcessPath ?? "RomCleanup.CLI.exe";
        // .reg format requires backslashes doubled and paths with spaces quoted
        var escapedPath = "\\\"" + exePath.Replace("\\", "\\\\") + "\\\"";
        return $"""
            Windows Registry Editor Version 5.00

            [HKEY_CURRENT_USER\Software\Classes\Directory\shell\RomCleanup_DryRun]
            @="ROM Cleanup – DryRun Scan"

            [HKEY_CURRENT_USER\Software\Classes\Directory\shell\RomCleanup_DryRun\command]
            @="{escapedPath} --roots \\\"%V\\\" --mode DryRun"

            [HKEY_CURRENT_USER\Software\Classes\Directory\shell\RomCleanup_Move]
            @="ROM Cleanup – Move Sort"

            [HKEY_CURRENT_USER\Software\Classes\Directory\shell\RomCleanup_Move\command]
            @="{escapedPath} --roots \\\"%V\\\" --mode Move"
            """;
    }


    // ═══ ACCESSIBILITY ══════════════════════════════════════════════════
    // Port of Accessibility.ps1

    public static bool IsHighContrastActive()
    {
        try
        {
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"Control Panel\Accessibility\HighContrast");
            var flags = key?.GetValue("Flags") as string;
            return flags is not null && int.TryParse(flags, out var f) && (f & 1) != 0;
        }
        catch { return false; }
    }


    // ═══ PLUGIN MARKETPLACE ═════════════════════════════════════════════

    /// <summary>Build a report of installed plugins.</summary>
    public static string BuildPluginMarketplaceReport(string pluginDir)
    {
        if (!Directory.Exists(pluginDir))
            Directory.CreateDirectory(pluginDir);

        var manifests = Directory.GetFiles(pluginDir, "*.json", SearchOption.AllDirectories);
        var dlls = Directory.GetFiles(pluginDir, "*.dll", SearchOption.AllDirectories);

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("Plugin-Manager (Coming Soon)\n");
        sb.AppendLine("  ℹ Das Plugin-System ist in Planung und noch nicht funktionsfähig.");
        sb.AppendLine($"  Plugin-Verzeichnis: {pluginDir}\n");
        sb.AppendLine($"  Manifeste:   {manifests.Length}");
        sb.AppendLine($"  DLLs:        {dlls.Length}\n");

        if (manifests.Length == 0 && dlls.Length == 0)
        {
            sb.AppendLine("  Keine Plugins installiert.\n");
            sb.AppendLine("  Plugin-Struktur:");
            sb.AppendLine("    plugins/");
            sb.AppendLine("      mein-plugin/");
            sb.AppendLine("        manifest.json");
            sb.AppendLine("        MeinPlugin.dll\n");
            sb.AppendLine("  Manifest-Format:");
            sb.AppendLine("    {");
            sb.AppendLine("      \"name\": \"Mein Plugin\",");
            sb.AppendLine("      \"version\": \"1.0.0\",");
            sb.AppendLine("      \"type\": \"console|format|report\"");
            sb.AppendLine("    }");
        }
        else
        {
            foreach (var manifest in manifests)
            {
                try
                {
                    var json = File.ReadAllText(manifest);
                    using var doc = System.Text.Json.JsonDocument.Parse(json);
                    var name = doc.RootElement.TryGetProperty("name", out var np) ? np.GetString() : Path.GetFileName(manifest);
                    var ver = doc.RootElement.TryGetProperty("version", out var vp) ? vp.GetString() : "?";
                    var type = doc.RootElement.TryGetProperty("type", out var tp) ? tp.GetString() : "?";
                    sb.AppendLine($"  [{type}] {name} v{ver}");
                    sb.AppendLine($"         {Path.GetDirectoryName(manifest)}");
                }
                catch
                {
                    sb.AppendLine($"  [?] {Path.GetFileName(manifest)} (manifest ungültig)");
                }
            }
            if (dlls.Length > 0)
            {
                sb.AppendLine($"\n  DLLs:");
                foreach (var dll in dlls)
                    sb.AppendLine($"    {Path.GetFileName(dll)}");
            }
        }
        return sb.ToString();
    }


    // ═══ MOBILE WEB UI ══════════════════════════════════════════════════

    /// <summary>Try to find the API project path.</summary>
    public static string? FindApiProjectPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "src", "RomCleanup.Api", "RomCleanup.Api.csproj"),
            Path.Combine(Directory.GetCurrentDirectory(), "src", "RomCleanup.Api", "RomCleanup.Api.csproj")
        };
        return candidates.Select(Path.GetFullPath).FirstOrDefault(File.Exists);
    }


    // ═══ RULE PACK SHARING ══════════════════════════════════════════════

    /// <summary>Export rules.json to a user-chosen path.</summary>
    public static bool ExportRulePack(string rulesPath, string savePath)
    {
        if (!File.Exists(rulesPath)) return false;
        File.Copy(rulesPath, savePath, overwrite: true);
        return true;
    }


    /// <summary>Import rules.json from an external file (validates JSON first).</summary>
    public static void ImportRulePack(string importPath, string rulesPath)
    {
        var json = File.ReadAllText(importPath);
        System.Text.Json.JsonDocument.Parse(json).Dispose(); // validate
        var dir = Path.GetDirectoryName(rulesPath);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.Copy(importPath, rulesPath, overwrite: true);
    }

}
