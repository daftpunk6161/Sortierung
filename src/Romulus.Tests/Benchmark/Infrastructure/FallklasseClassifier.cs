namespace Romulus.Tests.Benchmark.Infrastructure;

/// <summary>
/// Maps ground-truth entry tags to FC-XX Fallklassen (case classes).
/// An entry can match multiple Fallklassen if it has multiple relevant tags.
/// </summary>
internal static class FallklasseClassifier
{
    /// <summary>
    /// Returns all FC-XX codes that apply to an entry based on its tags.
    /// </summary>
    public static HashSet<string> Classify(IReadOnlyList<string> tags)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (tags is null || tags.Count == 0)
            return result;

        var tagSet = new HashSet<string>(tags, StringComparer.OrdinalIgnoreCase);

        if (tagSet.Contains("clean-reference"))
            result.Add("FC-01");
        if (tagSet.Contains("wrong-name"))
            result.Add("FC-02");
        if (tagSet.Contains("header-conflict") || tagSet.Contains("header-vs-headerless-pair"))
            result.Add("FC-03");
        if (tagSet.Contains("wrong-extension") || tagSet.Contains("extension-conflict"))
            result.Add("FC-04");
        if (tagSet.Contains("folder-header-conflict"))
            result.Add("FC-05");
        if (tagSet.Contains("dat-exact-match"))
            result.Add("FC-06");
        if (tagSet.Contains("dat-weak") || tagSet.Contains("dat-none"))
            result.Add("FC-07");
        if (tagSet.Contains("bios"))
            result.Add("FC-08");
        if (tagSet.Contains("bios-wrong-name") || tagSet.Contains("bios-wrong-folder")
            || tagSet.Contains("bios-false-positive") || tagSet.Contains("bios-shared"))
            result.Add("FC-08");
        if (tagSet.Contains("parent") || tagSet.Contains("clone")
            || tagSet.Contains("arcade-parent") || tagSet.Contains("arcade-clone"))
            result.Add("FC-09");
        if (tagSet.Contains("multi-disc"))
            result.Add("FC-10");
        if (tagSet.Contains("multi-file") || tagSet.Contains("cue-bin") || tagSet.Contains("gdi-tracks")
            || tagSet.Contains("ccd-img") || tagSet.Contains("mds-mdf") || tagSet.Contains("m3u-playlist"))
            result.Add("FC-11");
        if (tagSet.Contains("archive-inner"))
            result.Add("FC-12");
        if (tagSet.Contains("directory-based"))
            result.Add("FC-13");
        if (tagSet.Contains("expected-unknown"))
            result.Add("FC-14");
        if (tagSet.Contains("ambiguous"))
            result.Add("FC-15");
        if (tagSet.Contains("negative-control"))
            result.Add("FC-16");
        if (tagSet.Contains("sort-blocked") || tagSet.Contains("repair-safety"))
            result.Add("FC-17");
        if (tagSet.Contains("cross-system"))
            result.Add("FC-18");
        if (tagSet.Contains("junk") || tagSet.Contains("non-game"))
            result.Add("FC-19");
        if (tagSet.Contains("corrupt") || tagSet.Contains("truncated") || tagSet.Contains("broken-set")
            || tagSet.Contains("corrupt-archive") || tagSet.Contains("truncated-rom"))
            result.Add("FC-20");

        // Additional tag aliases for schema-aligned tags
        if (tagSet.Contains("region-variant") || tagSet.Contains("revision-variant"))
            result.Add("FC-01");
        if (tagSet.Contains("dat-exact"))
            result.Add("FC-06");
        if (tagSet.Contains("unknown-expected"))
            result.Add("FC-14");
        if (tagSet.Contains("cross-system-ambiguity") || tagSet.Contains("gb-gbc-ambiguity")
            || tagSet.Contains("md-32x-ambiguity") || tagSet.Contains("ps-disambiguation")
            || tagSet.Contains("arcade-confusion-split-merged") || tagSet.Contains("arcade-confusion-merged-nonmerged"))
            result.Add("FC-18");
        if (tagSet.Contains("confidence-low") || tagSet.Contains("confidence-borderline")
            || tagSet.Contains("repair-unsafe"))
            result.Add("FC-17");
        if (tagSet.Contains("folder-only-detection"))
            result.Add("FC-05");
        if (tagSet.Contains("folder-vs-header-conflict"))
            result.Add("FC-05");
        if (tagSet.Contains("dat-tosec") || tagSet.Contains("dat-nointro") || tagSet.Contains("dat-redump"))
            result.Add("FC-06");
        if (tagSet.Contains("demo") || tagSet.Contains("homebrew") || tagSet.Contains("hack"))
            result.Add("FC-19");

        return result;
    }

    /// <summary>
    /// All 20 FC codes with human-readable names.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> FallklasseNames =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["FC-01"] = "Saubere Referenz",
            ["FC-02"] = "Falsch benannt",
            ["FC-03"] = "Header-Konflikt",
            ["FC-04"] = "Extension-Konflikt",
            ["FC-05"] = "Folder-vs-Header",
            ["FC-06"] = "DAT exact",
            ["FC-07"] = "DAT weak/none",
            ["FC-08"] = "BIOS",
            ["FC-09"] = "Parent/Clone",
            ["FC-10"] = "Multi-Disc",
            ["FC-11"] = "Multi-File",
            ["FC-12"] = "Archive-Inner",
            ["FC-13"] = "Directory-based",
            ["FC-14"] = "UNKNOWN expected",
            ["FC-15"] = "Ambiguous",
            ["FC-16"] = "Negative Control",
            ["FC-17"] = "Repair/Sort-blocked",
            ["FC-18"] = "Cross-System",
            ["FC-19"] = "Junk/NonGame",
            ["FC-20"] = "Kaputte Sets",
        };

    public static int TotalFallklassen => FallklasseNames.Count;
}
