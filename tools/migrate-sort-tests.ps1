$files = @(
  'src/Romulus.Tests/SortAndDashboardCoverageTests.cs',
  'src/Romulus.Tests/Sorting/MultiDiscSetIntegrityTests.cs',
  'src/Romulus.Tests/Sorting/ArcadeSetIntegrityTests.cs',
  'src/Romulus.Tests/Sorting/BiosEndToEndTests.cs',
  'src/Romulus.Tests/Phase6QualityAssuranceTests.cs',
  'src/Romulus.Tests/Phase2RecognitionQualityTests.cs',
  'src/Romulus.Tests/HardCoreInvariantRegressionSuiteTests.cs',
  'src/Romulus.Tests/HardRegressionInvariantTests.cs',
  'src/Romulus.Tests/AuditCDRedTests.cs',
  'src/Romulus.Tests/TrackerAllFindingsBatch3RedTests.cs',
  'src/Romulus.Tests/TrackerBlock1To6RedTests.cs'
)
foreach ($f in $files) {
  if (-not (Test-Path $f)) { continue }
  $c = Get-Content $f -Raw
  $pattern1 = '\.SortWithAutoSortDecisions\((?<args>(?:[^()]|\((?:[^()]|\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))*)*)\)'
  $c = [regex]::Replace($c, $pattern1, { param($m) '.Sort(' + $m.Groups['args'].Value + ')' })
  $pattern2 = '\.Sort\((?<args>(?:[^()]|\((?:[^()]|\((?:[^()]|\((?:[^()]|\([^()]*\))*\))*\))*)*)\)'
  $c = [regex]::Replace($c, $pattern2, { param($m) $a = $m.Groups['args'].Value; if ($a -match 'enrichedSortDecisions') { return $m.Value }; return '.SortWithAutoSortDecisions(' + $a + ')' })
  Set-Content $f $c -NoNewline
  Write-Host ('Processed: ' + $f)
}
