using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Core.Conversion;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Configuration;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Logging;
using Romulus.Infrastructure.Paths;
using Romulus.Infrastructure.Tools;
using IO = Romulus.Infrastructure.IO;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Builds RunEnvironment from RunOptions + settings.
/// Shared setup for CLI, API, WPF.
/// ADR-008 §C-04.
/// </summary>
public sealed class RunEnvironmentBuilder
{
    private static readonly Regex RxValidRuntimeConsoleKey = new(@"^[A-Z0-9_-]+$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static readonly Regex RxTrailingDescriptorSuffix = new(@"\s*[\(\[][^\)\]]*[\)\]]\s*$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    public enum DatRootResolutionSource
    {
        None = 0,
        RunOption = 1,
        Settings = 2,
        CatalogState = 3,
        ConventionalPath = 4
    }

    public readonly record struct DatRootResolution(
        string? Path,
        DatRootResolutionSource Source);

    /// <summary>
    /// Try to resolve the data/ directory without throwing.
    /// Returns null when no candidate directory exists.
    /// Walks up the directory tree from AppContext.BaseDirectory and
    /// Directory.GetCurrentDirectory() looking for the application data folder
    /// (identified by data/consoles.json marker file).
    /// Honors ROMULUS_DATA_DIR environment variable as highest-priority override.
    /// </summary>
    public static string? TryResolveDataDir()
    {
        // Highest priority: explicit environment override (useful for CI / isolated test runners)
        var envOverride = Environment.GetEnvironmentVariable("ROMULUS_DATA_DIR");
        if (!string.IsNullOrWhiteSpace(envOverride))
        {
            var full = Path.GetFullPath(envOverride);
            if (Directory.Exists(full))
                return full;
        }

        // Walk up parent chain from each search root
        var searchRoots = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() }
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase);

        foreach (var root in searchRoots)
        {
            var current = new DirectoryInfo(root);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, "data");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "consoles.json")))
                    return candidate;
                current = current.Parent;
            }
        }

        // Fallback: locate via Assembly.Location (works when output dir differs from source)
        var asmLocation = typeof(RunEnvironmentBuilder).Assembly.Location;
        if (!string.IsNullOrWhiteSpace(asmLocation))
        {
            var current = new DirectoryInfo(Path.GetDirectoryName(asmLocation)!);
            while (current is not null)
            {
                var candidate = Path.Combine(current.FullName, "data");
                if (Directory.Exists(candidate) &&
                    File.Exists(Path.Combine(candidate, "consoles.json")))
                    return candidate;
                current = current.Parent;
            }
        }

        return null;
    }

    /// <summary>
    /// Resolve the data/ directory by searching multiple candidate locations.
    /// Priority: next to executable → workspace root → current working directory.
    /// </summary>
    public static string ResolveDataDir()
    {
        var resolved = TryResolveDataDir();
        if (resolved is not null)
            return resolved;

        throw new DirectoryNotFoundException(
            "Could not locate required data directory. Walked parent chain from: " +
            $"AppContext.BaseDirectory={Path.GetFullPath(AppContext.BaseDirectory)}, " +
            $"CurrentDirectory={Path.GetFullPath(Directory.GetCurrentDirectory())}");
    }

    /// <summary>Load settings from defaults.json → user settings.</summary>
    public static RomulusSettings LoadSettings(string dataDir)
    {
        var defaultsPath = Path.Combine(dataDir, "defaults.json");
        return SettingsLoader.Load(File.Exists(defaultsPath) ? defaultsPath : null);
    }

    /// <summary>
    /// Load settings with an optional explicit override path (TASK-161).
    /// When settingsOverridePath is provided and exists, it is used instead of %APPDATA% user settings.
    /// When settingsOverridePath is provided but does not exist, only defaults are loaded (no %APPDATA% fallback).
    /// This allows the API to decouple from per-user %APPDATA% settings on server deployments.
    /// </summary>
    public static RomulusSettings LoadSettings(string dataDir, string? settingsOverridePath)
    {
        if (settingsOverridePath is null)
            return LoadSettings(dataDir);

        var defaultsPath = Path.Combine(dataDir, "defaults.json");
        var defaults = File.Exists(defaultsPath) ? defaultsPath : null;

        // When an explicit override is given, skip %APPDATA% entirely
        if (File.Exists(settingsOverridePath))
            return SettingsLoader.LoadWithExplicitUserPath(defaults, settingsOverridePath);

        // Override path given but file doesn't exist → defaults-only (no %APPDATA%)
        return SettingsLoader.LoadDefaultsOnly(defaults);
    }

    public static DatRootResolution ResolveEffectiveDatRoot(
        RunOptions runOptions,
        RomulusSettings settings,
        string dataDir)
    {
        ArgumentNullException.ThrowIfNull(runOptions);
        ArgumentNullException.ThrowIfNull(settings);

        return ResolveEffectiveDatRoot(runOptions.DatRoot, settings.Dat.DatRoot, dataDir);
    }

    public static DatRootResolution ResolveEffectiveDatRoot(
        string? runOptionDatRoot,
        string? settingsDatRoot,
        string dataDir)
    {
        string? statePath = null;
        try
        {
            statePath = DatCatalogStateService.GetDefaultStatePath();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            statePath = null;
        }

        return ResolveEffectiveDatRoot(runOptionDatRoot, settingsDatRoot, dataDir, statePath);
    }

    internal static DatRootResolution ResolveEffectiveDatRoot(
        string? runOptionDatRoot,
        string? settingsDatRoot,
        string dataDir,
        string? statePath)
    {
        if (TryNormalizeExistingDirectory(runOptionDatRoot, out var explicitDatRoot))
            return new DatRootResolution(explicitDatRoot, DatRootResolutionSource.RunOption);

        string? configuredDatRootWithoutDatFiles = null;
        if (TryNormalizeExistingDirectory(settingsDatRoot, out var configuredDatRoot))
        {
            if (DirectoryHasDatCandidates(configuredDatRoot))
                return new DatRootResolution(configuredDatRoot, DatRootResolutionSource.Settings);

            configuredDatRootWithoutDatFiles = configuredDatRoot;
        }

        if (TryAutoDetectDatRoot(dataDir, statePath, out var autoDetectedDatRoot, out var autoSource))
            return new DatRootResolution(autoDetectedDatRoot, autoSource);

        if (configuredDatRootWithoutDatFiles is not null)
            return new DatRootResolution(configuredDatRootWithoutDatFiles, DatRootResolutionSource.Settings);

        return new DatRootResolution(null, DatRootResolutionSource.None);
    }

    private static bool TryNormalizeExistingDirectory(string? rawPath, out string normalizedPath)
    {
        normalizedPath = string.Empty;
        if (string.IsNullOrWhiteSpace(rawPath))
            return false;

        try
        {
            var full = Path.GetFullPath(rawPath.Trim());
            if (!Directory.Exists(full))
                return false;

            normalizedPath = full;
            return true;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }
    }

    private static bool TryAutoDetectDatRoot(
        string dataDir,
        string? statePath,
        out string datRoot,
        out DatRootResolutionSource source)
    {
        datRoot = string.Empty;
        source = DatRootResolutionSource.None;

        foreach (var candidate in EnumerateStateBackedDatRootCandidates(statePath))
        {
            if (!Directory.Exists(candidate))
                continue;

            datRoot = candidate;
            source = DatRootResolutionSource.CatalogState;
            return true;
        }

        foreach (var candidate in EnumerateConventionalDatRootCandidates(dataDir))
        {
            if (!DirectoryHasDatCandidates(candidate))
                continue;

            datRoot = candidate;
            source = DatRootResolutionSource.ConventionalPath;
            return true;
        }

        return false;
    }

    private static bool DirectoryHasDatCandidates(string directoryPath)
    {
        try
        {
            return DatCatalogStateService.EnumerateLocalDatFilesSafe(directoryPath).Count > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static IEnumerable<string> EnumerateStateBackedDatRootCandidates(string? statePath)
    {
        if (string.IsNullOrWhiteSpace(statePath))
            yield break;

        DatCatalogState state;
        try
        {
            state = DatCatalogStateService.LoadState(statePath);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            yield break;
        }

        if (state.Entries.Count == 0)
            yield break;

        var directories = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var localPath in state.Entries.Values
                     .Select(static info => info.LocalPath)
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Order(StringComparer.OrdinalIgnoreCase))
        {
            string fullPath;
            try
            {
                fullPath = Path.GetFullPath(localPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                continue;
            }

            if (!File.Exists(fullPath))
                continue;

            if (!fullPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                && !fullPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var parentDirectory = Path.GetDirectoryName(fullPath);
            if (string.IsNullOrWhiteSpace(parentDirectory) || !Directory.Exists(parentDirectory))
                continue;

            directories[parentDirectory] = directories.TryGetValue(parentDirectory, out var count)
                ? count + 1
                : 1;
        }

        if (directories.Count == 0)
            yield break;

        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var commonDirectory = TryResolveCommonDirectory(directories.Keys);
        if (commonDirectory is not null && yielded.Add(commonDirectory))
            yield return commonDirectory;

        foreach (var directory in directories
                     .OrderByDescending(static pair => pair.Value)
                     .ThenBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                     .Select(static pair => pair.Key))
        {
            if (yielded.Add(directory))
                yield return directory;
        }
    }

    private static string? TryResolveCommonDirectory(IEnumerable<string> paths)
    {
        var orderedPaths = paths
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (orderedPaths.Length < 2)
            return null;

        var common = orderedPaths[0].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        for (var index = 1; index < orderedPaths.Length; index++)
        {
            var current = orderedPaths[index].TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            while (!string.IsNullOrWhiteSpace(common) && !IsSameOrNestedPath(current, common))
                common = Path.GetDirectoryName(common);

            if (string.IsNullOrWhiteSpace(common))
                return null;
        }

        if (string.IsNullOrWhiteSpace(common))
            return null;

        var normalized = common.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetPathRoot(normalized)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (!string.IsNullOrWhiteSpace(root)
            && string.Equals(normalized, root, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return Directory.Exists(normalized) ? normalized : null;
    }

    private static bool IsSameOrNestedPath(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(rootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        if (string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase))
            return true;

        return candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static IEnumerable<string> EnumerateConventionalDatRootCandidates(string dataDir)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        void YieldCandidate(string? rawPath)
        {
            if (string.IsNullOrWhiteSpace(rawPath))
                return;

            try
            {
                var fullPath = Path.GetFullPath(rawPath);
                if (Directory.Exists(fullPath) && yielded.Add(fullPath))
                    ordered.Add(fullPath);
            }
            catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
            {
                // ignore invalid candidate
            }
        }

        if (!string.IsNullOrWhiteSpace(dataDir))
        {
            YieldCandidate(Path.Combine(dataDir, "dats"));
            var dataParent = Path.GetDirectoryName(Path.GetFullPath(dataDir));
            if (!string.IsNullOrWhiteSpace(dataParent))
                YieldCandidate(Path.Combine(dataParent, "dats"));
        }

        try { YieldCandidate(AppStoragePathResolver.ResolveRoamingPath("dats")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // ignore inaccessible application storage candidates
        }

        try { YieldCandidate(AppStoragePathResolver.ResolveLocalPath("dats")); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            // ignore inaccessible application storage candidates
        }

        foreach (var candidate in ordered)
            yield return candidate;
    }

    /// <summary>
    /// Build complete environment for a run.
    /// </summary>
    public static RunEnvironment Build(RunOptions runOptions, RomulusSettings settings,
        string dataDir, Action<string>? onWarning = null,
        string? collectionDatabasePath = null,
        string? datCatalogStatePath = null)
    {
        var classificationIo = new IO.ClassificationIo();

        var fs = new FileSystemAdapter();
        var audit = new AuditCsvStore(fs, onWarning ?? (_ => { }),
            AuditSecurityPaths.GetDefaultSigningKeyPath());

        // ToolRunner
        var toolHashesPath = Path.Combine(dataDir, "tool-hashes.json");
        var toolRunner = new ToolRunnerAdapter(File.Exists(toolHashesPath) ? toolHashesPath : null);
        var consolesJsonPath = Path.Combine(dataDir, "consoles.json");

        // FormatConverter
        FormatConverterAdapter? converter = null;
        if (runOptions.ConvertFormat != null)
        {
            IConversionRegistry? conversionRegistry = null;
            IConversionPlanner? conversionPlanner = null;
            IConversionExecutor? conversionExecutor = null;

            try
            {
                var conversionRegistryPath = Path.Combine(dataDir, "conversion-registry.json");
                if (File.Exists(conversionRegistryPath) && File.Exists(consolesJsonPath))
                {
                    conversionRegistry = new ConversionRegistryLoader(conversionRegistryPath, consolesJsonPath);
                    var invokers = new IToolInvoker[]
                    {
                        new ChdmanInvoker(toolRunner),
                        new DolphinToolInvoker(toolRunner),
                        new SevenZipInvoker(toolRunner),
                        new PsxtractInvoker(toolRunner),
                        new CisoInvoker(toolRunner),
                        new EcmInvoker(toolRunner),
                        new NkitInvoker(toolRunner)
                    };
                    conversionExecutor = new ConversionExecutor(invokers, runOptions.ApproveConversionReview);

                    conversionPlanner = new ConversionPlanner(
                        conversionRegistry,
                        toolRunner.FindTool,
                        path => new FileInfo(path).Length,
                        PbpEncryptionDetector.IsEncrypted,
                        ToolInvokerSupport.TryResolvePs2CdFromSystemCnf);
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or InvalidOperationException)
            {
                onWarning?.Invoke($"[Warning] Conversion registry loading failed, fallback to legacy mapping: {ex.Message}");
            }

            converter = new FormatConverterAdapter(toolRunner, null, conversionRegistry, conversionPlanner, conversionExecutor, runOptions.ApproveConversionReview);
        }

        // ArchiveHashService: enables DAT matching and console detection for ROMs inside ZIP/7z archives
        // Created early so ConsoleDetector can use it for 7z inner-extension detection.
        var archiveHashService = new ArchiveHashService(toolRunner);

        // ConsoleDetector
        ConsoleDetector? consoleDetector = null;
        var discHeaderDetector = new DiscHeaderDetector(classificationIo: classificationIo);
        var cartridgeHeaderDetector = new CartridgeHeaderDetector(classificationIo: classificationIo);
        if (File.Exists(consolesJsonPath))
        {
            var consolesJson = File.ReadAllText(consolesJsonPath);
            consoleDetector = ConsoleDetector.LoadFromJson(
                consolesJson,
                discHeaderDetector,
                archiveEntryProvider: archiveHashService.GetArchiveEntryNames,
            cartridgeHeaderDetector: cartridgeHeaderDetector,
            classificationIo: classificationIo);
        }
        else if (runOptions.SortConsole || runOptions.EnableDat)
        {
            onWarning?.Invoke("[Warning] consoles.json not found, --SortConsole/--EnableDat require it");
        }

        // DAT
        DatIndex? datIndex = null;
        FileHashService? hashService = null;
        IHeaderlessHasher? headerlessHasher = null;
        ICollectionIndex? collectionIndex = null;
        var knownBiosHashes = LoadKnownBiosHashes(dataDir, onWarning);
        var datRootResolution = string.IsNullOrWhiteSpace(datCatalogStatePath)
            ? ResolveEffectiveDatRoot(runOptions, settings, dataDir)
            : ResolveEffectiveDatRoot(runOptions.DatRoot, settings.Dat.DatRoot, dataDir, datCatalogStatePath);
        var effectiveDatRoot = datRootResolution.Path;
        Dictionary<string, string>? datConsoleMap = null;

        try
        {
            collectionIndex = new LiteDbCollectionIndex(
                CollectionIndexPaths.ResolveDatabasePath(collectionDatabasePath),
                onWarning);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            onWarning?.Invoke($"[CollectionIndex] Primary database unavailable: {ex.Message}");

            try
            {
                var fallbackDirectory = Path.Combine(Path.GetTempPath(), AppIdentity.AppFolderName, "collection-index-fallback");
                Directory.CreateDirectory(fallbackDirectory);
                var fallbackDatabasePath = Path.Combine(fallbackDirectory, $"collection-{Guid.NewGuid():N}.db");
                collectionIndex = new LiteDbCollectionIndex(fallbackDatabasePath, onWarning);
                onWarning?.Invoke($"[CollectionIndex] Using fallback database: {fallbackDatabasePath}");
            }
            catch (Exception fallbackEx) when (fallbackEx is IOException or UnauthorizedAccessException or InvalidOperationException)
            {
                onWarning?.Invoke($"[CollectionIndex] Disabled for this run: {fallbackEx.Message}");
            }
        }

        if (runOptions.EnableDat)
        {
            // Keep hash-service availability deterministic for DAT-enabled runs,
            // even when DatRoot is missing/unavailable.
            hashService = collectionIndex is not null
                ? new FileHashService(collectionIndex: collectionIndex)
                : new FileHashService(persistentCachePath: FileHashService.ResolveDefaultPersistentCachePath());
            headerlessHasher = new HeaderlessHasher();
        }

        if (runOptions.EnableDat
            && !string.IsNullOrWhiteSpace(effectiveDatRoot)
            && (datRootResolution.Source is DatRootResolutionSource.CatalogState or DatRootResolutionSource.ConventionalPath))
        {
            var sourceLabel = datRootResolution.Source == DatRootResolutionSource.CatalogState
                ? "dat-catalog-state"
                : "konventioneller Pfad";
            onWarning?.Invoke($"[DAT] DatRoot automatisch erkannt ({sourceLabel}): '{effectiveDatRoot}'");
        }

        if (runOptions.EnableDat && !string.IsNullOrWhiteSpace(effectiveDatRoot) && Directory.Exists(effectiveDatRoot))
        {
            var datRepo = new DatRepositoryAdapter(toolRunner: toolRunner);
            datConsoleMap = BuildConsoleMap(dataDir, effectiveDatRoot, out var supplementalDats);
            NormalizeRuntimeDatMappings(
                datConsoleMap,
                supplementalDats,
                consoleDetector,
                effectiveDatRoot,
                onWarning);

            // Bridge unmapped consoles via datSources aliases (e.g. ARCADE → MAME DAT)
            if (consoleDetector is not null)
                BridgeDatSourceAliases(datConsoleMap, consoleDetector, dataDir);

            // Diagnostic: show what BuildConsoleMap found.
            var datFileCount = fs.TestPath(effectiveDatRoot, "Container")
                ? fs.GetFilesSafe(effectiveDatRoot, [".dat", ".xml"]).Count
                : 0;
            var supplementalCount = supplementalDats.Values.Sum(l => l.Count);
            onWarning?.Invoke($"[DAT] DatRoot '{effectiveDatRoot}': {datFileCount} DAT/XML-Dateien gefunden, {datConsoleMap.Count} Konsolen gemappt, {supplementalCount} ergaenzende DATs");

            if (datConsoleMap.Count > 0)
            {
                var hashType = runOptions.HashType ?? settings.Dat.HashType;
                datIndex = datRepo.GetDatIndex(effectiveDatRoot, datConsoleMap, hashType);

                // Load supplemental DATs (e.g. FBNeo DATs for consoles already mapped via No-Intro)
                foreach (var (consoleKey, extraPaths) in supplementalDats)
                {
                    foreach (var extraPath in extraPaths)
                    {
                        var supplementalMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            [consoleKey] = extraPath
                        };
                        var extraIndex = datRepo.GetDatIndex(effectiveDatRoot, supplementalMap, hashType);
                        // Merge entries into main index.
                        MergeDatIndices(datIndex, extraIndex);
                    }
                }

                onWarning?.Invoke($"[DAT] Loaded {datIndex.TotalEntries} hashes for {datIndex.ConsoleCount} consoles");
            }
            else
            {
                onWarning?.Invoke("[Warning] No DAT files mapped — check dat-catalog.json and DatRoot. Redump-DATs erfordern manuellen Download von redump.org (Login). Non-Redump-/No-Intro-Packs muessen unter DatRoot liegen.");
            }
        }
        else if (runOptions.EnableDat)
        {
            onWarning?.Invoke("[Warning] DAT enabled but DatRoot not set or not found (auto-detection found no DAT root)");
        }

        var enrichmentFingerprint = ComputeEnrichmentFingerprint(
            runOptions,
            dataDir,
            effectiveDatRoot,
            datConsoleMap,
            knownBiosHashes);

        return new RunEnvironment(
            fs,
            audit,
            consoleDetector,
            hashService,
            headerlessHasher,
            converter,
            datIndex,
            archiveHashService,
            knownBiosHashes,
            collectionIndex,
            enrichmentFingerprint);
    }

    private static IReadOnlySet<string>? LoadKnownBiosHashes(string dataDir, Action<string>? onWarning)
    {
        var catalogPath = Path.Combine(dataDir, "bios-hashes.json");
        if (!File.Exists(catalogPath))
            return null;

        try
        {
            var json = File.ReadAllText(catalogPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array)
                return null;

            var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.String)
                    continue;

                var value = item.GetString();
                if (!string.IsNullOrWhiteSpace(value))
                    hashes.Add(value.Trim());
            }

            return hashes.Count > 0 ? hashes : null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        {
            onWarning?.Invoke($"[Warning] Could not load bios-hashes.json: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Build a console→DAT-filename map from dat-catalog.json.
    /// Falls back to scanning datRoot for .dat files with console key as stem.
    /// </summary>
    public static Dictionary<string, string> BuildConsoleMap(string dataDir, string datRoot)
        => BuildConsoleMap(dataDir, datRoot, out _);

    public static Dictionary<string, string> BuildConsoleMap(string dataDir, string datRoot,
        out Dictionary<string, List<string>> supplementalDats)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        supplementalDats = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var runtimeConsoleDetector = TryLoadConsoleDetector(dataDir);

        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        List<DatCatalogEntry>? entries = null;
        if (File.Exists(catalogPath))
        {
            try
            {
                var json = File.ReadAllText(catalogPath);
                entries = JsonSerializer.Deserialize<List<DatCatalogEntry>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (entries != null)
                {
                    // Cache datRoot DAT/XML files (recursive) for exact and PackMatch lookup.
                    string[]? datRootFiles = null;

                    foreach (var entry in entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.ConsoleKey))
                            continue;

                        // Already mapped by a previous entry (e.g. same ConsoleKey from different group):
                        // try to add as supplemental DAT so both No-Intro + FBNeo hashes are indexed.
                        var alreadyMapped = map.ContainsKey(entry.ConsoleKey);

                        var candidates = new[]
                        {
                            Path.Combine(datRoot, entry.Id + ".dat"),
                            Path.Combine(datRoot, entry.Id + ".xml"),
                            Path.Combine(datRoot, entry.System + ".dat"),
                            Path.Combine(datRoot, entry.System + ".xml"),
                            Path.Combine(datRoot, entry.ConsoleKey + ".dat")
                            ,Path.Combine(datRoot, entry.ConsoleKey + ".xml")
                        };

                        var found = false;
                        string? resolvedPath = null;
                        foreach (var candidate in candidates)
                        {
                            if (File.Exists(candidate))
                            {
                                resolvedPath = candidate;
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            datRootFiles ??= GetDatCandidateFiles(datRoot);
                            var exactMatch = FindExactStemMatch(datRootFiles, entry.Id, entry.System, entry.ConsoleKey);
                            if (exactMatch is not null)
                            {
                                resolvedPath = exactMatch;
                                found = true;
                            }
                        }

                        // PackMatch glob: match extracted No-Intro daily pack filenames
                        if (!found && !string.IsNullOrWhiteSpace(entry.PackMatch))
                        {
                            datRootFiles ??= GetDatCandidateFiles(datRoot);

                            var matched = MatchPackGlob(datRootFiles, entry.PackMatch);
                            if (matched != null)
                            {
                                resolvedPath = matched;
                                found = true;
                            }
                        }

                        if (found && resolvedPath is not null)
                        {
                            if (!alreadyMapped)
                            {
                                map[entry.ConsoleKey] = resolvedPath;
                            }
                            else
                            {
                                // Supplemental DAT: same ConsoleKey but different DAT source (e.g. FBNeo + No-Intro)
                                if (!supplementalDats.TryGetValue(entry.ConsoleKey, out var list))
                                {
                                    list = new List<string>();
                                    supplementalDats[entry.ConsoleKey] = list;
                                }
                                if (!string.Equals(map[entry.ConsoleKey], resolvedPath, StringComparison.OrdinalIgnoreCase)
                                    && !list.Contains(resolvedPath, StringComparer.OrdinalIgnoreCase))
                                {
                                    list.Add(resolvedPath);
                                }
                            }
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Malformed catalog — fall through to directory scan
            }
        }

        if (Directory.Exists(datRoot))
        {
            // Collect already-mapped file paths to avoid adding catalog-matched DATs
            // under their raw stem as a phantom console key (e.g. "FBNEO-NES" when
            // fbneo-nes.dat is already mapped via catalog to ConsoleKey "NES").
            var mappedPaths = new HashSet<string>(map.Values, StringComparer.OrdinalIgnoreCase);
            foreach (var extraList in supplementalDats.Values)
                foreach (var p in extraList)
                    mappedPaths.Add(p);

            foreach (var datFile in GetDatCandidateFiles(datRoot))
            {
                if (mappedPaths.Contains(datFile))
                    continue;

                var stem = Path.GetFileNameWithoutExtension(datFile);
                var resolvedKey = ResolveRuntimeDatConsoleKey(stem, datFile, datRoot, runtimeConsoleDetector);
                if (string.IsNullOrWhiteSpace(resolvedKey))
                    continue;

                if (!map.ContainsKey(resolvedKey))
                {
                    map[resolvedKey] = datFile;
                    continue;
                }

                if (string.Equals(map[resolvedKey], datFile, StringComparison.OrdinalIgnoreCase))
                    continue;

                if (!supplementalDats.TryGetValue(resolvedKey, out var list))
                {
                    list = new List<string>();
                    supplementalDats[resolvedKey] = list;
                }

                if (!list.Contains(datFile, StringComparer.OrdinalIgnoreCase))
                    list.Add(datFile);
            }
        }

        return map;
    }

    internal static void NormalizeRuntimeDatMappings(
        Dictionary<string, string> map,
        Dictionary<string, List<string>> supplementalDats,
        ConsoleDetector? consoleDetector,
        string datRoot,
        Action<string>? onWarning)
    {
        ArgumentNullException.ThrowIfNull(map);
        ArgumentNullException.ThrowIfNull(supplementalDats);

        if (map.Count == 0 && supplementalDats.Count == 0)
            return;

        var combined = map
            .Select(static entry => (SourceKey: entry.Key, DatPath: entry.Value))
            .Concat(supplementalDats
                .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .SelectMany(static pair => pair.Value.Select(path => (SourceKey: pair.Key, DatPath: path))))
            .Where(static entry =>
                !string.IsNullOrWhiteSpace(entry.SourceKey) &&
                !string.IsNullOrWhiteSpace(entry.DatPath))
            .OrderBy(static entry => entry.SourceKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static entry => entry.DatPath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var normalizedMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var normalizedSupplementals = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        var droppedMappings = 0;
        var remappedKeys = 0;

        foreach (var (sourceKey, datPath) in combined)
        {
            var canonicalKey = ResolveRuntimeDatConsoleKey(sourceKey, datPath, datRoot, consoleDetector);
            if (string.IsNullOrWhiteSpace(canonicalKey))
            {
                droppedMappings++;
                continue;
            }

            if (!sourceKey.Equals(canonicalKey, StringComparison.OrdinalIgnoreCase))
                remappedKeys++;

            if (!normalizedMap.TryGetValue(canonicalKey, out var primaryPath))
            {
                normalizedMap[canonicalKey] = datPath;
                continue;
            }

            if (string.Equals(primaryPath, datPath, StringComparison.OrdinalIgnoreCase))
                continue;

            if (!normalizedSupplementals.TryGetValue(canonicalKey, out var list))
            {
                list = new List<string>();
                normalizedSupplementals[canonicalKey] = list;
            }

            if (!list.Contains(datPath, StringComparer.OrdinalIgnoreCase))
                list.Add(datPath);
        }

        if (droppedMappings > 0)
        {
            onWarning?.Invoke(
                $"[DAT] {droppedMappings} DAT-Zuordnung(en) ohne aufloesbaren ConsoleKey ignoriert (verhindert UNKNOWN-Routing durch Phantom-Keys).");
        }

        if (remappedKeys > 0)
            onWarning?.Invoke($"[DAT] {remappedKeys} DAT-Zuordnung(en) auf kanonische ConsoleKeys normalisiert.");

        map.Clear();
        foreach (var entry in normalizedMap)
            map[entry.Key] = entry.Value;

        supplementalDats.Clear();
        foreach (var entry in normalizedSupplementals)
            supplementalDats[entry.Key] = entry.Value
                .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
                .ToList();
    }

    internal static string? ResolveRuntimeDatConsoleKey(
        string sourceKey,
        string datPath,
        string datRoot,
        ConsoleDetector? consoleDetector)
    {
        if (string.IsNullOrWhiteSpace(sourceKey))
            return null;

        var trimmedKey = sourceKey.Trim();
        if (IsRuntimeSentinelConsoleKey(trimmedKey))
            return null;

        if (consoleDetector is not null)
        {
            var directConsole = consoleDetector.GetConsole(trimmedKey);
            if (directConsole is not null)
                return directConsole.Key;

            foreach (var descriptor in EnumerateRuntimeConsoleDescriptors(trimmedKey, datPath, datRoot))
            {
                var resolved = ResolveConsoleKeyFromDescriptor(consoleDetector, descriptor);
                if (!string.IsNullOrWhiteSpace(resolved))
                    return resolved;
            }
        }

        var uppercaseKey = trimmedKey.ToUpperInvariant();
        if (IsRuntimeSentinelConsoleKey(uppercaseKey))
            return null;

        if (IsValidRuntimeConsoleKey(uppercaseKey))
            return uppercaseKey;

        // Fallback: sanitize the stem (strip trailing suffixes like dates/versions,
        // replace invalid chars) so unresolvable DATs stay in the index and remain
        // available for cross-console hash lookup instead of being silently dropped.
        var sanitized = SanitizeStemToConsoleKey(trimmedKey);
        return sanitized;
    }

    internal static string? SanitizeStemToConsoleKey(string stem)
    {
        if (string.IsNullOrWhiteSpace(stem))
            return null;

        // Strip trailing descriptor suffixes like (date), (TOSEC-version), [format]
        var stripped = StripTrailingDescriptorSuffixes(stem);
        if (string.IsNullOrWhiteSpace(stripped))
            stripped = stem;

        var upper = stripped.ToUpperInvariant();

        // Replace sequences of non-alphanumeric / non-dash / non-underscore with a single underscore
        var sb = new StringBuilder(upper.Length);
        var lastWasSeparator = true; // suppress leading separator
        foreach (var ch in upper)
        {
            if (char.IsLetterOrDigit(ch) || ch == '-')
            {
                sb.Append(ch);
                lastWasSeparator = false;
            }
            else if (!lastWasSeparator)
            {
                sb.Append('_');
                lastWasSeparator = true;
            }
        }

        // Trim trailing separator
        while (sb.Length > 0 && sb[sb.Length - 1] == '_')
            sb.Length--;

        if (sb.Length == 0)
            return null;

        var result = sb.ToString();
        return IsRuntimeSentinelConsoleKey(result) ? null : result;
    }

    private static IEnumerable<string> EnumerateRuntimeConsoleDescriptors(
        string sourceKey,
        string datPath,
        string datRoot)
    {
        var yielded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        void AddDescriptor(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return;

            var trimmed = raw.Trim();
            if (trimmed.Length == 0 || yielded.Contains(trimmed))
                return;

            // Keep candidates deterministic and bounded.
            if (yielded.Count >= 24)
                return;

            yielded.Add(trimmed);
        }

        AddDescriptor(sourceKey);
        AddDescriptor(StripTrailingDescriptorSuffixes(sourceKey));

        var fileStem = Path.GetFileNameWithoutExtension(datPath);
        AddDescriptor(fileStem);
        AddDescriptor(StripTrailingDescriptorSuffixes(fileStem));

        var datHeaderName = TryReadDatHeaderName(datPath);
        AddDescriptor(datHeaderName);
        AddDescriptor(StripTrailingDescriptorSuffixes(datHeaderName));

        var datDirectory = Path.GetDirectoryName(datPath);
        if (!string.IsNullOrWhiteSpace(datDirectory))
        {
            var relativeDirectory = GetRelativeDirectorySafe(datDirectory, datRoot);
            AddDescriptor(relativeDirectory);
            AddDescriptor(Path.GetFileName(datDirectory));
        }

        foreach (var descriptor in yielded.ToArray())
        {
            foreach (var segment in SplitDescriptorSegments(descriptor))
                AddDescriptor(segment);
        }

        return yielded.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase);
    }

    private static string? ResolveConsoleKeyFromDescriptor(ConsoleDetector consoleDetector, string descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
            return null;

        var normalizedDescriptor = NormalizeDescriptor(descriptor);
        if (normalizedDescriptor.Length == 0)
            return null;

        var bestScore = -1;
        string? bestKey = null;
        var ambiguous = false;

        foreach (var key in consoleDetector.AllConsoleKeys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
        {
            var info = consoleDetector.GetConsole(key);
            if (info is null)
                continue;

            var score = ScoreDescriptorAgainstConsole(normalizedDescriptor, info);
            if (score <= 0)
                continue;

            if (score > bestScore)
            {
                bestScore = score;
                bestKey = info.Key;
                ambiguous = false;
                continue;
            }

            if (score == bestScore && !string.Equals(bestKey, info.Key, StringComparison.OrdinalIgnoreCase))
                ambiguous = true;
        }

        return ambiguous ? null : bestKey;
    }

    private static int ScoreDescriptorAgainstConsole(string normalizedDescriptor, ConsoleInfo info)
    {
        var aliases = EnumerateConsoleAliases(info);
        var score = 0;

        foreach (var alias in aliases)
        {
            var normalizedAlias = NormalizeDescriptor(alias);
            if (normalizedAlias.Length == 0)
                continue;

            if (string.Equals(normalizedDescriptor, normalizedAlias, StringComparison.Ordinal))
            {
                score = Math.Max(score, 1000 + normalizedAlias.Length);
                continue;
            }

            if (ContainsWholeAlias(normalizedDescriptor, normalizedAlias))
                score = Math.Max(score, normalizedAlias.Length);
        }

        return score;
    }

    private static IEnumerable<string> EnumerateConsoleAliases(ConsoleInfo info)
    {
        yield return info.Key;
        yield return info.DisplayName;

        foreach (var alias in info.FolderAliases)
            yield return alias;

        foreach (var keyword in info.Keywords)
            yield return keyword;
    }

    private static bool ContainsWholeAlias(string normalizedDescriptor, string normalizedAlias)
    {
        if (normalizedAlias.Length == 0)
            return false;

        var paddedDescriptor = $" {normalizedDescriptor} ";
        var paddedAlias = $" {normalizedAlias} ";
        return paddedDescriptor.Contains(paddedAlias, StringComparison.Ordinal);
    }

    private static string NormalizeDescriptor(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        var pendingSpace = false;
        foreach (var ch in value.Trim())
        {
            if (char.IsLetterOrDigit(ch))
            {
                if (pendingSpace && sb.Length > 0)
                    sb.Append(' ');
                sb.Append(char.ToUpperInvariant(ch));
                pendingSpace = false;
            }
            else
            {
                pendingSpace = sb.Length > 0;
            }
        }

        return sb.ToString();
    }

    private static IEnumerable<string> SplitDescriptorSegments(string descriptor)
    {
        if (string.IsNullOrWhiteSpace(descriptor))
            yield break;

        foreach (var segment in descriptor.Split(
                     [" - ", "/", "\\", "|", ":"],
                     StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (!string.IsNullOrWhiteSpace(segment))
                yield return segment;
        }
    }

    private static string StripTrailingDescriptorSuffixes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var current = value.Trim();
        while (true)
        {
            string next;
            try
            {
                next = RxTrailingDescriptorSuffix.Replace(current, string.Empty).Trim();
            }
            catch (RegexMatchTimeoutException)
            {
                return current;
            }

            if (next.Length == 0 || string.Equals(next, current, StringComparison.Ordinal))
                return current;

            current = next;
        }
    }

    private static string? GetRelativeDirectorySafe(string directoryPath, string rootPath)
    {
        if (string.IsNullOrWhiteSpace(directoryPath) || string.IsNullOrWhiteSpace(rootPath))
            return null;

        try
        {
            var relative = Path.GetRelativePath(rootPath, directoryPath);
            if (relative.StartsWith("..", StringComparison.Ordinal))
                return Path.GetFileName(directoryPath);

            return relative;
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return Path.GetFileName(directoryPath);
        }
    }

    private static ConsoleDetector? TryLoadConsoleDetector(string dataDir)
    {
        if (string.IsNullOrWhiteSpace(dataDir) || !Directory.Exists(dataDir))
            return null;

        var consolesPath = Path.Combine(dataDir, "consoles.json");
        if (!File.Exists(consolesPath))
            return null;

        try
        {
            return ConsoleDetector.LoadFromJson(File.ReadAllText(consolesPath));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static string? TryReadDatHeaderName(string datPath)
    {
        if (string.IsNullOrWhiteSpace(datPath) || !File.Exists(datPath))
            return null;

        // DAT header extraction is best-effort. Failures keep the existing descriptor heuristics.
        var strictSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        var relaxedSettings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Ignore,
            XmlResolver = null,
            IgnoreComments = true,
            IgnoreWhitespace = true
        };

        return ReadDatHeaderName(datPath, strictSettings) ?? ReadDatHeaderName(datPath, relaxedSettings);
    }

    private static string? ReadDatHeaderName(string datPath, XmlReaderSettings settings)
    {
        try
        {
            using var reader = XmlReader.Create(datPath, settings);
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                    continue;

                if (!reader.LocalName.Equals("header", StringComparison.OrdinalIgnoreCase))
                    continue;

                using var headerReader = reader.ReadSubtree();
                while (headerReader.Read())
                {
                    if (headerReader.NodeType != XmlNodeType.Element)
                        continue;

                    if (!headerReader.LocalName.Equals("name", StringComparison.OrdinalIgnoreCase))
                        continue;

                    var headerName = headerReader.ReadElementContentAsString()?.Trim();
                    if (!string.IsNullOrWhiteSpace(headerName))
                        return headerName;
                }

                return null;
            }
        }
        catch (Exception ex) when (ex is XmlException or IOException or UnauthorizedAccessException)
        {
            // Best-effort only; fallback resolver continues without header hint.
        }

        return null;
    }

    private static bool IsValidRuntimeConsoleKey(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
            return false;

        try
        {
            return RxValidRuntimeConsoleKey.IsMatch(key);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }

    private static bool IsRuntimeSentinelConsoleKey(string key)
        => key.Equals("UNKNOWN", StringComparison.OrdinalIgnoreCase)
           || key.Equals("AMBIGUOUS", StringComparison.OrdinalIgnoreCase);

    private static string[] GetDatCandidateFiles(string datRoot)
    {
        if (!Directory.Exists(datRoot))
            return [];

        return new FileSystemAdapter().GetFilesSafe(datRoot, [".dat", ".xml"])
            .Order(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    internal static string? FindExactStemMatch(string[] datRootFiles, params string[] stems)
    {
        foreach (var stem in stems)
        {
            if (string.IsNullOrWhiteSpace(stem))
                continue;

            var match = datRootFiles
                .Where(path => Path.GetFileNameWithoutExtension(path)
                    .Equals(stem, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(path => Path.GetFileNameWithoutExtension(path), StringComparer.OrdinalIgnoreCase)
                .ThenByDescending(path => path, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            if (match is not null)
                return match;
        }

        return null;
    }

    /// <summary>
    /// Match a PackMatch glob pattern (supports trailing * wildcard) against DAT file stems.
    /// If multiple files match, picks the one whose name sorts last (newest daily pack by date suffix).
    /// </summary>
    internal static string? MatchPackGlob(string[] datFiles, string packMatch)
    {
        if (string.IsNullOrWhiteSpace(packMatch) || datFiles.Length == 0)
            return null;

        // PackMatch patterns use trailing * (e.g. "Nintendo - Nintendo Entertainment System (Headered)*")
        var trimmed = packMatch.TrimEnd('*');
        string? best = null;
        foreach (var file in datFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(file);
            if (stem.StartsWith(trimmed, StringComparison.OrdinalIgnoreCase))
            {
                // Pick by stem first (daily-pack timestamp in filename), then full path as deterministic tie-breaker.
                if (best == null || CompareDatCandidatePriority(file, best) > 0)
                    best = file;
            }
        }

        return best;
    }

    private static int CompareDatCandidatePriority(string left, string right)
    {
        var leftStem = Path.GetFileNameWithoutExtension(left);
        var rightStem = Path.GetFileNameWithoutExtension(right);

        var stemCompare = string.Compare(leftStem, rightStem, StringComparison.OrdinalIgnoreCase);
        if (stemCompare != 0)
            return stemCompare;

        return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static void MergeDatIndices(DatIndex? target, DatIndex? source)
    {
        if (target is null || source is null)
            return;

        foreach (var consoleKey in source.ConsoleKeys.OrderBy(static key => key, StringComparer.OrdinalIgnoreCase))
        {
            var entries = source.GetConsoleEntries(consoleKey);
            if (entries is null || entries.Count == 0)
                continue;

            foreach (var hash in entries.Keys.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))
            {
                var entry = source.LookupWithFilename(consoleKey, hash);
                if (entry is null)
                    continue;

                var value = entry.Value;
                target.Add(
                    consoleKey,
                    hash,
                    value.GameName,
                    value.RomFileName,
                    value.IsBios,
                    value.ParentGameName);
            }
        }
    }

    private static string ComputeEnrichmentFingerprint(
        RunOptions runOptions,
        string dataDir,
        string? datRoot,
        IReadOnlyDictionary<string, string>? datConsoleMap,
        IReadOnlySet<string>? knownBiosHashes)
    {
        ArgumentNullException.ThrowIfNull(runOptions);
        ArgumentNullException.ThrowIfNull(dataDir);

        var lines = new List<string>
        {
            $"AggressiveJunk={runOptions.AggressiveJunk}",
            $"EnableDat={runOptions.EnableDat}",
            $"HashType={CollectionIndexCandidateMapper.NormalizeHashType(runOptions.HashType)}",
            $"PreferRegions={string.Join(",", runOptions.PreferRegions.OrderBy(static value => value, StringComparer.OrdinalIgnoreCase))}"
        };

        AppendFileStamp(lines, Path.Combine(dataDir, "consoles.json"));
        AppendFileStamp(lines, Path.Combine(dataDir, "bios-hashes.json"));

        if (runOptions.EnableDat)
        {
            lines.Add(string.IsNullOrWhiteSpace(datRoot)
                ? "DatRoot=<unset>"
                : $"DatRoot={Path.GetFullPath(datRoot)}");

            foreach (var datPath in (datConsoleMap ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
                         .OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                         .Select(static pair => pair.Value)
                         .Distinct(StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrWhiteSpace(datPath))
                {
                    lines.Add("DatFile=<empty>");
                    continue;
                }

                AppendFileStamp(lines, datPath);
            }
        }

        if (knownBiosHashes is not null)
            lines.Add($"KnownBiosCount={knownBiosHashes.Count}");

        using var sha256 = SHA256.Create();
        var payload = string.Join('\n', lines);
        return Convert.ToHexString(sha256.ComputeHash(Encoding.UTF8.GetBytes(payload))).ToLowerInvariant();
    }

    private static void AppendFileStamp(List<string> lines, string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!File.Exists(fullPath))
        {
            lines.Add($"Missing={fullPath}");
            return;
        }

        var info = new FileInfo(fullPath);
        lines.Add($"File={fullPath}|{info.Length}|{info.LastWriteTimeUtc.Ticks}");
    }

    /// <summary>
    /// Bridges unmapped consoles to existing DAT paths via their datSources aliases.
    /// Example: ARCADE has datSources ["mame","fbneo"]. If "MAME" is mapped but "ARCADE" is not,
    /// this resolves ARCADE → MAME's DAT path via the catalog id→ConsoleKey link.
    /// </summary>
    internal static void BridgeDatSourceAliases(
        Dictionary<string, string> map,
        ConsoleDetector consoleDetector,
        string dataDir)
    {
        var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
        if (!File.Exists(catalogPath))
            return;

        List<DatCatalogEntry>? entries;
        try
        {
            var json = File.ReadAllText(catalogPath);
            entries = JsonSerializer.Deserialize<List<DatCatalogEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return;
        }

        if (entries is null or { Count: 0 })
            return;

        // Build catalog id → ConsoleKey mapping (first occurrence wins)
        var idToConsoleKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry.Id) && !string.IsNullOrWhiteSpace(entry.ConsoleKey))
                idToConsoleKey.TryAdd(entry.Id, entry.ConsoleKey);
        }

        foreach (var consoleKey in consoleDetector.AllConsoleKeys)
        {
            if (map.ContainsKey(consoleKey))
                continue;

            var info = consoleDetector.GetConsole(consoleKey);
            if (info is null || info.DatSources.Length == 0)
                continue;

            foreach (var datSourceId in info.DatSources)
            {
                // datSourceId is a catalog Id (e.g. "mame").
                // Find the catalog ConsoleKey for that Id (e.g. "MAME").
                // If that ConsoleKey is already in the map, bridge it.
                if (idToConsoleKey.TryGetValue(datSourceId, out var bridgeKey)
                    && map.TryGetValue(bridgeKey, out var datPath))
                {
                    map[consoleKey] = datPath;
                    break;
                }
            }
        }
    }

    public sealed class DatCatalogEntry
    {
        public string Group { get; set; } = "";
        public string System { get; set; } = "";
        public string Id { get; set; } = "";
        public string ConsoleKey { get; set; } = "";
        public string PackMatch { get; set; } = "";
    }
}

/// <summary>
/// All runtime dependencies needed to execute a run.
/// </summary>
public sealed class RunEnvironment
    : IRunEnvironment
{
    public IFileSystem FileSystem { get; }
    public IAuditStore AuditStore { get; }
    public AuditCsvStore Audit => (AuditCsvStore)AuditStore;
    public ConsoleDetector? ConsoleDetector { get; }
    public FileHashService? HashService { get; }
    public ArchiveHashService? ArchiveHashService { get; }
    public IHeaderlessHasher? HeaderlessHasher { get; }
    public IFormatConverter? Converter { get; }
    public DatIndex? DatIndex { get; }
    public IReadOnlySet<string>? KnownBiosHashes { get; }
    public ICollectionIndex? CollectionIndex { get; }
    public string? EnrichmentFingerprint { get; }

    public RunEnvironment(FileSystemAdapter fileSystem, AuditCsvStore audit,
        ConsoleDetector? consoleDetector, FileHashService? hashService,
        IHeaderlessHasher? headerlessHasher,
        FormatConverterAdapter? converter, DatIndex? datIndex,
        ArchiveHashService? archiveHashService = null,
        IReadOnlySet<string>? knownBiosHashes = null,
        ICollectionIndex? collectionIndex = null,
        string? enrichmentFingerprint = null)
    {
        FileSystem = fileSystem;
        AuditStore = audit;
        ConsoleDetector = consoleDetector;
        HashService = hashService;
        ArchiveHashService = archiveHashService;
        HeaderlessHasher = headerlessHasher;
        Converter = converter;
        DatIndex = datIndex;
        KnownBiosHashes = knownBiosHashes;
        CollectionIndex = collectionIndex;
        EnrichmentFingerprint = enrichmentFingerprint;
    }

    public void Dispose()
    {
        HashService?.Dispose();
        if (CollectionIndex is IDisposable disposableCollectionIndex)
            disposableCollectionIndex.Dispose();
    }
}
