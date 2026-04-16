// Quick diagnostic: how many DATs does BuildConsoleMap find?
// Run: dotnet script tools/DatDiag.csx

var dataDir = @"c:\Code\Sortierung\data";
var datRoot = @"C:\dat";

// Count all .dat/.xml files recursively
var allDats = Directory.GetFiles(datRoot, "*.*", SearchOption.AllDirectories)
    .Where(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
    .ToArray();

Console.WriteLine($"Total DAT/XML files on disk: {allDats.Length}");

// Load catalog
var catalogPath = Path.Combine(dataDir, "dat-catalog.json");
var catalogJson = File.ReadAllText(catalogPath);

// Simple count of "ConsoleKey" entries
var matches = System.Text.RegularExpressions.Regex.Matches(catalogJson, "\"ConsoleKey\"");
Console.WriteLine($"Catalog entries (approx): {matches.Count}");

// Count unique stems
var stems = allDats.Select(f => Path.GetFileNameWithoutExtension(f).ToUpperInvariant()).Distinct().Count();
Console.WriteLine($"Unique DAT stems: {stems}");

// Show folders
foreach (var dir in Directory.GetDirectories(datRoot))
{
    var count = Directory.GetFiles(dir, "*.*", SearchOption.AllDirectories)
        .Count(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase));
    Console.WriteLine($"  {Path.GetFileName(dir)}: {count} DATs");
}
