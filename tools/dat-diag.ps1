$dats = Get-ChildItem "C:\dat" -Recurse -File -Filter "*.dat"
$groups = $dats | Group-Object { [IO.Path]::GetFileNameWithoutExtension($_.Name).ToUpperInvariant() }
$dupes = $groups | Where-Object { $_.Count -gt 1 }

Write-Host "Total .dat files: $($dats.Count)"
Write-Host "Unique stems: $($groups.Count)"
Write-Host "Duplicate stems (same name, different folders): $($dupes.Count)"
Write-Host ""
Write-Host "=== DUPLICATES ==="
$dupes | Sort-Object Count -Descending | Select-Object -First 30 | ForEach-Object {
    Write-Host "$($_.Count)x $($_.Name)"
    $_.Group | ForEach-Object { Write-Host "  -> $($_.FullName)" }
}
