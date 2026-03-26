using System.Runtime.CompilerServices;

namespace RomCleanup.Tests;

/// <summary>
/// Ensures ROMCLEANUP_DATA_DIR is set before any test runs.
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
        // Skip if already set (e.g. by CI or manual override)
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ROMCLEANUP_DATA_DIR")))
            return;

        var dataDir = ResolveDataDirFromSource();
        if (dataDir is not null)
            Environment.SetEnvironmentVariable("ROMCLEANUP_DATA_DIR", dataDir);
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
