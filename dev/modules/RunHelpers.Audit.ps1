# NOTE:
# Extracted from RunHelpers.ps1 for audit signing and rollback responsibilities.

# Use AppDomain data for process-global HMAC key caching. This is immune to
# PowerShell scope isolation issues in dot-sourced modules and Pester tests.
# AppDomain.CurrentDomain.GetData/SetData persist across all scope boundaries.

function Get-AuditSigningKeyBytes {
  # BUG-015 FIX: Prefer in-memory session key over environment variable.
  # Environment variables are readable by all processes in the same user context.
  $cached = [AppDomain]::CurrentDomain.GetData('_RomCleanup_AuditHmacKeyBytes')
  if ($cached) { return $cached }

  $rawKey = [string]$env:ROMCLEANUP_AUDIT_HMAC_KEY
  if ([string]::IsNullOrWhiteSpace($rawKey)) {
    # Generate a session-scoped random key (32 bytes / 256 bit) if not configured
    $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
    $keyBytes = [byte[]]::new(32)
    $rng.GetBytes($keyBytes)
    $rng.Dispose()
    [AppDomain]::CurrentDomain.SetData('_RomCleanup_AuditHmacKeyBytes', $keyBytes)
    return $keyBytes
  }
  # BUG-041 FIX: Enforce minimum key length (32 bytes) for HMAC security
  if ($rawKey.Length -lt 32) {
    Write-Warning '[SEC] ROMCLEANUP_AUDIT_HMAC_KEY ist zu kurz (< 32 Zeichen). Audit-Signierung deaktiviert. Bitte einen Key mit mindestens 32 Zeichen setzen.'
    return $null
  }
  $keyBytes = [System.Text.Encoding]::UTF8.GetBytes($rawKey)
  [AppDomain]::CurrentDomain.SetData('_RomCleanup_AuditHmacKeyBytes', $keyBytes)
  return $keyBytes
}

function Get-FileSha256Hex {
  param([Parameter(Mandatory=$true)][string]$Path)

  $hash = Get-FileHash -LiteralPath $Path -Algorithm SHA256 -ErrorAction Stop
  return [string]$hash.Hash.ToLowerInvariant()
}

function Get-AuditSignaturePayload {
  param(
    [Parameter(Mandatory=$true)][string]$AuditFileName,
    [Parameter(Mandatory=$true)][string]$CsvSha256,
    [Parameter(Mandatory=$true)][int]$RowCount,
    [Parameter(Mandatory=$true)][string]$CreatedUtc
  )

  return ('v1|{0}|{1}|{2}|{3}' -f $AuditFileName, $CsvSha256, $RowCount, $CreatedUtc)
}

function Get-HmacSha256Hex {
  param(
    [Parameter(Mandatory=$true)][byte[]]$Key,
    [Parameter(Mandatory=$true)][string]$Text
  )

  $hmac = [System.Security.Cryptography.HMACSHA256]::new($Key)
  try {
    $bytes = [System.Text.Encoding]::UTF8.GetBytes($Text)
    $hashBytes = $hmac.ComputeHash($bytes)
    return ([BitConverter]::ToString($hashBytes) -replace '-', '').ToLowerInvariant()
  } finally {
    $hmac.Dispose()
  }
}

function Write-AuditMetadataSidecar {
  param(
    [Parameter(Mandatory=$true)][string]$AuditCsvPath,
    [Parameter(Mandatory=$true)][object[]]$Rows,
    [scriptblock]$Log
  )

  if (-not (Test-Path -LiteralPath $AuditCsvPath -PathType Leaf)) { return $null }

  $csvSha256 = Get-FileSha256Hex -Path $AuditCsvPath
  $metaPath = [System.IO.Path]::ChangeExtension($AuditCsvPath, '.meta.json')
  $createdUtc = [DateTime]::UtcNow.ToString('o')
  $auditLeaf = [System.IO.Path]::GetFileName($AuditCsvPath)
  $rowCount = @($Rows).Count

  $actionCounts = [ordered]@{}
  foreach ($row in @($Rows)) {
    $action = [string]$row.Action
    if ([string]::IsNullOrWhiteSpace($action)) { continue }
    if (-not $actionCounts.Contains($action)) { $actionCounts[$action] = 0 }
    $actionCounts[$action] = [int]$actionCounts[$action] + 1
  }

  $payload = Get-AuditSignaturePayload -AuditFileName $auditLeaf -CsvSha256 $csvSha256 -RowCount $rowCount -CreatedUtc $createdUtc
  $keyBytes = Get-AuditSigningKeyBytes
  $signature = $null
  $signed = $false
  if ($keyBytes) {
    $signature = Get-HmacSha256Hex -Key $keyBytes -Text $payload
    $signed = $true
  }

  $meta = [ordered]@{
    Version = 1
    AuditFile = $auditLeaf
    CreatedUtc = $createdUtc
    CsvSha256 = $csvSha256
    RowCount = $rowCount
    ActionCounts = $actionCounts
    SignatureAlgorithm = 'HMACSHA256'
    Signed = $signed
    Signature = $signature
  }

  Write-JsonFile -Path $metaPath -Data $meta -Depth 8
  if ($Log) {
    if ($signed) {
      & $Log ("Audit-Meta (signiert): {0}" -f $metaPath)
    } else {
      & $Log ("Audit-Meta (ohne Signatur): {0}" -f $metaPath)
    }
  }
  return $metaPath
}

function Test-AuditMetadataSidecar {
  param(
    [Parameter(Mandatory=$true)][string]$AuditCsvPath,
    [scriptblock]$Log
  )

  if (-not (Test-Path -LiteralPath $AuditCsvPath -PathType Leaf)) {
    throw ("Audit-CSV nicht gefunden: {0}" -f $AuditCsvPath)
  }

  $metaPath = [System.IO.Path]::ChangeExtension($AuditCsvPath, '.meta.json')
  $requireMeta = [string]$env:ROMCLEANUP_AUDIT_REQUIRE_META
  $requireSigned = [string]$env:ROMCLEANUP_AUDIT_REQUIRE_SIGNED
  $isRequireMeta = (-not [string]::IsNullOrWhiteSpace($requireMeta)) -and ($requireMeta.Trim().ToLowerInvariant() -in @('1','true','yes','on'))
  $isRequireSigned = (-not [string]::IsNullOrWhiteSpace($requireSigned)) -and ($requireSigned.Trim().ToLowerInvariant() -in @('1','true','yes','on'))

  if (-not (Test-Path -LiteralPath $metaPath -PathType Leaf)) {
    if ($isRequireMeta -or $isRequireSigned) {
      throw ("Audit-Verifikation fehlgeschlagen: Meta-Datei erforderlich, fehlt aber ({0})" -f $metaPath)
    }
    if ($Log) { & $Log ("WARN: Audit-Meta fehlt (legacy/unsigniert): {0}" -f $metaPath) }
    return $true
  }

  $metaRaw = Get-Content -LiteralPath $metaPath -Raw -Encoding UTF8 -ErrorAction Stop
  $meta = $metaRaw | ConvertFrom-Json

  # ConvertFrom-Json auto-converts ISO 8601 strings to DateTime objects.
  # When cast back to [string], the locale format differs from the original ISO 8601.
  # Extract the original CreatedUtc string directly from the raw JSON to preserve it.
  $createdUtcOriginal = [string]$meta.CreatedUtc
  if ($metaRaw -match '"CreatedUtc"\s*:\s*"([^"]+)"') {
    $createdUtcOriginal = $Matches[1]
  }

  $currentCsvSha = Get-FileSha256Hex -Path $AuditCsvPath
  if ([string]::IsNullOrWhiteSpace([string]$meta.CsvSha256) -or $currentCsvSha -ne [string]$meta.CsvSha256) {
    throw ("Audit-Verifikation fehlgeschlagen: CSV-Hash stimmt nicht ({0})" -f $AuditCsvPath)
  }

  $isSigned = [bool]$meta.Signed
  if (-not $isSigned) {
    if ($isRequireSigned) {
      throw ("Audit-Verifikation fehlgeschlagen: Signierte Meta erforderlich, aber Audit ist unsigniert ({0})" -f $metaPath)
    }
    if ($Log) { & $Log ("Audit-Meta validiert (unsigniert): {0}" -f $metaPath) }
    return $true
  }

  $keyBytes = Get-AuditSigningKeyBytes
  if (-not $keyBytes) {
    throw ("Audit-Verifikation fehlgeschlagen: Signatur vorhanden, aber ROMCLEANUP_AUDIT_HMAC_KEY fehlt ({0})" -f $metaPath)
  }

  $payload = Get-AuditSignaturePayload -AuditFileName ([string]$meta.AuditFile) -CsvSha256 ([string]$meta.CsvSha256) `
    -RowCount ([int]$meta.RowCount) -CreatedUtc $createdUtcOriginal
  $expected = Get-HmacSha256Hex -Key $keyBytes -Text $payload
  if ([string]::IsNullOrWhiteSpace([string]$meta.Signature) -or $expected -ne [string]$meta.Signature) {
    throw ("Audit-Verifikation fehlgeschlagen: Signatur ungueltig ({0})" -f $metaPath)
  }

  if ($Log) { & $Log ("Audit-Meta validiert (signiert): {0}" -f $metaPath) }
  return $true
}

function Invoke-AuditRollback {
  <# Roll back a previous move run using a rom-move-audit CSV (reverse order). #>
  param(
    [Parameter(Mandatory=$true)][string]$AuditCsvPath,
    [string[]]$AllowedRestoreRoots,
    [string[]]$AllowedCurrentRoots,
    [switch]$DryRun,
    [scriptblock]$Log
  )

  if ([string]::IsNullOrWhiteSpace($AuditCsvPath)) {
    throw 'AuditCsvPath ist leer.'
  }
  if (-not (Test-Path -LiteralPath $AuditCsvPath -PathType Leaf)) {
    throw ("Audit-CSV nicht gefunden: {0}" -f $AuditCsvPath)
  }

  [void](Test-AuditMetadataSidecar -AuditCsvPath $AuditCsvPath -Log $Log)

  $rows = @()
  try {
    $rows = @(Import-Csv -LiteralPath $AuditCsvPath -Encoding UTF8)
  } catch {
    throw ("Audit-CSV konnte nicht gelesen werden: {0}" -f $_.Exception.Message)
  }

  if ($rows.Count -eq 0) {
    return [pscustomobject]@{
      AuditCsvPath      = $AuditCsvPath
      TotalRows         = 0
      EligibleRows      = 0
      SkippedUnsafe     = 0
      RolledBack        = 0
      DryRunPlanned     = 0
      SkippedMissingDest = 0
      SkippedCollision  = 0
      Failed            = 0
      DryRun            = [bool]$DryRun
    }
  }

  $normalizeCsvValue = {
    param([string]$Value)
    if ($null -eq $Value) { return $null }
    $text = [string]$Value
    if ($text.Length -gt 1 -and $text.StartsWith("'")) {
      $candidate = $text.Substring(1)
      $trimmed = $candidate.TrimStart([char[]]@(' ', "`t", "`r", "`n"))
      if ($trimmed -match '^[=+\-@\|]') {
        return $candidate
      }
    }
    return $text
  }

  $normalizeRoots = {
    param([string[]]$Roots)
    $list = New-Object System.Collections.Generic.List[string]
    if (-not $Roots) { return @() }
    foreach ($root in $Roots) {
      if ([string]::IsNullOrWhiteSpace($root)) { continue }
      try {
        $full = [System.IO.Path]::GetFullPath($root)
        [void]$list.Add($full)
      } catch {
        if ($Log) { & $Log ("Rollback Allowlist: invalid root ignored: {0}" -f $root) }
      }
    }
    return @($list)
  }

  $resolveAllowedRoot = {
    param(
      [string]$Path,
      [string[]]$Roots
    )
    if (-not $Roots -or @($Roots).Count -eq 0) { return $null }
    foreach ($candidateRoot in $Roots) {
      if (Test-PathWithinRoot -Path $Path -Root $candidateRoot -DisallowReparsePoints) {
        return $candidateRoot
      }
    }
    return $null
  }

  $normalizedRestoreRoots = & $normalizeRoots $AllowedRestoreRoots
  if ((-not $AllowedCurrentRoots -or @($AllowedCurrentRoots).Count -eq 0) -and @($normalizedRestoreRoots).Count -gt 0) {
    $AllowedCurrentRoots = @($normalizedRestoreRoots)
  }
  $normalizedCurrentRoots = & $normalizeRoots $AllowedCurrentRoots

  $eligible = New-Object System.Collections.Generic.List[psobject]
  $skippedUnsafe = 0
  foreach ($row in $rows) {
    $actionRaw = [string]$row.Action
    $sourceRaw = [string]$row.Source
    $destRaw = [string]$row.Dest
    if ([string]::IsNullOrWhiteSpace($actionRaw) -or [string]::IsNullOrWhiteSpace($sourceRaw) -or [string]::IsNullOrWhiteSpace($destRaw)) { continue }

    $action = (& $normalizeCsvValue $actionRaw).Trim().ToUpperInvariant()
    if ($action -notin @('MOVE','JUNK','BIOS-MOVE')) { continue }

    $source = & $normalizeCsvValue $sourceRaw
    $dest = & $normalizeCsvValue $destRaw
    if ([string]::IsNullOrWhiteSpace($source) -or [string]::IsNullOrWhiteSpace($dest)) { continue }

    $restoreRoot = $null
    if (@($normalizedRestoreRoots).Count -gt 0) {
      $restoreRoot = & $resolveAllowedRoot -Path $source -Roots $normalizedRestoreRoots
      if (-not $restoreRoot) {
        $skippedUnsafe++
        if ($Log) { & $Log ("Rollback skip (unsafe restore target): {0}" -f $source) }
        continue
      }
    }

    $currentRoot = $null
    if (@($normalizedCurrentRoots).Count -gt 0) {
      $currentRoot = & $resolveAllowedRoot -Path $dest -Roots $normalizedCurrentRoots
      if (-not $currentRoot) {
        $skippedUnsafe++
        if ($Log) { & $Log ("Rollback skip (unsafe current location): {0}" -f $dest) }
        continue
      }
    }

    [void]$eligible.Add([pscustomobject]@{
      Action = $action
      Source = $source
      Dest   = $dest
      SourceRoot = $restoreRoot
      DestRoot   = $currentRoot
    })
  }

  $rolledBack = 0
  $dryRunPlanned = 0
  $skippedMissingDest = 0
  $skippedCollision = 0
  $failed = 0
  # BUG-021 FIX: Track all rollback operations for audit trail
  $rollbackTrail = [System.Collections.Generic.List[pscustomobject]]::new()

  for ($i = $eligible.Count - 1; $i -ge 0; $i--) {
    $entry = $eligible[$i]
    $from = [string]$entry.Dest
    $to = [string]$entry.Source

    if (-not (Test-Path -LiteralPath $from)) {
      $skippedMissingDest++
      if ($Log) { & $Log ("Rollback skip (Quelle fehlt): {0}" -f $from) }
      continue
    }
    if (Test-Path -LiteralPath $to) {
      $skippedCollision++
      if ($Log) { & $Log ("Rollback skip (Ziel existiert): {0}" -f $to) }
      continue
    }

    if ($DryRun) {
      $dryRunPlanned++
      if ($Log) { & $Log ("Rollback DRYRUN: {0} -> {1}" -f $from, $to) }
      continue
    }

    try {
      $fromRoot = if (-not [string]::IsNullOrWhiteSpace([string]$entry.DestRoot)) {
        [string]$entry.DestRoot
      } else {
        [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($from))
      }
      $toRoot = if (-not [string]::IsNullOrWhiteSpace([string]$entry.SourceRoot)) {
        [string]$entry.SourceRoot
      } else {
        [System.IO.Path]::GetPathRoot([System.IO.Path]::GetFullPath($to))
      }
      [void](Invoke-RootSafeMove -Source $from -Dest $to -SourceRoot $fromRoot -DestRoot $toRoot)
      $rolledBack++
      # BUG-021 FIX: Track successful rollback entries for audit trail
      [void]$rollbackTrail.Add([pscustomobject]@{ Status='OK'; From=$from; To=$to })
    } catch {
      $failed++
      [void]$rollbackTrail.Add([pscustomobject]@{ Status='FAILED'; From=$from; To=$to; Error=$_.Exception.Message })
      if ($Log) { & $Log ("Rollback FEHLER: {0} -> {1} ({2})" -f $from, $to, $_.Exception.Message) }
    }
  }

  # BUG-021 FIX: Write rollback audit trail CSV if any operations were attempted
  $rollbackAuditPath = $null
  if ($rollbackTrail.Count -gt 0 -and -not $DryRun) {
    $rollbackAuditPath = [System.IO.Path]::ChangeExtension($AuditCsvPath, '.rollback-audit.csv')
    $timestamp = [DateTime]::UtcNow.ToString('o')
    $trailLines = [System.Collections.Generic.List[string]]::new()
    [void]$trailLines.Add('Status,From,To,Error,Timestamp')
    foreach ($t in $rollbackTrail) {
      $errVal = if ($t.PSObject.Properties['Error']) { [string]$t.Error } else { '' }
      $line = '"{0}","{1}","{2}","{3}","{4}"' -f $t.Status, ($t.From -replace '"','""'), ($t.To -replace '"','""'), ($errVal -replace '"','""'), $timestamp
      [void]$trailLines.Add($line)
    }
    try {
      $trailLines -join "`r`n" | Set-Content -LiteralPath $rollbackAuditPath -Encoding UTF8 -Force
      if ($Log) { & $Log ("Rollback-Audit geschrieben: {0}" -f $rollbackAuditPath) }
    } catch {
      if ($Log) { & $Log ("Rollback-Audit konnte nicht geschrieben werden: {0}" -f $_.Exception.Message) }
    }
  }

  return [pscustomobject]@{
    AuditCsvPath       = $AuditCsvPath
    RollbackAuditPath  = $rollbackAuditPath
    TotalRows          = $rows.Count
    EligibleRows       = $eligible.Count
    SkippedUnsafe      = $skippedUnsafe
    RolledBack         = $rolledBack
    DryRunPlanned      = $dryRunPlanned
    SkippedMissingDest = $skippedMissingDest
    SkippedCollision   = $skippedCollision
    Failed             = $failed
    DryRun             = [bool]$DryRun
  }
}
