# ================================================================
#  WpfSlice.DatMapping.ps1  –  Slice 4: DAT grid & mapping handlers
#  Extracted from WpfEventHandlers.ps1 (TD-001 Slice 4/6)
# ================================================================

function Get-WpfDatHashType {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $selectedDatHash = 'sha1'
  if ($Ctx.ContainsKey('cmbDatHash') -and $Ctx['cmbDatHash']) {
    $combo = $Ctx['cmbDatHash']
    if ($combo.SelectedItem) {
      if ($combo.SelectedItem.PSObject.Properties['Content']) {
        $selectedDatHash = [string]$combo.SelectedItem.Content
      } else {
        $selectedDatHash = [string]$combo.SelectedItem
      }
    } elseif (-not [string]::IsNullOrWhiteSpace([string]$combo.Text)) {
      $selectedDatHash = [string]$combo.Text
    }
  }

  $normalizedDatHash = $selectedDatHash.Trim().ToLowerInvariant()
  if ($normalizedDatHash -eq 'crc') { $normalizedDatHash = 'crc32' }
  if ($normalizedDatHash -notin @('sha1','md5','crc32')) {
    $normalizedDatHash = 'sha1'
  }
  return $normalizedDatHash
}

function New-WpfDatMapRow {
  param(
    [string]$Console = '',
    [string]$DatFile = ''
  )

  return [pscustomobject]@{
    Console = [string]$Console
    DatFile = [string]$DatFile
  }
}

function Get-WpfDatConsoleOptions {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $known = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)

  $addValue = {
    param([object]$Value)
    $candidate = [string]$Value
    if ([string]::IsNullOrWhiteSpace($candidate)) { return }
    [void]$known.Add($candidate.Trim())
  }

  try {
    if ($script:CONSOLE_FOLDER_MAP) {
      foreach ($entry in $script:CONSOLE_FOLDER_MAP.GetEnumerator()) {
        & $addValue $entry.Value
      }
    }
  } catch { }

  try {
    if ($script:CONSOLE_EXT_MAP) {
      foreach ($entry in $script:CONSOLE_EXT_MAP.GetEnumerator()) {
        & $addValue $entry.Value
      }
    }
  } catch { }

  try {
    if ($Ctx.ContainsKey('gridDatMap') -and $Ctx['gridDatMap']) {
      $grid = $Ctx['gridDatMap']
      $rows = @()
      if ($grid.PSObject.Properties.Name -contains 'ItemsSource' -and $grid.ItemsSource) {
        $rows = @($grid.ItemsSource)
      } elseif ($grid.PSObject.Properties.Name -contains 'Items') {
        $rows = @($grid.Items)
      }
      foreach ($row in $rows) {
        if ($row -and $row.PSObject.Properties['Console']) {
          & $addValue $row.PSObject.Properties['Console'].Value
        }
      }
    }
  } catch { }

  $ordered = @($known | Sort-Object)
  $collection = New-Object 'System.Collections.ObjectModel.ObservableCollection[string]'
  foreach ($name in $ordered) {
    [void]$collection.Add([string]$name)
  }
  return $collection
}

function Ensure-WpfDatMapConsoleColumn {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not $Ctx.ContainsKey('gridDatMap') -or -not $Ctx['gridDatMap']) { return }
  $grid = $Ctx['gridDatMap']
  if (-not $grid.Columns -or $grid.Columns.Count -eq 0) { return }

  $consoleOptions = Get-WpfDatConsoleOptions -Ctx $Ctx

  $existingFirst = $grid.Columns[0]
  if ($existingFirst -is [System.Windows.Controls.DataGridComboBoxColumn]) {
    $existingFirst.ItemsSource = $consoleOptions
    return
  }

  $binding = New-Object System.Windows.Data.Binding('Console')
  $binding.Mode = [System.Windows.Data.BindingMode]::TwoWay
  $binding.UpdateSourceTrigger = [System.Windows.Data.UpdateSourceTrigger]::PropertyChanged

  $consoleColumn = New-Object System.Windows.Controls.DataGridComboBoxColumn
  $consoleColumn.Header = 'Konsole'
  $consoleColumn.Width = 130
  $consoleColumn.SelectedItemBinding = $binding
  $consoleColumn.ItemsSource = $consoleOptions
  $consoleColumn.IsEditable = $true

  $grid.Columns.RemoveAt(0)
  $grid.Columns.Insert(0, $consoleColumn)
}

function Initialize-WpfDatMapBinding {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not $Ctx.ContainsKey('gridDatMap')) { return }
  $grid = $Ctx['gridDatMap']
  if (-not $grid) { return }

  if (-not ($grid.PSObject.Properties.Name -contains 'ItemsSource')) {
    return
  }

  if ($grid.ItemsSource) {
    return
  }

  $collection = New-Object 'System.Collections.ObjectModel.ObservableCollection[object]'
  $grid.ItemsSource = $collection

  Ensure-WpfDatMapConsoleColumn -Ctx $Ctx
}

function Get-WpfDatMapCollection {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  Initialize-WpfDatMapBinding -Ctx $Ctx
  if (-not $Ctx.ContainsKey('gridDatMap')) { return $null }
  $grid = $Ctx['gridDatMap']
  if (-not $grid) { return $null }
  if (-not ($grid.PSObject.Properties.Name -contains 'ItemsSource')) { return $null }
  return $grid.ItemsSource
}

function Set-WpfDatMapFromSettings {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [object]$DatMapEntries
  )

  $collection = Get-WpfDatMapCollection -Ctx $Ctx
  if (-not $collection) { return }

  try { $collection.Clear() } catch { }

  foreach ($entry in @($DatMapEntries)) {
    try {
      $consoleValue = $null
      $pathValue = $null
      if ($entry -and $entry.PSObject.Properties['console']) {
        $consoleValue = [string]$entry.PSObject.Properties['console'].Value
      } elseif ($entry -and $entry.PSObject.Properties['Console']) {
        $consoleValue = [string]$entry.PSObject.Properties['Console'].Value
      }
      if ($entry -and $entry.PSObject.Properties['path']) {
        $pathValue = [string]$entry.PSObject.Properties['path'].Value
      } elseif ($entry -and $entry.PSObject.Properties['DatFile']) {
        $pathValue = [string]$entry.PSObject.Properties['DatFile'].Value
      }

      if (-not [string]::IsNullOrWhiteSpace($consoleValue) -and -not [string]::IsNullOrWhiteSpace($pathValue)) {
        [void]$collection.Add((New-WpfDatMapRow -Console $consoleValue -DatFile $pathValue))
      }
    } catch { }
  }

  Ensure-WpfDatMapConsoleColumn -Ctx $Ctx
}

function Get-WpfDatMapEntries {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $result = New-Object System.Collections.Generic.List[object]

  if ($Ctx.ContainsKey('gridDatMap') -and $Ctx['gridDatMap']) {
    $grid = $Ctx['gridDatMap']
    $rows = @()
    $hasItemsSource = $false
    $hasItems = $false
    try { $hasItemsSource = ($grid.PSObject.Properties.Name -contains 'ItemsSource') } catch { }
    try { $hasItems = ($grid.PSObject.Properties.Name -contains 'Items') } catch { }

    if ($hasItemsSource -and $grid.ItemsSource) {
      $rows = @($grid.ItemsSource)
    } elseif ($hasItems) {
      $rows = @($grid.Items)
    }

    foreach ($row in $rows) {
      if ($row -is [System.Data.DataRowView]) { continue }
      try {
        $consKey = $null
        $datFile = $null
        if ($row.PSObject.Properties['Console']) {
          $consKey = [string]$row.PSObject.Properties['Console'].Value
        }
        if ($row.PSObject.Properties['DatFile']) {
          $datFile = [string]$row.PSObject.Properties['DatFile'].Value
        }
        if (-not [string]::IsNullOrWhiteSpace($consKey) -and -not [string]::IsNullOrWhiteSpace($datFile)) {
          [void]$result.Add([pscustomobject]@{ console = $consKey.Trim(); path = $datFile.Trim() })
        }
      } catch { }
    }
  }

  return @($result.ToArray())
}

function Update-WpfCrcVerifyOptions {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not $Ctx.ContainsKey('chkCrcVerifyScan') -or -not $Ctx['chkCrcVerifyScan']) { return }

  $useDat = Get-WpfIsChecked -Ctx $Ctx -Key 'chkDatUse'
  $Ctx['chkCrcVerifyScan'].IsEnabled = $useDat
  if ($Ctx.ContainsKey('chkCrcVerifyDat') -and $Ctx['chkCrcVerifyDat']) {
    $baseCrc = [bool]$Ctx['chkCrcVerifyScan'].IsChecked
    $Ctx['chkCrcVerifyDat'].IsEnabled = ($useDat -and $baseCrc)
  }
  if (-not $useDat) {
    if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'CrcVerifyScan' -Value $false)) {
      $Ctx['chkCrcVerifyScan'].IsChecked = $false
    }
    if ($Ctx.ContainsKey('chkCrcVerifyDat') -and $Ctx['chkCrcVerifyDat']) {
      if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'CrcVerifyDat' -Value $false)) {
        $Ctx['chkCrcVerifyDat'].IsChecked = $false
      }
    }
    $Ctx['chkCrcVerifyScan'].ToolTip = 'Benötigt zuerst: DAT-Verifikation.'
  } else {
    $Ctx['chkCrcVerifyScan'].ToolTip = 'Basis-CRC inklusive DAT-Abgleich, solange DAT aktiv ist.'
  }
}

function Register-WpfDatMappingHandlers {
  <#
  .SYNOPSIS
    Registers DAT-mapping related event handlers (Slice 4).
  .PARAMETER Ctx
    The named-element hashtable from Get-WpfNamedElements.
  .PARAMETER BrowseFolder
    Scriptblock for folder browse dialog.
  .PARAMETER PersistSettingsNow
    Scriptblock to persist settings immediately.
  .PARAMETER RefreshStatus
    Scriptblock to refresh status bar.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$BrowseFolder,
    [Parameter(Mandatory)][scriptblock]$PersistSettingsNow,
    [Parameter(Mandatory)][scriptblock]$RefreshStatus
  )

  # ── DAT map grid init ──────────────────────────────────────────────────
  Initialize-WpfDatMapBinding -Ctx $Ctx
  Ensure-WpfDatMapConsoleColumn -Ctx $Ctx

  # ── CRC option sync ────────────────────────────────────────────────────
  $crcOptionRefresh = {
    Update-WpfCrcVerifyOptions -Ctx $Ctx
    & $RefreshStatus
  }.GetNewClosure()

  $Ctx['chkDatUse'].add_Checked($crcOptionRefresh)
  $Ctx['chkDatUse'].add_Unchecked($crcOptionRefresh)
  $Ctx['chkDatFallback'].add_Checked($RefreshStatus)
  $Ctx['chkDatFallback'].add_Unchecked($RefreshStatus)
  $Ctx['chkCrcVerifyScan'].add_Checked($crcOptionRefresh)
  $Ctx['chkCrcVerifyScan'].add_Unchecked($crcOptionRefresh)

  Update-WpfCrcVerifyOptions -Ctx $Ctx

  # ── DAT browse ────────────────────────────────────────────────────────
  $Ctx['btnBrowseDat'].add_Click({
    $path = & $BrowseFolder 'DAT-Root auswählen' $Ctx['txtDatRoot'].Text
    if ($path) {
      if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'DatRoot' -Value ([string]$path))) {
        $Ctx['txtDatRoot'].Text = $path
      }
      & $PersistSettingsNow
    }
    Update-WpfStatusBar -Ctx $Ctx
  }.GetNewClosure())

  # ── DAT hash combo ────────────────────────────────────────────────────
  $Ctx['cmbDatHash'].add_SelectionChanged($RefreshStatus)
}
