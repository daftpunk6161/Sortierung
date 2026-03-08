function Export-OpsBundle {
  param(
    [Parameter(Mandatory = $true)][string]$DestinationZipPath,
    [hashtable]$SettingsSnapshot,
    [hashtable]$RulesSnapshot,
    [int]$ReportLimit = 12,
    [int]$AuditLimit = 12
  )

  if ([string]::IsNullOrWhiteSpace($DestinationZipPath)) {
    throw 'DestinationZipPath darf nicht leer sein.'
  }

  $destPath = $DestinationZipPath
  if (-not $destPath.EndsWith('.zip', [StringComparison]::OrdinalIgnoreCase)) {
    $destPath = "$destPath.zip"
  }

  $destDir = Split-Path -Parent $destPath
  if (-not [string]::IsNullOrWhiteSpace($destDir)) {
    Assert-DirectoryExists -Path $destDir
  }

  $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ("romcleanup-opsbundle-{0}" -f ([Guid]::NewGuid().ToString('N')))
  $bundleRoot = Join-Path $tempRoot ("ops-bundle-{0}" -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
  $metaDir = Join-Path $bundleRoot 'meta'
  $artifactsDir = Join-Path $bundleRoot 'artifacts'

  Assert-DirectoryExists -Path $metaDir
  Assert-DirectoryExists -Path $artifactsDir

  $included = New-Object System.Collections.Generic.List[object]

  $addCopy = {
    param([string]$SourcePath, [string]$TargetRelativePath)
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) { return }

    $targetPath = Join-Path $bundleRoot $TargetRelativePath
    $targetParent = Split-Path -Parent $targetPath
    Assert-DirectoryExists -Path $targetParent

    Copy-Item -LiteralPath $SourcePath -Destination $targetPath -Force
    [void]$included.Add([pscustomobject]@{
      source = $SourcePath
      target = $TargetRelativePath
      sizeBytes = (Get-Item -LiteralPath $targetPath).Length
    })
  }

  if ($SettingsSnapshot) {
    $settingsPath = Join-Path $metaDir 'settings.snapshot.json'
    Write-JsonFile -Path $settingsPath -Data $SettingsSnapshot -Depth 8
    [void]$included.Add([pscustomobject]@{ source = '<memory>'; target = 'meta/settings.snapshot.json'; sizeBytes = (Get-Item -LiteralPath $settingsPath).Length })
  }

  if ($RulesSnapshot) {
    $rulesPath = Join-Path $metaDir 'rules.snapshot.json'
    Write-JsonFile -Path $rulesPath -Data $RulesSnapshot -Depth 8
    [void]$included.Add([pscustomobject]@{ source = '<memory>'; target = 'meta/rules.snapshot.json'; sizeBytes = (Get-Item -LiteralPath $rulesPath).Length })
  }

  $toolHashesPath = Join-Path (Get-Location).Path 'data\tool-hashes.json'
  if (Test-Path -LiteralPath $toolHashesPath -PathType Leaf) {
    & $addCopy $toolHashesPath 'meta/tool-hashes.json'
  }

  if (Test-Path -LiteralPath $script:SETTINGS_PATH -PathType Leaf) {
    & $addCopy $script:SETTINGS_PATH 'meta/settings.persisted.json'
  }
  if (Test-Path -LiteralPath $script:PROFILES_PATH -PathType Leaf) {
    & $addCopy $script:PROFILES_PATH 'meta/profiles.persisted.json'
  }

  $telemetryPath = [string](Get-AppStateValue -Key 'UiTelemetryPath' -Default $null)
  if (-not [string]::IsNullOrWhiteSpace($telemetryPath) -and (Test-Path -LiteralPath $telemetryPath -PathType Leaf)) {
    & $addCopy $telemetryPath ('artifacts/telemetry/{0}' -f (Split-Path -Leaf $telemetryPath))
  }

  $reportsDir = Join-Path (Get-Location).Path 'reports'
  if (Test-Path -LiteralPath $reportsDir -PathType Container) {
    $reportFiles = @(Get-ChildItem -LiteralPath $reportsDir -File | Where-Object { $_.Extension -in @('.md', '.json', '.html', '.xml', '.txt') } | Sort-Object LastWriteTime -Descending | Select-Object -First $ReportLimit)
    foreach ($file in $reportFiles) {
      & $addCopy $file.FullName ('artifacts/reports/{0}' -f $file.Name)
    }
  }

  $auditDir = Join-Path (Get-Location).Path 'audit-logs'
  if (Test-Path -LiteralPath $auditDir -PathType Container) {
    $auditFiles = @(Get-ChildItem -LiteralPath $auditDir -File | Where-Object { $_.Extension -in @('.csv', '.json') } | Sort-Object LastWriteTime -Descending | Select-Object -First $AuditLimit)
    foreach ($file in $auditFiles) {
      & $addCopy $file.FullName ('artifacts/audit-logs/{0}' -f $file.Name)
    }
  }

  $manifestPath = Join-Path $metaDir 'manifest.json'
  $manifest = [ordered]@{
    schemaVersion = 'romcleanup-ops-bundle-v1'
    exportedUtc = (Get-Date).ToUniversalTime().ToString('o')
    machine = $env:COMPUTERNAME
    user = $env:USERNAME
    psVersion = $PSVersionTable.PSVersion.ToString()
    fileCount = $included.Count
    files = $included.ToArray()
  }
  Write-JsonFile -Path $manifestPath -Data $manifest -Depth 10

  if (Test-Path -LiteralPath $destPath -PathType Leaf) {
    Remove-Item -LiteralPath $destPath -Force
  }
  Compress-Archive -Path $bundleRoot -DestinationPath $destPath -Force

  try { Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue } catch { }

  return [pscustomobject]@{
    Path = $destPath
    FileCount = $included.Count
  }
}
