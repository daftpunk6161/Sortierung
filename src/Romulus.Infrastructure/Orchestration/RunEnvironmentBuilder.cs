using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
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
using Romulus.Infrastructure.Tools;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Builds RunEnvironment from RunOptions + settings.
/// Shared setup for CLI, API, WPF.
/// ADR-008 §C-04.
/// </summary>
public sealed class RunEnvironmentBuilder
{
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

    /// <summary>
    /// Build complete environment for a run.
    /// </summary>
    public static RunEnvironment Build(RunOptions runOptions, RomulusSettings settings,
        string dataDir, Action<string>? onWarning = null,
        string? collectionDatabasePath = null)
    {
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
                        new EcmInvoker(toolRunner),
                        new NkitInvoker(toolRunner)
                    };
                    conversionExecutor = new ConversionExecutor(invokers, runOptions.ApproveConversionReview);

                    conversionPlanner = new ConversionPlanner(
                        conversionRegistry,
                        toolRunner.FindTool,
                        path => new FileInfo(path).Length,
                        PbpEncryptionDetector.IsEncrypted);
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
        var discHeaderDetector = new DiscHeaderDetector();
        var cartridgeHeaderDetector = new CartridgeHeaderDetector();
        if (File.Exists(consolesJsonPath))
        {
            var consolesJson = File.ReadAllText(consolesJsonPath);
            consoleDetector = ConsoleDetector.LoadFromJson(
                consolesJson,
                discHeaderDetector,
                archiveEntryProvider: archiveHashService.GetArchiveEntryNames,
                cartridgeHeaderDetector: cartridgeHeaderDetector);
        }
        else if (runOptions.SortConsole || runOptions.EnableDat)
        {
            onWarning?.Invoke("[Warning] consoles.json not found, --SortConsole/--EnableDat require it");
        }

        // DAT
        DatIndex? datIndex = null;
        FileHashService? hashService = null;
        ICollectionIndex? collectionIndex = null;
        var knownBiosHashes = LoadKnownBiosHashes(dataDir, onWarning);
        var effectiveDatRoot = !string.IsNullOrWhiteSpace(runOptions.DatRoot)
            ? runOptions.DatRoot
            : settings.Dat.DatRoot;
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
        }

        if (runOptions.EnableDat && !string.IsNullOrWhiteSpace(effectiveDatRoot) && Directory.Exists(effectiveDatRoot))
        {
            var datRepo = new DatRepositoryAdapter(toolRunner: toolRunner);
            datConsoleMap = BuildConsoleMap(dataDir, effectiveDatRoot, out var supplementalDats);

            // Bridge unmapped consoles via datSources aliases (e.g. ARCADE → MAME DAT)
            if (consoleDetector is not null)
                BridgeDatSourceAliases(datConsoleMap, consoleDetector, dataDir);

            // Diagnostic: show what BuildConsoleMap found.
            var datFileCount = Directory.Exists(effectiveDatRoot)
                ? Directory.GetFiles(effectiveDatRoot, "*.*", SearchOption.AllDirectories)
                    .Count(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                             || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
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
            onWarning?.Invoke("[Warning] DAT enabled but DatRoot not set or not found");
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

                var stem = Path.GetFileNameWithoutExtension(datFile).ToUpperInvariant();
                if (!map.ContainsKey(stem))
                    map[stem] = datFile;
            }
        }

        return map;
    }

    private static string[] GetDatCandidateFiles(string datRoot)
    {
        if (!Directory.Exists(datRoot))
            return [];

        return Directory.GetFiles(datRoot, "*.*", SearchOption.AllDirectories)
            .Where(path => path.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                || path.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
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
    public IFormatConverter? Converter { get; }
    public DatIndex? DatIndex { get; }
    public IReadOnlySet<string>? KnownBiosHashes { get; }
    public ICollectionIndex? CollectionIndex { get; }
    public string? EnrichmentFingerprint { get; }

    public RunEnvironment(FileSystemAdapter fileSystem, AuditCsvStore audit,
        ConsoleDetector? consoleDetector, FileHashService? hashService,
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
