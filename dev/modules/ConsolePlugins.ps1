function Get-DefaultConsolePluginPath {
  <#
  .SYNOPSIS
    Liefert den Standardpfad für Konsolen-Plugindateien.
  .EXAMPLE
    Get-DefaultConsolePluginPath
  #>
  $root = (Get-Location).Path
  return (Join-Path $root 'plugins\consoles')
}

function Import-ConsolePlugins {
  <#
  .SYNOPSIS
    Lädt externe Konsolen-Plugins (JSON) und merged Mappings in die Laufzeit.
  .PARAMETER Path
    Verzeichnis mit Plugin-JSON-Dateien.
  .PARAMETER Log
    Optionaler Logger-Callback.
  .EXAMPLE
    Import-ConsolePlugins -Path '.\plugins\consoles'
  #>
  param(
    [string]$Path = (Get-DefaultConsolePluginPath),
    [scriptblock]$Log
  )

  $loaded = 0
  $errors = New-Object System.Collections.Generic.List[string]
  if ([string]::IsNullOrWhiteSpace($Path) -or -not (Test-Path -LiteralPath $Path -PathType Container)) {
    return [pscustomobject]@{ Loaded = 0; Errors = @(); Path = $Path }
  }

  $jsonFiles = @(Get-ChildItem -LiteralPath $Path -Filter '*.json' -File -ErrorAction SilentlyContinue)
  foreach ($file in $jsonFiles) {
    try {
      $raw = Get-Content -LiteralPath $file.FullName -Raw -ErrorAction Stop
      if ([string]::IsNullOrWhiteSpace($raw)) { continue }
      $plugin = $raw | ConvertFrom-Json -ErrorAction Stop
      if (-not $plugin) { continue }

      if ($plugin.folderMap) {
        foreach ($prop in $plugin.folderMap.PSObject.Properties) {
          $k = [string]$prop.Name
          $v = [string]$prop.Value
          if ([string]::IsNullOrWhiteSpace($k) -or [string]::IsNullOrWhiteSpace($v)) { continue }
          $script:CONSOLE_FOLDER_MAP[$k.Trim().ToLowerInvariant()] = $v.Trim().ToUpperInvariant()
        }
      }
      if ($plugin.extMap) {
        foreach ($prop in $plugin.extMap.PSObject.Properties) {
          $k = [string]$prop.Name
          $v = [string]$prop.Value
          if ([string]::IsNullOrWhiteSpace($k) -or [string]::IsNullOrWhiteSpace($v)) { continue }
          $ext = $k.Trim().ToLowerInvariant()
          if (-not $ext.StartsWith('.')) { $ext = '.' + $ext }
          $script:CONSOLE_EXT_MAP[$ext] = $v.Trim().ToUpperInvariant()
          # Plugin-Extensions gelten als UNIQUE (explizit registriert)
          if ($script:CONSOLE_EXT_MAP_UNIQUE) {
            $script:CONSOLE_EXT_MAP_UNIQUE[$ext] = $v.Trim().ToUpperInvariant()
          }
        }
      }
      if ($plugin.regexMap) {
        foreach ($prop in $plugin.regexMap.PSObject.Properties) {
          $key = [string]$prop.Name
          $rx = [string]$prop.Value
          if ([string]::IsNullOrWhiteSpace($key) -or [string]::IsNullOrWhiteSpace($rx)) { continue }
          $existing = @($script:CONSOLE_RX_MAP_BASE | Where-Object { $_.Key -eq $key })
          if ($existing.Count -eq 0) {
            $script:CONSOLE_RX_MAP_BASE += @{ Key = $key.Trim().ToUpperInvariant(); Rx = $rx }
          }
        }
      }

      $loaded++
      if ($Log) { & $Log ("PLUGIN: geladen {0}" -f $file.Name) }
    } catch {
      $msg = "Plugin-Fehler {0}: {1}" -f $file.Name, $_.Exception.Message
      [void]$errors.Add($msg)
      if ($Log) { & $Log ("WARNUNG: {0}" -f $msg) }
    }
  }

  # Recompile the cached regex maps so newly-loaded plugin entries are included.
  if (Get-Command Rebuild-ClassificationCompiledMaps -ErrorAction SilentlyContinue) {
    Rebuild-ClassificationCompiledMaps
  } else {
    $script:CONSOLE_FOLDER_RX_MAP = @($script:CONSOLE_RX_MAP_BASE | ForEach-Object {
      @{ Key = $_.Key; Rx = $_.Rx; RxObj = [regex]::new($_.Rx, 'IgnoreCase, Compiled') }
    })
    $script:CONSOLE_NAME_RX_MAP = @($script:CONSOLE_RX_MAP_BASE | ForEach-Object {
      @{ Key = $_.Key; Rx = $_.Rx; RxObj = [regex]::new($_.Rx, 'IgnoreCase, Compiled') }
    })
  }
  Reset-ClassificationCaches

  return [pscustomobject]@{
    Loaded = [int]$loaded
    Errors = @($errors)
    Path = $Path
  }
}
