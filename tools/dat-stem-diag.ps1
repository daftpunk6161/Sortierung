$dataDir = "c:\Code\Sortierung\data"
$datRoot = "C:\dat"

$consoles = Get-Content "$dataDir\consoles.json" -Raw | ConvertFrom-Json
$allKeys = @{}
foreach ($c in $consoles) {
    if ($c.Key) { $allKeys[$c.Key] = $c }
}

$dats = Get-ChildItem $datRoot -Recurse -File -Filter "*.dat"
$validCount = 0
$invalidCount = 0
$resolvedByConsoleDetector = 0

$invalidSamples = @()

foreach ($d in $dats) {
    $stem = [IO.Path]::GetFileNameWithoutExtension($d.Name).ToUpperInvariant()
    if ($stem -match '^[A-Z0-9_-]+$') {
        $validCount++
    } else {
        $invalidCount++
        if ($invalidSamples.Count -lt 30) {
            $invalidSamples += $stem
        }
    }
}

Write-Host "Valid console key stems: $validCount"
Write-Host "Invalid console key stems (need ConsoleDetector): $invalidCount"
Write-Host ""
Write-Host "=== SAMPLE INVALID STEMS (first 30) ==="
$invalidSamples | ForEach-Object { Write-Host $_ }
