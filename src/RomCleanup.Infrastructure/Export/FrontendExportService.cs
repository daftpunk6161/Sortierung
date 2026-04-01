using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Analysis;
using RomCleanup.Infrastructure.Safety;

namespace RomCleanup.Infrastructure.Export;

public static class FrontendExportService
{
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

        var loaded = await LoadGamesAsync(
            request,
            fileSystem,
            collectionIndex,
            enrichmentFingerprint,
            fallbackCandidateFactory,
            runCandidates,
            ct).ConfigureAwait(false);

        var artifacts = normalizedFrontend switch
        {
            FrontendExportTargets.RetroArch => WriteRetroArchArtifacts(loaded.Games, request.OutputPath, request.CollectionName),
            FrontendExportTargets.LaunchBox => WriteLaunchBoxArtifacts(loaded.Games, request.OutputPath),
            FrontendExportTargets.EmulationStation => WriteEmulationStationArtifacts(loaded.Games, request.OutputPath),
            FrontendExportTargets.Playnite => WritePlayniteArtifacts(loaded.Games, request.OutputPath),
            FrontendExportTargets.Csv => WriteSingleArtifact(request.OutputPath, "Collection CSV", CollectionExportService.ExportCollectionCsv(loaded.Candidates)),
            FrontendExportTargets.Json => WriteSingleArtifact(
                request.OutputPath,
                "Collection JSON",
                JsonSerializer.Serialize(loaded.Games, new JsonSerializerOptions { WriteIndented = true })),
            FrontendExportTargets.Excel => WriteSingleArtifact(request.OutputPath, "Collection Excel XML", CollectionExportService.ExportExcelXml(loaded.Candidates)),
            _ => throw new InvalidOperationException($"Unsupported frontend '{request.Frontend}'.")
        };

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

    private static IReadOnlyList<FrontendExportArtifact> WriteRetroArchArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath,
        string collectionName)
    {
        if (HasFileExtension(outputPath))
        {
            var content = BuildRetroArchContent(games, collectionName);
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
                File.WriteAllText(targetPath, BuildRetroArchContent(group, group.Key), Encoding.UTF8);
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static string BuildRetroArchContent(IEnumerable<ExportableGame> games, string collectionName)
    {
        return JsonSerializer.Serialize(new
        {
            version = "1.5",
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
        string outputPath)
    {
        if (HasFileExtension(outputPath))
        {
            SaveXml(outputPath, BuildLaunchBoxDocument("Collection", games));
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
                SaveXml(targetPath, BuildLaunchBoxDocument(group.Key, group));
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteEmulationStationArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath)
    {
        if (HasFileExtension(outputPath))
        {
            SaveXml(outputPath, BuildEmulationStationDocument(games));
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
                SaveXml(targetPath, BuildEmulationStationDocument(group));
                return new FrontendExportArtifact(targetPath, group.Key, group.Count());
            })
            .ToArray();
    }

    private static IReadOnlyList<FrontendExportArtifact> WritePlayniteArtifacts(
        IReadOnlyList<ExportableGame> games,
        string outputPath)
    {
        if (HasFileExtension(outputPath))
        {
            var payload = JsonSerializer.Serialize(new
            {
                format = "playnite-library",
                generatedUtc = DateTime.UtcNow,
                games = games.Select(BuildPlaynitePayload).ToArray()
            }, new JsonSerializerOptions { WriteIndented = true });
            return WriteSingleArtifact(outputPath, "Playnite library", payload);
        }

        var root = EnsureTargetRoot(outputPath, Path.Combine("library", "games"));
        var artifacts = new List<FrontendExportArtifact>(games.Count);
        foreach (var game in games)
        {
            var fileName = SanitizeFileNameSegment(BuildStableId(game)) + ".json";
            var targetPath = EnsureChildPath(root, fileName);
            var payload = JsonSerializer.Serialize(BuildPlaynitePayload(game), new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(targetPath, payload, Encoding.UTF8);
            artifacts.Add(new FrontendExportArtifact(targetPath, game.DisplayName, 1));
        }

        return artifacts;
    }

    private static object BuildPlaynitePayload(ExportableGame game)
    {
        return new
        {
            gameId = BuildStableId(game),
            name = game.DisplayName,
            platform = game.ConsoleLabel,
            romPath = game.SourcePath,
            gameKey = game.GameKey,
            region = game.Region,
            extension = game.Extension,
            sizeBytes = game.SizeBytes,
            datVerified = game.DatVerified,
            addedUtc = DateTime.UtcNow
        };
    }

    private static XDocument BuildLaunchBoxDocument(string platform, IEnumerable<ExportableGame> games)
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("LaunchBox",
                games.Select(game => new XElement("Game",
                    new XElement("Title", game.DisplayName),
                    new XElement("ApplicationPath", game.SourcePath),
                    new XElement("Platform", platform),
                    new XElement("Region", game.Region),
                    new XElement("DateAdded", DateTime.UtcNow.ToString("o"))))));
    }

    private static XDocument BuildEmulationStationDocument(IEnumerable<ExportableGame> games)
    {
        return new XDocument(
            new XDeclaration("1.0", "utf-8", null),
            new XElement("gameList",
                games.Select(game => new XElement("game",
                    new XElement("path", game.SourcePath),
                    new XElement("name", game.DisplayName),
                    new XElement("region", game.Region),
                    new XElement("platform", game.ConsoleLabel)))));
    }

    private static void SaveXml(string outputPath, XDocument doc)
    {
        var fullOutputPath = ValidateOutputPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        using var stream = File.Create(fullOutputPath);
        doc.Save(stream);
    }

    private static IReadOnlyList<FrontendExportArtifact> WriteSingleArtifact(string outputPath, string label, string content)
    {
        var fullOutputPath = ValidateOutputPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        File.WriteAllText(fullOutputPath, content, Encoding.UTF8);
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

    private static string BuildStableId(ExportableGame game)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{game.ConsoleLabel}|{game.GameKey}|{game.SourcePath}|{game.Extension}"));
        return Convert.ToHexString(bytes[..8]);
    }
}
