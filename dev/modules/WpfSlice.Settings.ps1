function Register-WpfSettingsProfileHandlers {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$BrowseFile,
    [Parameter(Mandatory)][scriptblock]$SaveFile,
    [Parameter(Mandatory)][scriptblock]$PersistSettingsNow
  )

  if ($Ctx.ContainsKey('btnThemeToggle') -and $Ctx['btnThemeToggle']) {
    $Ctx['btnThemeToggle'].add_Click({
      $isLight = $false
      try {
        # GUI-12: Use AppState as authoritative theme source instead of color string comparison
        if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
          $currentTheme = [string](Get-AppStateValue -Key 'UITheme' -Default 'dark')
          $isLight = ($currentTheme -eq 'dark')  # toggle: dark→light, light→dark
        } else {
          $bgBrush = $Window.Resources['BrushBackground']
          if ($bgBrush -is [System.Windows.Media.SolidColorBrush]) {
            $isDarkNow = ($bgBrush.Color.ToString().ToUpperInvariant() -eq '#FF0D0D1F')
            $isLight = $isDarkNow
          }
        }
      } catch { }

      Set-WpfThemePalette -Window $Window -Ctx $Ctx -Light:$isLight
      if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
        try { [void](Set-AppStateValue -Key 'UITheme' -Value $(if ($isLight) { 'light' } else { 'dark' })) } catch { }
      }
      Save-WpfToSettings -Ctx $Ctx
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('chkReportDryRun') -and $Ctx['chkReportDryRun']) {
    $Ctx['chkReportDryRun'].add_Checked({ Update-WpfDryRunBanner -Ctx $Ctx }.GetNewClosure())
    $Ctx['chkReportDryRun'].add_Unchecked({ Update-WpfDryRunBanner -Ctx $Ctx }.GetNewClosure())
  }

  if (-not ($Ctx.ContainsKey('cmbConfigProfile') -and $Ctx['cmbConfigProfile'])) {
    return
  }

  if ($Ctx.ContainsKey('btnProfileSave') -and $Ctx['btnProfileSave']) {
    $Ctx['btnProfileSave'].add_Click({
      try {
        if (-not (Get-Command Set-ConfigProfile -ErrorAction SilentlyContinue)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Profil-Speichern nicht verfügbar (Set-ConfigProfile fehlt).' -Level 'WARN'
          return
        }

        Save-WpfToSettings -Ctx $Ctx
        $profileName = [string]$Ctx['cmbConfigProfile'].Text
        if ([string]::IsNullOrWhiteSpace($profileName)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Profilname fehlt.' -Level 'WARN'
          return
        }

        $settings = if (Get-Command Get-UserSettings -ErrorAction SilentlyContinue) { Get-UserSettings } else { $null }
        if (-not $settings) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Profil konnte nicht gespeichert werden: keine Settings geladen.' -Level 'ERROR'
          return
        }

        [void](Set-ConfigProfile -Name $profileName -Settings $settings)
        Update-WpfProfileCombo -Ctx $Ctx
        $Ctx['cmbConfigProfile'].SelectedItem = $profileName
        Add-WpfLogLine -Ctx $Ctx -Line "Profil gespeichert: $profileName" -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line "Profil-Speichern fehlgeschlagen: $($_.Exception.Message)" -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnProfileLoad') -and $Ctx['btnProfileLoad']) {
    $Ctx['btnProfileLoad'].add_Click({
      try {
        if (-not (Get-Command Get-ConfigProfiles -ErrorAction SilentlyContinue)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Profil-Laden nicht verfügbar (Get-ConfigProfiles fehlt).' -Level 'WARN'
          return
        }
        if (-not (Get-Command Set-UserSettings -ErrorAction SilentlyContinue)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Profil-Laden nicht verfügbar (Set-UserSettings fehlt).' -Level 'WARN'
          return
        }

        $profileName = [string]$Ctx['cmbConfigProfile'].Text
        if ([string]::IsNullOrWhiteSpace($profileName)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Bitte ein Profil auswählen.' -Level 'WARN'
          return
        }

        $profiles = Get-ConfigProfiles
        if (-not $profiles.ContainsKey($profileName)) {
          Add-WpfLogLine -Ctx $Ctx -Line "Profil nicht gefunden: $profileName" -Level 'WARN'
          return
        }

        Set-UserSettings -Settings $profiles[$profileName]
        Initialize-WpfFromSettings -Ctx $Ctx
        Add-WpfLogLine -Ctx $Ctx -Line "Profil geladen: $profileName" -Level 'INFO'
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line "Profil-Laden fehlgeschlagen: $($_.Exception.Message)" -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnProfileDelete') -and $Ctx['btnProfileDelete']) {
    $Ctx['btnProfileDelete'].add_Click({
      $profileName = [string]$Ctx['cmbConfigProfile'].Text
      if (-not [string]::IsNullOrWhiteSpace($profileName)) {
        $dlg = [System.Windows.MessageBox]::Show(
          "Profil '$profileName' wirklich löschen?",
          'ROM Cleanup',
          [System.Windows.MessageBoxButton]::YesNo,
          [System.Windows.MessageBoxImage]::Warning
        )
        if ($dlg -eq [System.Windows.MessageBoxResult]::Yes) {
          try {
            if (-not (Get-Command Remove-ConfigProfile -ErrorAction SilentlyContinue)) {
              Add-WpfLogLine -Ctx $Ctx -Line 'Profil-Löschen nicht verfügbar (Remove-ConfigProfile fehlt).' -Level 'WARN'
              return
            }

            [void](Remove-ConfigProfile -Name $profileName)
            Update-WpfProfileCombo -Ctx $Ctx
            Add-WpfLogLine -Ctx $Ctx -Line "Profil gelöscht: $profileName" -Level 'INFO'
          } catch {
            Add-WpfLogLine -Ctx $Ctx -Line "Profil-Löschen fehlgeschlagen: $($_.Exception.Message)" -Level 'ERROR'
          }
        }
      }
    }.GetNewClosure())
  }

  $exportProfile = {
    if (-not (Get-Command Export-ConfigProfile -ErrorAction SilentlyContinue)) {
      Add-WpfLogLine -Ctx $Ctx -Line 'Profil-Export nicht verfügbar.' -Level 'WARN'
      return
    }
    $profileName = [string]$Ctx['cmbConfigProfile'].Text
    if ([string]::IsNullOrWhiteSpace($profileName)) {
      Add-WpfLogLine -Ctx $Ctx -Line 'Bitte ein Profil auswählen.' -Level 'WARN'
      return
    }

    $targetPath = & $SaveFile 'Profil exportieren' 'JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*' ("{0}.json" -f ($profileName -replace '[^a-zA-Z0-9_-]','_'))
    if (-not [string]::IsNullOrWhiteSpace($targetPath)) {
      Export-ConfigProfile -Name $profileName -Path $targetPath
      Add-WpfLogLine -Ctx $Ctx -Line "Profil exportiert: $targetPath" -Level 'INFO'
    }
  }.GetNewClosure()

  $exportConfig = {
    if (-not (Get-Command Get-UserSettings -ErrorAction SilentlyContinue)) {
      Add-WpfLogLine -Ctx $Ctx -Line 'Konfig-Export nicht verfügbar (Get-UserSettings fehlt).' -Level 'WARN'
      return
    }
    Save-WpfToSettings -Ctx $Ctx

    $targetPath = & $SaveFile 'Konfiguration exportieren' 'JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*' ("romcleanup-config-$(Get-Date -Format 'yyyyMMdd-HHmmss').json")
    if (-not [string]::IsNullOrWhiteSpace($targetPath)) {
      $settings = Get-UserSettings
      Write-JsonFile -Path $targetPath -Data $settings -Depth 10
      Add-WpfLogLine -Ctx $Ctx -Line "Konfiguration exportiert: $targetPath" -Level 'INFO'
    }
  }.GetNewClosure()

  if ($Ctx.ContainsKey('btnProfileImport') -and $Ctx['btnProfileImport']) {
    $Ctx['btnProfileImport'].add_Click({
      try {
        if (-not (Get-Command Import-ConfigProfile -ErrorAction SilentlyContinue)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Profil-Import nicht verfügbar.' -Level 'WARN'
          return
        }

        $sourcePath = Show-WpfOpenFileDialog -Title 'Profil importieren' `
          -Filter 'JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*' `
          -Owner $Window
        if (-not [string]::IsNullOrWhiteSpace($sourcePath)) {
          [void](Import-ConfigProfile -Path $sourcePath)
          Update-WpfProfileCombo -Ctx $Ctx
          Add-WpfLogLine -Ctx $Ctx -Line "Profil importiert: $sourcePath" -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line "Profil-Import fehlgeschlagen: $($_.Exception.Message)" -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  # Config-Diff-View (5.6.2)
  if ($Ctx.ContainsKey('btnConfigDiff') -and $Ctx['btnConfigDiff']) {
    $Ctx['btnConfigDiff'].add_Click({
      try {
        if (-not (Get-Command Get-ConfigurationDiff -ErrorAction SilentlyContinue)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Config-Diff nicht verfuegbar (Get-ConfigurationDiff fehlt).' -Level 'WARN'
          return
        }

        $profileName = ''
        if ($Ctx.ContainsKey('cmbConfigProfile') -and $Ctx['cmbConfigProfile'] -and $Ctx['cmbConfigProfile'].SelectedItem) {
          $profileName = [string]$Ctx['cmbConfigProfile'].SelectedItem
        }
        $rows = @(Get-ConfigurationDiff -ProfileName $profileName)

        $diffDialog = New-WpfStyledDialog -Owner $Window -Title 'Konfigurationsunterschiede' -Width 650 -Height 400

        $grid = New-Object System.Windows.Controls.Grid
        $grid.Margin = [System.Windows.Thickness]::new(12)
        $rd1 = New-Object System.Windows.Controls.RowDefinition; $rd1.Height = [System.Windows.GridLength]::Auto
        $rd2 = New-Object System.Windows.Controls.RowDefinition; $rd2.Height = [System.Windows.GridLength]::new(1, [System.Windows.GridUnitType]::Star)
        $rd3 = New-Object System.Windows.Controls.RowDefinition; $rd3.Height = [System.Windows.GridLength]::Auto
        [void]$grid.RowDefinitions.Add($rd1)
        [void]$grid.RowDefinitions.Add($rd2)
        [void]$grid.RowDefinitions.Add($rd3)

        $header = New-Object System.Windows.Controls.TextBlock
        $header.Text = if ($rows.Count -eq 0) { 'Keine Unterschiede zur Standardkonfiguration.' } else { "$($rows.Count) geaenderte Einstellung(en):" }
        # GUI-10: Theme-Farben statt hardcoded
        $header.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, 'BrushTextMuted')
        $header.Margin = [System.Windows.Thickness]::new(0,0,0,8)
        [System.Windows.Controls.Grid]::SetRow($header, 0)

        $dg = New-Object System.Windows.Controls.DataGrid
        $dg.AutoGenerateColumns = $true
        $dg.IsReadOnly = $true
        $dg.SetResourceReference([System.Windows.Controls.DataGrid]::BackgroundProperty, 'BrushSurfaceAlt')
        $dg.SetResourceReference([System.Windows.Controls.DataGrid]::ForegroundProperty, 'BrushTextPrimary')
        $dg.SetResourceReference([System.Windows.Controls.DataGrid]::BorderBrushProperty, 'BrushBorder')
        $dg.HeadersVisibility = [System.Windows.Controls.DataGridHeadersVisibility]::Column
        $dg.GridLinesVisibility = [System.Windows.Controls.DataGridGridLinesVisibility]::Horizontal
        $dg.SetResourceReference([System.Windows.Controls.DataGrid]::HorizontalGridLineBrushProperty, 'BrushBorder')
        $dg.ItemsSource = @($rows)
        [System.Windows.Controls.Grid]::SetRow($dg, 1)

        $btnClose = New-Object System.Windows.Controls.Button
        $btnClose.Content = 'Schliessen'
        $btnClose.Width = 110
        $btnClose.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Right
        $btnClose.Margin = [System.Windows.Thickness]::new(0,10,0,0)
        $btnClose.add_Click({ $diffDialog.Close() }.GetNewClosure())
        [System.Windows.Controls.Grid]::SetRow($btnClose, 2)

        [void]$grid.Children.Add($header)
        [void]$grid.Children.Add($dg)
        [void]$grid.Children.Add($btnClose)
        $diffDialog.Content = $grid
        [void]$diffDialog.ShowDialog()
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ('Config-Diff fehlgeschlagen: {0}' -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnExportUnified') -and $Ctx['btnExportUnified']) {
    $Ctx['btnExportUnified'].add_Click({
      try {
        # GUI-05: Eigener Dialog mit klaren Buttons statt Ja/Nein/Abbrechen
        $exportDialog = New-WpfStyledDialog -Owner $Window -Title 'Exportmodus waehlen' -Width 380 -Height 180
        $sp = New-Object System.Windows.Controls.StackPanel
        $sp.Margin = [System.Windows.Thickness]::new(16)
        $lbl = New-Object System.Windows.Controls.TextBlock
        $lbl.Text = 'Was soll exportiert werden?'
        $lbl.SetResourceReference([System.Windows.Controls.TextBlock]::ForegroundProperty, 'BrushTextPrimary')
        $lbl.Margin = [System.Windows.Thickness]::new(0,0,0,16)
        [void]$sp.Children.Add($lbl)
        $wp = New-Object System.Windows.Controls.WrapPanel
        $wp.HorizontalAlignment = [System.Windows.HorizontalAlignment]::Center
        $btnProfile = New-Object System.Windows.Controls.Button; $btnProfile.Content = 'Profil exportieren'; $btnProfile.Width = 150; $btnProfile.Margin = [System.Windows.Thickness]::new(0,0,8,0)
        $btnConfig = New-Object System.Windows.Controls.Button; $btnConfig.Content = 'Konfiguration exportieren'; $btnConfig.Width = 170
        $exportDialogResult = $null
        $btnProfile.add_Click({ $exportDialog.Tag = 'profile'; $exportDialog.Close() }.GetNewClosure())
        $btnConfig.add_Click({ $exportDialog.Tag = 'config'; $exportDialog.Close() }.GetNewClosure())
        [void]$wp.Children.Add($btnProfile); [void]$wp.Children.Add($btnConfig)
        [void]$sp.Children.Add($wp)
        $exportDialog.Content = $sp
        [void]$exportDialog.ShowDialog()

        if ($exportDialog.Tag -eq 'profile') {
          & $exportProfile
        } elseif ($exportDialog.Tag -eq 'config') {
          & $exportConfig
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("Export fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnConfigImport') -and $Ctx['btnConfigImport']) {
    $Ctx['btnConfigImport'].add_Click({
      try {
        if (-not (Get-Command Set-UserSettings -ErrorAction SilentlyContinue)) {
          Add-WpfLogLine -Ctx $Ctx -Line 'Konfig-Import nicht verfügbar (Set-UserSettings fehlt).' -Level 'WARN'
          return
        }

        $sourcePath = & $BrowseFile 'Konfiguration importieren' 'JSON-Datei (*.json)|*.json|Alle Dateien (*.*)|*.*'
        if (-not [string]::IsNullOrWhiteSpace($sourcePath)) {
          $imported = Get-Content -LiteralPath $sourcePath -Raw -ErrorAction Stop | ConvertFrom-Json -ErrorAction Stop
          Set-UserSettings -Settings $imported
          Initialize-WpfFromSettings -Ctx $Ctx
          Add-WpfLogLine -Ctx $Ctx -Line "Konfiguration importiert: $sourcePath" -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line "Konfig-Import fehlgeschlagen: $($_.Exception.Message)" -Level 'ERROR'
      }
    }.GetNewClosure())
  }

  if ($Ctx.ContainsKey('btnApplyLocale') -and $Ctx['btnApplyLocale']) {
    $Ctx['btnApplyLocale'].add_Click({
      try { Set-WpfLocale -Ctx $Ctx } catch { Add-WpfLogLine -Ctx $Ctx -Line ("Sprache anwenden fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR' }
    }.GetNewClosure())
  }

  # NOTE: No & $PersistSettingsNow here — settings are not yet loaded at
  # registration time.  Persisting now would overwrite saved tool paths
  # with empty defaults.  Settings are saved on Window.Closing and on
  # individual control LostFocus instead.
}
