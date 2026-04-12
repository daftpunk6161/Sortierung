using System.Text.Json;

namespace Romulus.Tests.TestFixtures;

internal static class CliSummaryJsonParser
{
    internal static JsonDocument ParseSummary(string stdout, string? stderr = null, params string[] requiredProperties)
    {
        var text = string.IsNullOrWhiteSpace(stdout) ? (stderr ?? string.Empty) : stdout;

        if (TryParseSummaryPayload(text, requiredProperties, out var summary))
            return summary;

        var start = text.IndexOf('{');
        if (start < 0)
            return JsonDocument.Parse(text);

        var end = text.LastIndexOf('}');
        if (end > start)
            return JsonDocument.Parse(text[start..(end + 1)]);

        return JsonDocument.Parse(text[start..]);
    }

    private static bool TryParseSummaryPayload(string text, IReadOnlyList<string> requiredProperties, out JsonDocument summary)
    {
        summary = null!;
        JsonDocument? lastMatching = null;

        var depth = 0;
        var inString = false;
        var escaped = false;
        var start = -1;

        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];

            if (inString)
            {
                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (ch == '\\')
                {
                    escaped = true;
                    continue;
                }

                if (ch == '"')
                    inString = false;

                continue;
            }

            if (ch == '"')
            {
                inString = true;
                continue;
            }

            if (ch == '{')
            {
                if (depth == 0)
                    start = i;

                depth++;
                continue;
            }

            if (ch != '}' || depth == 0)
                continue;

            depth--;
            if (depth != 0 || start < 0)
                continue;

            var candidate = text[start..(i + 1)];
            try
            {
                var doc = JsonDocument.Parse(candidate);
                if (IsMatchingSummary(doc.RootElement, requiredProperties))
                {
                    lastMatching?.Dispose();
                    lastMatching = doc;
                }
                else
                {
                    doc.Dispose();
                }
            }
            catch (JsonException)
            {
                // Ignore unrelated or malformed brace fragments in captured console output.
            }
        }

        if (lastMatching is null)
            return false;

        summary = lastMatching;
        return true;
    }

    private static bool IsMatchingSummary(JsonElement root, IReadOnlyList<string> requiredProperties)
    {
        if (root.ValueKind != JsonValueKind.Object)
            return false;

        if (requiredProperties.Count == 0)
            return root.TryGetProperty("TotalFiles", out _);

        foreach (var property in requiredProperties)
        {
            if (!root.TryGetProperty(property, out _))
                return false;
        }

        return true;
    }
}