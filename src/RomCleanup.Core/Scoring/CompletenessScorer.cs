using RomCleanup.Core.SetParsing;

namespace RomCleanup.Core.Scoring;

/// <summary>
/// Calculates a completeness score for ROM candidates based on DAT match,
/// set membership and file integrity. Pure domain logic — no I/O.
/// Extracted from EnrichmentPipelinePhase (ADR-0007 §3.2).
/// </summary>
public static class CompletenessScorer
{
    /// <summary>
    /// Compute the completeness score for a ROM file.
    /// </summary>
    /// <param name="filePath">Full path to the ROM file (used to check missing set members).</param>
    /// <param name="ext">Lowercase file extension including leading dot.</param>
    /// <param name="setMembers">Related files parsed from the set descriptor (CUE/GDI/CCD/M3U).</param>
    /// <param name="datMatch">Whether the file matched a DAT entry.</param>
    /// <returns>Score: +50 for DAT match, +50 for complete set, -50 for incomplete, +25 for standalone.</returns>
    public static int Calculate(string filePath, string ext, IReadOnlyList<string> setMembers, bool datMatch)
    {
        int score = 0;

        if (datMatch)
            score += 50;

        if (ext is ".cue" or ".gdi" or ".ccd" or ".m3u")
        {
            var missing = ext switch
            {
                ".cue" => CueSetParser.GetMissingFiles(filePath),
                ".gdi" => GdiSetParser.GetMissingFiles(filePath),
                ".ccd" => CcdSetParser.GetMissingFiles(filePath),
                ".m3u" => M3uPlaylistParser.GetMissingFiles(filePath),
                _ => Array.Empty<string>()
            };

            score += missing.Count == 0 ? 50 : -50;
        }
        else if (setMembers.Count == 0)
        {
            score += 25;
        }

        return score;
    }
}
