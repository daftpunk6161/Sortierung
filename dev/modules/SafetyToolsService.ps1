function Get-SafetyPolicyProfiles {
  $systemDrive = [Environment]::GetEnvironmentVariable('SystemDrive')
  if ([string]::IsNullOrWhiteSpace($systemDrive)) { $systemDrive = 'C:' }
  $usersRoot = Join-Path $systemDrive 'Users'

  $profiles = [ordered]@{}
  $profiles['Conservative'] = [pscustomobject]@{
    Name = 'Conservative'
    Strict = $true
    ProtectedPaths = @(
      'C:\Windows',
      'C:\Program Files',
      'C:\Program Files (x86)',
      $usersRoot
    )
  }
  $profiles['Balanced'] = [pscustomobject]@{
    Name = 'Balanced'
    Strict = $true
    ProtectedPaths = @(
      'C:\Windows',
      'C:\Program Files',
      'C:\Program Files (x86)'
    )
  }
  $profiles['Expert'] = [pscustomobject]@{
    Name = 'Expert'
    Strict = $false
    ProtectedPaths = @(
      'C:\Windows'
    )
  }
  return $profiles
}

function Invoke-SafetyPolicyProfileApply {
  [CmdletBinding()]
  param(
    [Parameter(Mandatory=$true)][string]$Profile
  )

  $profiles = Get-SafetyPolicyProfiles
  if (-not $profiles.Contains($Profile)) {
    throw "Unbekanntes Safety-Profil: $Profile"
  }

  $selected = $profiles[$Profile]
  return [pscustomobject]@{
    Name = [string]$selected.Name
    Strict = [bool]$selected.Strict
    ProtectedPaths = @($selected.ProtectedPaths)
    ProtectedPathsText = (@($selected.ProtectedPaths) -join ',')
  }
}

function Resolve-SafetyNormalizedPath {
  param([string]$Path)

  if ([string]::IsNullOrWhiteSpace($Path)) { return $null }
  return (Resolve-RootPath -Path $Path)
}

function Invoke-SafetySandboxRun {
  param(
    [string[]]$Roots,
    [string]$TrashRoot,
    [string]$AuditRoot,
    [bool]$StrictSafety,
    [string]$ProtectedPathsText,
    [bool]$UseDat,
    [string]$DatRoot,
    [bool]$ConvertEnabled,
    [hashtable]$ToolOverrides,
    [string[]]$Extensions,
    [scriptblock]$Log
  )

  $blockers = New-Object System.Collections.Generic.List[string]
  $warnings = New-Object System.Collections.Generic.List[string]
  $recommendations = New-Object System.Collections.Generic.List[string]
  $pathChecks = New-Object System.Collections.Generic.List[psobject]

  $normalizePath = {
    param([string]$Path)
    $r = Resolve-NormalizedPath -Path $Path
    if ($null -eq $r -and -not [string]::IsNullOrWhiteSpace($Path)) { return $Path }
    return $r
  }

  $protectedPaths = New-Object System.Collections.Generic.List[string]
  foreach ($entry in @([string]$ProtectedPathsText -split '[,;]')) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }
    $candidate = & $normalizePath $entry.Trim()
    if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
    [void]$protectedPaths.Add($candidate)
  }

  $rootList = New-Object System.Collections.Generic.List[string]
  foreach ($root in @($Roots)) {
    if ([string]::IsNullOrWhiteSpace([string]$root)) { continue }
    $resolvedRoot = & $normalizePath ([string]$root)
    if (-not [string]::IsNullOrWhiteSpace($resolvedRoot)) {
      [void]$rootList.Add($resolvedRoot)
    }
  }

  if ($rootList.Count -eq 0) {
    [void]$blockers.Add('Mindestens 1 ROM-Ordner erforderlich.')
    [void]$recommendations.Add('Fix: Root-Ordner hinzufügen (Button, Drag&Drop oder Ctrl+V).')
  }

  foreach ($rootPath in @($rootList)) {
    if (-not (Test-Path -LiteralPath $rootPath -PathType Container)) {
      [void]$blockers.Add(('Root nicht gefunden: {0}' -f $rootPath))
      [void]$recommendations.Add(('Fix: Root-Pfad korrigieren oder entfernen: {0}' -f $rootPath))
    }
  }

  $extCount = @($Extensions | Where-Object { -not [string]::IsNullOrWhiteSpace([string]$_) }).Count
  if ($extCount -eq 0) {
    [void]$blockers.Add('Keine Dateiendungen gesetzt.')
    [void]$recommendations.Add('Fix: Mindestens eine Endung setzen (z.B. .chd,.iso).')
  }

  $trashResolved = if ([string]::IsNullOrWhiteSpace($TrashRoot)) { $null } else { & $normalizePath $TrashRoot }
  $auditResolved = if ([string]::IsNullOrWhiteSpace($AuditRoot)) { $null } else { & $normalizePath $AuditRoot }

  if ($UseDat) {
    if ([string]::IsNullOrWhiteSpace($DatRoot)) {
      [void]$blockers.Add('DAT aktiviert, aber DatRoot fehlt.')
      [void]$recommendations.Add('Fix: DAT-Ordner setzen oder DAT deaktivieren.')
    } elseif (-not (Test-Path -LiteralPath $DatRoot -PathType Container)) {
      [void]$blockers.Add(('DAT-Ordner nicht gefunden: {0}' -f $DatRoot))
      [void]$recommendations.Add('Fix: Existierenden DAT-Ordner auswählen.')
    }
  }

  if (-not $StrictSafety) {
    [void]$warnings.Add('Strict Safety ist deaktiviert.')
    [void]$recommendations.Add('Empfohlen: Strict Safety aktivieren für Move-Läufe.')
  }

  $pathsToInspect = New-Object System.Collections.Generic.List[string]
  foreach ($entry in @($rootList)) { [void]$pathsToInspect.Add([string]$entry) }
  if (-not [string]::IsNullOrWhiteSpace($trashResolved)) { [void]$pathsToInspect.Add([string]$trashResolved) }
  if (-not [string]::IsNullOrWhiteSpace($auditResolved)) { [void]$pathsToInspect.Add([string]$auditResolved) }

  foreach ($candidate in @($pathsToInspect)) {
    $isProtected = $false
    foreach ($protectedBase in @($protectedPaths)) {
      if ([string]::IsNullOrWhiteSpace($protectedBase)) { continue }
      if ($candidate.StartsWith($protectedBase, [StringComparison]::OrdinalIgnoreCase)) {
        $isProtected = $true
        break
      }
    }

    [void]$pathChecks.Add([pscustomobject]@{
      Path = [string]$candidate
      IsProtected = [bool]$isProtected
      Exists = (Test-Path -LiteralPath $candidate)
    })

    if ($StrictSafety -and $isProtected) {
      [void]$blockers.Add(('Safety-Policy blockiert Pfad: {0}' -f $candidate))
      [void]$recommendations.Add(('Fix: Pfad anpassen oder Protected-Paths prüfen: {0}' -f $candidate))
    }
  }

  if ($ConvertEnabled) {
    $hasConvertTool = $false
    foreach ($toolName in @('chdman','dolphintool','7z')) {
      $toolPath = $null
      if ($ToolOverrides -and $ToolOverrides.ContainsKey($toolName) -and -not [string]::IsNullOrWhiteSpace([string]$ToolOverrides[$toolName])) {
        $toolPath = [string]$ToolOverrides[$toolName]
      } else {
        $toolPath = Find-ConversionTool -ToolName $toolName
      }
      if (-not [string]::IsNullOrWhiteSpace($toolPath) -and (Test-Path -LiteralPath $toolPath -PathType Leaf)) {
        $hasConvertTool = $true
        break
      }
    }
    if (-not $hasConvertTool) {
      [void]$warnings.Add('Konvertierung aktiviert, aber kein Conversion-Tool gefunden.')
      [void]$recommendations.Add('Fix: chdman, dolphintool oder 7z im Tool-Setup hinterlegen.')
    }
  }

  $status = 'Ready'
  if ($blockers.Count -gt 0) {
    $status = 'Blocked'
  } elseif ($warnings.Count -gt 0) {
    $status = 'Review'
  }

  $result = [pscustomobject]@{
    Status = $status
    BlockerCount = [int]$blockers.Count
    WarningCount = [int]$warnings.Count
    RootCount = [int]$rootList.Count
    StrictSafety = [bool]$StrictSafety
    UseDat = [bool]$UseDat
    ConvertEnabled = [bool]$ConvertEnabled
    Blockers = @($blockers)
    Warnings = @($warnings)
    Recommendations = @($recommendations | Select-Object -Unique)
    PathChecks = @($pathChecks)
    CheckedAt = (Get-Date).ToUniversalTime().ToString('o')
  }

  if ($Log) {
    & $Log ('Safety Sandbox: status={0}, blockers={1}, warnings={2}' -f $result.Status, $result.BlockerCount, $result.WarningCount)
  }

  return $result
}

function Invoke-ToolSelfTest {
  param(
    [hashtable]$ToolOverrides,
    [int]$TimeoutSeconds = 8,
    [scriptblock]$Log
  )

  if (-not $ToolOverrides) { $ToolOverrides = @{} }
  if ($TimeoutSeconds -lt 1) { $TimeoutSeconds = 1 }

  $spec = @(
    @{ Name = 'chdman'; Label = 'chdman'; ProbeArgs = @('-help') },
    @{ Name = 'dolphintool'; Label = 'DolphinTool'; ProbeArgs = @('--help') },
    @{ Name = '7z'; Label = '7z'; ProbeArgs = @('-h') },
    @{ Name = 'psxtract'; Label = 'psxtract'; ProbeArgs = @('-h') },
    @{ Name = 'ciso'; Label = 'ciso'; ProbeArgs = @('--help') }
  )

  $results = New-Object System.Collections.Generic.List[psobject]
  foreach ($entry in $spec) {
    $name = [string]$entry.Name
    $source = 'auto-detect'
    $path = $null
    if ($ToolOverrides.ContainsKey($name) -and -not [string]::IsNullOrWhiteSpace([string]$ToolOverrides[$name])) {
      $source = 'override'
      $path = [string]$ToolOverrides[$name]
    } else {
      $path = Find-ConversionTool -ToolName $name
    }

    if ([string]::IsNullOrWhiteSpace($path) -or -not (Test-Path -LiteralPath $path -PathType Leaf)) {
      [void]$results.Add([pscustomobject]@{
        Name = $name
        Label = [string]$entry.Label
        Path = $path
        Source = $source
        Found = $false
        Healthy = $false
        Version = $null
        ExitCode = $null
        Message = 'Nicht gefunden'
      })
      continue
    }

    $version = $null
    try {
      $file = Get-Item -LiteralPath $path -ErrorAction Stop
      $version = $file.VersionInfo.ProductVersion
      if ([string]::IsNullOrWhiteSpace($version)) { $version = $file.VersionInfo.FileVersion }
    } catch {
      $version = $null
    }

    $probeOut = New-TemporaryFile
    $probeErr = New-TemporaryFile
    $proc = $null
    $timedOut = $false
    $exitCode = $null
    $message = 'OK'
    try {
      $argLine = ConvertTo-ArgString -ArgList @($entry.ProbeArgs)
      $startArgs = @{
        FilePath = $path
        PassThru = $true
        WindowStyle = 'Hidden'
        RedirectStandardOutput = $probeOut
        RedirectStandardError = $probeErr
      }
      if ($argLine) { $startArgs.ArgumentList = $argLine }
      $proc = Start-Process @startArgs

      if (-not $proc.WaitForExit($TimeoutSeconds * 1000)) {
        $timedOut = $true
        try { $proc.Kill() } catch { }
      }
      if (-not $timedOut) { $exitCode = [int]$proc.ExitCode }

      $healthy = (-not $timedOut) -and ($exitCode -in @(0,1,2))
      if ($timedOut) {
        $message = 'Timeout beim Probe-Start'
      } elseif (-not $healthy) {
        $stdErr = Get-Content -LiteralPath $probeErr -Raw -ErrorAction SilentlyContinue
        if ([string]::IsNullOrWhiteSpace($stdErr)) {
          $stdErr = Get-Content -LiteralPath $probeOut -Raw -ErrorAction SilentlyContinue
        }
        $message = if ([string]::IsNullOrWhiteSpace($stdErr)) { "Probe fehlgeschlagen (ExitCode=$exitCode)" } else { ([string]$stdErr).Trim() }
      }

      [void]$results.Add([pscustomobject]@{
        Name = $name
        Label = [string]$entry.Label
        Path = $path
        Source = $source
        Found = $true
        Healthy = $healthy
        Version = $version
        ExitCode = $exitCode
        Message = $message
      })
    } catch {
      [void]$results.Add([pscustomobject]@{
        Name = $name
        Label = [string]$entry.Label
        Path = $path
        Source = $source
        Found = $true
        Healthy = $false
        Version = $version
        ExitCode = $null
        Message = $_.Exception.Message
      })
    } finally {
      Remove-Item -LiteralPath $probeOut -Force -ErrorAction SilentlyContinue
      Remove-Item -LiteralPath $probeErr -Force -ErrorAction SilentlyContinue
    }
  }

  $healthyCount = @($results | Where-Object { $_.Healthy }).Count
  $missingCount = @($results | Where-Object { -not $_.Found }).Count
  $warningCount = @($results | Where-Object { $_.Found -and -not $_.Healthy }).Count

  if ($Log) {
    & $Log ("Tool Self-Test: OK={0}, Fehlt={1}, Warnung={2}" -f $healthyCount, $missingCount, $warningCount)
    foreach ($entry in $results) {
      $status = if (-not $entry.Found) { 'FEHLT' } elseif ($entry.Healthy) { 'OK' } else { 'WARNUNG' }
      $versionText = if ([string]::IsNullOrWhiteSpace([string]$entry.Version)) { '-' } else { [string]$entry.Version }
      & $Log ("  [{0}] {1} ({2}) v={3}" -f $status, $entry.Label, $entry.Path, $versionText)
    }
  }

  return [pscustomobject]@{
    Results = @($results)
    HealthyCount = $healthyCount
    MissingCount = $missingCount
    WarningCount = $warningCount
  }
}
