<#
.SYNOPSIS
    Split FeatureService.cs into partial class files by functional category (RF-005).
#>
$ErrorActionPreference = 'Stop'
$path = "c:\Code\Sortierung\src\RomCleanup.UI.Wpf\Services\FeatureService.cs"
$lines = [System.IO.File]::ReadAllLines($path)
$total = $lines.Count

# ── Method/field start lines (1-indexed) → category ──────────────────
# Each entry: line (1-indexed) → category
$map = @{}
# Shared utilities (stay in main file)
foreach ($l in @(26,115,275,579,994,1045,1201,1210)) { $map[$l] = "Shared" }
# Analysis
foreach ($l in @(83,95,125,235,813,843,861,929,949,972,1000,1019,1119,1129,1157,1172,1320,2573,2597,2614,2627)) { $map[$l] = "Analysis" }
# Conversion
foreach ($l in @(40,48,71,358,650,671,698,716,2103,2401,2478,2504,2539,2638)) { $map[$l] = "Conversion" }
# Dat
foreach ($l in @(1417,1458,1473,1616,1637,1827,1863,2043,2438,2449,2557,2561)) { $map[$l] = "Dat" }
# Collection
foreach ($l in @(1369,1378,1391,1408,1735,1779,1925,2186,2199)) { $map[$l] = "Collection" }
# Security
foreach ($l in @(374,449,453,463,470,498,502,529,589,611,629,1187,1542,1589)) { $map[$l] = "Security" }
# Workflow
foreach ($l in @(326,686,1064,1076,1673,2134,2305,2346,2362)) { $map[$l] = "Workflow" }
# Export
foreach ($l in @(153,173,181,201,250,289,1488,1984,2513)) { $map[$l] = "Export" }
# Infra
foreach ($l in @(733,750,773,804,1239,1254,1280,1305,2243,2368,2381,2389)) { $map[$l] = "Infra" }

# Sort by line number
$sorted = $map.GetEnumerator() | Sort-Object { [int]$_.Key } | ForEach-Object { [pscustomobject]@{Line=[int]$_.Key; Cat=$_.Value} }

# Find the class closing brace line (2644 expected)
$classEndLine = -1
for ($i = $total - 1; $i -ge 0; $i--) {
    if ($lines[$i].Trim() -eq '}') { $classEndLine = $i + 1; break }  # 1-indexed
}
Write-Host "Class ends at line $classEndLine"

# ── Extract blocks ────────────────────────────────────────────────────
# For each method, find its block:
#   Start: scan backward from method line to find first line that's part of this method's section
#          (preceding comment lines, blank lines, section headers)
#   End: line just before the next method's block start
$blocks = @{}  # category → list of line ranges (0-indexed arrays)

for ($idx = 0; $idx -lt $sorted.Count; $idx++) {
    $entry = $sorted[$idx]
    $methodLine = $entry.Line - 1  # 0-indexed
    $cat = $entry.Cat

    # Scan backward for preceding comments/blank lines (including // ═══ headers)
    $blockStart = $methodLine
    while ($blockStart -gt 0) {
        $prevLine = $lines[$blockStart - 1].TrimStart()
        if ($prevLine -eq '' -or $prevLine.StartsWith('//') -or $prevLine.StartsWith('/// ') -or $prevLine.StartsWith('///')) {
            $blockStart--
        } else {
            break
        }
    }
    # Don't go before the previous block's last content line
    if ($idx -gt 0) {
        $prevMethodLine = $sorted[$idx - 1].Line - 1
        if ($blockStart -le $prevMethodLine) { $blockStart = $prevMethodLine + 1 }
    }
    # Don't go before the class opening brace (line 22, 0-indexed = 21)
    if ($blockStart -le 21) { $blockStart = 22 }

    # End: up to (next method's block start - 1), or class end
    if ($idx -lt $sorted.Count - 1) {
        $nextMethodLine = $sorted[$idx + 1].Line - 1
        # Scan backward from next method to find its block start
        $nextBlockStart = $nextMethodLine
        while ($nextBlockStart -gt 0) {
            $prevLine = $lines[$nextBlockStart - 1].TrimStart()
            if ($prevLine -eq '' -or $prevLine.StartsWith('//') -or $prevLine.StartsWith('/// ') -or $prevLine.StartsWith('///')) {
                $nextBlockStart--
            } else {
                break
            }
        }
        $blockEnd = $nextBlockStart - 1
    } else {
        $blockEnd = $classEndLine - 2  # Don't include the closing brace
    }

    if (-not $blocks.ContainsKey($cat)) { $blocks[$cat] = [System.Collections.Generic.List[string]]::new() }
    for ($li = $blockStart; $li -le $blockEnd; $li++) {
        $blocks[$cat].Add($lines[$li])
    }
    # Add a blank separator between methods
    $blocks[$cat].Add("")
}

# ── Build usings header ──────────────────────────────────────────────
$usings = @()
for ($i = 0; $i -lt $total; $i++) {
    $line = $lines[$i]
    if ($line.StartsWith('using ') -or $line -eq '') { $usings += $line }
    elseif ($line.StartsWith('namespace ')) { break }
}
$usingsBlock = $usings -join "`n"

# ── Write partial files ──────────────────────────────────────────────
$dir = Split-Path $path
$categories = @("Analysis","Conversion","Dat","Collection","Security","Workflow","Export","Infra")

foreach ($cat in $categories) {
    if (-not $blocks.ContainsKey($cat)) {
        Write-Host "SKIP: No blocks for category '$cat'"
        continue
    }
    $fileName = "FeatureService.$cat.cs"
    $filePath = Join-Path $dir $fileName
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.AppendLine($usingsBlock)
    [void]$sb.AppendLine("namespace RomCleanup.UI.Wpf.Services;")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("public static partial class FeatureService")
    [void]$sb.AppendLine("{")
    foreach ($blockLine in $blocks[$cat]) {
        [void]$sb.AppendLine($blockLine)
    }
    [void]$sb.AppendLine("}")
    [System.IO.File]::WriteAllText($filePath, $sb.ToString())
    Write-Host "Created $fileName ($($blocks[$cat].Count) lines)"
}

# ── Rewrite main file with only Shared methods + records ─────────────
$sb = [System.Text.StringBuilder]::new()
# Usings
[void]$sb.AppendLine($usingsBlock)
[void]$sb.AppendLine("namespace RomCleanup.UI.Wpf.Services;")
[void]$sb.AppendLine()
# Class header (lines 17-22, 0-indexed 16-21)
for ($i = 16; $i -le 21; $i++) { [void]$sb.AppendLine($lines[$i]) }

# Shared blocks
if ($blocks.ContainsKey("Shared")) {
    foreach ($blockLine in $blocks["Shared"]) {
        [void]$sb.AppendLine($blockLine)
    }
}
[void]$sb.AppendLine("}")
[void]$sb.AppendLine()

# Records (from classEndLine to end of file)
for ($i = $classEndLine; $i -lt $total; $i++) { [void]$sb.AppendLine($lines[$i]) }

[System.IO.File]::WriteAllText($path, $sb.ToString())
Write-Host "`nRewritten main FeatureService.cs ($($blocks['Shared'].Count + 30) lines approx)"
Write-Host "Done!"
