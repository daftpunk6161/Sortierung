using System.Globalization;
using System.IO;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Tools;
using Romulus.Infrastructure.Reporting;

namespace Romulus.UI.Wpf.Services;

public static partial class FeatureService
{

    // ═══ DAT FILE COMPARE ══════════════════════════════════════════════
    // Delegates to DatAnalysisService (single source of truth).

    public static DatDiffResult CompareDatFiles(string pathA, string pathB)
        => DatAnalysisService.CompareDatFiles(pathA, pathB);


    // ═══ LOGIQX XML GENERATOR ══════════════════════════════════════════
    // Generate Logiqx XML string for a single game entry.

    public static string GenerateLogiqxEntry(string gameName, string romName, string crc, string sha1, long size)
    {
        var doc = new XDocument(
            new XDeclaration("1.0", "UTF-8", null),
            new XElement("datafile",
                new XElement("game",
                    new XAttribute("name", gameName),
                    new XElement("description", gameName),
                    new XElement("rom",
                        new XAttribute("name", romName),
                        new XAttribute("size", size),
                        new XAttribute("crc", crc),
                        new XAttribute("sha1", sha1)))));

        return doc.Declaration + Environment.NewLine + doc.Root;
    }


    /// <summary>
    /// Append a custom DAT entry (Logiqx XML fragment) to <paramref name="datRoot"/>/custom.dat.
    /// Creates the file if it doesn't exist. Atomic write via temp+move.
    /// </summary>
    public static void AppendCustomDatEntry(string datRoot, string xmlEntry)
    {
        var customDatPath = Path.Combine(datRoot, "custom.dat");
        if (File.Exists(customDatPath))
        {
            var content = File.ReadAllText(customDatPath);
            var closeTag = "</datafile>";
            var idx = content.LastIndexOf(closeTag, StringComparison.OrdinalIgnoreCase);
            content = idx >= 0
                ? content[..idx] + xmlEntry + "\n" + closeTag
                : content + "\n" + xmlEntry;
            var tempPath = customDatPath + ".tmp";
            File.WriteAllText(tempPath, content);
            File.Move(tempPath, customDatPath, overwrite: true);
        }
        else
        {
            var fullXml = "<?xml version=\"1.0\"?>\n" +
                          "<!DOCTYPE datafile SYSTEM \"http://www.logiqx.com/Dats/datafile.dtd\">\n" +
                          "<datafile>\n" +
                          "  <header>\n" +
                          "    <name>Custom DAT</name>\n" +
                          "    <description>Benutzerdefinierte DAT-Einträge</description>\n" +
                          "  </header>\n" +
                          xmlEntry + "\n" +
                          "</datafile>";
            File.WriteAllText(customDatPath, fullXml);
        }
    }


    // ═══ COVER SCRAPER ═════════════════════════════════════════════════

    /// <summary>
    /// Format a DatDiffResult into a human-readable report.
    /// </summary>
    public static string FormatDatDiffReport(string fileA, string fileB, DatDiffResult diff)
    {
        var sb = new StringBuilder();
        sb.AppendLine("DAT-Diff-Viewer (Logiqx XML)");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  A: {Path.GetFileName(fileA)}");
        sb.AppendLine($"  B: {Path.GetFileName(fileB)}");
        sb.AppendLine($"\n  Gleich:       {diff.UnchangedCount}");
        sb.AppendLine($"  Geändert:     {diff.ModifiedCount}");
        sb.AppendLine($"  Hinzugefügt:  {diff.Added.Count}");
        sb.AppendLine($"  Entfernt:     {diff.Removed.Count}");

        if (diff.Added.Count > 0)
        {
            sb.AppendLine($"\n  --- Hinzugefügt (erste {Math.Min(30, diff.Added.Count)}) ---");
            foreach (var name in diff.Added.Take(30))
                sb.AppendLine($"    + {name}");
            if (diff.Added.Count > 30)
                sb.AppendLine($"    … und {diff.Added.Count - 30} weitere");
        }

        if (diff.Removed.Count > 0)
        {
            sb.AppendLine($"\n  --- Entfernt (erste {Math.Min(30, diff.Removed.Count)}) ---");
            foreach (var name in diff.Removed.Take(30))
                sb.AppendLine($"    - {name}");
            if (diff.Removed.Count > 30)
                sb.AppendLine($"    … und {diff.Removed.Count - 30} weitere");
        }

        return sb.ToString();
    }


    /// <summary>
    /// Build a DAT auto-update status report.
    /// Delegates to <see cref="DatAnalysisService.BuildDatAutoUpdateReport"/> (single source of truth).
    /// </summary>
    public static (string Report, int LocalCount, int OldCount) BuildDatAutoUpdateReport(string datRoot)
        => DatAnalysisService.BuildDatAutoUpdateReport(datRoot);


    // ═══ ARCADE MERGE/SPLIT REPORT ══════════════════════════════════════

    /// <summary>
    /// Parse a MAME/FBNEO DAT file and build a merge/split analysis report.
    /// </summary>
    public static string BuildArcadeMergeSplitReport(string datPath)
    {
        var settings = new XmlReaderSettings
        {
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null
        };
        using var reader = XmlReader.Create(datPath, settings);
        var doc = XDocument.Load(reader);

        var games = doc.Descendants("game").ToList();
        if (games.Count == 0)
            games = doc.Descendants("machine").ToList();

        var parents = games.Where(g => g.Attribute("cloneof") == null).ToList();
        var clones = games.Where(g => g.Attribute("cloneof") != null).ToList();

        var cloneMap = clones.GroupBy(g => g.Attribute("cloneof")?.Value ?? "")
            .ToDictionary(g => g.Key, g => g.ToList());

        var totalRoms = games.Sum(g => g.Descendants("rom").Count());
        var largestParent = parents.OrderByDescending(p =>
        {
            var name = p.Attribute("name")?.Value ?? "";
            return cloneMap.TryGetValue(name, out var c) ? c.Count : 0;
        }).FirstOrDefault();
        var largestName = largestParent?.Attribute("name")?.Value ?? "?";
        var largestCloneCount = cloneMap.TryGetValue(largestName, out var lc) ? lc.Count : 0;

        var sb = new StringBuilder();
        sb.AppendLine("Arcade Merge/Split Analyse");
        sb.AppendLine(new string('═', 50));
        sb.AppendLine($"\n  DAT: {Path.GetFileName(datPath)}");
        sb.AppendLine($"  Einträge gesamt: {games.Count}");
        sb.AppendLine($"  Parents:         {parents.Count}");
        sb.AppendLine($"  Clones:          {clones.Count}");
        sb.AppendLine($"  ROMs gesamt:     {totalRoms}");
        sb.AppendLine($"\n  Größte Familie: {largestName} ({largestCloneCount} Clones)");
        sb.AppendLine($"\n  Set-Typ-Empfehlung:");
        sb.AppendLine($"    Non-Merged: {parents.Count + clones.Count} Sets (portabel, groß)");
        sb.AppendLine($"    Split:      {parents.Count + clones.Count} Sets (Clones nur diff)");
        sb.AppendLine($"    Merged:     {parents.Count} Sets (Parents enthalten alles)");

        var top10 = parents
            .Select(p => new { Name = p.Attribute("name")?.Value ?? "?",
                Clones = cloneMap.TryGetValue(p.Attribute("name")?.Value ?? "", out var cc) ? cc.Count : 0 })
            .OrderByDescending(x => x.Clones)
            .Take(10);
        sb.AppendLine($"\n  Top 10 Parents (meiste Clones):");
        foreach (var p in top10)
            sb.AppendLine($"    {p.Name,-30} {p.Clones} Clones");

        return sb.ToString();
    }


    /// <summary>Import a DAT file into datRoot with path-traversal protection.</summary>
    public static string ImportDatFileToRoot(string sourcePath, string datRoot)
    {
        var safeName = Path.GetFileName(sourcePath);
        var targetPath = Path.GetFullPath(Path.Combine(datRoot, safeName));
        if (!targetPath.StartsWith(Path.GetFullPath(datRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Pfad außerhalb des DatRoot.");
        File.Copy(sourcePath, targetPath, overwrite: true);
        return targetPath;
    }


    /// <summary>Validate and parse an FTP/SFTP URL. Returns report text.</summary>
    public static (bool Valid, bool IsPlainFtp, string Report) BuildFtpSourceReport(string input)
    {
        var isValid = input.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase) ||
                      input.StartsWith("sftp://", StringComparison.OrdinalIgnoreCase);
        if (!isValid)
            return (false, false, $"Ungültige FTP-URL: {input} (muss mit ftp:// oder sftp:// beginnen)");

        var isPlainFtp = input.StartsWith("ftp://", StringComparison.OrdinalIgnoreCase);
        try
        {
            var uri = new Uri(input);
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("FTP-Quelle konfiguriert\n");
            sb.AppendLine($"  Protokoll: {uri.Scheme.ToUpperInvariant()}");
            sb.AppendLine($"  Host:      {uri.Host}");
            sb.AppendLine($"  Port:      {(uri.Port > 0 ? uri.Port : (uri.Scheme == "sftp" ? 22 : 21))}");
            sb.AppendLine($"  Pfad:      {uri.AbsolutePath}");
            sb.AppendLine("\n  ℹ FTP-Download ist noch nicht implementiert.");
            sb.AppendLine("  Aktuell wird die URL nur registriert und angezeigt.");
            sb.AppendLine("  Geplantes Feature: Dateien vor Verarbeitung lokal cachen.");
            return (true, isPlainFtp, sb.ToString());
        }
        catch (Exception ex)
        {
            return (false, isPlainFtp, $"FTP-URL ungültig: {ex.Message}");
        }
    }


    /// <summary>Validate a hex hash string (CRC32=8, SHA1=40 chars).</summary>
    private static readonly TimeSpan HexRxTimeout = TimeSpan.FromMilliseconds(200);

    public static bool IsValidHexHash(string hash, int expectedLength) =>
        hash.Length == expectedLength && Regex.IsMatch(hash, $"^[0-9A-Fa-f]{{{expectedLength}}}$", RegexOptions.None, HexRxTimeout);


    /// <summary>Build custom DAT XML entry using SecurityElement.Escape for safe XML.</summary>
    public static string BuildCustomDatXmlEntry(string gameName, string romName, string crc32, string sha1)
    {
        return $"  <game name=\"{System.Security.SecurityElement.Escape(gameName)}\">\n" +
               $"    <description>{System.Security.SecurityElement.Escape(gameName)}</description>\n" +
               $"    <rom name=\"{System.Security.SecurityElement.Escape(romName)}\" size=\"0\" crc=\"{crc32}\"" +
               (sha1.Length > 0 ? $" sha1=\"{sha1}\"" : "") + " />\n" +
               $"  </game>";
    }

}
