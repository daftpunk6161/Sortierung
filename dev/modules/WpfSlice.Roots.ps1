function Initialize-WpfRootsCollection {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $resolvedRootsCollection = $null
  $vmForRoots = $null
  if (Get-Command Get-WpfViewModel -ErrorAction SilentlyContinue) {
    $vmForRoots = Get-WpfViewModel -Ctx $Ctx
  }

  if ($vmForRoots -and $vmForRoots.Roots) {
    $resolvedRootsCollection = $vmForRoots.Roots
  } elseif ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
    $itemsSource = $Ctx['listRoots'].ItemsSource
    if ($itemsSource -and ($itemsSource -is [System.Collections.IList])) {
      $resolvedRootsCollection = $itemsSource
    } elseif ($itemsSource -and $itemsSource.PSObject.Properties['SourceCollection'] -and ($itemsSource.SourceCollection -is [System.Collections.IList])) {
      $resolvedRootsCollection = $itemsSource.SourceCollection
    }
  }

  if (-not $resolvedRootsCollection) {
    $resolvedRootsCollection = New-Object 'System.Collections.ObjectModel.ObservableCollection[string]'
    if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
      try { $Ctx['listRoots'].ItemsSource = $resolvedRootsCollection } catch { }
    }
  }

  $Ctx['__rootsCollection'] = $resolvedRootsCollection

  # Hide empty-state overlay if roots already loaded
  if ($Ctx.ContainsKey('pnlRootsEmptyState') -and $Ctx['pnlRootsEmptyState']) {
    $Ctx['pnlRootsEmptyState'].Visibility = if ($resolvedRootsCollection.Count -gt 0) { 'Collapsed' } else { 'Visible' }
  }

  return $resolvedRootsCollection
}

function Register-WpfRootInputHandlers {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$BrowseFolder,
    [Parameter(Mandatory)][scriptblock]$PersistSettingsNow
  )

  if (-not ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots'])) { return }

  $addRootPath = {
    param([string]$Path)
    if ([string]::IsNullOrWhiteSpace([string]$Path)) { return $false }
    $normalized = ([string]$Path).Trim().Trim('"')

    $isContainer = $false
    try {
      $isContainer = (Test-Path -LiteralPath $normalized -PathType Container)
    } catch {
      $isContainer = $false
    }
    if (-not $isContainer -and -not $normalized.StartsWith('\\')) {
      return $false
    }

    $rootsCollection = $Ctx['__rootsCollection']
    if (-not $rootsCollection) {
      Add-WpfLogLine -Ctx $Ctx -Line 'INTERN: __rootsCollection initialisiert.' -Level 'DEBUG'
      $rootsCollection = New-Object 'System.Collections.ObjectModel.ObservableCollection[string]'
      $Ctx['__rootsCollection'] = $rootsCollection
      if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
        try { $Ctx['listRoots'].ItemsSource = $rootsCollection } catch { }
      }
    }

    foreach ($item in @($rootsCollection)) {
      if ([string]$item -ieq $normalized) { return $false }
    }

    [void]$rootsCollection.Add($normalized)
    if (Get-Command Sync-WpfViewModelRootsFromControl -ErrorAction SilentlyContinue) {
      try { Sync-WpfViewModelRootsFromControl -Ctx $Ctx } catch { }
    }
    if ($Ctx.ContainsKey('pnlRootsEmptyState') -and $Ctx['pnlRootsEmptyState']) {
      $Ctx['pnlRootsEmptyState'].Visibility = 'Collapsed'
    }
    if (Get-Command Update-WpfStepIndicator -ErrorAction SilentlyContinue) {
      Update-WpfStepIndicator -Ctx $Ctx
    }
    return $true
  }.GetNewClosure()

  $removeRootPath = {
    param($Path)
    if ($null -eq $Path) { return $false }
    $normalized = ([string]$Path).Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) { return $false }

    $rootsCollection = $Ctx['__rootsCollection']
    if (-not $rootsCollection) { return $false }

    for ($i = 0; $i -lt $rootsCollection.Count; $i++) {
      if ([string]$rootsCollection[$i] -ieq $normalized) {
        [void]$rootsCollection.RemoveAt($i)
        if (Get-Command Sync-WpfViewModelRootsFromControl -ErrorAction SilentlyContinue) {
          try { Sync-WpfViewModelRootsFromControl -Ctx $Ctx } catch { }
        }
        if ($rootsCollection.Count -eq 0 -and $Ctx.ContainsKey('pnlRootsEmptyState') -and $Ctx['pnlRootsEmptyState']) {
          $Ctx['pnlRootsEmptyState'].Visibility = 'Visible'
        }
        if (Get-Command Update-WpfStepIndicator -ErrorAction SilentlyContinue) {
          Update-WpfStepIndicator -Ctx $Ctx
        }
        return $true
      }
    }

    return $false
  }.GetNewClosure()

  $Ctx['__addRootPath'] = $addRootPath
  $Ctx['__removeRootPath'] = $removeRootPath

  $Ctx['listRoots'].add_DragEnter({
    if ($_.Data.GetDataPresent([System.Windows.DataFormats]::FileDrop)) {
      $_.Effects = [System.Windows.DragDropEffects]::Copy
    } else {
      $_.Effects = [System.Windows.DragDropEffects]::None
    }
    $_.Handled = $true
  }.GetNewClosure())

  $Ctx['listRoots'].add_DragOver({
    if ($_.Data.GetDataPresent([System.Windows.DataFormats]::FileDrop)) {
      $_.Effects = [System.Windows.DragDropEffects]::Copy
    } else {
      $_.Effects = [System.Windows.DragDropEffects]::None
    }
    $_.Handled = $true
  }.GetNewClosure())

  $Ctx['listRoots'].add_PreviewDragOver({
    if ($_.Data.GetDataPresent([System.Windows.DataFormats]::FileDrop)) {
      $_.Effects = [System.Windows.DragDropEffects]::Copy
    } else {
      $_.Effects = [System.Windows.DragDropEffects]::None
    }
    $_.Handled = $true
  }.GetNewClosure())

  $Ctx['listRoots'].add_PreviewDrop({
    try {
      if (-not $_.Data.GetDataPresent([System.Windows.DataFormats]::FileDrop)) { return }
      $paths = $_.Data.GetData([System.Windows.DataFormats]::FileDrop)
      if (-not $paths) { return }

      $added = 0
      foreach ($path in $paths) {
        if (& $addRootPath ([string]$path)) {
          $added++
        }
      }

      if ($added -gt 0) {
        Add-WpfLogLine -Ctx $Ctx -Line ("{0} ROM-Ordner per Drag & Drop hinzugefügt." -f $added) -Level 'INFO'
      }
      Update-WpfStatusBar -Ctx $Ctx
      $_.Handled = $true
    } catch {
      Add-WpfLogLine -Ctx $Ctx -Line ("Drag & Drop (Preview) fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'WARN'
      $_.Handled = $true
    }
  }.GetNewClosure())

  $Ctx['listRoots'].add_Drop({
    try {
      if (-not $_.Data.GetDataPresent([System.Windows.DataFormats]::FileDrop)) { return }
      $paths = $_.Data.GetData([System.Windows.DataFormats]::FileDrop)
      if (-not $paths) { return }

      $added = 0
      foreach ($path in $paths) {
        if (& $addRootPath ([string]$path)) {
          $added++
        }
      }

      if ($added -gt 0) {
        Add-WpfLogLine -Ctx $Ctx -Line ("{0} ROM-Ordner per Drag & Drop hinzugefügt." -f $added) -Level 'INFO'
      }
      Update-WpfStatusBar -Ctx $Ctx
    } catch {
      Add-WpfLogLine -Ctx $Ctx -Line ("Drag & Drop fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'WARN'
    }
  }.GetNewClosure())

  if ($Ctx.ContainsKey('btnAddRoot') -and $Ctx['btnAddRoot']) {
    $Ctx['btnAddRoot'].add_Click({
      try {
        $path = & $BrowseFolder 'ROM-Verzeichnis hinzufügen' ''
        if ($path) {
          if (& $addRootPath $path) {
            Add-WpfLogLine -Ctx $Ctx -Line ("Root hinzugefügt: {0}" -f $path) -Level 'INFO'
            Update-WpfStatusBar -Ctx $Ctx
            & $PersistSettingsNow
          }
        } else {
          Add-WpfLogLine -Ctx $Ctx -Line 'Ordnerauswahl abgebrochen oder kein Ordner gewählt.' -Level 'DEBUG'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Root hinzufügen fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnRemoveRoot') -and $Ctx['btnRemoveRoot']) {
    $Ctx['btnRemoveRoot'].add_Click({
      try {
        $sel = $Ctx['listRoots'].SelectedItem
        if ($null -ne $sel) {
          [void](& $removeRootPath $sel)
          Update-WpfStatusBar -Ctx $Ctx
          & $PersistSettingsNow
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Root entfernen fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }
}
