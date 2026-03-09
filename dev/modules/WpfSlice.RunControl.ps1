function Register-WpfRunControlHandlers {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$ResolveLatestHtmlReport,
    [Parameter(Mandatory)][scriptblock]$RefreshReportPreview
  )

  $requestCancel = {
    param([string]$Reason, [bool]$DisableCancelButton = $true)

    if ($script:WpfCancelToken) {
      try { $script:WpfCancelToken.Cancel() } catch { }
    }
    # BUG GUI-001 FIX: Signal the background runspace's ManualResetEventSlim
    if ($Ctx.ContainsKey('_activeBackgroundJob') -and $Ctx['_activeBackgroundJob'] -and $Ctx['_activeBackgroundJob'].CancelEvent) {
      try { $Ctx['_activeBackgroundJob'].CancelEvent.Set() } catch { }
    }
    if (Get-Command Set-AppRunState -ErrorAction SilentlyContinue) {
      try { [void](Set-AppRunState -State 'Canceling') } catch { }
    }
    if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
      try { Set-AppStateValue -Key 'CancelRequested' -Value $true } catch { }
    }

    if (-not [string]::IsNullOrWhiteSpace($Reason)) {
      Add-WpfLogLine -Ctx $Ctx -Line $Reason -Level 'WARN'
    }

    if ($DisableCancelButton -and $Ctx.ContainsKey('btnCancelGlobal') -and $Ctx['btnCancelGlobal']) {
      $Ctx['btnCancelGlobal'].IsEnabled = $false
    }
  }.GetNewClosure()

  $openLatestReport = {
    try {
      $htmlReport = & $ResolveLatestHtmlReport
      if ($htmlReport) {
        Start-Process $htmlReport.FullName
        & $RefreshReportPreview
      } else {
        [System.Windows.MessageBox]::Show(
          'Kein HTML-Bericht gefunden. Erst einen Lauf starten.',
          'ROM Cleanup',
          [System.Windows.MessageBoxButton]::OK,
          [System.Windows.MessageBoxImage]::Information) | Out-Null
      }
    } catch {
      Add-WpfLogLine -Ctx $Ctx -Line "Bericht öffnen fehlgeschlagen: $($_.Exception.Message)" -Level 'ERROR'
    }
  }.GetNewClosure()

  if ($Ctx.ContainsKey('btnOpenReport') -and $Ctx['btnOpenReport']) {
    $Ctx['btnOpenReport'].add_Click($openLatestReport)
  }
  if ($Ctx.ContainsKey('btnOpenReportLog') -and $Ctx['btnOpenReportLog']) {
    $Ctx['btnOpenReportLog'].add_Click($openLatestReport)
  }
  if ($Ctx.ContainsKey('btnRefreshReportPreview') -and $Ctx['btnRefreshReportPreview']) {
    $Ctx['btnRefreshReportPreview'].add_Click($RefreshReportPreview)
  }

  if ($Ctx.ContainsKey('btnRunGlobal') -and $Ctx['btnRunGlobal']) {
    $Ctx['btnRunGlobal'].add_Click({
      Start-WpfOperationAsync -Window $Window -Ctx $Ctx
    }.GetNewClosure())
  }

  # "Move starten" button – visible after a successful DryRun
  if ($Ctx.ContainsKey('btnStartMove') -and $Ctx['btnStartMove']) {
    $Ctx['btnStartMove'].add_Click({
      # UX-02/UX-06: Styled Move-confirmation dialog with checkbox gate
      $confirmed = $false
      if (Get-Command Show-WpfMoveConfirmDialog -ErrorAction SilentlyContinue) {
        $confirmed = Show-WpfMoveConfirmDialog -Owner $Window -Ctx $Ctx
      } else {
        # Fallback to MessageBox if dialog function not available
        $confirmMsg = "ACHTUNG: Im Move-Modus werden Duplikate und Junk physisch in den Papierkorb verschoben.`n`nFortfahren?"
        $confirmResult = [System.Windows.MessageBox]::Show(
          $confirmMsg,
          'ROM Cleanup – Move-Modus',
          [System.Windows.MessageBoxButton]::OKCancel,
          [System.Windows.MessageBoxImage]::Warning)
        $confirmed = ($confirmResult -eq [System.Windows.MessageBoxResult]::OK)
      }
      if (-not $confirmed) { return }
      # Set force-move flag so Get-WpfRunParameters uses Move mode even in Einfach-Modus
      $Ctx['_forceMoveMode'] = $true
      $Ctx['btnStartMove'].Visibility = [System.Windows.Visibility]::Collapsed
      Start-WpfOperationAsync -Window $Window -Ctx $Ctx
      # Clear force-move flag after starting
      $Ctx['_forceMoveMode'] = $false
    }.GetNewClosure())
  }

  # UX-11: Inline Rollback button (visible after Move completion)
  if ($Ctx.ContainsKey('btnRollbackInline') -and $Ctx['btnRollbackInline']) {
    $Ctx['btnRollbackInline'].add_Click({
      if (Get-Command Invoke-WpfRollbackWizard -ErrorAction SilentlyContinue) {
        Invoke-WpfRollbackWizard -Window $Window -Ctx $Ctx
      } elseif ($Ctx.ContainsKey('btnRollback') -and $Ctx['btnRollback'] -and $Ctx['btnRollback'].IsEnabled) {
        try { $Ctx['btnRollback'].RaiseEvent([System.Windows.RoutedEventArgs]::new([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)) } catch { }
      }
    }.GetNewClosure())
  }

  # UX-14: Scroll position preservation on tab switch
  $Ctx['_scrollOffsets'] = @{}
  if ($Ctx.ContainsKey('tabMain') -and $Ctx['tabMain']) {
    $Ctx['tabMain'].add_SelectionChanged({
      param($sender, $e)
      try {
        if ($e.Source -ne $Ctx['tabMain']) { return }
        # Save offset of the tab we're leaving
        foreach ($old in $e.RemovedItems) {
          if (-not $old -or -not $old.Name) { continue }
          $svName = switch ($old.Name) {
            'tabSort'           { 'svSort' }
            'tabConfigBasis'    { 'svConfigBasis' }
            'tabConfigAdvanced' { 'svConfigAdvanced' }
            default             { $null }
          }
          if ($svName -and $Ctx.ContainsKey($svName) -and $Ctx[$svName]) {
            $Ctx['_scrollOffsets'][$svName] = $Ctx[$svName].VerticalOffset
          }
        }
        # Restore offset of the tab we're entering
        foreach ($new in $e.AddedItems) {
          if (-not $new -or -not $new.Name) { continue }
          $svName = switch ($new.Name) {
            'tabSort'           { 'svSort' }
            'tabConfigBasis'    { 'svConfigBasis' }
            'tabConfigAdvanced' { 'svConfigAdvanced' }
            default             { $null }
          }
          if ($svName -and $Ctx.ContainsKey($svName) -and $Ctx[$svName] -and $Ctx['_scrollOffsets'].ContainsKey($svName)) {
            $Ctx[$svName].ScrollToVerticalOffset($Ctx['_scrollOffsets'][$svName])
          }
        }
      } catch { } # non-fatal UX enhancement
    }.GetNewClosure())
  }
  # Also handle sub-tab switches in Config tab
  if ($Ctx.ContainsKey('tabConfigSub') -and $Ctx['tabConfigSub']) {
    $Ctx['tabConfigSub'].add_SelectionChanged({
      param($sender, $e)
      try {
        if ($e.Source -ne $Ctx['tabConfigSub']) { return }
        foreach ($old in $e.RemovedItems) {
          if (-not $old -or -not $old.Name) { continue }
          $svName = switch ($old.Name) { 'tabConfigBasis' { 'svConfigBasis' }; 'tabConfigTools' { 'svConfigTools' }; 'tabConfigProfiles' { 'svConfigProfiles' }; 'tabConfigAdvanced' { 'svConfigAdvanced' }; default { $null } }
          if ($svName -and $Ctx.ContainsKey($svName) -and $Ctx[$svName]) {
            $Ctx['_scrollOffsets'][$svName] = $Ctx[$svName].VerticalOffset
          }
        }
        foreach ($new in $e.AddedItems) {
          if (-not $new -or -not $new.Name) { continue }
          $svName = switch ($new.Name) { 'tabConfigBasis' { 'svConfigBasis' }; 'tabConfigTools' { 'svConfigTools' }; 'tabConfigProfiles' { 'svConfigProfiles' }; 'tabConfigAdvanced' { 'svConfigAdvanced' }; default { $null } }
          if ($svName -and $Ctx.ContainsKey($svName) -and $Ctx[$svName] -and $Ctx['_scrollOffsets'].ContainsKey($svName)) {
            $Ctx[$svName].ScrollToVerticalOffset($Ctx['_scrollOffsets'][$svName])
          }
        }
      } catch { }
    }.GetNewClosure())
  }

  $Window.add_PreviewKeyDown({
    param($sender, $e)

    try {
      if (-not $e) { return }

      if (($e.Key -eq [System.Windows.Input.Key]::S) -and (([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control) -ne 0)) {
        Save-WpfToSettings -Ctx $Ctx
        Add-WpfLogLine -Ctx $Ctx -Line 'Shortcut: Einstellungen gespeichert (Ctrl+S).' -Level 'INFO'
        $e.Handled = $true
        return
      }

      if ($e.Key -eq [System.Windows.Input.Key]::F5) {
        if ($Ctx.ContainsKey('btnRunGlobal') -and $Ctx['btnRunGlobal'].IsEnabled) {
          Start-WpfOperationAsync -Window $Window -Ctx $Ctx
          $e.Handled = $true
          return
        }
      }

      if (($e.Key -eq [System.Windows.Input.Key]::Z) -and (([System.Windows.Input.Keyboard]::Modifiers -band [System.Windows.Input.ModifierKeys]::Control) -ne 0)) {
        if ($Ctx.ContainsKey('btnRollbackUndo') -and $Ctx['btnRollbackUndo'] -and $Ctx['btnRollbackUndo'].IsEnabled) {
          try { $Ctx['btnRollbackUndo'].RaiseEvent([System.Windows.RoutedEventArgs]::new([System.Windows.Controls.Primitives.ButtonBase]::ClickEvent)) } catch { }
          Add-WpfLogLine -Ctx $Ctx -Line 'Shortcut: Rollback Undo (Ctrl+Z).' -Level 'INFO'
          $e.Handled = $true
          return
        }
      }

      if ($e.Key -eq [System.Windows.Input.Key]::Escape) {
        if ($Ctx.ContainsKey('btnCancelGlobal') -and $Ctx['btnCancelGlobal'].IsEnabled) {
          & $requestCancel 'Shortcut: Abbruch angefordert (Esc).' $false
          $e.Handled = $true
          return
        }
      }

      if ($e.Key -eq [System.Windows.Input.Key]::F1) {
        try {
          $repoRoot = if ($script:_RomCleanupModuleRoot) { Split-Path -Parent (Split-Path -Parent $script:_RomCleanupModuleRoot) } else { (Get-Location).Path }
          $handbookPath = Join-Path $repoRoot 'docs\USER_HANDBOOK.md'
          if (Test-Path -LiteralPath $handbookPath -PathType Leaf) {
            try {
              Start-Process $handbookPath | Out-Null
            } catch {
              # GUI-08: Fallback auf notepad wenn kein Markdown-Viewer
              Start-Process 'notepad.exe' -ArgumentList $handbookPath | Out-Null
            }
            $e.Handled = $true
            return
          }
        } catch { }
      }
    } catch { }
  }.GetNewClosure())

  if ($Ctx.ContainsKey('btnCancelGlobal') -and $Ctx['btnCancelGlobal']) {
    $Ctx['btnCancelGlobal'].add_Click({
      & $requestCancel 'Abbruch angefordert...' $true
    }.GetNewClosure())
  }

  $Window.add_Closing({
    try {
      Save-WpfToSettings -Ctx $Ctx
      try { Set-WpfWatchMode -Window $Window -Ctx $Ctx -Enabled:$false } catch { }

      if ($Ctx.ContainsKey('btnCancelGlobal') -and $Ctx['btnCancelGlobal'] -and $Ctx['btnCancelGlobal'].IsEnabled) {
        & $requestCancel $null $false
      }
    } catch {
      # GUI-09: Fehler beim Schliessen loggen statt verschlucken
      Write-Warning ("Fehler beim Speichern der Einstellungen beim Schliessen: {0}" -f $_.Exception.Message)
    }
  }.GetNewClosure())
}
