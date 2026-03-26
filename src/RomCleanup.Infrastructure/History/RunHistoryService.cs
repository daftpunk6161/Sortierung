using System.Text.Json;
using RomCleanup.Contracts.Models;

namespace RomCleanup.Infrastructure.History;

/// <summary>
/// [v2.1 deferred] Run history browser — reads move-plan JSON files.
/// Mirrors RunHistory.ps1. Not wired into RunOrchestrator pipeline yet.
/// </summary>
public sealed class RunHistoryService
{
    /// <summary>
    /// Retrieves all previous runs from move-plan JSON files in the reports directory.
    /// </summary>
    public RunHistoryResult GetHistory(string reportsDirectory, int maxEntries = 100)
    {
        if (!Directory.Exists(reportsDirectory))
            return new RunHistoryResult();

        var planFiles = Directory.GetFiles(reportsDirectory, "move-plan-*.json")
            .OrderByDescending(f => f)
            .ToArray();

        var entries = new List<RunHistoryEntry>();
        foreach (var file in planFiles.Take(maxEntries))
        {
            try
            {
                var json = File.ReadAllText(file);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;

                var entry = new RunHistoryEntry
                {
                    FileName = Path.GetFileName(file),
                    Date = File.GetLastWriteTimeUtc(file)
                };

                if (root.TryGetProperty("Roots", out var rootsEl) && rootsEl.ValueKind == JsonValueKind.Array)
                    entry.Roots = rootsEl.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();
                else if (root.TryGetProperty("roots", out rootsEl) && rootsEl.ValueKind == JsonValueKind.Array)
                    entry.Roots = rootsEl.EnumerateArray().Select(e => e.GetString() ?? "").ToArray();

                if (root.TryGetProperty("Mode", out var modeEl))
                    entry.Mode = modeEl.GetString() ?? "";
                else if (root.TryGetProperty("mode", out modeEl))
                    entry.Mode = modeEl.GetString() ?? "";

                if (root.TryGetProperty("Status", out var statusEl))
                    entry.Status = statusEl.GetString() ?? "";
                else if (root.TryGetProperty("status", out statusEl))
                    entry.Status = statusEl.GetString() ?? "";

                if (root.TryGetProperty("FileCount", out var fcEl) && fcEl.TryGetInt32(out var fc))
                    entry.FileCount = fc;
                else if (root.TryGetProperty("fileCount", out fcEl) && fcEl.TryGetInt32(out fc))
                    entry.FileCount = fc;
                else if (root.TryGetProperty("TotalFiles", out fcEl) && fcEl.TryGetInt32(out fc))
                    entry.FileCount = fc;

                entries.Add(entry);
            }
            catch (JsonException)
            {
                // Skip malformed plan files
            }
        }

        return new RunHistoryResult
        {
            Entries = entries,
            Total = planFiles.Length
        };
    }

    /// <summary>
    /// Gets detail from a specific run plan JSON.
    /// </summary>
    public Dictionary<string, object?>? GetDetail(string planFilePath)
    {
        if (!File.Exists(planFilePath))
            return null;

        try
        {
            var json = File.ReadAllText(planFilePath);
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
