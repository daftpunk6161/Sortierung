using System.Runtime.CompilerServices;
using Romulus.Core.GameKeys;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Tests;

/// <summary>
/// Ensures ROMULUS_DATA_DIR is set before any test runs.
/// When tests execute from an isolated output directory (e.g. dotnet test --output,
/// VS Code C# Dev Kit coverage runner), AppContext.BaseDirectory and
/// Directory.GetCurrentDirectory() may point to a temp folder outside the repo.
/// This initializer resolves the data directory from the compile-time source path.
/// </summary>
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Keep manual/CI overrides only when they point to a valid data directory.
        var configuredDataDir = Environment.GetEnvironmentVariable("ROMULUS_DATA_DIR");
        if (string.IsNullOrWhiteSpace(configuredDataDir) || !IsValidDataDir(configuredDataDir))
        {
            var dataDir = ResolveDataDirFromSource();
            if (dataDir is not null)
                Environment.SetEnvironmentVariable("ROMULUS_DATA_DIR", dataDir);
        }

        // Load rules.json-backed normalization data and register it explicitly.
        // This avoids relying on module initializer load-order across different test runners.
        var patterns = GameKeyNormalizationProfile.TagPatterns;
        var aliases = GameKeyNormalizationProfile.AlwaysAliasMap;
        if (patterns is { Count: > 0 })
            GameKeyNormalizer.RegisterDefaultPatterns(patterns, aliases);
    }

    private static bool IsValidDataDir(string path)
    {
        try
        {
            var fullPath = Path.GetFullPath(path);
            return Directory.Exists(fullPath) &&
                   File.Exists(Path.Combine(fullPath, "consoles.json")) &&
                   File.Exists(Path.Combine(fullPath, "rules.json"));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static string? ResolveDataDirFromSource([CallerFilePath] string? callerPath = null)
    {
        // Walk up from compile-time source path to find data/consoles.json
        if (callerPath is null)
            return null;

        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir, "data");
            if (Directory.Exists(candidate) &&
                File.Exists(Path.Combine(candidate, "consoles.json")))
                return candidate;
            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
