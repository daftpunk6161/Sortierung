using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using System.Text.RegularExpressions;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Safety;

namespace Romulus.Infrastructure.Export;

public static class FrontendExportService
{
    private static readonly Regex DiscMarkerRegex = new(
        @"(?ix)
        [\s._-]*
        [\(\[]?
        (?:
            (?:disc|disk|cd)\s*(?<disc>\d{1,2})(?:\s*of\s*\d{1,2})?
            |
            (?<disc2>\d{1,2})\s*of\s*\d{1,2}
        )
        [\)\]]?",
        RegexOptions.Compiled,
        TimeSpan.FromMilliseconds(100));

    public static async Task<FrontendExportResult> ExportAsync(
        FrontendExportRequest request,
        IFileSystem fileSystem,
        ICollectionIndex? collectionIndex,
        string? enrichmentFingerprint,
        Func<CancellationToken, Task<IReadOnlyList<RomCandidate>>>? fallbackCandidateFactory = null,
        IReadOnlyList<RomCandidate>? runCandidates = null,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(fileSystem);

        var normalizedFrontend = request.Frontend.Trim().ToLowerInvariant();
        if (!FrontendExportTargets.All.Contains(normalizedFrontend))
            throw new InvalidOperationException($"Unsupported frontend '{request.Frontend}'.");

        var generatedUtc = request.GeneratedUtc ?? DateTime.UtcNow;
        var stagingScope = TryCreateStagingScope(normalizedFrontend, request.OutputPath);
        var effectiveRequest = stagingScope is null
            ? request
            : request with { OutputPath = stagingScope.StagingRoot };

        var loaded = await LoadGamesAsync(
            effectiveRequest,
            fileSystem,
            collectionIndex,
            enrichmentFingerprint,
            fallbackCandidateFactory,
            runCandidates,
            ct).ConfigureAwait(false);

        IReadOnlyList<FrontendExportArtifact> artifacts;
        try
        {
            artifacts = normalizedFrontend switch
            {
                FrontendExportTargets.RetroArch => WriteRetroArchArtifacts(loaded.Games, effectiveRequest.OutputPath, effectiveRequest.CollectionName, generatedUtc),
                FrontendExportTargets.M3u => WriteM3uArtifacts(loaded.Games, effectiveRequest.OutputPath, effectiveRequest.CollectionName, generatedUtc),
                FrontendExportTargets.LaunchBox => WriteLaunchBoxArtifacts(loaded.Games, effectiveRequest.OutputPath, generatedUtc),
                FrontendExportTargets.EmulationStation => WriteEmulationStationArtifacts(loaded.Games, effectiveRequest.OutputPath, generatedUtc),
                FrontendExportTargets.Playnite => WritePlayniteArtifacts(loaded.Games, effectiveRequest.OutputPath, generatedUtc),
                FrontendExportTargets.MiSTer => WriteMiSTerArtifacts(loaded.Games, effectiveRequest.OutputPath, generatedUtc),
                FrontendExportTargets.AnaloguePocket => WriteAnaloguePocketArtifacts(loaded.Games, effectiveRequest.OutputPath, generatedUtc),
                FrontendExportTargets.OnionOs => WriteOnionOsArtifacts(loaded.Games, effectiveRequest.OutputPath, generatedUtc),
                FrontendExportTargets.Csv => WriteSingleArtifact(
                    effectiveRequest.OutputPath,
                    "Collection CSV",
                    BuildSchemaPrefixedCsv(CollectionExportService.ExportCollectionCsv(loaded.Candidates), generatedUtc)),
                FrontendExportTargets.Json => WriteSingleArtifact(
                    effectiveRequest.OutputPath,
                    "Collection JSON",
                    SerializeIndented(new
                    {
                        schemaVersion = RunConstants.FrontendExportSchemaVersion,
                        generatedUtc,
                        games = loaded.Games
                    })),
                FrontendExportTargets.Excel => WriteSingleArtifact(effectiveRequest.OutputPath, "Collection Excel XML", CollectionExportService.ExportExcelXml(loaded.Candidates)),
                _ => throw new InvalidOperationException($"Unsupported frontend '{request.Frontend}'.")
            };

            if (stagingScope is not null)
                artifacts = PromoteStagedDirectory(stagingScope, artifacts);
        }
        finally
        {
            if (stagingScope is not null && Directory.Exists(stagingScope.StagingRoot))
                Directory.Delete(stagingScope.StagingRoot, recursive: true);
        }

        return new FrontendExportResult(normalizedFrontend, loaded.Source, loaded.Games.Count, artifacts);
    }

    private static async Task<(IReadOnlyList<RomCandidate> Candidates, IReadOnlyList<ExportableGame> Games, string Source)> LoadGamesAsync(
        FrontendExportRequest request,
        IFileSystem fileSystem,
        ICollectionIndex? collectionIndex,
        string? enrichmentFingerprint,
        Func<CancellationToken, Task<IReadOnlyList<RomCandidate>>>? fallbackCandidateFactory,
        IReadOnlyList<RomCandidate>? runCandidates,
        CancellationToken ct)
    {
        IReadOnlyList<RomCandidate> candidates;
        string source;

        if (runCandidates is { Count: > 0 })
        {
            candidates = runCandidates;
            source = "run-candidates";
        }
        else
        {
            var scoped = await CollectionAnalysisService.TryLoadScopedCandidatesFromCollectionIndexAsync(
                collectionIndex,
                fileSystem,
                request.Roots,
                request.Extensions.ToArray(),
                enrichmentFingerprint,
                ct).ConfigureAwait(false);

            if (scoped.CanUse)
            {
                candidates = scoped.Candidates;
                source = scoped.Source;
            }
            else if (fallbackCandidateFactory is not null)
            {
                candidates = await fallbackCandidateFactory(ct).ConfigureAwait(false);
                source = ScopedCandidateSources.FallbackRun;
            }
            else
            {
                throw new InvalidOperationException($"Collection index not eligible for export: {scoped.Reason ?? "unknown reason"}.");
            }
        }

        var games = candidates
            .Where(static candidate => candidate.Category == FileCategory.Game)
            .Select(static candidate => new ExportableGame(
                candidate.MainPath,
                Path.GetFileName(candidate.MainPath),
                Path.GetFileNameWithoutExtension(candidate.MainPath),
                candidate.GameKey,
                candidate.ConsoleKey ?? string.Empty,
                CollectionAnalysisService.ResolveConsoleLabel(candidate),
                candidate.Region,
                candidate.Extension,
                candidate.SizeBytes,
                candidate.DatMatch))
            .OrderBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static game => game.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return (candidates, games, source);
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteM3uArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        string collectionName,
        DateTime generatedUtc)
    {
        var playlists = BuildMultiDiscPlaylists(games);

        if (HasFileExtension(outputPath))
        {
            var content = BuildMergedM3uContent(playlists, games, collectionName, generatedUtc);
            return WriteSingleArtifact(outputPath, "M3U playlist", content);
        }

        var root = EnsureTargetRoot(outputPath, "playlists");
        if (playlists.Count == 0)
        {
            var fallbackFileName = SanitizeFileNameSegment(collectionName) + ".m3u";
            var fallbackPath = EnsureChildPath(root, fallbackFileName);
            AtomicFileWriter.WriteAllText(fallbackPath, BuildM3uContent(collectionName, OrderPlaylistEntries(games), generatedUtc), Encoding.UTF8);
            return [new FrontendExportArtifact(fallbackPath, collectionName, games.Count)];
        }

        return playlists
            .Select(playlist =>
            {
                var consoleRoot = EnsureChildPath(root, SanitizeFileNameSegment(playlist.ConsoleLabel));
                Directory.CreateDirectory(consoleRoot);

                var fileName = SanitizeFileNameSegment(playlist.PlaylistName) + ".m3u";
                var targetPath = EnsureChildPath(consoleRoot, fileName);

                AtomicFileWriter.WriteAllText(targetPath, BuildM3uContent(playlist.PlaylistName, playlist.Entries, generatedUtc), Encoding.UTF8);
                return new FrontendExportArtifact(targetPath, $"{playlist.ConsoleLabel}: {playlist.PlaylistName}", playlist.Entries.Count);
            })
            .ToArray();
    }

    private static IReadOnlyList<M3uPlaylistGroup> BuildMultiDiscPlaylists(IReadOnlyList<ExportableGame> games)
    {
        var grouped = new Dictionary<(string ConsoleLabel, string GroupKey), List<M3uPlaylistEntry>>();

        foreach (var game in games)
        {
            var discEntry = TryBuildDiscPlaylistEntry(game);
            if (discEntry is null)
                continue;

            var key = (discEntry.ConsoleLabel, NormalizePlaylistKey(discEntry.PlaylistName));
            if (!grouped.TryGetValue(key, out var bucket))
            {
                bucket = new List<M3uPlaylistEntry>();
                grouped[key] = bucket;
            }

            bucket.Add(discEntry);
        }

        return grouped
            .Values
            .Where(static bucket => bucket.Count > 1)
            .Select(bucket =>
            {
                var ordered = bucket
                    .OrderBy(static entry => entry.DiscNumber)
                    .ThenBy(static entry => entry.Game.DisplayName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(static entry => entry.Game.SourcePath, StringComparer.OrdinalIgnoreCase)
                    .Select(static entry => entry.Game)
                    .ToArray();

                var first = bucket[0];
                return new M3uPlaylistGroup(first.ConsoleLabel, first.PlaylistName, ordered);
            })
            .OrderBy(static playlist => playlist.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static playlist => playlist.PlaylistName, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static M3uPlaylistEntry? TryBuildDiscPlaylistEntry(ExportableGame game)
    {
        var title = Path.GetFileNameWithoutExtension(game.FileName);
        if (string.IsNullOrWhiteSpace(title))
            title = game.DisplayName;

        var match = DiscMarkerRegex.Match(title);
        if (!match.Success)
            return null;

        var discToken = match.Groups["disc"].Success
            ? match.Groups["disc"].Value
            : match.Groups["disc2"].Success
                ? match.Groups["disc2"].Value
                : string.Empty;

        if (!int.TryParse(discToken, out var discNumber) || discNumber <= 0)
            return null;

        var playlistName = CollapseWhitespace(DiscMarkerRegex.Replace(title, " "));
        if (string.IsNullOrWhiteSpace(playlistName))
            playlistName = game.DisplayName;

        return new M3uPlaylistEntry(game, game.ConsoleLabel, playlistName, discNumber);
    }

    private static string BuildMergedM3uContent(
        IReadOnlyList<M3uPlaylistGroup> playlists,
        IReadOnlyList<ExportableGame> allGames,
        string collectionName,
        DateTime generatedUtc)
    {
        if (playlists.Count == 1)
            return BuildM3uContent(playlists[0].PlaylistName, playlists[0].Entries, generatedUtc);

        if (playlists.Count == 0)
            return BuildM3uContent(collectionName, OrderPlaylistEntries(allGames), generatedUtc);

        var lines = new List<string>
        {
            "#EXTM3U",
            $"#ROMULUS-SCHEMA-VERSION:{RunConstants.FrontendExportSchemaVersion}",
            $"#ROMULUS-GENERATED-UTC:{generatedUtc:o}",
            $"#PLAYLIST:{SanitizeM3uDisplayText(collectionName, "Collection")}"
        };

        foreach (var playlist in playlists)
        {
            lines.Add($"#GROUP:{SanitizeM3uDisplayText(playlist.ConsoleLabel, "Unknown")}" +
                      $" - {SanitizeM3uDisplayText(playlist.PlaylistName, "Playlist")}");
            AppendPlaylistEntries(lines, playlist.Entries);
            lines.Add(string.Empty);
        }

        return string.Join(Environment.NewLine, lines);
    }

    private static string BuildM3uContent(string playlistName, IReadOnlyList<ExportableGame> entries, DateTime generatedUtc)
    {
        var lines = new List<string>
        {
            "#EXTM3U",
            $"#ROMULUS-SCHEMA-VERSION:{RunConstants.FrontendExportSchemaVersion}",
            $"#ROMULUS-GENERATED-UTC:{generatedUtc:o}",
            $"#PLAYLIST:{SanitizeM3uDisplayText(playlistName, "Collection")}"
        };

        AppendPlaylistEntries(lines, entries);
        return string.Join(Environment.NewLine, lines);
    }

    private static void AppendPlaylistEntries(List<string> lines, IReadOnlyList<ExportableGame> entries)
    {
        foreach (var game in entries)
        {
            lines.Add($"#EXTINF:-1,{SanitizeM3uDisplayText(game.DisplayName, "Unknown")}");
            lines.Add(SanitizeM3uPath(game.SourcePath));
        }
    }

    private static ExportableGame[] OrderPlaylistEntries(IEnumerable<ExportableGame> entries)
    {
        return entries
            .OrderBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static game => game.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static game => game.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static string NormalizePlaylistKey(string value)
    {
        var normalized = new string(value
            .Where(char.IsLetterOrDigit)
            .Select(char.ToUpperInvariant)
            .ToArray());

        return string.IsNullOrWhiteSpace(normalized)
            ? value.Trim().ToUpperInvariant()
            : normalized;
    }

    private static string CollapseWhitespace(string value)
        => string.Join(" ", value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Trim();

    private static string SanitizeM3uDisplayText(string? value, string fallback)
    {
        var sanitized = CollapseWhitespace(RemoveControlChars(value));
        return string.IsNullOrWhiteSpace(sanitized) ? fallback : sanitized;
    }

    private static string SanitizeM3uPath(string? value)
    {
        var sanitized = RemoveControlChars(value).Trim().Replace('\\', '/');
        if (string.IsNullOrWhiteSpace(sanitized))
            return "./unknown";

        // Prevent comment-injection by ensuring entry paths cannot begin with '#'.
        if (sanitized.StartsWith('#'))
            sanitized = "_" + sanitized;

        return sanitized;
    }

    private static string RemoveControlChars(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var sb = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            sb.Append(char.IsControl(ch) ? ' ' : ch);
        }

        return sb.ToString();
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteRetroArchArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        string collectionName,
        DateTime generatedUtc)
    {
        if (HasFileExtension(outputPath))
        {
            var content = BuildRetroArchContent(games, collectionName, generatedUtc);
            return WriteSingleArtifact(outputPath, "RetroArch playlist", content);
        }

        var root = EnsureTargetRoot(outputPath, "playlists");
        return games
            .GroupBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var safeName = SanitizeFileNameSegment(group.Key) + ".lpl";
                var targetPath = EnsureChildPath(root, safeName);
                AtomicFileWriter.WriteAllText(targetPath, BuildRetroArchContent(group, group.Key, generatedUtc), Encoding.UTF8);
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static string BuildRetroArchContent(IEnumerable<ExportableGame> games, string collectionName, DateTime generatedUtc)
    {
        return JsonSerializer.Serialize(new
        {
            version = "1.5",
            romulus_schema_version = RunConstants.FrontendExportSchemaVersion,
            generated_utc = generatedUtc,
            default_core_path = string.Empty,
            default_core_name = string.Empty,
            items = games.Select(game =>
            {
                var core = CollectionAnalysisService.DefaultCoreMapping.GetValueOrDefault(game.ConsoleLabel.ToLowerInvariant(), string.Empty);
                return new
                {
                    path = game.SourcePath.Replace('\\', '/'),
                    label = game.DisplayName,
                    core_path = core,
                    core_name = core.Replace("_libretro", string.Empty, StringComparison.OrdinalIgnoreCase),
                    db_name = collectionName + ".lpl"
                };
            }).ToArray()
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteLaunchBoxArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        DateTime generatedUtc)
    {
        if (HasFileExtension(outputPath))
        {
            SaveXml(outputPath, BuildLaunchBoxDocument("Collection", games, generatedUtc));
            return [new FrontendExportArtifact(Path.GetFullPath(outputPath), "LaunchBox", games.Count)];
        }

        var root = EnsureTargetRoot(outputPath, "Platforms");
        return games
            .GroupBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var safeName = SanitizeFileNameSegment(group.Key) + ".xml";
                var targetPath = EnsureChildPath(root, safeName);
                SaveXml(targetPath, BuildLaunchBoxDocument(group.Key, group, generatedUtc));
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteEmulationStationArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        DateTime generatedUtc)
    {
        if (HasFileExtension(outputPath))
        {
            SaveXml(outputPath, BuildEmulationStationDocument(games, generatedUtc));
            return [new FrontendExportArtifact(Path.GetFullPath(outputPath), "EmulationStation", games.Count)];
        }

        var root = EnsureTargetRoot(outputPath, null);
        return games
            .GroupBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var directory = EnsureChildPath(root, SanitizeFileNameSegment(group.Key));
                Directory.CreateDirectory(directory);
                var targetPath = EnsureChildPath(directory, "gamelist.xml");
                SaveXml(targetPath, BuildEmulationStationDocument(group, generatedUtc));
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static IReadOnlyList<FrontendExportArtifact> WritePlayniteArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        DateTime generatedUtc)
    {
        if (HasFileExtension(outputPath))
        {
            var payload = JsonSerializer.Serialize(new
            {
                schemaVersion = RunConstants.FrontendExportSchemaVersion,
                format = "playnite-library",
                generatedUtc,
                games = games.Select(game => BuildPlaynitePayload(game, generatedUtc)).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true });
            return WriteSingleArtifact(outputPath, "Playnite library", payload);
        }

        var root = EnsureTargetRoot(outputPath, Path.Combine("library", "games"));
        var artifacts = new List<FrontendExportArtifact>(games.Count);
        foreach (var game in games)
        {
            var fileName = SanitizeFileNameSegment(BuildStableId(game)) + ".json";
            var targetPath = EnsureChildPath(root, fileName);
            var payload = JsonSerializer.Serialize(BuildPlaynitePayload(game, generatedUtc), new JsonSerializerOptions { WriteIndented = true });
            AtomicFileWriter.WriteAllText(targetPath, payload, Encoding.UTF8);
            artifacts.Add(new FrontendExportArtifact(targetPath, game.DisplayName, 1));
        }

        return artifacts;
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteMiSTerArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        DateTime generatedUtc)
    {
        if (HasFileExtension(outputPath))
        {
            var payload = SerializeIndented(new
            {
                schemaVersion = RunConstants.FrontendExportSchemaVersion,
                format = "mister-library",
                generatedUtc,
                systems = games
                    .GroupBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
                    .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
                    .Select(group => BuildMiSTerSystemPayload(group.Key, group, generatedUtc))
                    .ToArray()
            });

            return WriteSingleArtifact(outputPath, "MiSTer manifest", payload);
        }

        var root = EnsureTargetRoot(outputPath, "games");
        return games
            .GroupBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var consoleRoot = EnsureChildPath(root, SanitizeFileNameSegment(group.Key));
                Directory.CreateDirectory(consoleRoot);
                var targetPath = EnsureChildPath(consoleRoot, "_romulus-index.json");
                AtomicFileWriter.WriteAllText(targetPath, SerializeIndented(BuildMiSTerSystemPayload(group.Key, group, generatedUtc)), Encoding.UTF8);
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteAnaloguePocketArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        DateTime generatedUtc)
    {
        if (HasFileExtension(outputPath))
        {
            var payload = SerializeIndented(new
            {
                schemaVersion = RunConstants.FrontendExportSchemaVersion,
                format = "analogue-pocket-library",
                generatedUtc,
                assets = games.Select(BuildAnaloguePocketGamePayload).ToArray()
            });
            return WriteSingleArtifact(outputPath, "Analogue Pocket manifest", payload);
        }

        var root = EnsureTargetRoot(outputPath, "Assets");
        return games
            .GroupBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var consoleRoot = EnsureChildPath(root, SanitizeFileNameSegment(group.Key));
                Directory.CreateDirectory(consoleRoot);
                var targetPath = EnsureChildPath(consoleRoot, "library.json");
                var payload = SerializeIndented(new
                {
                    schemaVersion = RunConstants.FrontendExportSchemaVersion,
                    format = "analogue-pocket-system",
                    generatedUtc,
                    system = group.Key,
                    games = group.Select(BuildAnaloguePocketGamePayload).ToArray()
                });
                AtomicFileWriter.WriteAllText(targetPath, payload, Encoding.UTF8);
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteOnionOsArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        DateTime generatedUtc)
    {
        if (HasFileExtension(outputPath))
        {
            var payload = SerializeIndented(new
            {
                schemaVersion = RunConstants.FrontendExportSchemaVersion,
                format = "onionos-library",
                generatedUtc,
                roms = games.Select(BuildOnionOsGamePayload).ToArray()
            });
            return WriteSingleArtifact(outputPath, "OnionOS manifest", payload);
        }

        var root = EnsureTargetRoot(outputPath, "Roms");
        return games
            .GroupBy(static game => game.ConsoleLabel, StringComparer.OrdinalIgnoreCase)
            .OrderBy(static group => group.Key, StringComparer.OrdinalIgnoreCase)
            .Select(group =>
            {
                var consoleRoot = EnsureChildPath(root, SanitizeFileNameSegment(group.Key));
                Directory.CreateDirectory(consoleRoot);
                var targetPath = EnsureChildPath(consoleRoot, "romlist.json");
                var payload = SerializeIndented(new
                {
                    schemaVersion = RunConstants.FrontendExportSchemaVersion,
                    format = "onionos-system",
                    generatedUtc,
                    system = group.Key,
                    roms = group.Select(BuildOnionOsGamePayload).ToArray()
                });
                AtomicFileWriter.WriteAllText(targetPath, payload, Encoding.UTF8);
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static object BuildPlaynitePayload(ExportableGame game, DateTime generatedUtc)
    {
        return new
        {
            schemaVersion = RunConstants.FrontendExportSchemaVersion,
            gameId = BuildStableId(game),
            name = game.DisplayName,
            platform = game.ConsoleLabel,
            romPath = game.SourcePath,
            gameKey = game.GameKey,
            region = game.Region,
            extension = game.Extension,
            sizeBytes = game.SizeBytes,
            datVerified = game.DatVerified,
            addedUtc = generatedUtc
        };
    }

    private static object BuildMiSTerSystemPayload(string system, IEnumerable<ExportableGame> games, DateTime generatedUtc)
    {
        var core = CollectionAnalysisService.DefaultCoreMapping.GetValueOrDefault(system.ToLowerInvariant(), string.Empty);
        return new
        {
            schemaVersion = RunConstants.FrontendExportSchemaVersion,
            system,
            core,
            generatedUtc,
            games = games.Select(game => new
            {
                title = game.DisplayName,
                sourcePath = game.SourcePath,
                fileName = game.FileName,
                region = game.Region,
                gameKey = game.GameKey
            }).ToArray()
        };
    }

    private static object BuildAnaloguePocketGamePayload(ExportableGame game)
    {
        var consoleFolder = SanitizeFileNameSegment(game.ConsoleLabel);
        var fileName = SanitizeFileNameSegment(Path.GetFileNameWithoutExtension(game.FileName)) + game.Extension;
        return new
        {
            title = game.DisplayName,
            platform = game.ConsoleLabel,
            assetPath = $"Assets/{consoleFolder}/{fileName}",
            sourcePath = game.SourcePath,
            gameKey = game.GameKey,
            region = game.Region
        };
    }

    private static object BuildOnionOsGamePayload(ExportableGame game)
    {
        var consoleFolder = SanitizeFileNameSegment(game.ConsoleLabel);
        var targetFileName = SanitizeFileNameSegment(Path.GetFileNameWithoutExtension(game.FileName)) + game.Extension;
        return new
        {
            title = game.DisplayName,
            system = game.ConsoleLabel,
            targetPath = $"Roms/{consoleFolder}/{targetFileName}",
            sourcePath = game.SourcePath,
            gameKey = game.GameKey,
            region = game.Region
        };
    }

    private static XDocument BuildLaunchBoxDocument(string platform, IEnumerable<ExportableGame> games, DateTime generatedUtc)
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("LaunchBox",
                new XAttribute("romulusSchemaVersion", RunConstants.FrontendExportSchemaVersion),
                new XAttribute("generatedUtc", generatedUtc.ToString("o")),
                games.Select(game => new XElement("Game",
                    new XElement("Title", game.DisplayName),
                    new XElement("ApplicationPath", game.SourcePath),
                    new XElement("Platform", platform),
                    new XElement("Region", game.Region),
                    new XElement("DateAdded", generatedUtc.ToString("o"))))));
    }

    private static XDocument BuildEmulationStationDocument(IEnumerable<ExportableGame> games, DateTime generatedUtc)
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("gameList",
                new XAttribute("romulusSchemaVersion", RunConstants.FrontendExportSchemaVersion),
                new XAttribute("generatedUtc", generatedUtc.ToString("o")),
                games.Select(game => new XElement("game",
                    new XElement("path", game.SourcePath),
                    new XElement("name", game.DisplayName),
                    new XElement("region", game.Region),
                    new XElement("platform", game.ConsoleLabel)))));
    }

    private static string BuildSchemaPrefixedCsv(string csv, DateTime generatedUtc)
    {
        var bom = csv.StartsWith('\uFEFF') ? "\uFEFF" : string.Empty;
        var body = bom.Length == 0 ? csv : csv[1..];
        return bom
               + $"# schemaVersion={RunConstants.FrontendExportSchemaVersion};generatedUtc={generatedUtc:o}"
               + Environment.NewLine
               + body;
    }

    private static ExportStagingScope? TryCreateStagingScope(string frontend, string outputPath)
    {
        if (HasFileExtension(outputPath))
            return null;

        if (frontend is FrontendExportTargets.Csv or FrontendExportTargets.Json or FrontendExportTargets.Excel)
            return null;

        var finalRoot = ValidateOutputPath(outputPath);
        var trimmedFinalRoot = finalRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetDirectoryName(trimmedFinalRoot);
        if (string.IsNullOrWhiteSpace(parent))
            throw new InvalidOperationException("Directory export cannot be staged at a volume root.");

        Directory.CreateDirectory(parent);
        var leafName = Path.GetFileName(trimmedFinalRoot);
        var stagingRoot = Path.Combine(parent, $"__romulus_staging_{leafName}_{Environment.ProcessId}_{Guid.NewGuid():N}");
        return new ExportStagingScope(finalRoot, stagingRoot);
    }

    private static IReadOnlyList<FrontendExportArtifact> PromoteStagedDirectory(
        ExportStagingScope scope,
        IReadOnlyList<FrontendExportArtifact> stagedArtifacts)
    {
        var stagingRoot = Path.GetFullPath(scope.StagingRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var finalRoot = Path.GetFullPath(scope.FinalRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        foreach (var artifact in stagedArtifacts)
        {
            var fullArtifactPath = Path.GetFullPath(artifact.Path);
            if (!IsSameOrNestedPath(fullArtifactPath, stagingRoot) || !File.Exists(fullArtifactPath))
                throw new InvalidOperationException("Staged export validation failed.");
        }

        var parent = Path.GetDirectoryName(finalRoot)
            ?? throw new InvalidOperationException("Directory export has no parent directory.");
        var leafName = Path.GetFileName(finalRoot);
        var backupRoot = Path.Combine(parent, $"__romulus_backup_{leafName}_{Environment.ProcessId}_{Guid.NewGuid():N}");
        var hasBackup = false;

        try
        {
            if (Directory.Exists(finalRoot))
            {
                Directory.Move(finalRoot, backupRoot);
                hasBackup = true;
            }

            Directory.Move(stagingRoot, finalRoot);
            if (hasBackup)
                Directory.Delete(backupRoot, recursive: true);
        }
        catch
        {
            if (!Directory.Exists(finalRoot) && hasBackup && Directory.Exists(backupRoot))
                Directory.Move(backupRoot, finalRoot);

            throw;
        }

        return stagedArtifacts
            .Select(artifact =>
            {
                var relative = Path.GetRelativePath(stagingRoot, artifact.Path);
                return artifact with { Path = Path.GetFullPath(Path.Combine(finalRoot, relative)) };
            })
            .ToArray();
    }

    private static void SaveXml(string outputPath, XDocument doc)
    {
        var fullOutputPath = ValidateOutputPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream = new MemoryStream();
        doc.Save(stream);
        AtomicFileWriter.WriteAllBytes(fullOutputPath, stream.ToArray());
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteSingleArtifact(string outputPath, string label, string content)
    {
        var fullOutputPath = ValidateOutputPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        AtomicFileWriter.WriteAllText(fullOutputPath, content, Encoding.UTF8);
        return [new FrontendExportArtifact(fullOutputPath, label, 1)];
    }

    private static string EnsureTargetRoot(string outputPath, string? requiredSubDirectory)
    {
        var fullOutputPath = ValidateOutputPath(outputPath);

        var root = string.IsNullOrWhiteSpace(requiredSubDirectory)
            ? fullOutputPath
            : Path.Combine(fullOutputPath, requiredSubDirectory);
        Directory.CreateDirectory(root);
        return root;
    }

    private static string EnsureChildPath(string root, string childName)
    {
        var combined = Path.GetFullPath(Path.Combine(root, childName));
        if (!combined.StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Export path resolved outside the allowed output root.");

        return ValidateOutputPath(combined);
    }

    private static string ValidateOutputPath(string outputPath)
        => SafetyValidator.EnsureSafeOutputPath(outputPath);

    private static bool HasFileExtension(string path)
        => !string.IsNullOrWhiteSpace(Path.GetExtension(path));

    private static bool IsSameOrNestedPath(string candidatePath, string rootPath)
    {
        var candidate = Path.GetFullPath(candidatePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var root = Path.GetFullPath(rootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return string.Equals(candidate, root, StringComparison.OrdinalIgnoreCase)
               || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeFileNameSegment(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "unknown";

        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value.Trim())
            builder.Append(invalid.Contains(ch) ? '_' : ch);

        return builder.ToString().Trim('.', ' ');
    }

    private static string SerializeIndented<T>(T value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions { WriteIndented = true });

    private static string BuildStableId(ExportableGame game)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{game.ConsoleLabel}|{game.GameKey}|{game.SourcePath}|{game.Extension}"));
        return Convert.ToHexString(bytes[..8]);
    }

    private sealed record M3uPlaylistEntry(
        ExportableGame Game,
        string ConsoleLabel,
        string PlaylistName,
        int DiscNumber);

    private sealed record M3uPlaylistGroup(
        string ConsoleLabel,
        string PlaylistName,
        IReadOnlyList<ExportableGame> Entries);

    private sealed record ExportStagingScope(string FinalRoot, string StagingRoot);
}
