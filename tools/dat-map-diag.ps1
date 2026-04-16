$dataDir = "c:\Code\Sortierung\data"
$datRoot = "C:\dat"

$catalog = Get-Content "$dataDir\dat-catalog.json" -Raw | ConvertFrom-Json
$allDats = Get-ChildItem $datRoot -Recurse -File -Filter "*.dat" | Select-Object -ExpandProperty FullName | Sort-Object

Write-Host "Catalog entries: $($catalog.Count)"
Write-Host "DAT files on disk: $($allDats.Count)"
Write-Host ""

$map = @{}
$supplemental = @{}
$catalogMatchedPaths = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)

foreach ($entry in $catalog) {
    if ([string]::IsNullOrWhiteSpace($entry.ConsoleKey)) { continue }
    $alreadyMapped = $map.ContainsKey($entry.ConsoleKey)
    
    $found = $false
    $resolvedPath = $null
    
    foreach ($stem in @($entry.Id, $entry.System, $entry.ConsoleKey)) {
        if ([string]::IsNullOrWhiteSpace($stem)) { continue }
        $m = $allDats | Where-Object { [IO.Path]::GetFileNameWithoutExtension($_) -eq $stem } | Select-Object -First 1
        if ($m) { $resolvedPath = $m; $found = $true; break }
    }
    
    if (-not $found -and -not [string]::IsNullOrWhiteSpace($entry.PackMatch)) {
        $prefix = $entry.PackMatch.TrimEnd('*')
        $m = $allDats | Where-Object {
            [IO.Path]::GetFileNameWithoutExtension($_).StartsWith($prefix, [System.StringComparison]::OrdinalIgnoreCase)
        } | Sort-Object -Descending | Select-Object -First 1
        if ($m) { $resolvedPath = $m; $found = $true }
    }
    
    if ($found -and $resolvedPath) {
        [void]$catalogMatchedPaths.Add($resolvedPath)
        if (-not $alreadyMapped) {
            $map[$entry.ConsoleKey] = $resolvedPath
        } else {
            if (-not $supplemental.ContainsKey($entry.ConsoleKey)) { $supplemental[$entry.ConsoleKey] = @() }
            if ($map[$entry.ConsoleKey] -ne $resolvedPath) {
                $supplemental[$entry.ConsoleKey] += $resolvedPath
            }
        }
    }
}

Write-Host "=== CATALOG PHASE ==="
Write-Host "Primary console keys matched: $($map.Count)"
$supplCount = 0
$supplemental.Values | ForEach-Object { $supplCount += $_.Count }
Write-Host "Supplemental DATs: $supplCount"
Write-Host "Catalog-matched DAT files: $($catalogMatchedPaths.Count)"
Write-Host ""

$stemFallbackCount = 0
$stemKeys = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
foreach ($k in $map.Keys) { [void]$stemKeys.Add($k) }

foreach ($f in $allDats) {
    if ($catalogMatchedPaths.Contains($f)) { continue }
    $stem = [IO.Path]::GetFileNameWithoutExtension($f).ToUpperInvariant()
    if (-not $stemKeys.Contains($stem)) {
        $stemFallbackCount++
        [void]$stemKeys.Add($stem)
    }
}

Write-Host "=== STEM FALLBACK ==="
Write-Host "Extra console keys via stem: $stemFallbackCount"
Write-Host ""
Write-Host "=== TOTAL ==="
Write-Host "Total unique console keys: $($stemKeys.Count)"
Write-Host "Unmapped DAT files (stem collision): $($allDats.Count - $catalogMatchedPaths.Count - $stemFallbackCount)"
