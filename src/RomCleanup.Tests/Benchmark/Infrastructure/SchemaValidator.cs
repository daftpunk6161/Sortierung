using System.Text.Json;
using System.Text.RegularExpressions;
using RomCleanup.Tests.Benchmark.Models;

namespace RomCleanup.Tests.Benchmark.Infrastructure;

/// <summary>
/// Validates ground-truth entries against structural rules.
/// </summary>
internal sealed class SchemaValidator
{
    // ID format: {set-prefix}-{SYSTEM}-{subclass}-{number}
    private static readonly Regex IdPattern = new(
        @"^[a-z]{2,3}-[A-Z0-9]{1,12}-[a-z0-9-]+-\d{3,4}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private static readonly HashSet<string> ValidCategories = new(StringComparer.Ordinal)
    {
        "Game", "Bios", "NonGame", "Junk", "Unknown"
    };

    private static readonly HashSet<string> ValidDifficulties = new(StringComparer.Ordinal)
    {
        "easy", "medium", "hard", "adversarial"
    };

    private static readonly HashSet<string> ValidPrimaryMethods = new(StringComparer.Ordinal)
    {
        "CartridgeHeader", "DiscHeader", "FolderName", "UniqueExtension",
        "SerialNumber", "MagicBytes", "FileSize", "DatMatch", "DatLookup",
        "ArchiveContent", "Heuristic", "Keyword"
    };

    private static readonly HashSet<string> ValidFileModelTypes = new(StringComparer.Ordinal)
    {
        "single-file", "multi-file-set", "multi-disc", "archive", "directory"
    };

    private static readonly HashSet<string> ValidDatMatchLevels = new(StringComparer.Ordinal)
    {
        "exact", "fuzzy", "weak", "none"
    };

    private static readonly HashSet<string> ValidSortDecisions = new(StringComparer.Ordinal)
    {
        "sort", "block", "skip", "review", "manual"
    };

    private readonly HashSet<string> _validConsoleKeys;

    public SchemaValidator(IEnumerable<string> validConsoleKeys)
    {
        _validConsoleKeys = new HashSet<string>(validConsoleKeys, StringComparer.Ordinal);
    }

    /// <summary>
    /// Creates a validator using console keys from data/consoles.json.
    /// </summary>
    public static SchemaValidator CreateFromConsolesJson()
    {
        var path = BenchmarkPaths.ConsolesJsonPath;
        var json = File.ReadAllText(path);
        using var doc = JsonDocument.Parse(json);
        var keys = doc.RootElement
            .GetProperty("consoles")
            .EnumerateArray()
            .Select(e => e.GetProperty("key").GetString()!)
            .ToList();

        return new SchemaValidator(keys);
    }

    /// <summary>
    /// Validates a single entry. Returns empty list if valid, or list of error messages.
    /// </summary>
    public List<string> Validate(GroundTruthEntry entry)
    {
        var errors = new List<string>();

        // ID
        if (string.IsNullOrWhiteSpace(entry.Id))
            errors.Add("id is required");
        else if (!IdPattern.IsMatch(entry.Id))
            errors.Add($"id '{entry.Id}' does not match pattern ^[a-z]{{2,3}}-[A-Z0-9]{{1,12}}-[a-z0-9-]+-\\d{{3,4}}$");

        // Source
        if (entry.Source is null)
        {
            errors.Add("source is required");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(entry.Source.FileName))
                errors.Add($"[{entry.Id}] source.fileName is required");
            if (string.IsNullOrWhiteSpace(entry.Source.Extension))
                errors.Add($"[{entry.Id}] source.extension is required");
            if (entry.Source.SizeBytes < 0)
                errors.Add($"[{entry.Id}] source.sizeBytes must be >= 0");
        }

        // Tags
        if (entry.Tags is null || entry.Tags.Length == 0)
            errors.Add($"[{entry.Id}] tags must have at least one element");

        // Difficulty
        if (string.IsNullOrWhiteSpace(entry.Difficulty))
            errors.Add($"[{entry.Id}] difficulty is required");
        else if (!ValidDifficulties.Contains(entry.Difficulty))
            errors.Add($"[{entry.Id}] difficulty '{entry.Difficulty}' is not valid (expected: {string.Join(", ", ValidDifficulties)})");

        // Expected
        if (entry.Expected is null)
        {
            errors.Add($"[{entry.Id}] expected is required");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(entry.Expected.Category))
                errors.Add($"[{entry.Id}] expected.category is required");
            else if (!ValidCategories.Contains(entry.Expected.Category))
                errors.Add($"[{entry.Id}] expected.category '{entry.Expected.Category}' is not valid");

            // ConsoleKey is optional (null for negative controls/unknown) but must be valid if present
            if (!string.IsNullOrEmpty(entry.Expected.ConsoleKey) && !_validConsoleKeys.Contains(entry.Expected.ConsoleKey))
                errors.Add($"[{entry.Id}] expected.consoleKey '{entry.Expected.ConsoleKey}' is not a known system");

            if (entry.Expected.DatMatchLevel is not null && !ValidDatMatchLevels.Contains(entry.Expected.DatMatchLevel))
                errors.Add($"[{entry.Id}] expected.datMatchLevel '{entry.Expected.DatMatchLevel}' is not valid");

            if (entry.Expected.SortDecision is not null && !ValidSortDecisions.Contains(entry.Expected.SortDecision))
                errors.Add($"[{entry.Id}] expected.sortDecision '{entry.Expected.SortDecision}' is not valid");
        }

        // DetectionExpectations (optional, but validate if present)
        if (entry.DetectionExpectations is not null)
        {
            if (entry.DetectionExpectations.PrimaryMethod is not null &&
                !ValidPrimaryMethods.Contains(entry.DetectionExpectations.PrimaryMethod))
            {
                errors.Add($"[{entry.Id}] detectionExpectations.primaryMethod '{entry.DetectionExpectations.PrimaryMethod}' is not valid");
            }

            if (entry.DetectionExpectations.AcceptableAlternatives is not null)
            {
                foreach (var alt in entry.DetectionExpectations.AcceptableAlternatives)
                {
                    if (!ValidPrimaryMethods.Contains(alt))
                        errors.Add($"[{entry.Id}] detectionExpectations.acceptableAlternatives contains invalid method '{alt}'");
                }
            }

            if (entry.DetectionExpectations.AcceptableConsoleKeys is not null)
            {
                foreach (var key in entry.DetectionExpectations.AcceptableConsoleKeys)
                {
                    if (!_validConsoleKeys.Contains(key))
                        errors.Add($"[{entry.Id}] detectionExpectations.acceptableConsoleKeys contains unknown system '{key}'");
                }
            }
        }

        // FileModel (optional, but validate if present)
        if (entry.FileModel?.Type is not null && !ValidFileModelTypes.Contains(entry.FileModel.Type))
        {
            errors.Add($"[{entry.Id}] fileModel.type '{entry.FileModel.Type}' is not valid");
        }

        return errors;
    }

    /// <summary>
    /// Validates all entries. Returns dictionary of entry ID to error list.
    /// Only entries with errors are included.
    /// </summary>
    public Dictionary<string, List<string>> ValidateAll(IEnumerable<GroundTruthEntry> entries)
    {
        var errors = new Dictionary<string, List<string>>();

        foreach (var entry in entries)
        {
            var entryErrors = Validate(entry);
            if (entryErrors.Count > 0)
                errors[entry.Id ?? "(null)"] = entryErrors;
        }

        return errors;
    }
}
