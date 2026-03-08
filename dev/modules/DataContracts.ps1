function Get-RomCleanupDataDirectory {
  param([string]$ModuleRoot = $PSScriptRoot)

  $probe = New-Object System.Collections.Generic.List[string]
  if (-not [string]::IsNullOrWhiteSpace($ModuleRoot)) {
    [void]$probe.Add((Join-Path (Split-Path -Parent (Split-Path -Parent $ModuleRoot)) 'data'))
    [void]$probe.Add((Join-Path (Split-Path -Parent $ModuleRoot) 'data'))
  }
  try {
    $cwd = (Get-Location).Path
    if (-not [string]::IsNullOrWhiteSpace($cwd)) {
      [void]$probe.Add((Join-Path $cwd 'data'))
    }
  } catch { }

  foreach ($candidate in $probe) {
    if (Test-Path -LiteralPath $candidate -PathType Container) { return $candidate }
  }

  return $null
}

function Import-RomCleanupJsonData {
  param(
    [Parameter(Mandatory=$true)][string]$FileName
  )

  $dataDir = Get-RomCleanupDataDirectory
  if ([string]::IsNullOrWhiteSpace($dataDir)) { return $null }

  $path = Join-Path $dataDir $FileName
  if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { return $null }

  try {
    $raw = Get-Content -LiteralPath $path -Raw -ErrorAction Stop
    if ([string]::IsNullOrWhiteSpace($raw)) { return $null }
    return (Copy-ObjectDeep -Value ($raw | ConvertFrom-Json -ErrorAction Stop))
  } catch {
    return $null
  }
}

function Test-JsonPayloadSchema {
  param(
    [Parameter(Mandatory=$true)][object]$Payload,
    [Parameter(Mandatory=$true)][hashtable]$Schema,
    [System.Collections.Generic.List[string]]$Errors
  )

  if (-not $Errors) {
    $Errors = New-Object System.Collections.Generic.List[string]
  }

  $obj = Copy-ObjectDeep -Value $Payload
  if (-not ($obj -is [System.Collections.IDictionary])) {
    [void]$Errors.Add('payload must be object')
    return [pscustomobject]@{ IsValid = $false; Errors = @($Errors) }
  }

  # Detect schema format: Draft 2020-12 uses lowercase 'required'/'properties' and '$schema' key
  $isDraft = ($Schema.Contains('$schema') -or
              ($Schema.Contains('properties') -and -not $Schema.Contains('Properties')) -or
              ($Schema.Contains('required') -and -not $Schema.Contains('Required')))

  # --- Required-Felder ---
  $requiredKey = if ($isDraft) { 'required' } else { 'Required' }
  $required = @()
  if ($Schema.ContainsKey($requiredKey)) { $required = @($Schema[$requiredKey]) }
  foreach ($key in $required) {
    if (-not $obj.Contains([string]$key)) {
      [void]$Errors.Add(('missing: {0}' -f [string]$key))
    }
  }

  # --- Properties / Typprüfung ---
  $propsKey = if ($isDraft) { 'properties' } else { 'Properties' }
  if ($Schema.ContainsKey($propsKey)) {
    $props = $Schema[$propsKey]
    foreach ($entry in $props.GetEnumerator()) {
      $name = [string]$entry.Key
      if (-not $obj.Contains($name)) { continue }

      $value = $obj[$name]
      $decl = $entry.Value
      # Resolve declared type: Draft 2020-12 uses { type: "string", enum: [...] }
      # Legacy uses plain string like "string"
      $type = ''
      $enumValues = $null
      if ($decl -is [string]) {
        $type = [string]$decl
      } elseif ($decl -is [System.Collections.IDictionary]) {
        if ($decl.Contains('type') -or $decl.Contains('Type')) {
          $type = if ($decl.Contains('type')) { [string]$decl['type'] } else { [string]$decl['Type'] }
        }
        if ($decl.Contains('enum')) { $enumValues = @($decl['enum']) }
      }

      # Type validation (note: PowerShell ConvertFrom-Json may deserialize ISO 8601 strings as [datetime])
      switch ($type) {
        'string' {
          if ($null -ne $value -and -not ($value -is [string]) -and -not ($value -is [datetime])) { [void]$Errors.Add(('type mismatch: {0} expected string' -f $name)) }
        }
        'boolean' {
          if ($null -ne $value -and -not ($value -is [bool])) { [void]$Errors.Add(('type mismatch: {0} expected boolean' -f $name)) }
        }
        'bool' {
          if ($null -ne $value -and -not ($value -is [bool])) { [void]$Errors.Add(('type mismatch: {0} expected bool' -f $name)) }
        }
        'number' {
          if ($null -ne $value -and -not ($value -is [int] -or $value -is [long] -or $value -is [double] -or $value -is [decimal])) {
            [void]$Errors.Add(('type mismatch: {0} expected number' -f $name))
          }
        }
        'integer' {
          if ($null -ne $value -and -not ($value -is [int] -or $value -is [long])) {
            [void]$Errors.Add(('type mismatch: {0} expected integer' -f $name))
          }
        }
        'array' {
          if ($null -ne $value -and -not ($value -is [System.Array] -or $value -is [System.Collections.IList])) { [void]$Errors.Add(('type mismatch: {0} expected array' -f $name)) }
        }
        'object' {
          if ($null -ne $value -and -not ($value -is [System.Collections.IDictionary])) {
            [void]$Errors.Add(('type mismatch: {0} expected object' -f $name))
          }
        }
      }

      # Enum validation (Draft 2020-12)
      if ($enumValues -and $null -ne $value) {
        $match = $false
        foreach ($ev in $enumValues) { if ($value -eq $ev) { $match = $true; break } }
        if (-not $match) {
          [void]$Errors.Add(('enum violation: {0} must be one of [{1}]' -f $name, ($enumValues -join ', ')))
        }
      }
    }
  }

  # --- additionalProperties (Draft 2020-12) ---
  if ($isDraft -and $Schema.ContainsKey('additionalProperties') -and $Schema['additionalProperties'] -eq $false) {
    $allowedKeys = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    if ($Schema.ContainsKey($propsKey)) {
      foreach ($pk in $Schema[$propsKey].Keys) { [void]$allowedKeys.Add([string]$pk) }
    }
    foreach ($objKey in @($obj.Keys)) {
      if (-not $allowedKeys.Contains([string]$objKey)) {
        [void]$Errors.Add(('additional property not allowed: {0}' -f [string]$objKey))
      }
    }
  }

  return [pscustomobject]@{ IsValid = ($Errors.Count -eq 0); Errors = @($Errors) }
}

function Get-RomCleanupSchema {
  param([Parameter(Mandatory=$true)][string]$Name)

  if (Get-Command Import-RomCleanupJsonData -ErrorAction SilentlyContinue) {
    $schemaFile = switch ($Name) {
      'settings-v1' { 'schemas/settings.schema.json' }
      'profile-v1' { 'schemas/profile.schema.json' }
      'profiles-v1' { 'schemas/profiles.schema.json' }
      'plugin-manifest-v1' { 'schemas/plugin-manifest.schema.json' }
      'console-plugin-v1' { 'schemas/console-plugin.schema.json' }
      'defaults-v1' { 'schemas/defaults.schema.json' }
      'rules-v1' { 'schemas/rules.schema.json' }
      'console-maps-v1' { 'schemas/console-maps.schema.json' }
      default { $null }
    }
    if (-not [string]::IsNullOrWhiteSpace([string]$schemaFile)) {
      $externalSchema = Import-RomCleanupJsonData -FileName $schemaFile
      if ($externalSchema) { return $externalSchema }
    }
  }

  switch ($Name) {
    'settings-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        required = @('toolPaths','dat','general')
        properties = @{
          schemaVersion = @{ type = 'string' }
          toolPaths = @{ type = 'object' }
          dat = @{ type = 'object' }
          general = @{ type = 'object' }
          rules = @{ type = 'object' }
        }
        additionalProperties = $true
      }
    }
    'profile-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        required = @('schemaVersion','name','settings')
        properties = @{
          schemaVersion = @{ type = 'string' }
          name = @{ type = 'string' }
          settings = @{ type = 'object' }
          exportedUtc = @{ type = 'string' }
        }
        additionalProperties = $false
      }
    }
    'profiles-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        required = @('schemaVersion','profiles')
        properties = @{
          schemaVersion = @{ type = 'string' }
          profiles = @{ type = 'object' }
        }
        additionalProperties = $false
      }
    }
    'plugin-manifest-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        required = @('id','schemaVersion','version','capabilities')
        properties = @{
          id = @{ type = 'string' }
          schemaVersion = @{ type = 'string' }
          version = @{ type = 'string' }
          capabilities = @{ type = 'array' }
        }
        additionalProperties = $true
      }
    }
    'console-plugin-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        required = @('name')
        properties = @{
          name = @{ type = 'string' }
          folderMap = @{ type = 'object' }
          extMap = @{ type = 'object' }
          regexMap = @{ type = 'object' }
        }
        additionalProperties = $true
      }
    }
    'defaults-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        properties = @{
          mode = @{ type = 'string'; enum = @('DryRun','Move') }
          datRoot = @{ type = 'string' }
          trashRoot = @{ type = 'string' }
          auditRoot = @{ type = 'string' }
          extensions = @{ type = 'string' }
          logLevel = @{ type = 'string' }
          theme = @{ type = 'string' }
          locale = @{ type = 'string' }
        }
        additionalProperties = $false
      }
    }
    'rules-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        required = @('RegionOrdered')
        properties = @{
          LangPattern = @{ type = 'string' }
          RegionOrdered = @{ type = 'array' }
        }
        additionalProperties = $true
      }
    }
    'console-maps-v1' {
      return @{
        '$schema' = 'https://json-schema.org/draft/2020-12/schema'
        type = 'object'
        required = @('ConsoleFolderMap')
        properties = @{
          ConsoleFolderMap = @{ type = 'object' }
          ConsoleExtMap = @{ type = 'object' }
          ConsoleRegexMap = @{ type = 'object' }
          DatConsoleKeyMap = @{ type = 'object' }
        }
        additionalProperties = $true
      }
    }
    default { return $null }
  }
}
