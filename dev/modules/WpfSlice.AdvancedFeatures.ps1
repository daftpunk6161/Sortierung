# ================================================================
#  WpfSlice.AdvancedFeatures.ps1  –  Slice 6: Plugin, rollback, watch-mode & advanced handlers
#  Extracted from WpfEventHandlers.ps1 (TD-001 Slice 6/6)
# ================================================================

# ── Watch/Rollback variable initializations (moved from WpfEventHandlers.ps1) ──
if (-not (Get-Variable -Name WpfWatchRegistry -Scope Script -ErrorAction SilentlyContinue)) {
  $script:WpfWatchRegistry = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
}
if (-not (Get-Variable -Name WpfWatchPendingRun -Scope Script -ErrorAction SilentlyContinue)) {
  $script:WpfWatchPendingRun = $false
}
if (-not (Get-Variable -Name WpfWatchLastEventUtc -Scope Script -ErrorAction SilentlyContinue)) {
  $script:WpfWatchLastEventUtc = [datetime]::MinValue
}
if (-not (Get-Variable -Name WpfRollbackUndoStack -Scope Script -ErrorAction SilentlyContinue)) {
  $script:WpfRollbackUndoStack = New-Object System.Collections.Generic.List[object]
}
if (-not (Get-Variable -Name WpfRollbackRedoStack -Scope Script -ErrorAction SilentlyContinue)) {
  $script:WpfRollbackRedoStack = New-Object System.Collections.Generic.List[object]
}

function Show-WpfTextInputDialog {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Owner,
    [Parameter(Mandatory)][string]$Title,
    [Parameter(Mandatory)][string]$Message,
    [string]$DefaultValue = ''
  )

  if (Get-Command Read-WpfPromptText -ErrorAction SilentlyContinue) {
    return (Read-WpfPromptText -Owner $Owner -Title $Title -Message $Message -DefaultValue $DefaultValue)
  }

  $dialog = New-WpfStyledDialog -Owner $Owner -Title $Title -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
  $dialog.MinWidth = 460
  $surfaceBrush = $Owner.TryFindResource('BrushSurface')
  if (-not ($surfaceBrush -is [System.Windows.Media.SolidColorBrush])) { $surfaceBrush = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString('#1A1A3A')) }
  $dialog.Background = $surfaceBrush

  $root = New-Object System.Windows.Controls.Grid
  $root.Margin = [System.Windows.Thickness]::new(16,16,16,16)
  [void]$root.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = [System.Windows.GridLength]::Auto }))
  [void]$root.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = [System.Windows.GridLength]::Auto }))
  [void]$root.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = [System.Windows.GridLength]::Auto }))

  $label = New-Object System.Windows.Controls.TextBlock
  $label.Text = $Message
  $label.TextWrapping = [System.Windows.TextWrapping]::Wrap
  $label.Margin = [System.Windows.Thickness]::new(0,0,0,10)
  [System.Windows.Controls.Grid]::SetRow($label, 0)
  [void]$root.Children.Add($label)

  $tb = New-Object System.Windows.Controls.TextBox
  $tb.Text = [string]$DefaultValue
  $tb.MinWidth = 360
  $tb.Margin = [System.Windows.Thickness]::new(0,0,0,14)
  [System.Windows.Controls.Grid]::SetRow($tb, 1)
  [void]$root.Children.Add($tb)

  $buttons = New-Object System.Windows.Controls.StackPanel
  $buttons.Orientation = [System.Windows.Controls.Orientation]::Horizontal
  $buttons.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right

  $btnCancel = New-Object System.Windows.Controls.Button
  $btnCancel.Content = 'Abbrechen'
  $btnCancel.Width = 110
  $btnCancel.Margin = [System.Windows.Thickness]::new(0,0,8,0)

  $btnOk = New-Object System.Windows.Controls.Button
  $btnOk.Content = 'OK'
  $btnOk.Width = 110

  $btnCancel.add_Click({
    $dialog.DialogResult = $false
    $dialog.Close()
  })
  $btnOk.add_Click({
    $dialog.DialogResult = $true
    $dialog.Close()
  })

  [void]$buttons.Children.Add($btnCancel)
  [void]$buttons.Children.Add($btnOk)
  [System.Windows.Controls.Grid]::SetRow($buttons, 2)
  [void]$root.Children.Add($buttons)

  $dialog.Content = $root
  $tb.Focus() | Out-Null
  $tb.SelectAll()
  $shown = $dialog.ShowDialog()

  if ($shown -eq $true) {
    return [string]$tb.Text
  }
  return $null
}

function Get-WpfRollbackRoots {
  <# Collects restore-roots and current-roots from the UI context (DUP-106). #>
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $restoreRoots = New-Object System.Collections.Generic.List[string]
  foreach ($item in @($Ctx['listRoots'].Items)) {
    $s = if ($item -is [string]) { [string]$item } else { [string]$item.Content }
    if (-not [string]::IsNullOrWhiteSpace($s)) {
      [void]$restoreRoots.Add($s.Trim())
    }
  }

  $currentRoots = New-Object System.Collections.Generic.List[string]
  foreach ($rootPath in @($restoreRoots)) { [void]$currentRoots.Add([string]$rootPath) }
  if ($Ctx.ContainsKey('txtTrash') -and -not [string]::IsNullOrWhiteSpace([string]$Ctx['txtTrash'].Text)) {
    [void]$currentRoots.Add(([string]$Ctx['txtTrash'].Text).Trim())
  }

  return [pscustomobject]@{
    RestoreRoots = $restoreRoots
    CurrentRoots = $currentRoots
  }
}

function Invoke-WpfRollbackCore {
  <# Shared rollback execution + undo/redo stack update (DUP-106).
     Called by both Invoke-WpfRollbackWizard and Invoke-WpfQuickRollback. #>
  param(
    [Parameter(Mandatory)][string]$AuditCsvPath,
    [Parameter(Mandatory)][System.Collections.Generic.List[string]]$AllowedRestoreRoots,
    [Parameter(Mandatory)][System.Collections.Generic.List[string]]$AllowedCurrentRoots,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  if (-not (Get-Command Invoke-RunRollbackService -ErrorAction SilentlyContinue)) {
    throw 'Rollback-Service nicht verfügbar (Invoke-RunRollbackService fehlt).'
  }

  $rollback = Invoke-RunRollbackService -Parameters @{
    AuditCsvPath = $AuditCsvPath
    AllowedRestoreRoots = @($AllowedRestoreRoots)
    AllowedCurrentRoots = @($AllowedCurrentRoots)
    Log = { param([string]$m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' }
  }

  try {
    $inverseCsv = New-WpfInverseRollbackCsv -AuditCsvPath $AuditCsvPath
    [void]$script:WpfRollbackUndoStack.Add([pscustomobject]@{
      UndoCsvPath = $inverseCsv
      RedoCsvPath = $AuditCsvPath
      AllowedRestoreRoots = @($AllowedRestoreRoots)
      AllowedCurrentRoots = @($AllowedCurrentRoots)
    })
    $script:WpfRollbackRedoStack.Clear()
    if ($Ctx.ContainsKey('btnRollbackUndo') -and $Ctx['btnRollbackUndo']) { $Ctx['btnRollbackUndo'].IsEnabled = $true }
    if ($Ctx.ContainsKey('btnRollbackRedo') -and $Ctx['btnRollbackRedo']) { $Ctx['btnRollbackRedo'].IsEnabled = $false }
  } catch {
    Add-WpfLogLine -Ctx $Ctx -Line ("Undo/Redo-Stack konnte nicht aktualisiert werden: {0}" -f $_.Exception.Message) -Level 'WARN'
  }

  return $rollback
}

function Invoke-WpfRollbackWizard {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  $initialAuditDir = if ($Ctx.ContainsKey('txtAuditRoot') -and -not [string]::IsNullOrWhiteSpace([string]$Ctx['txtAuditRoot'].Text) -and (Test-Path -LiteralPath $Ctx['txtAuditRoot'].Text -PathType Container)) {
    [string]$Ctx['txtAuditRoot'].Text
  } else {
    Join-Path (Get-Location).Path 'audit-logs'
  }

  $dlg = New-Object Microsoft.Win32.OpenFileDialog
  $dlg.Title = 'Audit-CSV für Rollback auswählen'
  $dlg.Filter = 'Audit CSV (rom-move-audit-*.csv)|rom-move-audit-*.csv|CSV (*.csv)|*.csv|All files (*.*)|*.*'
  if (Test-Path -LiteralPath $initialAuditDir -PathType Container) {
    $dlg.InitialDirectory = $initialAuditDir
  }
  $selectedAudit = $null
  if ($dlg.ShowDialog($Window) -eq $true) {
    $selectedAudit = [string]$dlg.FileName
  }
  if ([string]::IsNullOrWhiteSpace($selectedAudit)) { return }

  $roots = Get-WpfRollbackRoots -Ctx $Ctx
  $rollbackAllowedRestoreRoots = $roots.RestoreRoots
  $rollbackAllowedCurrentRoots = $roots.CurrentRoots

  if ($rollbackAllowedRestoreRoots.Count -eq 0) {
    [System.Windows.MessageBox]::Show(
      'Rollback benötigt mindestens einen konfigurierten ROM-Root als Sicherheitsgrenze.',
      'Rollback',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Warning) | Out-Null
    return
  }

  $step1 = [System.Windows.MessageBox]::Show(
    ("Rollback Wizard (Schritt 1/3)`n`nAudit: {0}`nRestore-Roots: {1}`nCurrent-Roots: {2}`n`nWeiter zur Vorschau?" -f $selectedAudit, $rollbackAllowedRestoreRoots.Count, @($rollbackAllowedCurrentRoots).Count),
    'Rollback Wizard',
    [System.Windows.MessageBoxButton]::YesNo,
    [System.Windows.MessageBoxImage]::Question)
  if ($step1 -ne [System.Windows.MessageBoxResult]::Yes) { return }

  $previewRows = @()
  $previewEligible = 0
  $previewUnsafe = 0
  $previewParseErrors = 0
  try {
    $previewRows = @(Import-Csv -LiteralPath $selectedAudit -Encoding UTF8)
    foreach ($row in $previewRows) {
      try {
        $action = [string]$row.Action
        $source = [string]$row.Source
        $dest = [string]$row.Dest
        if ([string]::IsNullOrWhiteSpace($action) -or [string]::IsNullOrWhiteSpace($source) -or [string]::IsNullOrWhiteSpace($dest)) { continue }
        $actionNorm = $action.Trim().ToUpperInvariant()
        if ($actionNorm -notin @('MOVE','JUNK','BIOS-MOVE')) { continue }

        $restoreSafe = $false
        foreach ($rootCandidate in @($rollbackAllowedRestoreRoots)) {
          if (Test-PathWithinRoot -Path $source -Root $rootCandidate -DisallowReparsePoints) { $restoreSafe = $true; break }
        }
        $currentSafe = $false
        foreach ($rootCandidate in @($rollbackAllowedCurrentRoots)) {
          if (Test-PathWithinRoot -Path $dest -Root $rootCandidate -DisallowReparsePoints) { $currentSafe = $true; break }
        }

        if ($restoreSafe -and $currentSafe) {
          $previewEligible++
        } else {
          $previewUnsafe++
        }
      } catch {
        $previewParseErrors++
      }
    }
  } catch {
    [System.Windows.MessageBox]::Show(
      ("Rollback-Vorschau konnte Audit nicht lesen:`n{0}" -f $_.Exception.Message),
      'Rollback Wizard',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Error) | Out-Null
    return
  }

  $step2 = [System.Windows.MessageBox]::Show(
    ("Rollback Wizard (Schritt 2/3) - Vorschau`n`nAudit-Zeilen: {0}`nVoraussichtlich zulässige Restores: {1}`nPotenziell unsicher (wird geskippt): {2}`nParse-Fehler: {3}`n`nWeiter zur finalen Bestätigung?" -f $previewRows.Count, $previewEligible, $previewUnsafe, $previewParseErrors),
    'Rollback Wizard',
    [System.Windows.MessageBoxButton]::YesNo,
    [System.Windows.MessageBoxImage]::Information)
  if ($step2 -ne [System.Windows.MessageBoxResult]::Yes) { return }

  $confirmText = Read-WpfPromptText -Owner $Window -Title 'Rollback Wizard' -Message 'Rollback starten: bitte ROLLBACK eingeben, um Schritt 3/3 zu bestätigen.' -DefaultValue ''
  if ([string]::IsNullOrWhiteSpace($confirmText) -or $confirmText.Trim().ToUpperInvariant() -ne 'ROLLBACK') {
    [System.Windows.MessageBox]::Show(
      'Rollback abgebrochen: Bestätigungstext nicht korrekt.',
      'Rollback Wizard',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Warning) | Out-Null
    return
  }

  Add-WpfLogLine -Ctx $Ctx -Line ("=== Rollback aus Audit: {0} ===" -f $selectedAudit) -Level 'INFO'
  try {
    $rollback = Invoke-WpfRollbackCore -AuditCsvPath $selectedAudit -AllowedRestoreRoots $rollbackAllowedRestoreRoots -AllowedCurrentRoots $rollbackAllowedCurrentRoots -Ctx $Ctx
    Add-WpfLogLine -Ctx $Ctx -Line ("Rollback Ergebnis: zurück={0}, skip-unsafe={1}, skip-fehlt={2}, skip-existiert={3}, fehler={4}" -f $rollback.RolledBack, $rollback.SkippedUnsafe, $rollback.SkippedMissingDest, $rollback.SkippedCollision, $rollback.Failed) -Level 'INFO'

    [System.Windows.MessageBox]::Show(
      ("Rollback abgeschlossen.`n`nZurückverschoben: {0}`nSkip (unsicher): {1}`nSkip (Quelle fehlt): {2}`nSkip (Ziel existiert): {3}`nFehler: {4}" -f $rollback.RolledBack, $rollback.SkippedUnsafe, $rollback.SkippedMissingDest, $rollback.SkippedCollision, $rollback.Failed),
      'Rollback',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Information) | Out-Null

    if ($rollback.Failed -gt 0) {
      Set-QuickTone -Tone 'error' -Phase 'Rollback mit Fehlern'
    } else {
      Set-QuickTone -Tone 'success' -Phase 'Rollback abgeschlossen'
    }
  } catch {
    Add-WpfLogLine -Ctx $Ctx -Line ("Rollback fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
    [System.Windows.MessageBox]::Show(
      ("Rollback fehlgeschlagen:`n{0}" -f $_.Exception.Message),
      'Rollback',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Error) | Out-Null
  }
}

function Invoke-WpfCollectionDiffReport {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $reportsDir = Join-Path (Get-Location).Path 'reports'
  if (-not (Test-Path -LiteralPath $reportsDir -PathType Container)) {
    throw 'reports-Verzeichnis nicht gefunden.'
  }

  $newer = $null
  $older = $null

  try {
    $dialog = New-Object Microsoft.Win32.OpenFileDialog
    $dialog.Title = 'Collection-Diff: Zwei Reports auswählen (neu + alt)'
    $dialog.Filter = 'JSON (*.json)|*.json|Alle Dateien (*.*)|*.*'
    $dialog.Multiselect = $true
    $dialog.InitialDirectory = $reportsDir
    $picked = $dialog.ShowDialog()
    if ($picked -eq $true -and $dialog.FileNames.Count -ge 2) {
      $chosen = @($dialog.FileNames | ForEach-Object { Get-Item -LiteralPath $_ -ErrorAction SilentlyContinue } | Where-Object { $_ })
      if ($chosen.Count -ge 2) {
        $ordered = @($chosen | Sort-Object LastWriteTimeUtc -Descending)
        $newer = $ordered[0]
        $older = $ordered[1]
      }
    }
  } catch { }

  if (-not $newer -or -not $older) {
    $candidates = @(Get-ChildItem -LiteralPath $reportsDir -Filter 'test-pipeline-*.json' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending)
    if ($candidates.Count -lt 2) {
      throw 'Nicht genug Test-Pipeline-Reports für Diff vorhanden.'
    }
    $newer = $candidates[0]
    $older = $candidates[1]
  }

  $newObj = Get-Content -LiteralPath $newer.FullName -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
  $oldObj = Get-Content -LiteralPath $older.FullName -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop

  $outPath = Join-Path $reportsDir ('collection-diff-{0}.md' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
  $lines = New-Object System.Collections.Generic.List[string]
  [void]$lines.Add('# Collection Diff Report')
  [void]$lines.Add('')
  [void]$lines.Add(('Neu: {0}' -f $newer.Name))
  [void]$lines.Add(('Alt: {0}' -f $older.Name))
  [void]$lines.Add('')
  [void]$lines.Add(('Neu geändert: {0:o}' -f $newer.LastWriteTimeUtc))
  [void]$lines.Add(('Alt geändert: {0:o}' -f $older.LastWriteTimeUtc))
  [void]$lines.Add('')

  foreach ($key in @('stage','status','summary')) {
    $newValue = if ($newObj.PSObject.Properties.Name -contains $key) { [string]($newObj.$key | ConvertTo-Json -Compress -Depth 6) } else { '<n/a>' }
    $oldValue = if ($oldObj.PSObject.Properties.Name -contains $key) { [string]($oldObj.$key | ConvertTo-Json -Compress -Depth 6) } else { '<n/a>' }
    [void]$lines.Add(('## {0}' -f $key))
    [void]$lines.Add(('New: `{0}`' -f $newValue))
    [void]$lines.Add(('Old: `{0}`' -f $oldValue))
    [void]$lines.Add('')
  }

  $lines | Set-Content -LiteralPath $outPath -Encoding UTF8 -Force
  Add-WpfLogLine -Ctx $Ctx -Line ("Collection-Diff erstellt: {0}" -f $outPath) -Level 'INFO'
  Set-WpfAdvancedStatus -Ctx $Ctx -Text 'Collection-Diff erstellt'
  return $outPath
}

function Invoke-WpfPluginManager {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  $pluginRoots = @(
    Join-Path (Get-Location).Path 'plugins\consoles',
    Join-Path (Get-Location).Path 'plugins\operations',
    Join-Path (Get-Location).Path 'plugins\reports'
  )

  $items = New-Object System.Collections.Generic.List[object]
  foreach ($root in $pluginRoots) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    foreach ($f in @(Get-ChildItem -LiteralPath $root -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -like '*.ps1' -or $_.Name -like '*.ps1.disabled' })) {
      $manifestVersion = '0.0.0'
      $manifestId = [System.IO.Path]::GetFileNameWithoutExtension([string]$f.Name)
      $depCount = 0
      $manifestPath = [System.IO.Path]::ChangeExtension([string]$f.FullName, '.manifest.json')
      if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        try {
          $manifestRaw = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop
          if (-not [string]::IsNullOrWhiteSpace($manifestRaw)) {
            $manifestData = $manifestRaw | ConvertFrom-Json -ErrorAction Stop
            if ($manifestData.id -and -not [string]::IsNullOrWhiteSpace([string]$manifestData.id)) { $manifestId = [string]$manifestData.id }
            if ($manifestData.version -and -not [string]::IsNullOrWhiteSpace([string]$manifestData.version)) { $manifestVersion = [string]$manifestData.version }
            if ($manifestData.dependencies) { $depCount = @($manifestData.dependencies).Count }
          }
        } catch { }
      }

      [void]$items.Add([pscustomobject]@{
        Root = $root
        Name = $f.Name
        Path = $f.FullName
        PluginId = $manifestId
        Version = $manifestVersion
        DependencyCount = $depCount
      })
    }
  }

  if ($items.Count -eq 0) {
    [System.Windows.MessageBox]::Show('Keine Plugins gefunden.', 'Plugin-Manager', [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Information) | Out-Null
    return
  }

  $dialog = New-WpfStyledDialog -Owner $Window -Title 'Plugin-Manager' -Width 760 -Height 460

  $layout = New-Object System.Windows.Controls.Grid
  [void]$layout.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = [System.Windows.GridLength]::Auto }))
  [void]$layout.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = [System.Windows.GridLength]::new(1, [System.Windows.GridUnitType]::Star) }))
  [void]$layout.RowDefinitions.Add((New-Object System.Windows.Controls.RowDefinition -Property @{ Height = [System.Windows.GridLength]::Auto }))
  $layout.Margin = [System.Windows.Thickness]::new(12)

  $hint = New-Object System.Windows.Controls.TextBlock
  $hint.Text = 'Plugin auswählen und aktivieren/deaktivieren.'
  $hint.Margin = [System.Windows.Thickness]::new(0,0,0,8)
  [System.Windows.Controls.Grid]::SetRow($hint, 0)
  [void]$layout.Children.Add($hint)

  $list = New-Object System.Windows.Controls.ListBox
  $list.BorderThickness = [System.Windows.Thickness]::new(1)
  $list.BorderBrush = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushBorder' -Fallback '#2D2D5A'
  $list.Background = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSurfaceAlt' -Fallback '#13133A'
  [System.Windows.Controls.Grid]::SetRow($list, 1)
  [void]$layout.Children.Add($list)

  $refresh = {
    $list.Items.Clear()
    foreach ($plugin in @($items | Sort-Object Name)) {
      $isDisabled = [string]$plugin.Path -like '*.disabled'
      $display = '[{0}] {1}  -  {2}  ({3}@{4}, deps:{5})' -f $(if ($isDisabled) { 'AUS' } else { 'AN' }), [string]$plugin.Name, [string]$plugin.Root, [string]$plugin.PluginId, [string]$plugin.Version, [int]$plugin.DependencyCount
      [void]$list.Items.Add([pscustomobject]@{
        Display = $display
        Name = [string]$plugin.Name
        Path = [string]$plugin.Path
        Root = [string]$plugin.Root
        Disabled = $isDisabled
        PluginId = [string]$plugin.PluginId
        Version = [string]$plugin.Version
        DependencyCount = [int]$plugin.DependencyCount
      })
    }
    $list.DisplayMemberPath = 'Display'
  }.GetNewClosure()

  $buttonRow = New-Object System.Windows.Controls.StackPanel
  $buttonRow.Orientation = [System.Windows.Controls.Orientation]::Horizontal
  $buttonRow.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
  $buttonRow.Margin = [System.Windows.Thickness]::new(0,10,0,0)

  $btnEnable = New-Object System.Windows.Controls.Button
  $btnEnable.Content = 'Aktivieren'
  $btnEnable.Width = 110
  $btnEnable.Margin = [System.Windows.Thickness]::new(0,0,8,0)

  $btnDisable = New-Object System.Windows.Controls.Button
  $btnDisable.Content = 'Deaktivieren'
  $btnDisable.Width = 110
  $btnDisable.Margin = [System.Windows.Thickness]::new(0,0,8,0)

  $btnClose = New-Object System.Windows.Controls.Button
  $btnClose.Content = 'Schließen'
  $btnClose.Width = 110

  $btnInstallUrl = New-Object System.Windows.Controls.Button
  $btnInstallUrl.Content = 'Installieren (URL)'
  $btnInstallUrl.Width = 145
  $btnInstallUrl.Margin = [System.Windows.Thickness]::new(0,0,8,0)

  $btnEnable.add_Click({
    $selected = $list.SelectedItem
    if (-not $selected) { return }
    $currentPath = [string]$selected.Path
    if ($currentPath -like '*.disabled') {
      $dest = $currentPath.Substring(0, $currentPath.Length - '.disabled'.Length)
      Move-Item -LiteralPath $currentPath -Destination $dest -Force
      Add-WpfLogLine -Ctx $Ctx -Line ('Plugin aktiviert: {0}' -f [string]$selected.Name) -Level 'INFO'
      foreach ($entry in @($items)) {
        if ([string]$entry.Path -eq $currentPath) { $entry.Path = $dest; break }
      }
      & $refresh
    }
  }.GetNewClosure())

  $btnDisable.add_Click({
    $selected = $list.SelectedItem
    if (-not $selected) { return }
    $currentPath = [string]$selected.Path
    if ($currentPath -notlike '*.disabled') {
      $dest = ($currentPath + '.disabled')
      Move-Item -LiteralPath $currentPath -Destination $dest -Force
      Add-WpfLogLine -Ctx $Ctx -Line ('Plugin deaktiviert: {0}' -f [string]$selected.Name) -Level 'INFO'
      foreach ($entry in @($items)) {
        if ([string]$entry.Path -eq $currentPath) { $entry.Path = $dest; break }
      }
      & $refresh
    }
  }.GetNewClosure())

  $btnClose.add_Click({ $dialog.Close() })

  # ── Per-Plugin Konfiguration (5.4.1) ──
  $btnConfig = New-Object System.Windows.Controls.Button
  $btnConfig.Content = 'Konfigurieren'
  $btnConfig.Width = 120
  $btnConfig.Margin = [System.Windows.Thickness]::new(0,0,8,0)

  $btnConfig.add_Click({
    $selected = $list.SelectedItem
    if (-not $selected) { return }
    try {
      $manifestPath = [System.IO.Path]::ChangeExtension([string]$selected.Path -replace '\.disabled$','', '.manifest.json')
      if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        # Kein Manifest vorhanden -- Default erzeugen
        $defaultManifest = [ordered]@{
          id = [string]$selected.PluginId
          version = [string]$selected.Version
          dependencies = @()
          config = [ordered]@{}
        }
        Write-JsonFile -Path $manifestPath -Data $defaultManifest -Depth 5
      }

      $rawJson = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop
      $manifest = $rawJson | ConvertFrom-Json -ErrorAction Stop

      # Config-Sektion bauen (Key-Value-Paare)
      $configSection = [ordered]@{}
      if ($manifest.PSObject.Properties.Name -contains 'config' -and $manifest.config) {
        foreach ($prop in $manifest.config.PSObject.Properties) {
          $configSection[$prop.Name] = [string]$prop.Value
        }
      }

      # Dialog mit TextBox fuer JSON-Bearbeitung
      $cfgDialog = New-WpfStyledDialog -Owner $dialog -Title ('Plugin-Konfiguration: {0}' -f [string]$selected.PluginId) -Width 550 -Height 420

      $cfgGrid = New-Object System.Windows.Controls.Grid
      $cfgGrid.Margin = [System.Windows.Thickness]::new(12)
      $rd1 = New-Object System.Windows.Controls.RowDefinition; $rd1.Height = [System.Windows.GridLength]::Auto
      $rd2 = New-Object System.Windows.Controls.RowDefinition; $rd2.Height = [System.Windows.GridLength]::new(1, [System.Windows.GridUnitType]::Star)
      $rd3 = New-Object System.Windows.Controls.RowDefinition; $rd3.Height = [System.Windows.GridLength]::Auto
      [void]$cfgGrid.RowDefinitions.Add($rd1)
      [void]$cfgGrid.RowDefinitions.Add($rd2)
      [void]$cfgGrid.RowDefinitions.Add($rd3)

      $cfgLabel = New-Object System.Windows.Controls.TextBlock
      $cfgLabel.Text = 'Manifest-JSON bearbeiten (config-Sektion fuer Plugin-Einstellungen):'
      $cfgLabel.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushTextMuted' -Fallback '#9999CC'
      $cfgLabel.Margin = [System.Windows.Thickness]::new(0,0,0,8)
      [System.Windows.Controls.Grid]::SetRow($cfgLabel, 0)

      $cfgText = New-Object System.Windows.Controls.TextBox
      $cfgText.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
      $cfgText.FontSize = 12
      $cfgText.AcceptsReturn = $true
      $cfgText.AcceptsTab = $true
      $cfgText.VerticalScrollBarVisibility = [System.Windows.Controls.ScrollBarVisibility]::Auto
      $cfgText.Background = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSurfaceAlt' -Fallback '#13133A'
      $cfgText.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushTextPrimary' -Fallback '#E8E8F8'
      $cfgText.BorderBrush = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushBorder' -Fallback '#2D2D5A'
      $cfgText.Text = ($rawJson)
      [System.Windows.Controls.Grid]::SetRow($cfgText, 1)

      $cfgBtnRow = New-Object System.Windows.Controls.StackPanel
      $cfgBtnRow.Orientation = [System.Windows.Controls.Orientation]::Horizontal
      $cfgBtnRow.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
      $cfgBtnRow.Margin = [System.Windows.Thickness]::new(0,10,0,0)

      $cfgBtnSave = New-Object System.Windows.Controls.Button
      $cfgBtnSave.Content = 'Speichern'
      $cfgBtnSave.Width = 100
      $cfgBtnSave.Margin = [System.Windows.Thickness]::new(0,0,8,0)

      $cfgBtnCancel = New-Object System.Windows.Controls.Button
      $cfgBtnCancel.Content = 'Abbrechen'
      $cfgBtnCancel.Width = 100

      $cfgBtnSave.add_Click({
        try {
          $newJson = $cfgText.Text
          # Validate JSON
          $null = $newJson | ConvertFrom-Json -ErrorAction Stop
          $newJson | Out-File -LiteralPath $manifestPath -Encoding utf8 -Force
          Add-WpfLogLine -Ctx $Ctx -Line ('Plugin-Config gespeichert: {0}' -f [string]$selected.PluginId) -Level 'INFO'
          $cfgDialog.Close()
        } catch {
          [System.Windows.MessageBox]::Show(
            ('Ungültiges JSON: {0}' -f $_.Exception.Message),
            'Fehler',
            [System.Windows.MessageBoxButton]::OK,
            [System.Windows.MessageBoxImage]::Warning
          ) | Out-Null
        }
      }.GetNewClosure())

      $cfgBtnCancel.add_Click({ $cfgDialog.Close() }.GetNewClosure())

      [void]$cfgBtnRow.Children.Add($cfgBtnSave)
      [void]$cfgBtnRow.Children.Add($cfgBtnCancel)
      [System.Windows.Controls.Grid]::SetRow($cfgBtnRow, 2)

      [void]$cfgGrid.Children.Add($cfgLabel)
      [void]$cfgGrid.Children.Add($cfgText)
      [void]$cfgGrid.Children.Add($cfgBtnRow)
      $cfgDialog.Content = $cfgGrid
      [void]$cfgDialog.ShowDialog()
    } catch {
      Add-WpfLogLine -Ctx $Ctx -Line ('Plugin-Config fehlgeschlagen: {0}' -f $_.Exception.Message) -Level 'ERROR'
    }
  }.GetNewClosure())

  $btnInstallUrl.add_Click({
    try {
      # BUG-047 FIX: Check PluginTrustMode before allowing URL install
      $trustMode = 'trusted-only'
      $envTrust = [string]$env:ROMCLEANUP_PLUGIN_TRUST_MODE
      if (-not [string]::IsNullOrWhiteSpace($envTrust)) {
        $trustMode = $envTrust
      } elseif (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
        try { $trustMode = [string](Get-AppStateValue -Key 'PluginTrustMode' -Default 'trusted-only') } catch {}
      }
      if ($trustMode -eq 'signed-only') {
        [System.Windows.MessageBox]::Show(
          'Plugin-Installation von URLs ist im Modus "signed-only" nicht erlaubt. Nur signierte Plugins können installiert werden.',
          'TrustMode: signed-only',
          [System.Windows.MessageBoxButton]::OK,
          [System.Windows.MessageBoxImage]::Warning
        ) | Out-Null
        return
      }
      if ($trustMode -eq 'trusted-only') {
        $confirm = [System.Windows.MessageBox]::Show(
          ('TrustMode ist "trusted-only". Plugins von externen URLs sind nicht als vertrauenswürdig markiert.{0}{0}Trotzdem installieren?' -f [Environment]::NewLine),
          'TrustMode-Warnung',
          [System.Windows.MessageBoxButton]::YesNo,
          [System.Windows.MessageBoxImage]::Warning
        )
        if ($confirm -ne [System.Windows.MessageBoxResult]::Yes) { return }
      }

      $url = Show-WpfTextInputDialog -Owner $dialog -Title 'Plugin installieren' -Message 'Plugin-URL (.ps1) eingeben:' -DefaultValue ''
      if ([string]::IsNullOrWhiteSpace([string]$url)) { return }

      $operationsRoot = Join-Path (Get-Location).Path 'plugins\operations'
      Assert-DirectoryExists -Path $operationsRoot

      $uriObj = [System.Uri]::new([string]$url)
      $leaf = [System.IO.Path]::GetFileName($uriObj.AbsolutePath)
      if ([string]::IsNullOrWhiteSpace($leaf) -or -not $leaf.ToLowerInvariant().EndsWith('.ps1')) {
        throw 'URL muss auf eine .ps1-Datei zeigen.'
      }

      $target = Join-Path $operationsRoot $leaf
      Invoke-WebRequest -Uri $uriObj.AbsoluteUri -OutFile $target -UseBasicParsing -ErrorAction Stop | Out-Null
      Add-WpfLogLine -Ctx $Ctx -Line ('Plugin installiert: {0}' -f $target) -Level 'INFO'

      $manifestPath = [System.IO.Path]::ChangeExtension([string]$target, '.manifest.json')
      if (-not (Test-Path -LiteralPath $manifestPath -PathType Leaf)) {
        $defaultManifest = [ordered]@{
          id = [System.IO.Path]::GetFileNameWithoutExtension($leaf)
          version = '0.1.0'
          dependencies = @()
        }
        Write-JsonFile -Path $manifestPath -Data $defaultManifest -Depth 5
      }

      # Plugin-Schema-Validierung (5.4.4)
      if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
        if (Get-Command Test-JsonPayloadSchema -ErrorAction SilentlyContinue) {
          try {
            $mRaw = Get-Content -LiteralPath $manifestPath -Raw -ErrorAction Stop
            $mPayload = $mRaw | ConvertFrom-Json -ErrorAction Stop
            $schemaObj = Get-JsonSchema -Name 'plugin-manifest-v1' -ErrorAction SilentlyContinue
            if ($schemaObj) {
              $check = Test-JsonPayloadSchema -Payload $mPayload -Schema $schemaObj
              if (-not $check.IsValid) {
                $errMsg = ($check.Errors | Select-Object -First 3) -join '; '
                $result = [System.Windows.MessageBox]::Show(
                  ('Manifest-Schema-Validierung fehlgeschlagen:{0}{1}{0}{0}Plugin trotzdem installieren?' -f [Environment]::NewLine, $errMsg),
                  'Schema-Warnung',
                  [System.Windows.MessageBoxButton]::YesNo,
                  [System.Windows.MessageBoxImage]::Warning
                )
                if ($result -eq [System.Windows.MessageBoxResult]::No) {
                  Remove-Item -LiteralPath $target -Force -ErrorAction SilentlyContinue
                  Remove-Item -LiteralPath $manifestPath -Force -ErrorAction SilentlyContinue
                  Add-WpfLogLine -Ctx $Ctx -Line ('Plugin-Installation abgebrochen (Schema-Fehler): {0}' -f $leaf) -Level 'WARN'
                  return
                }
                Add-WpfLogLine -Ctx $Ctx -Line ('Plugin installiert mit Schema-Warnung: {0}' -f $errMsg) -Level 'WARN'
              }
            }
          } catch {
            Add-WpfLogLine -Ctx $Ctx -Line ('Schema-Pruefung uebersprungen: {0}' -f $_.Exception.Message) -Level 'DEBUG'
          }
        }
      }

      $f = Get-Item -LiteralPath $target -ErrorAction Stop
      [void]$items.Add([pscustomobject]@{
        Root = $operationsRoot
        Name = $f.Name
        Path = $f.FullName
        PluginId = [System.IO.Path]::GetFileNameWithoutExtension([string]$f.Name)
        Version = '0.1.0'
        DependencyCount = 0
      })
      & $refresh
    } catch {
      Add-WpfLogLine -Ctx $Ctx -Line ('Plugin-Installation fehlgeschlagen: {0}' -f $_.Exception.Message) -Level 'ERROR'
    }
  }.GetNewClosure())

  # ── Plugin-Marketplace (5.4.2) ──
  $btnMarketplace = New-Object System.Windows.Controls.Button
  $btnMarketplace.Content = 'Marketplace'
  $btnMarketplace.Width = 110
  $btnMarketplace.Margin = [System.Windows.Thickness]::new(0,0,8,0)

  $btnMarketplace.add_Click({
    try {
      $marketRoot = Join-Path (Get-Location).Path 'plugins\marketplace'
      $indexPath = Join-Path $marketRoot 'index.json'

      # Marketplace-Verzeichnis + index.json anlegen falls nicht vorhanden
      Assert-DirectoryExists -Path $marketRoot
      if (-not (Test-Path -LiteralPath $indexPath -PathType Leaf)) {
        $defaultIndex = [ordered]@{
          schemaVersion = 'marketplace-v1'
          description = 'Lokales Plugin-Repository. Plugins hier ablegen und in index.json registrieren.'
          plugins = @(
            [ordered]@{
              id = 'example-plugin'
              name = 'Beispiel-Plugin'
              version = '1.0.0'
              description = 'Ein Beispiel-Plugin zur Demonstration.'
              category = 'operations'
              file = 'example-plugin.ps1'
            }
          )
        }
        Write-JsonFile -Path $indexPath -Data $defaultIndex -Depth 5
      }

      $raw = Get-Content -LiteralPath $indexPath -Raw -ErrorAction Stop
      $index = $raw | ConvertFrom-Json -ErrorAction Stop
      $available = @()
      if ($index.plugins) { $available = @($index.plugins) }

      # Bereits installierte Plugin-IDs sammeln
      $installedIds = @{}
      foreach ($it in @($items)) { $installedIds[[string]$it.PluginId] = $true }

      # Marketplace-Dialog
      $mkDialog = New-WpfStyledDialog -Owner $dialog -Title 'Plugin-Marketplace (lokal)' -Width 620 -Height 400

      $mkGrid = New-Object System.Windows.Controls.Grid
      $mkGrid.Margin = [System.Windows.Thickness]::new(12)
      $mkRd1 = New-Object System.Windows.Controls.RowDefinition; $mkRd1.Height = [System.Windows.GridLength]::Auto
      $mkRd2 = New-Object System.Windows.Controls.RowDefinition; $mkRd2.Height = [System.Windows.GridLength]::new(1, [System.Windows.GridUnitType]::Star)
      $mkRd3 = New-Object System.Windows.Controls.RowDefinition; $mkRd3.Height = [System.Windows.GridLength]::Auto
      [void]$mkGrid.RowDefinitions.Add($mkRd1)
      [void]$mkGrid.RowDefinitions.Add($mkRd2)
      [void]$mkGrid.RowDefinitions.Add($mkRd3)

      $mkHint = New-Object System.Windows.Controls.TextBlock
      $mkHint.Text = '{0} Plugin(s) im Marketplace verfuegbar. Plugins liegen in plugins\marketplace\.' -f $available.Count
      $mkHint.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushTextMuted' -Fallback '#9999CC'
      $mkHint.Margin = [System.Windows.Thickness]::new(0,0,0,8)
      [System.Windows.Controls.Grid]::SetRow($mkHint, 0)

      $mkList = New-Object System.Windows.Controls.ListBox
      $mkList.Background = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSurfaceAlt' -Fallback '#13133A'
      $mkList.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushTextPrimary' -Fallback '#E8E8F8'
      $mkList.BorderBrush = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushBorder' -Fallback '#2D2D5A'
      $mkList.BorderThickness = [System.Windows.Thickness]::new(1)
      foreach ($p in $available) {
        $status = if ($installedIds.ContainsKey([string]$p.id)) { '[INSTALLIERT]' } else { '[VERFUEGBAR]' }
        $displayText = ('{0} {1} v{2} -- {3}' -f $status, [string]$p.name, [string]$p.version, [string]$p.description)
        [void]$mkList.Items.Add([pscustomobject]@{
          Display = $displayText
          PluginDef = $p
          Installed = $installedIds.ContainsKey([string]$p.id)
        })
      }
      $mkList.DisplayMemberPath = 'Display'
      [System.Windows.Controls.Grid]::SetRow($mkList, 1)

      $mkBtnRow = New-Object System.Windows.Controls.StackPanel
      $mkBtnRow.Orientation = [System.Windows.Controls.Orientation]::Horizontal
      $mkBtnRow.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
      $mkBtnRow.Margin = [System.Windows.Thickness]::new(0,10,0,0)

      $mkBtnInstall = New-Object System.Windows.Controls.Button
      $mkBtnInstall.Content = 'Installieren'
      $mkBtnInstall.Width = 110
      $mkBtnInstall.Margin = [System.Windows.Thickness]::new(0,0,8,0)
      $mkBtnInstall.add_Click({
        $sel = $mkList.SelectedItem
        if (-not $sel) { return }
        if ($sel.Installed) {
          [System.Windows.MessageBox]::Show('Plugin ist bereits installiert.', 'Info', [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Information) | Out-Null
          return
        }
        try {
          $pDef = $sel.PluginDef
          $category = if ([string]$pDef.category) { [string]$pDef.category } else { 'operations' }
          $destRoot = Join-Path (Get-Location).Path ('plugins\{0}' -f $category)
          Assert-DirectoryExists -Path $destRoot
          $sourceFile = Join-Path $marketRoot ([string]$pDef.file)
          if (-not (Test-Path -LiteralPath $sourceFile -PathType Leaf)) {
            [System.Windows.MessageBox]::Show(
              ('Plugin-Datei nicht gefunden: {0}{1}Bitte die Datei in plugins\marketplace\ ablegen.' -f [string]$pDef.file, [Environment]::NewLine),
              'Fehler', [System.Windows.MessageBoxButton]::OK, [System.Windows.MessageBoxImage]::Warning
            ) | Out-Null
            return
          }
          $destFile = Join-Path $destRoot ([string]$pDef.file)
          Copy-Item -LiteralPath $sourceFile -Destination $destFile -Force

          # Manifest erzeugen
          $mfPath = [System.IO.Path]::ChangeExtension($destFile, '.manifest.json')
          if (-not (Test-Path -LiteralPath $mfPath -PathType Leaf)) {
            $mf = [ordered]@{ id = [string]$pDef.id; version = [string]$pDef.version; dependencies = @() }
            Write-JsonFile -Path $mfPath -Data $mf -Depth 5
          }

          # In items-Liste aufnehmen
          $fInfo = Get-Item -LiteralPath $destFile -ErrorAction Stop
          [void]$items.Add([pscustomobject]@{
            Root = $destRoot; Name = $fInfo.Name; Path = $fInfo.FullName
            PluginId = [string]$pDef.id; Version = [string]$pDef.version; DependencyCount = 0
          })
          & $refresh

          Add-WpfLogLine -Ctx $Ctx -Line ('Plugin aus Marketplace installiert: {0}' -f [string]$pDef.name) -Level 'INFO'
          $mkDialog.Close()
        } catch {
          Add-WpfLogLine -Ctx $Ctx -Line ('Marketplace-Install fehlgeschlagen: {0}' -f $_.Exception.Message) -Level 'ERROR'
        }
      }.GetNewClosure())

      $mkBtnClose = New-Object System.Windows.Controls.Button
      $mkBtnClose.Content = 'Schliessen'
      $mkBtnClose.Width = 110
      $mkBtnClose.add_Click({ $mkDialog.Close() }.GetNewClosure())

      [void]$mkBtnRow.Children.Add($mkBtnInstall)
      [void]$mkBtnRow.Children.Add($mkBtnClose)
      [System.Windows.Controls.Grid]::SetRow($mkBtnRow, 2)

      [void]$mkGrid.Children.Add($mkHint)
      [void]$mkGrid.Children.Add($mkList)
      [void]$mkGrid.Children.Add($mkBtnRow)
      $mkDialog.Content = $mkGrid
      [void]$mkDialog.ShowDialog()
    } catch {
      Add-WpfLogLine -Ctx $Ctx -Line ('Marketplace fehlgeschlagen: {0}' -f $_.Exception.Message) -Level 'ERROR'
    }
  }.GetNewClosure())

  [void]$buttonRow.Children.Add($btnMarketplace)
  [void]$buttonRow.Children.Add($btnInstallUrl)
  [void]$buttonRow.Children.Add($btnConfig)
  [void]$buttonRow.Children.Add($btnEnable)
  [void]$buttonRow.Children.Add($btnDisable)
  [void]$buttonRow.Children.Add($btnClose)
  [System.Windows.Controls.Grid]::SetRow($buttonRow, 2)
  [void]$layout.Children.Add($buttonRow)

  $dialog.Content = $layout
  & $refresh
  [void]$dialog.ShowDialog()
}

function Invoke-WpfQuickRollback {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  if (-not (Get-Command Invoke-RunRollbackService -ErrorAction SilentlyContinue)) {
    throw 'Rollback-Service nicht verfügbar (Invoke-RunRollbackService fehlt).'
  }

  $auditRoot = ''
  if ($Ctx.ContainsKey('txtAuditRoot') -and $Ctx['txtAuditRoot']) {
    $auditRoot = [string]$Ctx['txtAuditRoot'].Text
  }
  if ([string]::IsNullOrWhiteSpace($auditRoot)) {
    $auditRoot = Join-Path (Get-Location).Path 'audit-logs'
  }
  if (-not (Test-Path -LiteralPath $auditRoot -PathType Container)) {
    throw 'Audit-Verzeichnis nicht gefunden.'
  }

  $selectedAudit = @(Get-ChildItem -LiteralPath $auditRoot -Filter '*.csv' -File -ErrorAction SilentlyContinue | Sort-Object LastWriteTimeUtc -Descending | Select-Object -First 1)
  if ($selectedAudit.Count -eq 0) {
    throw 'Keine Audit-CSV gefunden.'
  }

  $roots = Get-WpfRollbackRoots -Ctx $Ctx
  $restoreRoots = $roots.RestoreRoots
  $currentRoots = $roots.CurrentRoots

  $confirm = [System.Windows.MessageBox]::Show(
    ("Schnell-Rollback`n`nAudit: {0}`nRestore-Roots: {1}`nCurrent-Roots: {2}`n`nRollback jetzt ausführen?" -f $selectedAudit[0].Name, $restoreRoots.Count, $currentRoots.Count),
    'Schnell-Rollback',
    [System.Windows.MessageBoxButton]::YesNo,
    [System.Windows.MessageBoxImage]::Warning)
  if ($confirm -ne [System.Windows.MessageBoxResult]::Yes) { return }

  Add-WpfLogLine -Ctx $Ctx -Line ("=== Schnell-Rollback aus Audit: {0} ===" -f $selectedAudit[0].FullName) -Level 'INFO'

  $rollback = Invoke-WpfRollbackCore -AuditCsvPath ([string]$selectedAudit[0].FullName) -AllowedRestoreRoots $restoreRoots -AllowedCurrentRoots $currentRoots -Ctx $Ctx

  Add-WpfLogLine -Ctx $Ctx -Line ("Schnell-Rollback Ergebnis: zurück={0}, skip-unsafe={1}, skip-fehlt={2}, skip-existiert={3}, fehler={4}" -f $rollback.RolledBack, $rollback.SkippedUnsafe, $rollback.SkippedMissingDest, $rollback.SkippedCollision, $rollback.Failed) -Level 'INFO'
  [System.Windows.MessageBox]::Show(
    ("Schnell-Rollback abgeschlossen.`n`nZurückverschoben: {0}`nSkip (unsicher): {1}`nSkip (Quelle fehlt): {2}`nSkip (Ziel existiert): {3}`nFehler: {4}" -f $rollback.RolledBack, $rollback.SkippedUnsafe, $rollback.SkippedMissingDest, $rollback.SkippedCollision, $rollback.Failed),
    'Schnell-Rollback',
    [System.Windows.MessageBoxButton]::OK,
    [System.Windows.MessageBoxImage]::Information) | Out-Null
}

function Set-WpfWatchMode {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][bool]$Enabled
  )

  foreach ($entry in @($script:WpfWatchRegistry.Values)) {
    try { if ($entry.Subscribers) { foreach ($sub in @($entry.Subscribers)) { Unregister-Event -SourceIdentifier $sub -ErrorAction SilentlyContinue } } } catch { }
    try { if ($entry.Watcher) { $entry.Watcher.EnableRaisingEvents = $false; $entry.Watcher.Dispose() } } catch { }
  }
  $script:WpfWatchRegistry.Clear()
  $script:WpfWatchPendingRun = $false
  $script:WpfWatchLastEventUtc = [datetime]::MinValue

  if (-not $Enabled) {
    Set-WpfAdvancedStatus -Ctx $Ctx -Text 'Watch-Mode deaktiviert'
    return
  }

  $roots = @((Get-WpfRunParameters -Ctx $Ctx).Roots)
  foreach ($root in $roots) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) { continue }
    $watcher = New-Object System.IO.FileSystemWatcher
    $watcher.Path = $root
    $watcher.IncludeSubdirectories = $true
    $watcher.NotifyFilter = [IO.NotifyFilters]'FileName, DirectoryName, LastWrite, Size'
    $watcher.EnableRaisingEvents = $true

    $idBase = ('RomCleanup.WpfWatch.{0}' -f ([guid]::NewGuid().ToString('N')))
    $debounceMs = 5000
    $action = {
      try {
        $w = $Event.MessageData.Window
        $ctxRef = $Event.MessageData.Ctx
        $current = [datetime]::UtcNow
        $delta = ($current - $script:WpfWatchLastEventUtc).TotalMilliseconds
        if ($script:WpfWatchPendingRun -or ($delta -lt $debounceMs)) { return }
        $script:WpfWatchPendingRun = $true
        $script:WpfWatchLastEventUtc = $current
        if (-not $ctxRef['btnRunGlobal'].IsEnabled) { return }
        $w.Dispatcher.BeginInvoke([System.Action]{
          try {
            if (-not (Set-WpfViewModelProperty -Ctx $ctxRef -Name 'DryRun' -Value $true)) {
              $ctxRef['chkReportDryRun'].IsChecked = $true
            }
            Start-WpfOperationAsync -Window $w -Ctx $ctxRef
          } catch { }
          finally { $script:WpfWatchPendingRun = $false }
        }) | Out-Null
      } catch {
        $script:WpfWatchPendingRun = $false
      }
    }

    $sub1 = ($idBase + '.Created')
    $sub2 = ($idBase + '.Changed')
    Register-ObjectEvent -InputObject $watcher -EventName Created -SourceIdentifier $sub1 -Action $action -MessageData @{ Window = $Window; Ctx = $Ctx } | Out-Null
    Register-ObjectEvent -InputObject $watcher -EventName Changed -SourceIdentifier $sub2 -Action $action -MessageData @{ Window = $Window; Ctx = $Ctx } | Out-Null

    $script:WpfWatchRegistry[$root] = [pscustomobject]@{
      Watcher = $watcher
      Subscribers = @($sub1, $sub2)
    }
  }

  Set-WpfAdvancedStatus -Ctx $Ctx -Text ('Watch-Mode aktiv ({0} Roots)' -f $script:WpfWatchRegistry.Count)
}

function Invoke-WpfAutoProfileByConsole {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not (Get-Command Get-ConfigProfiles -ErrorAction SilentlyContinue)) { return }
  $profiles = Get-ConfigProfiles
  if (-not $profiles -or $profiles.Count -eq 0) { return }

  $roots = @((Get-WpfRunParameters -Ctx $Ctx).Roots)
  $detected = $null
  foreach ($root in $roots) {
    $leaf = [System.IO.Path]::GetFileName([string]$root)
    if ([string]::IsNullOrWhiteSpace($leaf)) { continue }
    $normalizedLeaf = $leaf.Trim().ToLowerInvariant()
    $candidates = @($profiles.Keys | Where-Object { ([string]$_).Trim().ToLowerInvariant() -eq $normalizedLeaf })
    if ($candidates.Count -gt 0) { $detected = [string]$candidates[0]; break }
  }

  if (-not [string]::IsNullOrWhiteSpace($detected) -and $Ctx.ContainsKey('cmbConfigProfile') -and $Ctx['cmbConfigProfile']) {
    $Ctx['cmbConfigProfile'].Text = $detected
    Add-WpfLogLine -Ctx $Ctx -Line ("Auto-Profil gewählt: {0}" -f $detected) -Level 'INFO'
  }
}

function New-WpfInverseRollbackCsv {
  param([Parameter(Mandatory)][string]$AuditCsvPath)

  $rows = @(Import-Csv -LiteralPath $AuditCsvPath -Encoding UTF8)
  $inverseRows = New-Object System.Collections.Generic.List[object]
  foreach ($row in $rows) {
    $action = ([string]$row.Action).Trim().ToUpperInvariant()
    if ($action -notin @('MOVE','JUNK','BIOS-MOVE')) { continue }
    [void]$inverseRows.Add([pscustomobject]@{
      Action = $action
      Source = [string]$row.Dest
      Dest = [string]$row.Source
    })
  }

  $target = Join-Path ([System.IO.Path]::GetDirectoryName($AuditCsvPath)) ('rollback-inverse-{0}.csv' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
  $inverseRows | Export-Csv -LiteralPath $target -NoTypeInformation -Encoding UTF8
  return $target
}

function Register-WpfAdvancedFeatureHandlers {
  <#
  .SYNOPSIS
    Registers advanced feature event handlers (Slice 6):
    Plugin manager, rollback, watch-mode, auto-profile, collection diff, conflict policy.
  .PARAMETER Window
    The WPF Window object.
  .PARAMETER Ctx
    The named-element hashtable from Get-WpfNamedElements.
  .PARAMETER PersistSettingsNow
    Scriptblock to persist settings immediately.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$PersistSettingsNow
  )

  # ── Collection Diff ────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnCollectionDiff') -and $Ctx['btnCollectionDiff']) {
    $Ctx['btnCollectionDiff'].add_Click({
      try {
        $outPath = Invoke-WpfCollectionDiffReport -Ctx $Ctx
        if ($outPath) { Start-Process $outPath | Out-Null }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Collection-Diff fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Plugin Manager ────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnPluginManager') -and $Ctx['btnPluginManager']) {
    $Ctx['btnPluginManager'].add_Click({
      try { Invoke-WpfPluginManager -Window $Window -Ctx $Ctx } catch { Add-WpfLogLine -Ctx $Ctx -Line ("Plugin-Manager fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR' }
    }.GetNewClosure())
  }

  # ── Auto Profile ──────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnAutoProfile') -and $Ctx['btnAutoProfile']) {
    $Ctx['btnAutoProfile'].add_Click({
      try { Invoke-WpfAutoProfileByConsole -Ctx $Ctx } catch { Add-WpfLogLine -Ctx $Ctx -Line ("Auto-Profil fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR' }
    }.GetNewClosure())
  }

  # ── Watch Mode ────────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnWatchApply') -and $Ctx['btnWatchApply']) {
    $Ctx['btnWatchApply'].add_Click({
      try {
        $enabled = (Get-WpfIsChecked -Ctx $Ctx -Key 'chkWatchMode')
        Set-WpfWatchMode -Window $Window -Ctx $Ctx -Enabled:$enabled
        Add-WpfLogLine -Ctx $Ctx -Line ("Watch-Mode {0}" -f $(if ($enabled) { 'aktiviert' } else { 'deaktiviert' })) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Watch-Mode fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Conflict Policy ───────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnConflictPolicy') -and $Ctx['btnConflictPolicy']) {
    $Ctx['btnConflictPolicy'].add_Click({
      try {
        $current = [string](Get-AppStateValue -Key 'ConflictPolicy' -Default 'KeepExisting')
          $policyInput = Read-WpfPromptText -Owner $Window -Title 'Conflict-Policy' -Message 'Policy eingeben: KeepExisting | Overwrite | CreateDuplicate' -DefaultValue $current
          if (-not [string]::IsNullOrWhiteSpace($policyInput)) {
            $normalized = $policyInput.Trim()
          if ($normalized -in @('KeepExisting','Overwrite','CreateDuplicate')) {
            [void](Set-AppStateValue -Key 'ConflictPolicy' -Value $normalized)
            Add-WpfLogLine -Ctx $Ctx -Line ("Conflict-Policy gesetzt: {0}" -f $normalized) -Level 'INFO'
          }
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Conflict-Policy fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Quick Rollback ────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnRollbackQuick') -and $Ctx['btnRollbackQuick']) {
    $Ctx['btnRollbackQuick'].add_Click({
      try {
        Invoke-WpfQuickRollback -Window $Window -Ctx $Ctx
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Schnell-Rollback fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Rollback Undo/Redo ────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnRollbackUndo') -and $Ctx['btnRollbackUndo']) {
    $Ctx['btnRollbackUndo'].IsEnabled = ($script:WpfRollbackUndoStack.Count -gt 0)
    $Ctx['btnRollbackUndo'].add_Click({
      try {
        if ($script:WpfRollbackUndoStack.Count -le 0) { return }
        $entry = $script:WpfRollbackUndoStack[$script:WpfRollbackUndoStack.Count - 1]
        $script:WpfRollbackUndoStack.RemoveAt($script:WpfRollbackUndoStack.Count - 1)
        $undoResult = Invoke-RunRollbackService -Parameters @{
          AuditCsvPath = [string]$entry.UndoCsvPath
          AllowedRestoreRoots = @($entry.AllowedRestoreRoots)
          AllowedCurrentRoots = @($entry.AllowedCurrentRoots)
          Log = { param([string]$m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' }
        }
        Add-WpfLogLine -Ctx $Ctx -Line ("Rollback Undo: zurück={0}, fehler={1}" -f $undoResult.RolledBack, $undoResult.Failed) -Level 'INFO'
        [void]$script:WpfRollbackRedoStack.Add($entry)
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Rollback Undo fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      } finally {
        $Ctx['btnRollbackUndo'].IsEnabled = ($script:WpfRollbackUndoStack.Count -gt 0)
        if ($Ctx.ContainsKey('btnRollbackRedo') -and $Ctx['btnRollbackRedo']) { $Ctx['btnRollbackRedo'].IsEnabled = ($script:WpfRollbackRedoStack.Count -gt 0) }
      }
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnRollbackRedo') -and $Ctx['btnRollbackRedo']) {
    $Ctx['btnRollbackRedo'].IsEnabled = ($script:WpfRollbackRedoStack.Count -gt 0)
    $Ctx['btnRollbackRedo'].add_Click({
      try {
        if ($script:WpfRollbackRedoStack.Count -le 0) { return }
        $entry = $script:WpfRollbackRedoStack[$script:WpfRollbackRedoStack.Count - 1]
        $script:WpfRollbackRedoStack.RemoveAt($script:WpfRollbackRedoStack.Count - 1)
        $redoResult = Invoke-RunRollbackService -Parameters @{
          AuditCsvPath = [string]$entry.RedoCsvPath
          AllowedRestoreRoots = @($entry.AllowedRestoreRoots)
          AllowedCurrentRoots = @($entry.AllowedCurrentRoots)
          Log = { param([string]$m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' }
        }
        Add-WpfLogLine -Ctx $Ctx -Line ("Rollback Redo: zurück={0}, fehler={1}" -f $redoResult.RolledBack, $redoResult.Failed) -Level 'INFO'
        [void]$script:WpfRollbackUndoStack.Add($entry)
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Rollback Redo fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      } finally {
        $Ctx['btnRollbackRedo'].IsEnabled = ($script:WpfRollbackRedoStack.Count -gt 0)
        if ($Ctx.ContainsKey('btnRollbackUndo') -and $Ctx['btnRollbackUndo']) { $Ctx['btnRollbackUndo'].IsEnabled = ($script:WpfRollbackUndoStack.Count -gt 0) }
      }
    }.GetNewClosure())
  }

  # ── Rollback Wizard ───────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnRollback') -and $Ctx['btnRollback']) {
    $Ctx['btnRollback'].add_Click({
      Invoke-WpfRollbackWizard -Window $Window -Ctx $Ctx
    }.GetNewClosure())
  }

  # ── DAT-Rename (ISS-003) ──────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnDatRename') -and $Ctx['btnDatRename']) {
    $Ctx['btnDatRename'].add_Click({
      try {
        $datRoot = Get-AppStateValue -Key 'DatRoot' -Default ''
        $hashType = Get-AppStateValue -Key 'DatHashType' -Default 'SHA1'
        if ([string]::IsNullOrWhiteSpace($datRoot)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'DAT-Rename: Kein DatRoot konfiguriert.' -Level 'WARNING'
          return
        }
        $roots = Get-WpfRootsList -Ctx $Ctx
        if (-not $roots -or $roots.Count -eq 0) {
          Add-WpfLogLine -Ctx $Ctx -Line 'DAT-Rename: Keine Roots konfiguriert.' -Level 'WARNING'
          return
        }
        $datIdx = Get-DatIndex -DatRoot $datRoot -HashType $hashType
        $files = foreach ($r in $roots) {
          if (Test-Path -LiteralPath $r -PathType Container) {
            Get-ChildItem -LiteralPath $r -Recurse -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
          }
        }
        if (-not $files) { Add-WpfLogLine -Ctx $Ctx -Line 'DAT-Rename: Keine Dateien gefunden.' -Level 'INFO'; return }
        $renameResult = Invoke-RunDatRenameService -Operation 'Preview' -Files @($files) -DatIndex $datIdx -HashType $hashType -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        Add-WpfLogLine -Ctx $Ctx -Line ("DAT-Rename Preview: {0} würden umbenannt, {1} kein Match, {2} Konflikte" -f $renameResult.Renamed, $renameResult.NoMatch, $renameResult.Conflicts) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("DAT-Rename fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── M3U-Generierung (ISS-004) ─────────────────────────────────────────
  if ($Ctx.ContainsKey('btnGenerateM3u') -and $Ctx['btnGenerateM3u']) {
    $Ctx['btnGenerateM3u'].add_Click({
      try {
        $roots = Get-WpfRootsList -Ctx $Ctx
        if (-not $roots -or $roots.Count -eq 0) {
          Add-WpfLogLine -Ctx $Ctx -Line 'M3U: Keine Roots konfiguriert.' -Level 'WARNING'
          return
        }
        $discFiles = foreach ($r in $roots) {
          if (Test-Path -LiteralPath $r -PathType Container) {
            Get-ChildItem -LiteralPath $r -Recurse -File -ErrorAction SilentlyContinue |
              Where-Object { $_.Extension -imatch '^\.(chd|cue|ccd|gdi|iso|pbp)$' } |
              Select-Object -ExpandProperty FullName
          }
        }
        if (-not $discFiles) { Add-WpfLogLine -Ctx $Ctx -Line 'M3U: Keine Disc-Dateien gefunden.' -Level 'INFO'; return }
        $m3uResult = Invoke-RunM3uGenerationService -Files @($discFiles) -OutputDir $roots[0] -Mode 'DryRun' -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        Add-WpfLogLine -Ctx $Ctx -Line ("M3U Preview: {0} würden generiert, {1} übersprungen" -f $m3uResult.Generated, $m3uResult.Skipped) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("M3U-Generierung fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── CSV-Export (ISS-009) ───────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnExportCsv') -and $Ctx['btnExportCsv']) {
    $Ctx['btnExportCsv'].add_Click({
      try {
        $lastResult = Get-AppStateValue -Key 'LastDedupeResult' -Default $null
        if (-not $lastResult) {
          Add-WpfLogLine -Ctx $Ctx -Line 'CSV-Export: Kein Scan-Ergebnis vorhanden. Zuerst DryRun ausführen.' -Level 'WARNING'
          return
        }
        $csvPath = Join-Path $PSScriptRoot ('reports\collection-export-{0}.csv' -f (Get-Date -Format 'yyyyMMdd-HHmmss'))
        $items = @()
        if ($lastResult.PSObject.Properties.Name -contains 'AllItems') { $items = @($lastResult.AllItems) }
        elseif ($lastResult.PSObject.Properties.Name -contains 'Winners') { $items = @($lastResult.Winners) }
        if ($items.Count -eq 0) { Add-WpfLogLine -Ctx $Ctx -Line 'CSV-Export: Keine Daten zum Exportieren.' -Level 'WARNING'; return }
        Invoke-RunCsvExportService -Items $items -OutputPath $csvPath -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        Add-WpfLogLine -Ctx $Ctx -Line ("CSV-Export: {0}" -f $csvPath) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("CSV-Export fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── CLI-Command Export (ISS-008) ───────────────────────────────────────
  if ($Ctx.ContainsKey('btnExportCliCommand') -and $Ctx['btnExportCliCommand']) {
    $Ctx['btnExportCliCommand'].add_Click({
      try {
        $settings = Get-WpfRunParameters -Ctx $Ctx
        $cmd = Invoke-RunCliExportService -Settings $settings -Ports @{}
        if ($cmd) {
          [System.Windows.Clipboard]::SetText($cmd)
          Add-WpfLogLine -Ctx $Ctx -Line ('CLI-Kommando in Zwischenablage kopiert: {0}' -f ($cmd.Substring(0, [Math]::Min(100, $cmd.Length)))) -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("CLI-Export fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── RetroArch Playlist Export (ISS-015) ────────────────────────────────
  if ($Ctx.ContainsKey('btnExportRetroArch') -and $Ctx['btnExportRetroArch']) {
    $Ctx['btnExportRetroArch'].add_Click({
      try {
        $lastResult = Get-AppStateValue -Key 'LastDedupeResult' -Default $null
        if (-not $lastResult) {
          Add-WpfLogLine -Ctx $Ctx -Line 'RetroArch-Export: Kein Scan-Ergebnis. Zuerst DryRun ausführen.' -Level 'WARNING'
          return
        }
        $roots = Get-WpfRootsList -Ctx $Ctx
        $outputPath = if ($roots -and $roots.Count -gt 0) { Join-Path $roots[0] '_playlists' } else { Join-Path $PSScriptRoot 'reports\_playlists' }
        $items = @()
        if ($lastResult.PSObject.Properties.Name -contains 'Winners') { $items = @($lastResult.Winners) }
        if ($items.Count -eq 0) { Add-WpfLogLine -Ctx $Ctx -Line 'RetroArch-Export: Keine Winner-Daten.' -Level 'WARNING'; return }
        Invoke-RunRetroArchExportService -Items $items -OutputPath $outputPath -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        Add-WpfLogLine -Ctx $Ctx -Line ("RetroArch-Playlists exportiert nach: {0}" -f $outputPath) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("RetroArch-Export fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── ECM-Dekompression (ISS-005) ───────────────────────────────────────
  if ($Ctx.ContainsKey('btnEcmDecompress') -and $Ctx['btnEcmDecompress']) {
    $Ctx['btnEcmDecompress'].add_Click({
      try {
        $roots = Get-WpfRootsList -Ctx $Ctx
        if (-not $roots -or $roots.Count -eq 0) {
          Add-WpfLogLine -Ctx $Ctx -Line 'ECM: Keine Roots konfiguriert.' -Level 'WARNING'
          return
        }
        $ecmFiles = foreach ($r in $roots) {
          if (Test-Path -LiteralPath $r -PathType Container) {
            Get-ChildItem -LiteralPath $r -Filter '*.ecm' -Recurse -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
          }
        }
        if (-not $ecmFiles) { Add-WpfLogLine -Ctx $Ctx -Line 'ECM: Keine .ecm-Dateien gefunden.' -Level 'INFO'; return }
        $ecmResult = Invoke-RunEcmDecompressService -Files @($ecmFiles) -Mode 'DryRun' -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        Add-WpfLogLine -Ctx $Ctx -Line ("ECM Preview: {0} würden dekomprimiert, {1} fehlgeschlagen" -f $ecmResult.Success, $ecmResult.Failed) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("ECM-Dekompression fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Archive-Repack (ISS-006) ──────────────────────────────────────────
  if ($Ctx.ContainsKey('btnArchiveRepack') -and $Ctx['btnArchiveRepack']) {
    $Ctx['btnArchiveRepack'].add_Click({
      try {
        $roots = Get-WpfRootsList -Ctx $Ctx
        if (-not $roots -or $roots.Count -eq 0) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Repack: Keine Roots konfiguriert.' -Level 'WARNING'
          return
        }
        $targetFormat = 'zip'
        if ($Ctx.ContainsKey('cmbRepackFormat') -and $Ctx['cmbRepackFormat']) {
          $sel = $Ctx['cmbRepackFormat'].SelectedItem
          if ($sel) { $targetFormat = [string]$sel }
        }
        $archiveFiles = foreach ($r in $roots) {
          if (Test-Path -LiteralPath $r -PathType Container) {
            $sourceExts = if ($targetFormat -eq 'zip') { @('*.7z','*.rar') } else { @('*.zip','*.rar') }
            foreach ($ext in $sourceExts) {
              Get-ChildItem -LiteralPath $r -Filter $ext -Recurse -File -ErrorAction SilentlyContinue | Select-Object -ExpandProperty FullName
            }
          }
        }
        if (-not $archiveFiles) { Add-WpfLogLine -Ctx $Ctx -Line 'Repack: Keine umzuwandelnden Archive gefunden.' -Level 'INFO'; return }
        $repackResult = Invoke-RunArchiveRepackService -Files @($archiveFiles) -TargetFormat $targetFormat -Mode 'DryRun' -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        Add-WpfLogLine -Ctx $Ctx -Line ("Repack Preview: {0} würden umgepackt, {1} übersprungen" -f $repackResult.Repacked, $repackResult.Skipped) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Archive-Repack fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Webhook Test (ISS-016) ────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnTestWebhook') -and $Ctx['btnTestWebhook']) {
    $Ctx['btnTestWebhook'].add_Click({
      try {
        $url = ''
        if ($Ctx.ContainsKey('txtWebhookUrl') -and $Ctx['txtWebhookUrl']) { $url = $Ctx['txtWebhookUrl'].Text }
        if ([string]::IsNullOrWhiteSpace($url)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Webhook: Keine URL eingegeben.' -Level 'WARNING'
          return
        }
        $testSummary = @{ Status = 'test'; Message = 'RomCleanup Webhook-Test'; Timestamp = (Get-Date).ToString('o') }
        Invoke-RunWebhookService -WebhookUrl $url -Summary $testSummary -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        Add-WpfLogLine -Ctx $Ctx -Line 'Webhook-Test erfolgreich gesendet.' -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Webhook-Test fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Run History (ISS-018) ──────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnRunHistory') -and $Ctx['btnRunHistory']) {
    $Ctx['btnRunHistory'].add_Click({
      try {
        $reportsDir = Join-Path $PSScriptRoot 'reports'
        $history = Get-RunHistory -ReportsDir $reportsDir -MaxEntries 50
        if (-not $history -or $history.Count -eq 0) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Run-History: Keine bisherigen Runs gefunden.' -Level 'INFO'
          return
        }
        foreach ($entry in $history | Select-Object -First 10) {
          $line = "{0} | {1} | {2}" -f $entry.Date, $entry.Mode, $entry.Status
          Add-WpfLogLine -Ctx $Ctx -Line $line -Level 'INFO'
        }
        Add-WpfLogLine -Ctx $Ctx -Line ("Run-History: {0} Einträge total" -f $history.Count) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Run-History fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ═══════════════════════════════════════════════════════════════════════
  # FEATURE-TAB HANDLERS (Phase 1-4 Feature Module)
  # ═══════════════════════════════════════════════════════════════════════

  # ── Analyse & Berichte ─────────────────────────────────────────────────

  # Konvertierungs-Schätzung (QW-04)
  if ($Ctx.ContainsKey('btnConversionEstimate') -and $Ctx['btnConversionEstimate']) {
    $Ctx['btnConversionEstimate'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Konvertierungs-Schätzung: Keine Roots konfiguriert.' -Level 'WARNING'; return }
        $files = foreach ($r in $roots) {
          if (Test-Path -LiteralPath $r -PathType Container) {
            Get-ChildItem -LiteralPath $r -Recurse -File -ErrorAction SilentlyContinue |
              Select-Object -ExpandProperty FullName
          }
        }
        if (-not $files) { Add-WpfLogLine -Ctx $Ctx -Line 'Konvertierungs-Schätzung: Keine Dateien gefunden.' -Level 'INFO'; return }
        $estimate = Get-ConversionSavingsEstimate -Files $files

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnConversionEstimate'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Konvertierungs-Schätzung' -Width 500 -Height 360

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Einsparungspotential durch Konvertierung'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $items = @(
          "Dateien gesamt:        $($estimate.FileCount)"
          "Konvertierbar:         $($estimate.ConvertibleCount)"
          "Aktuelle Größe:        {0:N0} MB" -f ($estimate.TotalSizeBytes / 1MB)
          "Geschätzte Einsparung: {0:N0} MB" -f ($estimate.TotalSavingsBytes / 1MB)
          "Einsparung:            {0:P0}" -f $(if($estimate.TotalSizeBytes -gt 0){ $estimate.TotalSavingsBytes / $estimate.TotalSizeBytes } else { 0 })
        )
        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        foreach ($line in $items) { [void]$lb.Items.Add($line) }

        if ($estimate.PSObject.Properties.Name -contains 'ByFormat' -and $estimate.ByFormat) {
          foreach ($fmt in $estimate.ByFormat.GetEnumerator() | Select-Object -First 8) {
            [void]$lb.Items.Add(("  {0,-8} → {1:N0} MB einsparbar" -f $fmt.Key, ($fmt.Value / 1MB)))
          }
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Schätzung: {0:N0} MB einsparbar bei {1} Dateien" -f ($estimate.TotalSavingsBytes / 1MB), $estimate.FileCount) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Konvertierungs-Schätzung fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Junk-Bericht (QW-05)
  if ($Ctx.ContainsKey('btnJunkReport') -and $Ctx['btnJunkReport']) {
    $Ctx['btnJunkReport'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Junk-Bericht: Keine Roots konfiguriert.' -Level 'WARNING'; return }
        $result = Invoke-RunJunkReportService -Roots $roots -Log { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnJunkReport'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Junk-Bericht' -Width 600 -Height 450

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = "Junk-Bericht: $($result.JunkCount) Junk-Dateien gefunden"
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(1)
        if ($result.PSObject.Properties.Name -contains 'Items' -and $result.Items) {
          foreach ($item in $result.Items) {
            $display = if ($item.PSObject.Properties.Name -contains 'Path') { $item.Path } else { $item.ToString() }
            [void]$lb.Items.Add($display)
          }
        } else {
          [void]$lb.Items.Add("$($result.JunkCount) Junk-Dateien gefunden")
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Junk-Bericht: {0} Junk-Dateien gefunden" -f $result.JunkCount) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Junk-Bericht fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ROM-Filter (QW-08)
  if ($Ctx.ContainsKey('btnRomFilter') -and $Ctx['btnRomFilter']) {
    $Ctx['btnRomFilter'].add_Click({
      try {
        $query = Show-WpfTextInputDialog -Window $Window -Title 'ROM-Filter' -Prompt 'Suchbegriff eingeben:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($query)) { return }
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'ROM-Filter: Keine Roots.' -Level 'WARNING'; return }
        $results = Search-RomCollection -Roots $roots -Query $query
        $allResults = @($results)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnRomFilter'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('ROM-Filter — {0} Treffer' -f $allResults.Count) -Width 650 -Height 480

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = "Suche: '$query' — $($allResults.Count) Treffer"
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($item in $allResults) {
          $display = if ($item.PSObject.Properties.Name -contains 'Name') { $item.Name } else { $item.ToString() }
          [void]$lb.Items.Add($display)
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("ROM-Filter fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Duplikat-Heatmap (QW-09)
  if ($Ctx.ContainsKey('btnDuplicateHeatmap') -and $Ctx['btnDuplicateHeatmap']) {
    $Ctx['btnDuplicateHeatmap'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Duplikat-Heatmap: Keine Roots.' -Level 'WARNING'; return }
        $data = Get-DuplicateHeatmapData -Roots $roots
        if (-not $data -or $data.Count -eq 0) { Add-WpfLogLine -Ctx $Ctx -Line 'Duplikat-Heatmap: Keine Duplikate gefunden.' -Level 'INFO'; return }

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnDuplicateHeatmap'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Duplikat-Heatmap' -Width 560 -Height 420

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Duplikate nach Konsole'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $maxDupes = ($data | Measure-Object -Property DuplicateCount -Maximum).Maximum
        foreach ($entry in $data | Sort-Object DuplicateCount -Descending) {
          $barLen = if ($maxDupes -gt 0) { [math]::Max(1, [int](($entry.DuplicateCount / $maxDupes) * 30)) } else { 1 }
          $bar = [string]::new([char]0x2588, $barLen)
          [void]$lb.Items.Add(("{0,-20} {1,5} Duplikate  {2}" -f $entry.Console, $entry.DuplicateCount, $bar))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Duplikat-Heatmap fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Fehlende ROMs (MF-01)
  if ($Ctx.ContainsKey('btnMissingRom') -and $Ctx['btnMissingRom']) {
    $Ctx['btnMissingRom'].add_Click({
      try {
        $datIndex = Get-AppStateValue 'DatIndex'
        if (-not $datIndex) { Add-WpfLogLine -Ctx $Ctx -Line 'Fehlende ROMs: Kein DAT-Index geladen.' -Level 'WARNING'; return }
        $foundHashes = @(Get-AppStateValue 'FoundHashes')
        $result = Invoke-RunMissingRomService -DatIndex $datIndex -FoundHashes $foundHashes -Ports @{}
        $allMissing = @($result)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnMissingRom'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('Fehlende ROMs — {0} Einträge' -f $allMissing.Count) -Width 650 -Height 480

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = "$($allMissing.Count) fehlende ROMs laut DAT-Index"
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($entry in $allMissing) {
          $display = if ($entry.PSObject.Properties.Name -contains 'Name') { $entry.Name } elseif ($entry.PSObject.Properties.Name -contains 'GameName') { $entry.GameName } else { $entry.ToString() }
          [void]$lb.Items.Add($display)
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Fehlende ROMs: {0} fehlende Einträge" -f $allMissing.Count) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Fehlende ROMs fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Cross-Root-Duplikate (MF-02)
  if ($Ctx.ContainsKey('btnCrossRootDupe') -and $Ctx['btnCrossRootDupe']) {
    $Ctx['btnCrossRootDupe'].add_Click({
      try {
        $fileIndex = Get-AppStateValue 'FileIndex'
        if (-not $fileIndex) { Add-WpfLogLine -Ctx $Ctx -Line 'Cross-Root: Kein FileIndex vorhanden. Bitte zuerst DryRun ausführen.' -Level 'WARNING'; return }
        $result = Invoke-RunCrossRootDupeService -FileIndex $fileIndex -Progress { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        $allGroups = @($result)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCrossRootDupe'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('Cross-Root-Duplikate — {0} Gruppen' -f $allGroups.Count) -Width 650 -Height 480

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = "$($allGroups.Count) Duplikatgruppen über Root-Ordner hinweg"
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($group in $allGroups) {
          $name = if ($group.PSObject.Properties.Name -contains 'Key') { $group.Key } else { $group.ToString() }
          $paths = if ($group.PSObject.Properties.Name -contains 'Paths') { ($group.Paths -join ', ') } else { '' }
          [void]$lb.Items.Add(("{0}  →  {1}" -f $name, $paths))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Cross-Root-Duplikate fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Header-Analyse (MF-03)
  if ($Ctx.ContainsKey('btnHeaderAnalysis') -and $Ctx['btnHeaderAnalysis']) {
    $Ctx['btnHeaderAnalysis'].add_Click({
      try {
        $filePath = Show-WpfTextInputDialog -Window $Window -Title 'Header-Analyse' -Prompt 'ROM-Dateipfad eingeben:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($filePath)) { return }
        if (-not (Test-Path -LiteralPath $filePath)) { Add-WpfLogLine -Ctx $Ctx -Line 'Header-Analyse: Datei nicht gefunden.' -Level 'WARNING'; return }
        $header = Read-RomHeader -Path $filePath

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnHeaderAnalysis'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Header-Analyse' -Width 520 -Height 380

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'ROM-Header-Informationen'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 180
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Datei:   $(Split-Path $filePath -Leaf)")
        foreach ($prop in $header.PSObject.Properties) {
          [void]$lb.Items.Add(("{0,-14} {1}" -f ($prop.Name + ':'), $prop.Value))
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Header-Analyse fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Vollständigkeit (MF-04)
  if ($Ctx.ContainsKey('btnCompleteness') -and $Ctx['btnCompleteness']) {
    $Ctx['btnCompleteness'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Vollständigkeit: Keine Roots.' -Level 'WARNING'; return }
        $report = Get-CompletenessReport -Roots $roots

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCompleteness'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Sammlungs-Vollständigkeit' -Width 480 -Height 340

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Vollständigkeits-Report'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        # Progress bar
        $pBar = New-Object System.Windows.Controls.ProgressBar
        $pBar.Minimum = 0; $pBar.Maximum = 100
        $pBar.Value = [math]::Round($report.Percentage * 100, 1)
        $pBar.Height = 28; $pBar.Margin = [System.Windows.Thickness]::new(0,0,0,8)
        [void]$root.Children.Add($pBar)

        $lblPct = New-Object System.Windows.Controls.TextBlock
        $lblPct.Text = ('{0:P1} komplett — {1} von {2} ROMs vorhanden' -f $report.Percentage, $report.OwnedCount, $report.TotalCount)
        $lblPct.FontSize = 14; $lblPct.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [void]$root.Children.Add($lblPct)

        if ($report.PSObject.Properties.Name -contains 'ByConsole' -and $report.ByConsole) {
          $lb = New-Object System.Windows.Controls.ListBox
          $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
          $lb.FontSize = 12; $lb.MaxHeight = 120
          $lb.Background = [System.Windows.Media.Brushes]::Transparent
          foreach ($c in $report.ByConsole.GetEnumerator()) {
            [void]$lb.Items.Add(("{0,-22} {1:P0}" -f $c.Key, $c.Value))
          }
          [void]$root.Children.Add($lb)
        }

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Vollständigkeit fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # DryRun-Vergleich (MF-21)
  if ($Ctx.ContainsKey('btnDryRunCompare') -and $Ctx['btnDryRunCompare']) {
    $Ctx['btnDryRunCompare'].add_Click({
      try {
        $reportsDir = Join-Path $PSScriptRoot 'reports'
        $plans = Get-ChildItem -Path $reportsDir -Filter 'move-plan-*.json' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 2
        if ($plans.Count -lt 2) { Add-WpfLogLine -Ctx $Ctx -Line 'DryRun-Vergleich: Mindestens 2 Move-Plans benötigt.' -Level 'WARNING'; return }
        $diff = Compare-DryRunResults -BaselinePath $plans[1].FullName -CurrentPath $plans[0].FullName

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnDryRunCompare'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'DryRun-Vergleich' -Width 560 -Height 400

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Vergleich der letzten zwei DryRun-Ergebnisse'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 160
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Baseline:  $($plans[1].Name)")
        [void]$lb.Items.Add("Aktuell:   $($plans[0].Name)")
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add(("+ Neu:      {0}" -f $diff.Added))
        [void]$lb.Items.Add(("- Entfernt: {0}" -f $diff.Removed))
        [void]$lb.Items.Add(("~ Geändert: {0}" -f $diff.Changed))
        if ($diff.PSObject.Properties.Name -contains 'Details' -and $diff.Details) {
          [void]$lb.Items.Add("")
          foreach ($d in $diff.Details | Select-Object -First 15) {
            [void]$lb.Items.Add(("  {0} {1}" -f $d.Action, $d.Name))
          }
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("DryRun-Vergleich fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Trend-Analyse (XL-06)
  if ($Ctx.ContainsKey('btnTrendAnalysis') -and $Ctx['btnTrendAnalysis']) {
    $Ctx['btnTrendAnalysis'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Trend-Analyse: Keine Roots.' -Level 'WARNING'; return }
        $snapshot = New-TrendSnapshot -Roots $roots

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnTrendAnalysis'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Trend-Analyse' -Width 520 -Height 360

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Sammlungs-Trend-Snapshot'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 140
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add(("Zeitstempel:  {0}" -f $snapshot.Timestamp))
        [void]$lb.Items.Add(("Dateien:      {0}" -f $snapshot.FileCount))
        [void]$lb.Items.Add(("Gesamtgröße:  {0:N0} MB" -f ($snapshot.TotalBytes / 1MB)))
        if ($snapshot.PSObject.Properties.Name -contains 'QualityScore') {
          [void]$lb.Items.Add(("Qualität:     {0:P0}" -f $snapshot.QualityScore))
        }
        if ($snapshot.PSObject.Properties.Name -contains 'ByConsole' -and $snapshot.ByConsole) {
          [void]$lb.Items.Add("")
          foreach ($c in $snapshot.ByConsole.GetEnumerator() | Select-Object -First 10) {
            [void]$lb.Items.Add(("  {0,-18} {1} Dateien" -f $c.Key, $c.Value))
          }
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Trend-Snapshot: {0} Dateien, {1:N0} MB" -f $snapshot.FileCount, ($snapshot.TotalBytes / 1MB)) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Trend-Analyse fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Emulator-Kompatibilität (XL-07)
  if ($Ctx.ContainsKey('btnEmulatorCompat') -and $Ctx['btnEmulatorCompat']) {
    $Ctx['btnEmulatorCompat'].add_Click({
      try {
        $profile = New-EmulatorProfile

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnEmulatorCompat'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Emulator-Kompatibilität' -Width 520 -Height 380

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Emulator-Profil'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 160
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add(("Konsolen:   {0}" -f $profile.ConsoleCount))
        [void]$lb.Items.Add(("Emulatoren: {0}" -f $profile.EmulatorCount))
        if ($profile.PSObject.Properties.Name -contains 'Mappings' -and $profile.Mappings) {
          [void]$lb.Items.Add("")
          foreach ($m in $profile.Mappings.GetEnumerator() | Select-Object -First 12) {
            [void]$lb.Items.Add(("  {0,-18} → {1}" -f $m.Key, $m.Value))
          }
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Emulator-Kompatibilität fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Konvertierung & Hashing ────────────────────────────────────────────

  # Konvertierungs-Pipeline (MF-06)
  if ($Ctx.ContainsKey('btnConversionPipeline') -and $Ctx['btnConversionPipeline']) {
    $Ctx['btnConversionPipeline'].add_Click({
      try {
        $pipeline = New-ConversionPipeline

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnConversionPipeline'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Konvertierungs-Pipeline' -Width 520 -Height 400

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Pipeline: {0} Schritte konfiguriert" -f $pipeline.Steps.Count)
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 180
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        $stepIdx = 1
        foreach ($step in $pipeline.Steps) {
          $stepName = if ($step.PSObject.Properties.Name -contains 'Name') { $step.Name } else { $step.ToString() }
          $stepFmt  = if ($step.PSObject.Properties.Name -contains 'Format') { " → $($step.Format)" } else { '' }
          [void]$lb.Items.Add(("{0}. {1}{2}" -f $stepIdx, $stepName, $stepFmt))
          $stepIdx++
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Konvertierungs-Pipeline fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # NKit-Konvertierung (MF-07)
  if ($Ctx.ContainsKey('btnNKitConvert') -and $Ctx['btnNKitConvert']) {
    $Ctx['btnNKitConvert'].add_Click({
      try {
        $filePath = Show-WpfTextInputDialog -Window $Window -Title 'NKit-Konvertierung' -Prompt 'NKit-Dateipfad eingeben:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($filePath)) { return }
        $result = Invoke-NKitConversion -Path $filePath -Mode 'DryRun'

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnNKitConvert'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'NKit-Konvertierung (DryRun)' -Width 480 -Height 300

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'NKit-Konvertierungs-Vorschau'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 100
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Datei:       $(Split-Path $filePath -Leaf)")
        [void]$lb.Items.Add("Quellformat: $($result.SourceFormat)")
        [void]$lb.Items.Add("Zielformat:  $($result.TargetFormat)")
        if ($result.PSObject.Properties.Name -contains 'EstimatedSize') {
          [void]$lb.Items.Add(("Geschätzt:   {0:N0} MB" -f ($result.EstimatedSize / 1MB)))
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("NKit-Konvertierung fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Konvert-Warteschlange (MF-08)
  if ($Ctx.ContainsKey('btnConvertQueue') -and $Ctx['btnConvertQueue']) {
    $Ctx['btnConvertQueue'].add_Click({
      try {
        $queue = Invoke-RunConvertQueueService -Operation 'Create' -Items @() -Ports @{}

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnConvertQueue'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Konvert-Warteschlange' -Width 520 -Height 380

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Warteschlange: {0} Einträge" -f $queue.Items.Count)
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($item in $queue.Items) {
          $display = if ($item.PSObject.Properties.Name -contains 'Name') { $item.Name } else { $item.ToString() }
          [void]$lb.Items.Add($display)
        }
        if ($queue.Items.Count -eq 0) { [void]$lb.Items.Add('(Warteschlange ist leer — Dateien über Konvertierungs-Pipeline hinzufügen)') }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Konvert-Warteschlange fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Konvertierung verifizieren (MF-09)
  if ($Ctx.ContainsKey('btnConversionVerify') -and $Ctx['btnConversionVerify']) {
    $Ctx['btnConversionVerify'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Konvertierungs-Verifizierung: Keine Roots.' -Level 'WARNING'; return }
        $result = Invoke-BatchVerify -Roots $roots

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnConversionVerify'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Konvertierungs-Verifizierung' -Width 500 -Height 360

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Verifizierungsergebnis'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 140
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add(("Bestanden:  {0}" -f $result.Passed))
        [void]$lb.Items.Add(("Fehlerhaft: {0}" -f $result.Failed))
        $total = $result.Passed + $result.Failed
        if ($total -gt 0) { [void]$lb.Items.Add(("Erfolgsrate: {0:P1}" -f ($result.Passed / $total))) }
        if ($result.PSObject.Properties.Name -contains 'FailedFiles' -and $result.FailedFiles) {
          [void]$lb.Items.Add("")
          [void]$lb.Items.Add("Fehlerhafte Dateien:")
          foreach ($f in $result.FailedFiles | Select-Object -First 15) {
            [void]$lb.Items.Add(("  ✗ {0}" -f $f))
          }
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Konvertierungs-Verifizierung fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Format-Priorität (MF-10)
  if ($Ctx.ContainsKey('btnFormatPriority') -and $Ctx['btnFormatPriority']) {
    $Ctx['btnFormatPriority'].add_Click({
      try {
        $priorities = Get-FormatPriority
        $allPrios = @($priorities)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnFormatPriority'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Format-Prioritäten' -Width 480 -Height 400

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Format-Prioritäten pro Konsole'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($p in $allPrios) {
          [void]$lb.Items.Add(("{0,-10} Priorität {1}" -f $p.Format, $p.Priority))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Format-Priorität fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Parallel-Hashing (MF-14)
  if ($Ctx.ContainsKey('btnParallelHashing') -and $Ctx['btnParallelHashing']) {
    $Ctx['btnParallelHashing'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Parallel-Hashing: Keine Roots.' -Level 'WARNING'; return }
        $files = foreach ($r in $roots) {
          if (Test-Path -LiteralPath $r -PathType Container) {
            Get-ChildItem -LiteralPath $r -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 100 -ExpandProperty FullName
          }
        }
        if (-not $files) { Add-WpfLogLine -Ctx $Ctx -Line 'Parallel-Hashing: Keine Dateien.' -Level 'INFO'; return }
        Add-WpfLogLine -Ctx $Ctx -Line 'Parallel-Hashing gestartet...' -Level 'INFO'
        $result = Invoke-RunParallelHashService -Files @($files) -Algorithm 'SHA1' -Progress { param($m) Add-WpfLogLine -Ctx $Ctx -Line $m -Level 'INFO' } -Ports @{}
        $allHashes = @($result)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnParallelHashing'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('Parallel-Hashing — {0} Dateien' -f $allHashes.Count) -Width 650 -Height 460

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allHashes.Count) Dateien gehasht (SHA1)")
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 11
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($h in $allHashes) {
          $name = if ($h.PSObject.Properties.Name -contains 'Name') { $h.Name } elseif ($h.PSObject.Properties.Name -contains 'Path') { Split-Path $h.Path -Leaf } else { $h.ToString() }
          $hash = if ($h.PSObject.Properties.Name -contains 'Hash') { $h.Hash.Substring(0, [math]::Min(16, $h.Hash.Length)) + '...' } else { '' }
          [void]$lb.Items.Add(("{0,-40} {1}" -f $name, $hash))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Parallel-Hashing fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # GPU-Hashing (XL-09)
  if ($Ctx.ContainsKey('btnGpuHashing') -and $Ctx['btnGpuHashing']) {
    $Ctx['btnGpuHashing'].add_Click({
      try {
        $available = Test-GpuHashingAvailable

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnGpuHashing'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'GPU-Hashing Status' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'GPU-Hashing-Unterstützung'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $dot = New-Object System.Windows.Shapes.Ellipse
        $dot.Width = 20; $dot.Height = 20
        $dot.Fill = if ($available) { [System.Windows.Media.Brushes]::LimeGreen } else { [System.Windows.Media.Brushes]::OrangeRed }
        $dot.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Left
        $dot.Margin = [System.Windows.Thickness]::new(0,0,0,8)
        [void]$root.Children.Add($dot)

        $lblStatus = New-Object System.Windows.Controls.TextBlock
        $lblStatus.Text = if ($available) { 'GPU-Hashing ist verfügbar und aktiv (OpenCL/CUDA erkannt)' } else { 'GPU-Hashing nicht verfügbar — Fallback auf CPU-Hashing' }
        $lblStatus.FontSize = 13; $lblStatus.TextWrapping = [System.Windows.TextWrapping]::Wrap
        $lblStatus.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblStatus)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("GPU-Hashing fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── DAT & Verifizierung ───────────────────────────────────────────────

  # DAT Auto-Update (MF-11)
  if ($Ctx.ContainsKey('btnDatAutoUpdate') -and $Ctx['btnDatAutoUpdate']) {
    $Ctx['btnDatAutoUpdate'].add_Click({
      try {
        Add-WpfLogLine -Ctx $Ctx -Line 'DAT Auto-Update: Prüfe auf Aktualisierungen...' -Level 'INFO'
        $updates = Test-DatUpdateAvailable

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnDatAutoUpdate'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'DAT Auto-Update' -Width 520 -Height 380

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $hasUpdates = ($updates -and $updates.Count -gt 0)
        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = if ($hasUpdates) { "$($updates.Count) DAT-Updates verfügbar" } else { 'Alle DATs sind aktuell' }
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        if ($hasUpdates) {
          foreach ($u in $updates) {
            $src = if ($u.PSObject.Properties.Name -contains 'Source') { $u.Source } else { $u.ToString() }
            $st  = if ($u.PSObject.Properties.Name -contains 'Status') { $u.Status } else { 'Update verfügbar' }
            [void]$lb.Items.Add(("{0,-24} {1}" -f $src, $st))
          }
        } else {
          [void]$lb.Items.Add('Keine Updates verfügbar — alle DAT-Quellen sind auf dem neuesten Stand.')
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("DAT Auto-Update fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # DAT-Diff-Viewer (MF-12)
  if ($Ctx.ContainsKey('btnDatDiffViewer') -and $Ctx['btnDatDiffViewer']) {
    $Ctx['btnDatDiffViewer'].add_Click({
      try {
        $datRoot = Get-AppStateValue 'DatRoot'
        if ([string]::IsNullOrWhiteSpace($datRoot)) { Add-WpfLogLine -Ctx $Ctx -Line 'DAT-Diff: Kein DAT-Root konfiguriert.' -Level 'WARNING'; return }
        $diff = Compare-DatVersions -DatRoot $datRoot

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnDatDiffViewer'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'DAT-Diff-Viewer' -Width 540 -Height 400

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'DAT-Versions-Vergleich'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 180
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add(("+ Neu:      {0} Einträge" -f $diff.Added))
        [void]$lb.Items.Add(("- Entfernt: {0} Einträge" -f $diff.Removed))
        [void]$lb.Items.Add(("~ Geändert: {0} Einträge" -f $diff.Changed))
        if ($diff.PSObject.Properties.Name -contains 'Details' -and $diff.Details) {
          [void]$lb.Items.Add("")
          foreach ($d in $diff.Details | Select-Object -First 20) {
            [void]$lb.Items.Add(("  {0}" -f $d))
          }
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("DAT-Diff fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # TOSEC-DAT (MF-13)
  if ($Ctx.ContainsKey('btnTosecDat') -and $Ctx['btnTosecDat']) {
    $Ctx['btnTosecDat'].add_Click({
      try {
        $filePath = Show-WpfTextInputDialog -Window $Window -Title 'TOSEC-DAT' -Prompt 'TOSEC-DAT-Dateipfad eingeben:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($filePath)) { return }
        $result = ConvertFrom-TosecDat -Path $filePath
        $allEntries = @($result)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnTosecDat'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('TOSEC-DAT — {0} Einträge' -f $allEntries.Count) -Width 600 -Height 450

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allEntries.Count) Einträge aus TOSEC-DAT importiert")
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($entry in $allEntries | Select-Object -First 100) {
          $display = if ($entry.PSObject.Properties.Name -contains 'Name') { $entry.Name } else { $entry.ToString() }
          [void]$lb.Items.Add($display)
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("TOSEC-DAT: {0} Einträge importiert" -f $allEntries.Count) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("TOSEC-DAT fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Custom-DAT-Editor (LF-09)
  if ($Ctx.ContainsKey('btnCustomDatEditor') -and $Ctx['btnCustomDatEditor']) {
    $Ctx['btnCustomDatEditor'].add_Click({
      try {
        $datName = Show-WpfTextInputDialog -Window $Window -Title 'Custom-DAT-Editor' -Prompt 'Name für neues Custom-DAT:' -DefaultValue 'MeinCustomDAT'
        if ([string]::IsNullOrWhiteSpace($datName)) { return }
        $dat = New-CustomDat

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCustomDatEditor'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Custom-DAT-Editor' -Width 500 -Height 350

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Custom-DAT: $datName")
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 140
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Name:      $($dat.Name)")
        if ($dat.PSObject.Properties.Name -contains 'Format') { [void]$lb.Items.Add("Format:    $($dat.Format)") }
        if ($dat.PSObject.Properties.Name -contains 'EntryCount') { [void]$lb.Items.Add("Einträge:  $($dat.EntryCount)") }
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("Das Custom-DAT kann über den DAT-Root geladen werden.")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Custom-DAT erstellt: {0}" -f $datName) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Custom-DAT-Editor fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Hash-Datenbank (LF-11)
  if ($Ctx.ContainsKey('btnHashDatabaseExport') -and $Ctx['btnHashDatabaseExport']) {
    $Ctx['btnHashDatabaseExport'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Hash-DB: Keine Roots.' -Level 'WARNING'; return }
        Add-WpfLogLine -Ctx $Ctx -Line 'Hash-Datenbank: Export wird erstellt...' -Level 'INFO'
        $db = New-HashDatabase -Roots $roots

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnHashDatabaseExport'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Hash-Datenbank-Export' -Width 480 -Height 320

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Hash-Datenbank exportiert'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 100
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add(("Einträge:  {0}" -f $db.EntryCount))
        if ($db.PSObject.Properties.Name -contains 'OutputPath') { [void]$lb.Items.Add("Pfad:      $($db.OutputPath)") }
        if ($db.PSObject.Properties.Name -contains 'Algorithm') { [void]$lb.Items.Add("Algorithmus: $($db.Algorithm)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Hash-Datenbank: {0} Einträge exportiert" -f $db.EntryCount) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Hash-Datenbank fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Sammlungsverwaltung ────────────────────────────────────────────────

  # Smart Collection (MF-05)
  if ($Ctx.ContainsKey('btnCollectionManager') -and $Ctx['btnCollectionManager']) {
    $Ctx['btnCollectionManager'].add_Click({
      try {
        $name = Show-WpfTextInputDialog -Window $Window -Title 'Smart Collection' -Prompt 'Name der Sammlung:' -DefaultValue 'Meine Sammlung'
        if ([string]::IsNullOrWhiteSpace($name)) { return }
        $collection = New-SmartCollection -Name $name

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCollectionManager'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Smart Collection' -Width 480 -Height 340

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Sammlung '{0}' erstellt" -f $collection.Name)
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 120
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Name:   $($collection.Name)")
        if ($collection.PSObject.Properties.Name -contains 'Filter') { [void]$lb.Items.Add("Filter: $($collection.Filter)") }
        if ($collection.PSObject.Properties.Name -contains 'ItemCount') { [void]$lb.Items.Add("Items:  $($collection.ItemCount)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Smart Collection '{0}' erstellt" -f $collection.Name) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Smart Collection fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Clone-Liste (LF-10)
  if ($Ctx.ContainsKey('btnCloneListViewer') -and $Ctx['btnCloneListViewer']) {
    $Ctx['btnCloneListViewer'].add_Click({
      try {
        $datIndex = Get-AppStateValue 'DatIndex'
        if (-not $datIndex) { Add-WpfLogLine -Ctx $Ctx -Line 'Clone-Liste: Kein DAT-Index geladen.' -Level 'WARNING'; return }
        $tree = Build-CloneTree -DatIndex $datIndex
        $allEntries = @($tree)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCloneListViewer'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('Clone-Baum — {0} Parents' -f $allEntries.Count) -Width 600 -Height 460

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = "$($allEntries.Count) Parent-Einträge im Clone-Baum"
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($entry in $allEntries | Select-Object -First 80) {
          $display = if ($entry.PSObject.Properties.Name -contains 'Parent') { $entry.Parent } else { $entry.ToString() }
          $clones = if ($entry.PSObject.Properties.Name -contains 'Clones') { " ({0} Clones)" -f $entry.Clones.Count } else { '' }
          [void]$lb.Items.Add(("{0}{1}" -f $display, $clones))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Clone-Liste fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Cover-Scraper (LF-01)
  if ($Ctx.ContainsKey('btnCoverScraper') -and $Ctx['btnCoverScraper']) {
    $Ctx['btnCoverScraper'].add_Click({
      try {
        $config = New-CoverScraperConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCoverScraper'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Cover-Scraper Konfiguration' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Cover-Scraper'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 80
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Provider:    $($config.Provider)")
        if ($config.PSObject.Properties.Name -contains 'OutputDir') { [void]$lb.Items.Add("Ausgabe:     $($config.OutputDir)") }
        if ($config.PSObject.Properties.Name -contains 'ImageSize') { [void]$lb.Items.Add("Bildgröße:   $($config.ImageSize)") }
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("Cover-Downloads starten über den Run-Button.")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Cover-Scraper konfiguriert: Provider={0}" -f $config.Provider) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Cover-Scraper fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Genre-Klassifikation (LF-02)
  if ($Ctx.ContainsKey('btnGenreClassification') -and $Ctx['btnGenreClassification']) {
    $Ctx['btnGenreClassification'].add_Click({
      try {
        $taxonomy = Get-GenreTaxonomy
        $allGenres = @($taxonomy)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnGenreClassification'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Genre-Klassifikation' -Width 480 -Height 400

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allGenres.Count) Genres verfügbar")
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($g in $allGenres) {
          $display = if ($g.PSObject.Properties.Name -contains 'Name') { $g.Name } else { $g.ToString() }
          [void]$lb.Items.Add($display)
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Genre-Klassifikation fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Spielzeit-Tracker (LF-04)
  if ($Ctx.ContainsKey('btnPlaytimeTracker') -and $Ctx['btnPlaytimeTracker']) {
    $Ctx['btnPlaytimeTracker'].add_Click({
      try {
        $logPath = Show-WpfTextInputDialog -Window $Window -Title 'Spielzeit-Tracker' -Prompt 'RetroArch-Log-Pfad eingeben:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($logPath)) { return }
        $data = Import-RetroArchPlaytime -LogPath $logPath
        $allEntries = @($data)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPlaytimeTracker'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('Spielzeit-Tracker — {0} Einträge' -f $allEntries.Count) -Width 580 -Height 430

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = "$($allEntries.Count) Spielzeit-Einträge importiert"
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($entry in $allEntries | Select-Object -First 50) {
          $game = if ($entry.PSObject.Properties.Name -contains 'Game') { $entry.Game } else { $entry.ToString() }
          $time = if ($entry.PSObject.Properties.Name -contains 'PlaytimeMinutes') { " ({0}h {1}m)" -f [math]::Floor($entry.PlaytimeMinutes/60), ($entry.PlaytimeMinutes % 60) } else { '' }
          [void]$lb.Items.Add(("{0}{1}" -f $game, $time))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Spielzeit: {0} Einträge importiert" -f $allEntries.Count) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Spielzeit-Tracker fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Sammlung teilen (XL-08)
  if ($Ctx.ContainsKey('btnCollectionSharing') -and $Ctx['btnCollectionSharing']) {
    $Ctx['btnCollectionSharing'].add_Click({
      try {
        $config = New-CollectionExportConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCollectionSharing'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Sammlung teilen' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Sammlungs-Export-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 80
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Format:      $($config.Format)")
        if ($config.PSObject.Properties.Name -contains 'IncludeHashes') { [void]$lb.Items.Add("Mit Hashes:  $($config.IncludeHashes)") }
        if ($config.PSObject.Properties.Name -contains 'Scope') { [void]$lb.Items.Add("Umfang:      $($config.Scope)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Sammlungs-Export: Format={0}" -f $config.Format) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Sammlung teilen fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Virtuelle Ordner (LF-12)
  if ($Ctx.ContainsKey('btnVirtualFolderPreview') -and $Ctx['btnVirtualFolderPreview']) {
    $Ctx['btnVirtualFolderPreview'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Virtuelle Ordner: Keine Roots.' -Level 'WARNING'; return }
        $data = Build-TreemapData -Roots $roots

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnVirtualFolderPreview'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Virtuelle Ordner-Vorschau' -Width 560 -Height 420

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Treemap: {0} Knoten, {1:N0} MB" -f $data.NodeCount, ($data.TotalBytes / 1MB))
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        if ($data.PSObject.Properties.Name -contains 'Nodes' -and $data.Nodes) {
          foreach ($node in $data.Nodes | Select-Object -First 40) {
            $name = if ($node.PSObject.Properties.Name -contains 'Name') { $node.Name } else { $node.ToString() }
            $size = if ($node.PSObject.Properties.Name -contains 'SizeBytes') { " ({0:N0} MB)" -f ($node.SizeBytes / 1MB) } else { '' }
            [void]$lb.Items.Add(("{0}{1}" -f $name, $size))
          }
        } else {
          [void]$lb.Items.Add(("{0} Knoten, {1:N0} MB gesamt" -f $data.NodeCount, ($data.TotalBytes / 1MB)))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Virtuelle Ordner fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Sicherheit & Integrität ────────────────────────────────────────────

  # Integritäts-Monitor (MF-24)
  if ($Ctx.ContainsKey('btnIntegrityMonitor') -and $Ctx['btnIntegrityMonitor']) {
    $Ctx['btnIntegrityMonitor'].add_Click({
      try {
        $roots = Get-AppStateValue 'RootPaths'
        if (-not $roots) { Add-WpfLogLine -Ctx $Ctx -Line 'Integrität: Keine Roots.' -Level 'WARNING'; return }
        $files = foreach ($r in $roots) {
          if (Test-Path -LiteralPath $r -PathType Container) {
            Get-ChildItem -LiteralPath $r -Recurse -File -ErrorAction SilentlyContinue | Select-Object -First 200
          }
        }
        Add-WpfLogLine -Ctx $Ctx -Line 'Integritäts-Baseline wird erstellt...' -Level 'INFO'
        $baseline = Invoke-RunIntegrityCheckService -Operation 'Baseline' -Files @($files) -Ports @{}

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnIntegrityMonitor'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Integritäts-Monitor' -Width 500 -Height 360

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Integritäts-Baseline erstellt'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $dot = New-Object System.Windows.Shapes.Ellipse
        $dot.Width = 20; $dot.Height = 20
        $dot.Fill = [System.Windows.Media.Brushes]::LimeGreen
        $dot.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Left
        $dot.Margin = [System.Windows.Thickness]::new(0,0,0,8)
        [void]$root.Children.Add($dot)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 100
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add(("Dateien erfasst: {0}" -f $baseline.FileCount))
        if ($baseline.PSObject.Properties.Name -contains 'Timestamp') { [void]$lb.Items.Add("Zeitstempel:     $($baseline.Timestamp)") }
        if ($baseline.PSObject.Properties.Name -contains 'Algorithm')  { [void]$lb.Items.Add("Hash-Algorithmus: $($baseline.Algorithm)") }
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("Baseline gespeichert. Nächste Prüfung erkennt Änderungen.")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Integrität: Baseline mit {0} Dateien erstellt" -f $baseline.FileCount) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Integritäts-Monitor fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Backup-Manager (MF-25)
  if ($Ctx.ContainsKey('btnBackupManager') -and $Ctx['btnBackupManager']) {
    $Ctx['btnBackupManager'].add_Click({
      try {
        $owner = [System.Windows.Window]::GetWindow($Ctx['btnBackupManager'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Backup-Manager' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 460

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Backup erstellen'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lblName = New-Object System.Windows.Controls.TextBlock
        $lblName.Text = 'Backup-Label:'; $lblName.FontSize = 13
        $lblName.Margin = [System.Windows.Thickness]::new(0,0,0,4)
        [void]$root.Children.Add($lblName)

        $tbLabel = New-Object System.Windows.Controls.TextBox
        $tbLabel.Text = ('GUI-Backup-{0:yyyyMMdd-HHmm}' -f (Get-Date))
        $tbLabel.FontSize = 13; $tbLabel.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($tbLabel)

        $buttonPanel = New-Object System.Windows.Controls.StackPanel
        $buttonPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $buttonPanel.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right

        $btnCancel = New-Object System.Windows.Controls.Button
        $btnCancel.Content = 'Abbrechen'; $btnCancel.Width = 110
        $btnCancel.Margin = [System.Windows.Thickness]::new(0,0,8,0)
        $btnCancel.add_Click({ $dialog.DialogResult = $false; $dialog.Close() }.GetNewClosure())

        $btnCreate = New-Object System.Windows.Controls.Button
        $btnCreate.Content = 'Backup starten'; $btnCreate.Width = 140
        $btnCreate.add_Click({ $dialog.DialogResult = $true; $dialog.Close() }.GetNewClosure())

        [void]$buttonPanel.Children.Add($btnCancel)
        [void]$buttonPanel.Children.Add($btnCreate)
        [void]$root.Children.Add($buttonPanel)

        $dialog.Content = $root
        $result = $dialog.ShowDialog()

        if ($result -eq $true) {
          $label = [string]$tbLabel.Text
          if ([string]::IsNullOrWhiteSpace($label)) { $label = 'GUI-Backup' }
          $config = New-BackupConfig
          $session = Invoke-RunBackupService -Operation 'Create' -Config $config -Label $label -Ports @{}
          Add-WpfLogLine -Ctx $Ctx -Line ("Backup erstellt: {0} (Session={1})" -f $label, $session.SessionId) -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Backup-Manager fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Quarantäne (MF-26)
  if ($Ctx.ContainsKey('btnQuarantine') -and $Ctx['btnQuarantine']) {
    $Ctx['btnQuarantine'].add_Click({
      try {
        $filePath = Show-WpfTextInputDialog -Window $Window -Title 'Quarantäne' -Prompt 'Dateipfad für Quarantäne:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($filePath)) { return }
        $qRoot = Join-Path (Get-AppStateValue 'TrashRoot') 'quarantine'
        $result = Invoke-RunQuarantineService -SourcePath $filePath -QuarantineRoot $qRoot -Reasons @('ManualReview') -Mode 'DryRun' -Ports @{}

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnQuarantine'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Quarantäne (DryRun-Vorschau)' -Width 540 -Height 340

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Quarantäne-Vorschau (DryRun)'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12; $lb.MinHeight = 100
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Quelle:  $($result.Source)")
        [void]$lb.Items.Add("Ziel:    $($result.Destination)")
        [void]$lb.Items.Add("Grund:   ManualReview")
        [void]$lb.Items.Add("Modus:   DryRun (keine Dateien verschoben)")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Quarantäne fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Regel-Engine (MF-19)
  if ($Ctx.ContainsKey('btnRuleEngine') -and $Ctx['btnRuleEngine']) {
    $Ctx['btnRuleEngine'].add_Click({
      try {
        $rules = Get-AppStateValue 'Rules'
        if (-not $rules) { $rules = @() }
        $allRules = @($rules)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnRuleEngine'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Regel-Engine' -Width 560 -Height 420

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allRules.Count) Regeln geladen")
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        if ($allRules.Count -eq 0) {
          [void]$lb.Items.Add('(Keine Regeln geladen — Standard-Regeln werden aus rules.json verwendet)')
        } else {
          foreach ($rule in $allRules) {
            $display = if ($rule -is [hashtable] -and $rule.ContainsKey('Name')) { $rule.Name } elseif ($rule.PSObject.Properties.Name -contains 'Name') { $rule.Name } else { $rule.ToString() }
            [void]$lb.Items.Add($display)
          }
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Regel-Engine fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Patch-Engine (LF-05)
  if ($Ctx.ContainsKey('btnPatchEngine') -and $Ctx['btnPatchEngine']) {
    $Ctx['btnPatchEngine'].add_Click({
      try {
        $patchPath = Show-WpfTextInputDialog -Window $Window -Title 'Patch-Engine' -Prompt 'Patch-Datei (.ips/.ups/.bps):' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($patchPath)) { return }
        $format = Test-PatchFormat -Path $patchPath

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPatchEngine'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Patch-Engine' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Patch-Format erkannt'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 80
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Datei:   $(Split-Path $patchPath -Leaf)")
        [void]$lb.Items.Add("Format:  $($format.Format)")
        if ($format.PSObject.Properties.Name -contains 'Version') { [void]$lb.Items.Add("Version: $($format.Version)") }
        if ($format.PSObject.Properties.Name -contains 'Size') { [void]$lb.Items.Add(("Größe:   {0:N0} KB" -f ($format.Size / 1KB))) }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Patch-Engine fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Header-Reparatur (LF-06)
  if ($Ctx.ContainsKey('btnHeaderRepair') -and $Ctx['btnHeaderRepair']) {
    $Ctx['btnHeaderRepair'].add_Click({
      try {
        $filePath = Show-WpfTextInputDialog -Window $Window -Title 'Header-Reparatur' -Prompt 'ROM-Dateipfad:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($filePath)) { return }
        $result = Repair-NesHeader -Path $filePath -Mode 'DryRun'

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnHeaderRepair'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Header-Reparatur (DryRun)' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Header-Reparatur-Vorschau'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 80
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Datei:   $(Split-Path $filePath -Leaf)")
        [void]$lb.Items.Add("Status:  $($result.Status)")
        [void]$lb.Items.Add("Modus:   DryRun (keine Änderungen)")
        if ($result.PSObject.Properties.Name -contains 'Issues') {
          foreach ($issue in $result.Issues) { [void]$lb.Items.Add(("  → {0}" -f $issue)) }
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Header-Reparatur fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Workflow & Automatisierung ─────────────────────────────────────────

  # Command-Palette (MF-15)
  if ($Ctx.ContainsKey('btnCommandPalette') -and $Ctx['btnCommandPalette']) {
    $Ctx['btnCommandPalette'].add_Click({
      try {
        $query = Show-WpfTextInputDialog -Window $Window -Title 'Command-Palette' -Prompt 'Befehl suchen:' -DefaultValue ''
        if ([string]::IsNullOrWhiteSpace($query)) { return }
        $results = Search-PaletteCommands -Query $query
        $allResults = @($results)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCommandPalette'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title ('Command-Palette — {0} Treffer' -f $allResults.Count) -Width 560 -Height 420

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = "Suche: '$query' — $($allResults.Count) Befehle"
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 12
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($cmd in $allResults) {
          $name = if ($cmd.PSObject.Properties.Name -contains 'Name') { $cmd.Name } else { $cmd.ToString() }
          $desc = if ($cmd.PSObject.Properties.Name -contains 'Description') { " — $($cmd.Description)" } else { '' }
          [void]$lb.Items.Add(("{0}{1}" -f $name, $desc))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Command-Palette fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Split-Panel (MF-16)
  if ($Ctx.ContainsKey('btnSplitPanelPreview') -and $Ctx['btnSplitPanelPreview']) {
    $Ctx['btnSplitPanelPreview'].add_Click({
      try {
        $reportsDir = Join-Path $PSScriptRoot 'reports'
        $latestPlan = Get-ChildItem -Path $reportsDir -Filter 'move-plan-*.json' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
        if (-not $latestPlan) { Add-WpfLogLine -Ctx $Ctx -Line 'Split-Panel: Kein Move-Plan vorhanden.' -Level 'WARNING'; return }
        $data = ConvertTo-SplitPanelData -PlanPath $latestPlan.FullName

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnSplitPanelPreview'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Split-Panel-Vorschau' -Width 600 -Height 460

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Move-Plan: {0} Einträge" -f $data.EntryCount)
        $lblTitle.FontSize = 15; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,8)
        [void]$root.Children.Add($lblTitle)

        $lblFile = New-Object System.Windows.Controls.TextBlock
        $lblFile.Text = ("Datei: {0}" -f $latestPlan.Name)
        $lblFile.FontSize = 11; $lblFile.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [void]$root.Children.Add($lblFile)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 11; $lb.MaxHeight = 260
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        if ($data.PSObject.Properties.Name -contains 'Entries' -and $data.Entries) {
          foreach ($e in $data.Entries | Select-Object -First 40) {
            $src = if ($e.PSObject.Properties.Name -contains 'Source') { $e.Source } else { '' }
            $dst = if ($e.PSObject.Properties.Name -contains 'Destination') { $e.Destination } else { '' }
            [void]$lb.Items.Add(("{0}  →  {1}" -f (Split-Path $src -Leaf), (Split-Path $dst -Leaf)))
          }
        } else {
          [void]$lb.Items.Add("$($data.EntryCount) Einträge im Plan")
        }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Split-Panel fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Filter-Builder (MF-17)
  if ($Ctx.ContainsKey('btnFilterBuilder') -and $Ctx['btnFilterBuilder']) {
    $Ctx['btnFilterBuilder'].add_Click({
      try {
        $owner = [System.Windows.Window]::GetWindow($Ctx['btnFilterBuilder'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Filter-Builder' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 460

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Neue Filterbedingung erstellen'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        # Filter type selection
        $lblType = New-Object System.Windows.Controls.TextBlock
        $lblType.Text = 'Filtertyp:'; $lblType.FontSize = 13
        $lblType.Margin = [System.Windows.Thickness]::new(0,0,0,4)
        [void]$root.Children.Add($lblType)

        $cmbType = New-Object System.Windows.Controls.ComboBox
        $cmbType.FontSize = 13; $cmbType.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        foreach ($t in @('Region', 'Format', 'Konsole', 'Größe', 'Name', 'Datum')) { [void]$cmbType.Items.Add($t) }
        $cmbType.SelectedIndex = 0
        [void]$root.Children.Add($cmbType)

        # Filter value
        $lblVal = New-Object System.Windows.Controls.TextBlock
        $lblVal.Text = 'Wert / Pattern:'; $lblVal.FontSize = 13
        $lblVal.Margin = [System.Windows.Thickness]::new(0,0,0,4)
        [void]$root.Children.Add($lblVal)

        $tbVal = New-Object System.Windows.Controls.TextBox
        $tbVal.FontSize = 13; $tbVal.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($tbVal)

        $buttonPanel = New-Object System.Windows.Controls.StackPanel
        $buttonPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $buttonPanel.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right

        $btnCancel = New-Object System.Windows.Controls.Button
        $btnCancel.Content = 'Abbrechen'; $btnCancel.Width = 110
        $btnCancel.Margin = [System.Windows.Thickness]::new(0,0,8,0)
        $btnCancel.add_Click({ $dialog.DialogResult = $false; $dialog.Close() }.GetNewClosure())

        $btnApply = New-Object System.Windows.Controls.Button
        $btnApply.Content = 'Filter erstellen'; $btnApply.Width = 130
        $btnApply.add_Click({ $dialog.DialogResult = $true; $dialog.Close() }.GetNewClosure())

        [void]$buttonPanel.Children.Add($btnCancel)
        [void]$buttonPanel.Children.Add($btnApply)
        [void]$root.Children.Add($buttonPanel)

        $dialog.Content = $root
        $result = $dialog.ShowDialog()

        if ($result -eq $true) {
          $filterType = [string]$cmbType.SelectedItem
          $filterVal  = [string]$tbVal.Text
          $filter = New-FilterCondition
          Add-WpfLogLine -Ctx $Ctx -Line ("Filter erstellt: Typ={0}, Wert='{1}'" -f $filterType, $filterVal) -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Filter-Builder fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Sort-Templates (MF-22)
  if ($Ctx.ContainsKey('btnSortTemplates') -and $Ctx['btnSortTemplates']) {
    $Ctx['btnSortTemplates'].add_Click({
      try {
        $templates = Get-DefaultSortTemplates
        $allTemplates = @($templates)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnSortTemplates'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Sort-Templates' -Width 520 -Height 400

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allTemplates.Count) Sortier-Vorlagen verfügbar")
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($t in $allTemplates) {
          $name = if ($t.PSObject.Properties.Name -contains 'Name') { $t.Name } else { $t.ToString() }
          $desc = if ($t.PSObject.Properties.Name -contains 'Description') { " — $($t.Description)" } else { '' }
          [void]$lb.Items.Add(("{0}{1}" -f $name, $desc))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Sort-Templates fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Pipeline-Engine (MF-20)
  if ($Ctx.ContainsKey('btnPipelineEngine') -and $Ctx['btnPipelineEngine']) {
    $Ctx['btnPipelineEngine'].add_Click({
      try {
        $step = New-PipelineStep

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPipelineEngine'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Pipeline-Engine' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Pipeline-Schritt erstellt'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 80
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Name:   $($step.Name)")
        if ($step.PSObject.Properties.Name -contains 'Type') { [void]$lb.Items.Add("Typ:    $($step.Type)") }
        if ($step.PSObject.Properties.Name -contains 'Action') { [void]$lb.Items.Add("Aktion: $($step.Action)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
        Add-WpfLogLine -Ctx $Ctx -Line ("Pipeline-Step erstellt: {0}" -f $step.Name) -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Pipeline-Engine fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # System-Tray (MF-18)
  if ($Ctx.ContainsKey('btnSystemTray') -and $Ctx['btnSystemTray']) {
    $Ctx['btnSystemTray'].add_Click({
      try {
        $config = New-TrayIconConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnSystemTray'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'System-Tray' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'System-Tray-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Icon:     $($config.IconPath)")
        if ($config.PSObject.Properties.Name -contains 'Tooltip') { [void]$lb.Items.Add("Tooltip:  $($config.Tooltip)") }
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("System-Tray erlaubt Hintergrund-Überwachung.")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("System-Tray fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Scheduler (MF-23)
  if ($Ctx.ContainsKey('btnSchedulerAdvanced') -and $Ctx['btnSchedulerAdvanced']) {
    $Ctx['btnSchedulerAdvanced'].add_Click({
      try {
        $owner = [System.Windows.Window]::GetWindow($Ctx['btnSchedulerAdvanced'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Scheduler (Erweitert)' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 460

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Zeitplaner-Eintrag erstellen'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lblCron = New-Object System.Windows.Controls.TextBlock
        $lblCron.Text = 'Cron-Ausdruck:'; $lblCron.FontSize = 13
        $lblCron.Margin = [System.Windows.Thickness]::new(0,0,0,4)
        [void]$root.Children.Add($lblCron)

        $tbCron = New-Object System.Windows.Controls.TextBox
        $tbCron.Text = '0 3 * * 0'; $tbCron.FontSize = 13
        $tbCron.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $tbCron.Margin = [System.Windows.Thickness]::new(0,0,0,8)
        [void]$root.Children.Add($tbCron)

        $lblHelp = New-Object System.Windows.Controls.TextBlock
        $lblHelp.Text = 'Format: Minute Stunde Tag Monat Wochentag (z.B. "0 3 * * 0" = So 03:00)'
        $lblHelp.FontSize = 11; $lblHelp.TextWrapping = [System.Windows.TextWrapping]::Wrap
        $lblHelp.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblHelp)

        $buttonPanel = New-Object System.Windows.Controls.StackPanel
        $buttonPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $buttonPanel.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right

        $btnCancel = New-Object System.Windows.Controls.Button
        $btnCancel.Content = 'Abbrechen'; $btnCancel.Width = 110
        $btnCancel.Margin = [System.Windows.Thickness]::new(0,0,8,0)
        $btnCancel.add_Click({ $dialog.DialogResult = $false; $dialog.Close() }.GetNewClosure())

        $btnCreate = New-Object System.Windows.Controls.Button
        $btnCreate.Content = 'Eintrag erstellen'; $btnCreate.Width = 140
        $btnCreate.add_Click({ $dialog.DialogResult = $true; $dialog.Close() }.GetNewClosure())

        [void]$buttonPanel.Children.Add($btnCancel)
        [void]$buttonPanel.Children.Add($btnCreate)
        [void]$root.Children.Add($buttonPanel)

        $dialog.Content = $root
        $result = $dialog.ShowDialog()

        if ($result -eq $true) {
          $entry = New-ScheduleEntry
          Add-WpfLogLine -Ctx $Ctx -Line ("Scheduler: Eintrag erstellt (Cron={0})" -f [string]$tbCron.Text) -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Scheduler fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Regel-Pakete (LF-19)
  if ($Ctx.ContainsKey('btnRulePackSharing') -and $Ctx['btnRulePackSharing']) {
    $Ctx['btnRulePackSharing'].add_Click({
      try {
        $packName = Show-WpfTextInputDialog -Window $Window -Title 'Regel-Paket' -Prompt 'Name des Regel-Pakets:' -DefaultValue 'Standard-Regelpaket'
        if ([string]::IsNullOrWhiteSpace($packName)) { return }
        $pack = New-RulePack

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnRulePackSharing'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Regel-Paket' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Regel-Paket '{0}' erstellt" -f $packName)
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Name:   $($pack.Name)")
        if ($pack.PSObject.Properties.Name -contains 'Rules') { [void]$lb.Items.Add("Regeln: $($pack.Rules.Count)") }
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("Regel-Pakete können mit anderen Benutzern geteilt werden.")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Regel-Pakete fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Arcade Merge/Split (LF-07)
  if ($Ctx.ContainsKey('btnArcadeMergeSplit') -and $Ctx['btnArcadeMergeSplit']) {
    $Ctx['btnArcadeMergeSplit'].add_Click({
      try {
        $types = Get-ArcadeSetTypes
        $allTypes = @($types)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnArcadeMergeSplit'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Arcade Merge/Split' -Width 480 -Height 380

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allTypes.Count) Arcade-Set-Typen verfügbar")
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($t in $allTypes) {
          $display = if ($t.PSObject.Properties.Name -contains 'Name') { $t.Name } else { $t.ToString() }
          $desc = if ($t.PSObject.Properties.Name -contains 'Description') { " — $($t.Description)" } else { '' }
          [void]$lb.Items.Add(("{0}{1}" -f $display, $desc))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Arcade Merge/Split fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Export & Integration ───────────────────────────────────────────────

  # PDF-Report (LF-14)
  if ($Ctx.ContainsKey('btnPdfReport') -and $Ctx['btnPdfReport']) {
    $Ctx['btnPdfReport'].add_Click({
      try {
        $config = New-PdfReportConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPdfReport'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'PDF-Report' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'PDF-Report-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Template: $($config.Template)")
        if ($config.PSObject.Properties.Name -contains 'PageSize') { [void]$lb.Items.Add("Seitenformat: $($config.PageSize)") }
        if ($config.PSObject.Properties.Name -contains 'IncludeCharts') { [void]$lb.Items.Add("Charts: $($config.IncludeCharts)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("PDF-Report fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Launcher-Integration (LF-03)
  if ($Ctx.ContainsKey('btnLauncherIntegration') -and $Ctx['btnLauncherIntegration']) {
    $Ctx['btnLauncherIntegration'].add_Click({
      try {
        $formats = Get-SupportedLauncherFormats
        $allFormats = @($formats)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnLauncherIntegration'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Launcher-Integration' -Width 480 -Height 380

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allFormats.Count) Launcher-Formate unterstützt")
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($f in $allFormats) {
          $name = if ($f.PSObject.Properties.Name -contains 'Name') { $f.Name } else { $f.ToString() }
          [void]$lb.Items.Add($name)
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Launcher-Integration fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Tool-Import (XL-12)
  if ($Ctx.ContainsKey('btnToolImport') -and $Ctx['btnToolImport']) {
    $Ctx['btnToolImport'].add_Click({
      try {
        $formats = Get-SupportedImportFormats
        $allFormats = @($formats)

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnToolImport'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Tool-Import' -Width 480 -Height 380

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("$($allFormats.Count) Import-Formate verfügbar")
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($f in $allFormats) {
          $name = if ($f.PSObject.Properties.Name -contains 'Name') { $f.Name } else { $f.ToString() }
          $desc = if ($f.PSObject.Properties.Name -contains 'Description') { " — $($f.Description)" } else { '' }
          [void]$lb.Items.Add(("{0}{1}" -f $name, $desc))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Tool-Import fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── Infrastruktur & Deployment ─────────────────────────────────────────

  # Storage-Tiering (LF-08)
  if ($Ctx.ContainsKey('btnStorageTiering') -and $Ctx['btnStorageTiering']) {
    $Ctx['btnStorageTiering'].add_Click({
      try {
        $config = Get-StorageTierConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnStorageTiering'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Storage-Tiering' -Width 480 -Height 380

        $root = New-Object System.Windows.Controls.DockPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Storage-Tiering: {0} Tier(s)" -f $config.Tiers.Count)
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        [System.Windows.Controls.DockPanel]::SetDock($lblTitle, [System.Windows.Controls.Dock]::Top)
        [void]$root.Children.Add($lblTitle)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schließen'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [System.Windows.Controls.DockPanel]::SetDock($btnClose, [System.Windows.Controls.Dock]::Bottom)
        [void]$root.Children.Add($btnClose)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        foreach ($tier in $config.Tiers) {
          $name = if ($tier.PSObject.Properties.Name -contains 'Name') { $tier.Name } else { $tier.ToString() }
          $path = if ($tier.PSObject.Properties.Name -contains 'Path') { " → $($tier.Path)" } else { '' }
          [void]$lb.Items.Add(("{0}{1}" -f $name, $path))
        }
        [void]$root.Children.Add($lb)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Storage-Tiering fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # NAS-Optimierung (LF-15)
  if ($Ctx.ContainsKey('btnNasOptimization') -and $Ctx['btnNasOptimization']) {
    $Ctx['btnNasOptimization'].add_Click({
      try {
        $profile = New-NasProfile

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnNasOptimization'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'NAS-Optimierung' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'NAS-Profil'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("BufferSize:  $($profile.BufferSize)")
        if ($profile.PSObject.Properties.Name -contains 'Protocol') { [void]$lb.Items.Add("Protokoll:   $($profile.Protocol)") }
        if ($profile.PSObject.Properties.Name -contains 'Parallel') { [void]$lb.Items.Add("Parallel:    $($profile.Parallel)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("NAS-Optimierung fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # FTP-Quelle (LF-16)
  if ($Ctx.ContainsKey('btnFtpSource') -and $Ctx['btnFtpSource']) {
    $Ctx['btnFtpSource'].add_Click({
      try {
        $config = New-FtpSourceConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnFtpSource'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'FTP-Quelle' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 460

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'FTP-Quell-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 80
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Protokoll:  $($config.Protocol)")
        if ($config.PSObject.Properties.Name -contains 'Host') { [void]$lb.Items.Add("Host:       $($config.Host)") }
        if ($config.PSObject.Properties.Name -contains 'Port') { [void]$lb.Items.Add("Port:       $($config.Port)") }
        if ($config.PSObject.Properties.Name -contains 'Passive') { [void]$lb.Items.Add("Passiv:     $($config.Passive)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("FTP-Quelle fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Cloud-Sync (LF-17)
  if ($Ctx.ContainsKey('btnCloudSync') -and $Ctx['btnCloudSync']) {
    $Ctx['btnCloudSync'].add_Click({
      try {
        $config = New-CloudSyncConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnCloudSync'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Cloud-Sync' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Cloud-Sync-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Provider: $($config.Provider)")
        if ($config.PSObject.Properties.Name -contains 'SyncDirection') { [void]$lb.Items.Add("Richtung: $($config.SyncDirection)") }
        if ($config.PSObject.Properties.Name -contains 'Interval') { [void]$lb.Items.Add("Intervall: $($config.Interval)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Cloud-Sync fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Plugin-Marktplatz (LF-18)
  if ($Ctx.ContainsKey('btnPluginMarketplaceFeature') -and $Ctx['btnPluginMarketplaceFeature']) {
    $Ctx['btnPluginMarketplaceFeature'].add_Click({
      try {
        $config = New-PluginMarketplaceConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPluginMarketplaceFeature'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Plugin-Marktplatz' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = ("Plugin-Marktplatz — {0} Plugins" -f $config.AvailableCount)
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Verfügbar: $($config.AvailableCount)")
        if ($config.PSObject.Properties.Name -contains 'Installed') { [void]$lb.Items.Add("Installiert: $($config.Installed)") }
        if ($config.PSObject.Properties.Name -contains 'Registry') { [void]$lb.Items.Add("Registry: $($config.Registry)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Plugin-Marktplatz fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Portable Modus (QW-12)
  if ($Ctx.ContainsKey('btnPortableMode') -and $Ctx['btnPortableMode']) {
    $Ctx['btnPortableMode'].add_Click({
      try {
        $root = Get-PortableSettingsRoot

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPortableMode'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Portable Modus' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $rootPanel = New-Object System.Windows.Controls.StackPanel
        $rootPanel.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Portable Modus'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$rootPanel.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Settings-Root: $root")
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("Im portablen Modus werden alle Einstellungen")
        [void]$lb.Items.Add("im Programmverzeichnis gespeichert.")
        [void]$rootPanel.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$rootPanel.Children.Add($btnClose)

        $dialog.Content = $rootPanel
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Portable Modus fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Docker (XL-01)
  if ($Ctx.ContainsKey('btnDockerContainer') -and $Ctx['btnDockerContainer']) {
    $Ctx['btnDockerContainer'].add_Click({
      try {
        $config = New-DockerfileConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnDockerContainer'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Docker-Container' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Dockerfile-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Base-Image: $($config.BaseImage)")
        if ($config.PSObject.Properties.Name -contains 'ExposedPorts') { [void]$lb.Items.Add("Ports:      $($config.ExposedPorts -join ', ')") }
        if ($config.PSObject.Properties.Name -contains 'Volumes') { [void]$lb.Items.Add("Volumes:    $($config.Volumes -join ', ')") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Docker fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Mobile Web UI (XL-02)
  if ($Ctx.ContainsKey('btnMobileWebUI') -and $Ctx['btnMobileWebUI']) {
    $Ctx['btnMobileWebUI'].add_Click({
      try {
        $config = New-WebUIConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnMobileWebUI'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Mobile Web UI' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Web-UI-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Port:    $($config.Port)")
        [void]$lb.Items.Add("Bind:    $($config.BindAddress)")
        if ($config.PSObject.Properties.Name -contains 'Theme') { [void]$lb.Items.Add("Theme:   $($config.Theme)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Mobile Web UI fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Windows-Kontextmenü (XL-03)
  if ($Ctx.ContainsKey('btnWindowsContextMenu') -and $Ctx['btnWindowsContextMenu']) {
    $Ctx['btnWindowsContextMenu'].add_Click({
      try {
        $entry = New-ContextMenuEntry

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnWindowsContextMenu'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Windows-Kontextmenü' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Kontextmenü-Eintrag'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Label:   $($entry.Label)")
        if ($entry.PSObject.Properties.Name -contains 'Command') { [void]$lb.Items.Add("Befehl:  $($entry.Command)") }
        if ($entry.PSObject.Properties.Name -contains 'Icon') { [void]$lb.Items.Add("Icon:    $($entry.Icon)") }
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("Integriert RomCleanup in den Windows Explorer.")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Kontextmenü fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # PSGallery (XL-04)
  if ($Ctx.ContainsKey('btnPSGallery') -and $Ctx['btnPSGallery']) {
    $Ctx['btnPSGallery'].add_Click({
      try {
        $manifest = New-PSGalleryManifest

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPSGallery'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'PSGallery-Manifest' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'PSGallery-Manifest'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Version:   $($manifest.ModuleVersion)")
        if ($manifest.PSObject.Properties.Name -contains 'Author') { [void]$lb.Items.Add("Autor:     $($manifest.Author)") }
        if ($manifest.PSObject.Properties.Name -contains 'Description') { [void]$lb.Items.Add("Beschreibung: $($manifest.Description)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("PSGallery fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Paketmanager (XL-05)
  if ($Ctx.ContainsKey('btnPackageManager') -and $Ctx['btnPackageManager']) {
    $Ctx['btnPackageManager'].add_Click({
      try {
        $manifest = New-WingetManifest

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnPackageManager'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Paketmanager (Winget)' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Winget-Manifest'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("PackageId: $($manifest.PackageIdentifier)")
        if ($manifest.PSObject.Properties.Name -contains 'Version') { [void]$lb.Items.Add("Version:   $($manifest.Version)") }
        if ($manifest.PSObject.Properties.Name -contains 'Publisher') { [void]$lb.Items.Add("Publisher: $($manifest.Publisher)") }
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Paketmanager fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Hardlink-Modus (XL-11)
  if ($Ctx.ContainsKey('btnHardlinkMode') -and $Ctx['btnHardlinkMode']) {
    $Ctx['btnHardlinkMode'].add_Click({
      try {
        $supported = Test-HardlinkSupported

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnHardlinkMode'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Hardlink-Modus' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Hardlink-Modus'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $statusPanel = New-Object System.Windows.Controls.StackPanel
        $statusPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $statusPanel.Margin = [System.Windows.Thickness]::new(0,0,0,12)

        $dot = New-Object System.Windows.Shapes.Ellipse
        $dot.Width = 14; $dot.Height = 14
        $dot.Margin = [System.Windows.Thickness]::new(0,2,8,0)
        if ($supported) {
          $dot.Fill = [System.Windows.Media.Brushes]::LimeGreen
        } else {
          $dot.Fill = [System.Windows.Media.Brushes]::OrangeRed
        }
        [void]$statusPanel.Children.Add($dot)

        $lblStatus = New-Object System.Windows.Controls.TextBlock
        $lblStatus.FontSize = 14
        $lblStatus.Text = if ($supported) { 'Hardlinks werden unterstützt' } else { 'Hardlinks nicht verfügbar' }
        [void]$statusPanel.Children.Add($lblStatus)
        [void]$root.Children.Add($statusPanel)

        $lblHint = New-Object System.Windows.Controls.TextBlock
        $lblHint.Text = 'Hardlinks sparen Speicherplatz, indem Dateien nur einmal auf der Festplatte existieren.'
        $lblHint.TextWrapping = [System.Windows.TextWrapping]::Wrap
        $lblHint.FontSize = 12
        $lblHint.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblHint)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Hardlink-Modus fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # USN-Journal (XL-10)
  if ($Ctx.ContainsKey('btnUsnJournal') -and $Ctx['btnUsnJournal']) {
    $Ctx['btnUsnJournal'].add_Click({
      try {
        $available = Test-UsnJournalAvailable

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnUsnJournal'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'USN-Journal' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'USN-Journal-Status'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $statusPanel = New-Object System.Windows.Controls.StackPanel
        $statusPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $statusPanel.Margin = [System.Windows.Thickness]::new(0,0,0,12)

        $dot = New-Object System.Windows.Shapes.Ellipse
        $dot.Width = 14; $dot.Height = 14
        $dot.Margin = [System.Windows.Thickness]::new(0,2,8,0)
        if ($available) {
          $dot.Fill = [System.Windows.Media.Brushes]::LimeGreen
        } else {
          $dot.Fill = [System.Windows.Media.Brushes]::OrangeRed
        }
        [void]$statusPanel.Children.Add($dot)

        $lblStatus = New-Object System.Windows.Controls.TextBlock
        $lblStatus.FontSize = 14
        $lblStatus.Text = if ($available) { 'USN-Journal verfügbar' } else { 'USN-Journal nicht verfügbar (Admin-Rechte benötigt)' }
        [void]$statusPanel.Children.Add($lblStatus)
        [void]$root.Children.Add($statusPanel)

        $lblHint = New-Object System.Windows.Controls.TextBlock
        $lblHint.Text = 'Das USN-Journal ermöglicht schnelle Änderungserkennung auf NTFS-Laufwerken.'
        $lblHint.TextWrapping = [System.Windows.TextWrapping]::Wrap
        $lblHint.FontSize = 12
        $lblHint.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblHint)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("USN-Journal fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Multi-Instanz (XL-13)
  if ($Ctx.ContainsKey('btnMultiInstanceSync') -and $Ctx['btnMultiInstanceSync']) {
    $Ctx['btnMultiInstanceSync'].add_Click({
      try {
        $identity = New-InstanceIdentity

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnMultiInstanceSync'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Multi-Instanz-Sync' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Instanz-Identität'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $lb = New-Object System.Windows.Controls.ListBox
        $lb.FontFamily = New-Object System.Windows.Media.FontFamily('Consolas')
        $lb.FontSize = 13; $lb.MinHeight = 60
        $lb.Background = [System.Windows.Media.Brushes]::Transparent
        $lb.BorderThickness = [System.Windows.Thickness]::new(0)
        [void]$lb.Items.Add("Instanz-ID: $($identity.InstanceId)")
        if ($identity.PSObject.Properties.Name -contains 'MachineName') { [void]$lb.Items.Add("Rechner:    $($identity.MachineName)") }
        if ($identity.PSObject.Properties.Name -contains 'StartTime') { [void]$lb.Items.Add("Startzeit:  $($identity.StartTime)") }
        [void]$lb.Items.Add("")
        [void]$lb.Items.Add("Mehrere Instanzen können synchronisiert werden.")
        [void]$root.Children.Add($lb)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,8,0,0)
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Multi-Instanz fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Telemetrie (XL-14)
  if ($Ctx.ContainsKey('btnTelemetry') -and $Ctx['btnTelemetry']) {
    $Ctx['btnTelemetry'].add_Click({
      try {
        $config = New-TelemetryConfig

        $owner = [System.Windows.Window]::GetWindow($Ctx['btnTelemetry'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Telemetrie' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Telemetrie-Konfiguration'
        $lblTitle.FontSize = 16; $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $statusPanel = New-Object System.Windows.Controls.StackPanel
        $statusPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $statusPanel.Margin = [System.Windows.Thickness]::new(0,0,0,12)

        $dot = New-Object System.Windows.Shapes.Ellipse
        $dot.Width = 14; $dot.Height = 14
        $dot.Margin = [System.Windows.Thickness]::new(0,2,8,0)
        if ($config.Enabled) {
          $dot.Fill = [System.Windows.Media.Brushes]::LimeGreen
        } else {
          $dot.Fill = [System.Windows.Media.Brushes]::OrangeRed
        }
        [void]$statusPanel.Children.Add($dot)

        $lblStatus = New-Object System.Windows.Controls.TextBlock
        $lblStatus.FontSize = 14
        $lblStatus.Text = if ($config.Enabled) { 'Telemetrie ist aktiviert' } else { 'Telemetrie ist deaktiviert' }
        [void]$statusPanel.Children.Add($lblStatus)
        [void]$root.Children.Add($statusPanel)

        $lblHint = New-Object System.Windows.Controls.TextBlock
        $lblHint.Text = 'Telemetrie sammelt anonyme Nutzungsstatistiken zur Verbesserung der Anwendung.'
        $lblHint.TextWrapping = [System.Windows.TextWrapping]::Wrap
        $lblHint.FontSize = 12
        $lblHint.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblHint)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'OK'; $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.add_Click({ $dialog.Close() }.GetNewClosure())
        [void]$root.Children.Add($btnClose)

        $dialog.Content = $root
        [void]$dialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Telemetrie fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # ── UI & Erscheinungsbild ──────────────────────────────────────────────

  # Barrierefreiheit (LF-13) — High-Contrast + Font-Skalierung
  if ($Ctx.ContainsKey('btnAccessibility') -and $Ctx['btnAccessibility']) {
    $Ctx['btnAccessibility'].add_Click({
      try {
        $owner = [System.Windows.Window]::GetWindow($Ctx['btnAccessibility'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Barrierefreiheit' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 420

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20,20,20,20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Barrierefreiheits-Einstellungen'
        $lblTitle.FontSize = 16
        $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        # High-Contrast toggle
        $chkContrast = New-Object System.Windows.Controls.CheckBox
        $chkContrast.Content = 'High-Contrast-Modus aktivieren'
        $chkContrast.Margin = [System.Windows.Thickness]::new(0,0,0,12)
        $chkContrast.FontSize = 13
        $currentTheme = 'dark'
        if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
          $currentTheme = [string](Get-AppStateValue -Key 'UITheme' -Default 'dark')
        }
        $chkContrast.IsChecked = ($currentTheme -eq 'high-contrast')
        [void]$root.Children.Add($chkContrast)

        # Font scale
        $lblScale = New-Object System.Windows.Controls.TextBlock
        $lblScale.Text = 'Schriftgröße-Skalierung:'
        $lblScale.Margin = [System.Windows.Thickness]::new(0,4,0,4)
        $lblScale.FontSize = 13
        [void]$root.Children.Add($lblScale)

        $scalePanel = New-Object System.Windows.Controls.StackPanel
        $scalePanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $scalePanel.Margin = [System.Windows.Thickness]::new(0,0,0,12)

        $sldScale = New-Object System.Windows.Controls.Slider
        $sldScale.Minimum = 0.8
        $sldScale.Maximum = 1.5
        $sldScale.Value = 1.0
        $sldScale.Width = 200
        $sldScale.TickFrequency = 0.1
        $sldScale.IsSnapToTickEnabled = $true
        $sldScale.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
        $currentScale = 1.0
        if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
          try { $currentScale = [double](Get-AppStateValue -Key 'UIFontScale' -Default 1.0) } catch { }
        }
        $sldScale.Value = $currentScale

        $lblScaleVal = New-Object System.Windows.Controls.TextBlock
        $lblScaleVal.Text = ('{0:P0}' -f $sldScale.Value)
        $lblScaleVal.Margin = [System.Windows.Thickness]::new(8,0,0,0)
        $lblScaleVal.VerticalAlignment = [System.Windows.VerticalAlignment]::Center
        $lblScaleVal.MinWidth = 40

        $sldScale.add_ValueChanged({
          $lblScaleVal.Text = ('{0:P0}' -f $sldScale.Value)
        }.GetNewClosure())

        [void]$scalePanel.Children.Add($sldScale)
        [void]$scalePanel.Children.Add($lblScaleVal)
        [void]$root.Children.Add($scalePanel)

        # Reduced motion
        $chkMotion = New-Object System.Windows.Controls.CheckBox
        $chkMotion.Content = 'Animationen reduzieren'
        $chkMotion.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        $chkMotion.FontSize = 13
        $reducedMotion = $false
        if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
          try { $reducedMotion = [bool](Get-AppStateValue -Key 'UIReducedMotion' -Default $false) } catch { }
        }
        $chkMotion.IsChecked = $reducedMotion
        [void]$root.Children.Add($chkMotion)

        # Buttons
        $buttonPanel = New-Object System.Windows.Controls.StackPanel
        $buttonPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $buttonPanel.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right

        $btnCancel = New-Object System.Windows.Controls.Button
        $btnCancel.Content = 'Abbrechen'
        $btnCancel.Width = 110
        $btnCancel.Margin = [System.Windows.Thickness]::new(0,0,8,0)
        $btnCancel.add_Click({ $dialog.DialogResult = $false; $dialog.Close() }.GetNewClosure())

        $btnApply = New-Object System.Windows.Controls.Button
        $btnApply.Content = 'Anwenden'
        $btnApply.Width = 110
        $btnApply.add_Click({ $dialog.DialogResult = $true; $dialog.Close() }.GetNewClosure())

        [void]$buttonPanel.Children.Add($btnCancel)
        [void]$buttonPanel.Children.Add($btnApply)
        [void]$root.Children.Add($buttonPanel)

        $dialog.Content = $root
        $result = $dialog.ShowDialog()

        if ($result -eq $true) {
          $wantHighContrast = [bool]$chkContrast.IsChecked
          $fontScale = [double]$sldScale.Value
          $wantReducedMotion = [bool]$chkMotion.IsChecked

          if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
            [void](Set-AppStateValue -Key 'UIFontScale' -Value $fontScale)
            [void](Set-AppStateValue -Key 'UIReducedMotion' -Value $wantReducedMotion)
          }

          if ($wantHighContrast) {
            # Apply high-contrast palette
            $hcPalette = @{
              BrushBackground   = '#000000'
              BrushSurface      = '#1A1A1A'
              BrushSurfaceAlt   = '#0D0D0D'
              BrushSurfaceLight = '#262626'
              BrushAccentCyan   = '#FFD700'
              BrushAccentPurple = '#FFFFFF'
              BrushDanger       = '#FF0000'
              BrushSuccess      = '#00FF00'
              BrushWarning      = '#FFFF00'
              BrushTextPrimary  = '#FFFFFF'
              BrushTextMuted    = '#CCCCCC'
              BrushBorder       = '#FFFFFF'
            }
            foreach ($k in $hcPalette.Keys) {
              try { $owner.Resources[$k] = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString([string]$hcPalette[$k])) } catch { }
            }
            if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
              [void](Set-AppStateValue -Key 'UITheme' -Value 'high-contrast')
            }
            if ($Ctx.ContainsKey('btnThemeToggle') -and $Ctx['btnThemeToggle']) {
              $Ctx['btnThemeToggle'].Content = '◑ Kontrast'
            }
            Add-WpfLogLine -Ctx $Ctx -Line 'High-Contrast-Modus aktiviert' -Level 'INFO'
          } else {
            # Revert to current non-HC theme
            $themeNow = 'dark'
            if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
              $themeNow = [string](Get-AppStateValue -Key 'UITheme' -Default 'dark')
            }
            if ($themeNow -eq 'high-contrast') {
              Set-WpfThemePalette -Window $owner -Ctx $Ctx -Light:$false
              if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
                [void](Set-AppStateValue -Key 'UITheme' -Value 'dark')
              }
            }
          }

          # Apply font scale
          if ($fontScale -ne 1.0) {
            $baseSizes = @(11, 13, 14, 16, 18, 20, 24)
            foreach ($ctrl in @($owner.Content)) {
              if ($ctrl -and $ctrl.PSObject.Properties.Name -contains 'FontSize') {
                try { $ctrl.FontSize = [math]::Round(13 * $fontScale, 1) } catch { }
              }
            }
            Add-WpfLogLine -Ctx $Ctx -Line ('Schriftskalierung auf {0:P0} gesetzt' -f $fontScale) -Level 'INFO'
          }

          Add-WpfLogLine -Ctx $Ctx -Line ('Barrierefreiheit: HighContrast={0}, FontScale={1:P0}, ReducedMotion={2}' -f $wantHighContrast, $fontScale, $wantReducedMotion) -Level 'INFO'
          try { Save-WpfToSettings -Ctx $Ctx } catch { }
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Barrierefreiheit fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Theme-Engine (LF-20) — Theme-Auswahl-Dialog
  if ($Ctx.ContainsKey('btnThemeEngine') -and $Ctx['btnThemeEngine']) {
    $Ctx['btnThemeEngine'].add_Click({
      try {
        $themes = Get-BuiltinThemes
        $owner = [System.Windows.Window]::GetWindow($Ctx['btnThemeEngine'])
        $dialog = New-WpfStyledDialog -Owner $owner -Title 'Theme-Engine' -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
        $dialog.MinWidth = 460

        $root = New-Object System.Windows.Controls.StackPanel
        $root.Margin = [System.Windows.Thickness]::new(20,20,20,20)

        $lblTitle = New-Object System.Windows.Controls.TextBlock
        $lblTitle.Text = 'Theme auswählen'
        $lblTitle.FontSize = 16
        $lblTitle.FontWeight = [System.Windows.FontWeights]::Bold
        $lblTitle.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$root.Children.Add($lblTitle)

        $currentTheme = 'dark'
        if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
          $currentTheme = [string](Get-AppStateValue -Key 'UITheme' -Default 'dark')
        }

        # Map internal theme names to theme keys
        $themeKeyMap = @{
          'dark'          = 'retro-dark'
          'light'         = 'retro-light'
          'high-contrast' = 'high-contrast'
          'retro-dark'    = 'retro-dark'
          'retro-light'   = 'retro-light'
        }
        $currentKey = if ($themeKeyMap.ContainsKey($currentTheme)) { $themeKeyMap[$currentTheme] } else { 'retro-dark' }

        $radioButtons = @{}
        foreach ($theme in $themes) {
          $border = New-Object System.Windows.Controls.Border
          $border.BorderThickness = [System.Windows.Thickness]::new(1)
          $border.CornerRadius = [System.Windows.CornerRadius]::new(4)
          $border.Padding = [System.Windows.Thickness]::new(12,8,12,8)
          $border.Margin = [System.Windows.Thickness]::new(0,0,0,6)
          try {
            $border.BorderBrush = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString($theme.Colors.Border))
            $border.Background = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString($theme.Colors.Background))
          } catch { }

          $innerPanel = New-Object System.Windows.Controls.StackPanel

          $rb = New-Object System.Windows.Controls.RadioButton
          $rb.GroupName = 'ThemeGroup'
          $rb.Content = ('{0} — {1}' -f $theme.Name, $theme.Description)
          $rb.FontSize = 13
          $rb.IsChecked = ($theme.Key -eq $currentKey)
          try {
            $rb.Foreground = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString($theme.Colors.TextPrimary))
          } catch { }
          $rb.Tag = $theme.Key
          $radioButtons[$theme.Key] = $rb

          # Color preview dots
          $colorPanel = New-Object System.Windows.Controls.WrapPanel
          $colorPanel.Margin = [System.Windows.Thickness]::new(20,4,0,0)
          foreach ($colorName in @('Accent','Success','Warning','Error')) {
            if ($theme.Colors.ContainsKey($colorName)) {
              $dot = New-Object System.Windows.Shapes.Ellipse
              $dot.Width = 14
              $dot.Height = 14
              $dot.Margin = [System.Windows.Thickness]::new(0,0,6,0)
              $dot.ToolTip = $colorName
              try { $dot.Fill = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString($theme.Colors[$colorName])) } catch { }
              [void]$colorPanel.Children.Add($dot)
            }
          }

          [void]$innerPanel.Children.Add($rb)
          [void]$innerPanel.Children.Add($colorPanel)
          $border.Child = $innerPanel
          [void]$root.Children.Add($border)
        }

        # Buttons
        $buttonPanel = New-Object System.Windows.Controls.StackPanel
        $buttonPanel.Orientation = [System.Windows.Controls.Orientation]::Horizontal
        $buttonPanel.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $buttonPanel.Margin = [System.Windows.Thickness]::new(0,12,0,0)

        $btnCancel = New-Object System.Windows.Controls.Button
        $btnCancel.Content = 'Abbrechen'
        $btnCancel.Width = 110
        $btnCancel.Margin = [System.Windows.Thickness]::new(0,0,8,0)
        $btnCancel.add_Click({ $dialog.DialogResult = $false; $dialog.Close() }.GetNewClosure())

        $btnApply = New-Object System.Windows.Controls.Button
        $btnApply.Content = 'Anwenden'
        $btnApply.Width = 110
        $btnApply.add_Click({ $dialog.DialogResult = $true; $dialog.Close() }.GetNewClosure())

        [void]$buttonPanel.Children.Add($btnCancel)
        [void]$buttonPanel.Children.Add($btnApply)
        [void]$root.Children.Add($buttonPanel)

        $dialog.Content = $root
        $result = $dialog.ShowDialog()

        if ($result -eq $true) {
          $selectedKey = $null
          foreach ($key in $radioButtons.Keys) {
            if ([bool]$radioButtons[$key].IsChecked) {
              $selectedKey = $key
              break
            }
          }

          if ($selectedKey) {
            $selectedTheme = $themes | Where-Object { $_.Key -eq $selectedKey } | Select-Object -First 1
            if ($selectedTheme) {
              # Build brush palette from theme colors
              $colorToBrush = @{
                'Background'    = 'BrushBackground'
                'Surface'       = 'BrushSurface'
                'Primary'       = 'BrushSurfaceAlt'
                'Accent'        = 'BrushAccentCyan'
                'TextPrimary'   = 'BrushTextPrimary'
                'TextSecondary' = 'BrushTextMuted'
                'Success'       = 'BrushSuccess'
                'Warning'       = 'BrushWarning'
                'Error'         = 'BrushDanger'
                'Border'        = 'BrushBorder'
              }
              foreach ($colorKey in $colorToBrush.Keys) {
                if ($selectedTheme.Colors.ContainsKey($colorKey)) {
                  $brushKey = $colorToBrush[$colorKey]
                  try { $owner.Resources[$brushKey] = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString([string]$selectedTheme.Colors[$colorKey])) } catch { }
                }
              }

              # Map theme key to internal UITheme value
              $internalTheme = switch ($selectedKey) {
                'retro-dark'    { 'dark' }
                'retro-light'   { 'light' }
                'high-contrast' { 'high-contrast' }
                default         { 'dark' }
              }
              if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
                [void](Set-AppStateValue -Key 'UITheme' -Value $internalTheme)
              }
              if ($Ctx.ContainsKey('btnThemeToggle') -and $Ctx['btnThemeToggle']) {
                $Ctx['btnThemeToggle'].Content = switch ($internalTheme) {
                  'light'         { '☀ Hell' }
                  'high-contrast' { '◑ Kontrast' }
                  default         { '☾ Dunkel' }
                }
              }
              Add-WpfLogLine -Ctx $Ctx -Line ('Theme gewechselt: {0}' -f $selectedTheme.Name) -Level 'INFO'
              try { Save-WpfToSettings -Ctx $Ctx } catch { }
            }
          }
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Theme-Engine fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }
}
