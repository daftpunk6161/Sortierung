# ================================================================
#  WpfEventHandlers.ps1  –  Wire WPF controls to Core Engine
#  Provides: Register-WpfEventHandlers
#            Get-WpfRunParameters
#            Start-WpfOperationAsync
# ================================================================

# ── Shared Dialog Helpers ─────────────────────────────────────────────────────

function ConvertTo-SafeBool {
  <# Converts a value to [bool] safely, handling empty strings that [bool] cannot cast. #>
  param($Value)
  if ($Value -is [bool]) { return $Value }
  if ($Value -is [string]) { return ($Value -eq 'True' -or $Value -eq '1') }
  if ($null -eq $Value) { return $false }
  return [bool]$Value
}

function Show-WpfOpenFileDialog {
  <# Opens a Win32 OpenFileDialog. Returns the selected path or $null. #>
  param(
    [string]$Title,
    [string]$Filter,
    [System.Windows.Window]$Owner
  )
  try {
    $diag = New-Object Microsoft.Win32.OpenFileDialog
    $diag.Title  = $Title
    $diag.Filter = if ($Filter) { $Filter } else { 'All Files (*.*)|*.*' }
    $dialogResult = if ($Owner) { $diag.ShowDialog($Owner) } else { $diag.ShowDialog() }
    if ($dialogResult -eq $true) { return $diag.FileName }
  } catch {
    Write-Verbose ('[WPF] OpenFileDialog failed: {0}' -f $_.Exception.Message)
  }
  return $null
}
if (-not (Get-Variable -Name WpfCancelToken -Scope Script -ErrorAction SilentlyContinue)) {
  $script:WpfCancelToken = $null
}

function ConvertTo-WpfStringList {
  param([object]$Value)

  $result = New-Object System.Collections.Generic.List[string]
  if ($null -eq $Value) { return @() }

  if ($Value -is [string]) {
    foreach ($entry in ($Value -split '[,;]')) {
      $normalized = [string]$entry
      if (-not [string]::IsNullOrWhiteSpace($normalized)) {
        [void]$result.Add($normalized.Trim())
      }
    }
    return @($result.ToArray())
  }

  if ($Value -is [System.Collections.IEnumerable]) {
    foreach ($entry in $Value) {
      $normalized = [string]$entry
      if (-not [string]::IsNullOrWhiteSpace($normalized)) {
        [void]$result.Add($normalized.Trim())
      }
    }
    return @($result.ToArray())
  }

  return @()
}

function Get-WpfRootsCollection {
  <# Returns the IList backing the listRoots control.
     Simplified to 3 clear paths: ViewModel, ItemsSource, Items fallback. #>
  param([Parameter(Mandatory)][hashtable]$Ctx)

  # Path 1: ViewModel (preferred - create if missing)
  $vm = $null
  if (Get-Command Get-WpfViewModel -ErrorAction SilentlyContinue) {
    $vm = Get-WpfViewModel -Ctx $Ctx
  }
  if (-not $vm -and (Get-Command New-WpfMainViewModel -ErrorAction SilentlyContinue) -and (Get-Command Set-WpfViewModel -ErrorAction SilentlyContinue)) {
    try {
      $vm = New-WpfMainViewModel
      Set-WpfViewModel -Ctx $Ctx -ViewModel $vm
      if ($Ctx.ContainsKey('Window') -and $Ctx['Window']) { $Ctx['Window'].DataContext = $vm }
      if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots'] -and $vm.Roots) { $Ctx['listRoots'].ItemsSource = $vm.Roots }
    } catch { $vm = $null }
  }
  if ($vm -and $vm.Roots -and ($vm.Roots -is [System.Collections.IList])) { return $vm.Roots }

  # Path 2: listRoots.ItemsSource as IList (or its SourceCollection)
  if (-not $Ctx.ContainsKey('listRoots') -or -not $Ctx['listRoots']) { return $null }
  $listRoots = $Ctx['listRoots']
  $itemsSource = if ($listRoots.PSObject.Properties['ItemsSource']) { $listRoots.ItemsSource } else { $null }
  if ($itemsSource -is [System.Collections.IList]) { return $itemsSource }
  if ($itemsSource -and $itemsSource.PSObject.Properties['SourceCollection'] -and ($itemsSource.SourceCollection -is [System.Collections.IList])) {
    return $itemsSource.SourceCollection
  }

  # Path 3: Fallback to Items collection
  if ($null -eq $itemsSource) { return $listRoots.Items }
  return $null
}

function Get-WpfAdvancedSelectionConfig {
  if (Get-Command Get-RomCleanupWpfSelectionConfig -ErrorAction SilentlyContinue) {
    return (Get-RomCleanupWpfSelectionConfig)
  }
  throw 'WpfSelectionConfig fehlt: Get-RomCleanupWpfSelectionConfig ist nicht verfügbar.'
}

function Get-WpfCheckedValues {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][hashtable]$ControlMap,
    [string[]]$DefaultValues = @(),
    [string]$FallbackTextControl = ''
  )

  $selected = New-Object System.Collections.Generic.List[string]
  foreach ($entry in $ControlMap.GetEnumerator()) {
    if ($null -eq $entry.Key) { continue }
    if ($Ctx.ContainsKey($entry.Key) -and $Ctx[$entry.Key] -and $Ctx[$entry.Key].PSObject.Properties['IsChecked']) {
      if ($Ctx[$entry.Key].IsChecked -eq $true) {
        [void]$selected.Add([string]$entry.Value)
      }
    }
  }

  if ($selected.Count -gt 0) {
    return @($selected.ToArray())
  }

  if (-not [string]::IsNullOrWhiteSpace($FallbackTextControl) -and $Ctx.ContainsKey($FallbackTextControl) -and $Ctx[$FallbackTextControl]) {
    $fallback = ConvertTo-WpfStringList -Value ([string]$Ctx[$FallbackTextControl].Text)
    if (@($fallback).Count -gt 0) {
      return @($fallback)
    }
  }

  return @($DefaultValues)
}

function Set-WpfCheckedValues {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][hashtable]$ControlMap,
    [string[]]$Values
  )

  $lookup = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @(ConvertTo-WpfStringList -Value $Values)) {
    [void]$lookup.Add($entry)
  }

  foreach ($entry in $ControlMap.GetEnumerator()) {
    if ($null -eq $entry.Key) { continue }
    if ($Ctx.ContainsKey($entry.Key) -and $Ctx[$entry.Key] -and $Ctx[$entry.Key].PSObject.Properties['IsChecked']) {
      $Ctx[$entry.Key].IsChecked = $lookup.Contains([string]$entry.Value)
    }
  }
}

function Get-WpfIsChecked {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][string]$Key
  )

  if (-not $Ctx.ContainsKey($Key)) { return $false }
  $ctrl = $Ctx[$Key]
  if ($null -eq $ctrl) { return $false }

  if ($ctrl -is [bool]) {
    return [bool]$ctrl
  }

  try {
    if ($ctrl.PSObject.Properties.Name -contains 'IsChecked') {
      return ($ctrl.IsChecked -eq $true)
    }
  } catch { }

  return $false
}

function Read-WpfPromptText {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Owner,
    [Parameter(Mandatory)][string]$Title,
    [Parameter(Mandatory)][string]$Message,
    [string]$DefaultValue = ''
  )

  $dialog = if (Get-Command New-WpfStyledDialog -ErrorAction SilentlyContinue) {
    New-WpfStyledDialog -Owner $Owner -Title $Title -ResizeMode ([System.Windows.ResizeMode]::NoResize) -SizeToContent ([System.Windows.SizeToContent]::WidthAndHeight)
  } else {
    $d = New-Object System.Windows.Window
    $d.Title = $Title
    $d.Owner = $Owner
    $d.WindowStartupLocation = [System.Windows.WindowStartupLocation]::CenterOwner
    $d.ResizeMode = [System.Windows.ResizeMode]::NoResize
    $d.SizeToContent = [System.Windows.SizeToContent]::WidthAndHeight
    $bgBrush = $null; $fgBrush = $null
    if ($Owner) {
      $bgBrush = $Owner.TryFindResource('BrushSurface')
      $fgBrush = $Owner.TryFindResource('BrushTextPrimary')
    }
    if (-not ($bgBrush -is [System.Windows.Media.SolidColorBrush])) { $bgBrush = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString('#1A1A3A')) }
    if (-not ($fgBrush -is [System.Windows.Media.SolidColorBrush])) { $fgBrush = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString('#E8E8F8')) }
    $d.Background = $bgBrush
    $d.Foreground = $fgBrush
    $d
  }
  $dialog.MinWidth = 460
  $dlgBg = $null
  if ($Owner) { $dlgBg = $Owner.TryFindResource('BrushSurface') }
  if (-not ($dlgBg -is [System.Windows.Media.SolidColorBrush])) { $dlgBg = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString('#1A1A3A')) }
  $dialog.Background = $dlgBg

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

function Invoke-WpfGameKeyPreview {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not ($Ctx.ContainsKey('txtGameKeyPreviewInput') -and $Ctx.ContainsKey('lblGameKeyPreviewOutput'))) {
    return $null
  }

  $inputValue = [string]$Ctx['txtGameKeyPreviewInput'].Text
  if ([string]::IsNullOrWhiteSpace($inputValue)) {
    $Ctx['lblGameKeyPreviewOutput'].Text = '–'
    return $null
  }

  $aliasEditionKeying = Get-WpfIsChecked -Ctx $Ctx -Key 'chkAliasKeying'
  $key = ConvertTo-GameKey -BaseName $inputValue -AliasEditionKeying:$aliasEditionKeying
  $Ctx['lblGameKeyPreviewOutput'].Text = [string]$key
  return [string]$key
}

function Set-WpfAdvancedStatus {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][string]$Text
  )

  if ($Ctx.ContainsKey('lblAdvancedStatus') -and $Ctx['lblAdvancedStatus']) {
    $Ctx['lblAdvancedStatus'].Text = $Text
  }
}

function Update-WpfDryRunBanner {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not $Ctx.ContainsKey('brdDryRunBanner') -or -not $Ctx['brdDryRunBanner']) { return }
  $isDryRun = Get-WpfIsChecked -Ctx $Ctx -Key 'chkReportDryRun'
  $Ctx['brdDryRunBanner'].Visibility = if ($isDryRun) { [System.Windows.Visibility]::Visible } else { [System.Windows.Visibility]::Collapsed }
  if ($isDryRun -and $Ctx.ContainsKey('lblDryRunBanner') -and $Ctx['lblDryRunBanner']) {
    # DUP-10: Text is canonical in MainWindow.xaml; only reset here if banner was cleared
    if ([string]::IsNullOrWhiteSpace($Ctx['lblDryRunBanner'].Text)) {
      $Ctx['lblDryRunBanner'].Text = 'DryRun aktiv: Es werden keine Dateien verschoben.'
    }
  }
}

function Set-WpfInlineValidationState {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][string]$ControlName,
    [Parameter(Mandatory)][bool]$IsValid
  )

  if (-not $Ctx.ContainsKey($ControlName) -or -not $Ctx[$ControlName]) { return }

  $control = $Ctx[$ControlName]
  try {
    if ($IsValid) {
      if ($control.PSObject.Properties.Name -contains 'BorderThickness') {
        $control.BorderThickness = New-Object System.Windows.Thickness(1)
      }
      if ($control.PSObject.Properties.Name -contains 'BorderBrush') {
        $control.BorderBrush = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushBorder' -Fallback '#2D2D5A'
      }
      return
    }

    if ($control.PSObject.Properties.Name -contains 'BorderThickness') {
      $control.BorderThickness = New-Object System.Windows.Thickness(2)
    }
    if ($control.PSObject.Properties.Name -contains 'BorderBrush') {
      $control.BorderBrush = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushDanger' -Fallback '#FF0044'
    }
  } catch { }
}

function Reset-WpfInlineValidationState {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  foreach ($name in @('listRoots','txtTrash')) {
    try { Set-WpfInlineValidationState -Ctx $Ctx -ControlName $name -IsValid $true } catch { }
  }
}

function Update-WpfRuntimeStatus {
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][TimeSpan]$Elapsed,
    [switch]$Reset
  )

  if (-not $Ctx.ContainsKey('lblStatusRuntime') -or -not $Ctx['lblStatusRuntime']) { return }
  # GUI-16: Dispatcher-Check — UI-Zugriff nur vom UI-Thread
  $wnd = $Ctx['Window']
  if ($wnd -and $wnd.Dispatcher -and -not $wnd.Dispatcher.CheckAccess()) {
    $rtSb = { Update-WpfRuntimeStatus -Ctx $Ctx -Elapsed $Elapsed -Reset:$Reset }.GetNewClosure()
    [void]$wnd.Dispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background, [System.Action]$rtSb)
    return
  }
  $Ctx['lblStatusRuntime'].Text = if ($Reset) {
    'Laufzeit: –'
  } else {
    ('Laufzeit: {0}' -f (Format-WpfDuration -Span $Elapsed))
  }
}

function Set-WpfLocale {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not $Ctx.ContainsKey('cmbLocale') -or -not $Ctx['cmbLocale']) { return }
  $locale = if ($Ctx['cmbLocale'].SelectedItem -and $Ctx['cmbLocale'].SelectedItem.Content) { [string]$Ctx['cmbLocale'].SelectedItem.Content } else { 'de' }
  [void](Set-AppStateValue -Key 'UILocale' -Value $locale)

  if (Get-Command Initialize-Localization -ErrorAction SilentlyContinue) {
    try { Initialize-Localization -Locale $locale } catch { }
  }

  [void](Set-WpfViewModelProperty -Ctx $Ctx -Name 'Locale' -Value $locale)

  $window = if ($Ctx.ContainsKey('Window')) { $Ctx['Window'] } else { $null }
  if ($window) {
    try {
      if (Get-Command Get-UIString -ErrorAction SilentlyContinue) {
        $window.Title = [string](Get-UIString 'App.Title')
      }
    } catch { }
  }

  try {
    if (Get-Command Get-UIString -ErrorAction SilentlyContinue) {
      if ($Ctx.ContainsKey('tabSort') -and $Ctx['tabSort']) { $Ctx['tabSort'].Header = [string](Get-UIString 'Tab.Start') }
      if ($Ctx.ContainsKey('tabConfig') -and $Ctx['tabConfig']) { $Ctx['tabConfig'].Header = [string](Get-UIString 'Tab.Settings') }
      if ($Ctx.ContainsKey('tabProgress') -and $Ctx['tabProgress']) { $Ctx['tabProgress'].Header = [string](Get-UIString 'Tab.Log') }
      if ($Ctx.ContainsKey('tabConfigBasis') -and $Ctx['tabConfigBasis']) { $Ctx['tabConfigBasis'].Header = [string](Get-UIString 'Tab.ConfigBasic') }
      if ($Ctx.ContainsKey('tabConfigAdvanced') -and $Ctx['tabConfigAdvanced']) { $Ctx['tabConfigAdvanced'].Header = [string](Get-UIString 'Tab.ConfigAdvanced') }
      if ($Ctx.ContainsKey('expCfgDatMap') -and $Ctx['expCfgDatMap']) { $Ctx['expCfgDatMap'].Header = [string](Get-UIString 'Exp.CfgDatMap') }

      $localizedTargets = @(
        # ── Buttons (Content) ─────────────────────────────────────────
        @{ Key = 'btnAddRoot';            Property = 'Content'; I18n = 'Start.AddRoot' },
        @{ Key = 'btnRemoveRoot';         Property = 'Content'; I18n = 'Start.RemoveRoot' },
        @{ Key = 'btnBrowseRoot';         Property = 'Content'; I18n = 'Start.BrowseRoot' },
        @{ Key = 'btnAutoFindTools';      Property = 'Content'; I18n = 'Tools.AutoFind' },
        @{ Key = 'btnProfileSave';        Property = 'Content'; I18n = 'Profile.Save' },
        @{ Key = 'btnProfileLoad';        Property = 'Content'; I18n = 'Profile.Load' },
        @{ Key = 'btnProfileDelete';      Property = 'Content'; I18n = 'Profile.Delete' },
        @{ Key = 'btnProfileImport';      Property = 'Content'; I18n = 'Profile.Import' },
        @{ Key = 'btnGameKeyPreview';     Property = 'Content'; I18n = 'GameKey.Preview' },
        @{ Key = 'btnExportUnified';      Property = 'Content'; I18n = 'Config.Export' },
        @{ Key = 'btnConfigImport';       Property = 'Content'; I18n = 'Config.Import' },
        @{ Key = 'btnQuickPreview';       Property = 'Content'; I18n = 'Advanced.QuickPreview' },
        @{ Key = 'btnCollectionDiff';     Property = 'Content'; I18n = 'Advanced.CollectionDiff' },
        @{ Key = 'btnHealthScore';        Property = 'Content'; I18n = 'Advanced.HealthScore' },
        @{ Key = 'btnDuplicateInspector'; Property = 'Content'; I18n = 'Advanced.DuplicateInspector' },
        @{ Key = 'btnDuplicateExport';    Property = 'Content'; I18n = 'Advanced.DuplicateExport' },
        @{ Key = 'btnPluginManager';      Property = 'Content'; I18n = 'Advanced.PluginManager' },
        @{ Key = 'btnAutoProfile';        Property = 'Content'; I18n = 'Advanced.AutoProfile' },
        @{ Key = 'btnWatchApply';         Property = 'Content'; I18n = 'Advanced.WatchApply' },
        @{ Key = 'btnApplyLocale';        Property = 'Content'; I18n = 'Advanced.ApplyLocale' },
        @{ Key = 'btnExportCsv';          Property = 'Content'; I18n = 'Advanced.CsvExport' },
        @{ Key = 'btnExportExcel';        Property = 'Content'; I18n = 'Advanced.ExcelExport' },
        @{ Key = 'btnRollbackQuick';      Property = 'Content'; I18n = 'Advanced.RollbackQuick' },
        @{ Key = 'btnRollbackUndo';       Property = 'Content'; I18n = 'Advanced.RollbackUndo' },
        @{ Key = 'btnRollbackRedo';       Property = 'Content'; I18n = 'Advanced.RollbackRedo' },
        @{ Key = 'btnConflictPolicy';     Property = 'Content'; I18n = 'Advanced.ConflictPolicy' },
        @{ Key = 'btnClearLog';           Property = 'Content'; I18n = 'Log.Clear' },
        @{ Key = 'btnExportLog';          Property = 'Content'; I18n = 'Log.Export' },
        @{ Key = 'btnRefreshReportPreview'; Property = 'Content'; I18n = 'Report.Refresh' },
        # ── CheckBoxes (Content) ──────────────────────────────────────
        @{ Key = 'chkDatUse';             Property = 'Content'; I18n = 'Dat.UseDat' },
        @{ Key = 'chkReportDryRun';       Property = 'Content'; I18n = 'Start.DryRunCheck' },
        @{ Key = 'chkConvert';            Property = 'Content'; I18n = 'Start.ConvertCheck' },
        @{ Key = 'chkExpertMode';         Property = 'Content'; I18n = 'Start.ExpertMode' },
        @{ Key = 'chkConfirmMove';        Property = 'Content'; I18n = 'Start.ConfirmMove' },
        @{ Key = 'chkSortConsole';        Property = 'Content'; I18n = 'Config.SortConsole' },
        @{ Key = 'chkJunkAggressive';     Property = 'Content'; I18n = 'Config.JunkAggressive' },
        @{ Key = 'chkAliasKeying';        Property = 'Content'; I18n = 'Config.AliasKeying' },
        @{ Key = 'chkSafetyMode';         Property = 'Content'; I18n = 'Config.SafetyMode' },
        @{ Key = 'chkJpOnlySelected';     Property = 'Content'; I18n = 'Config.JpOnlySelected' },
        @{ Key = 'chkDatFallback';        Property = 'Content'; I18n = 'Dat.Fallback' },
        @{ Key = 'chkCrcVerifyScan';      Property = 'Content'; I18n = 'Dat.CrcVerifyScan' },
        @{ Key = 'chkWatchMode';          Property = 'Content'; I18n = 'Advanced.WatchMode' },
        # ── Expanders (Header) ────────────────────────────────────────
        @{ Key = 'expMoveDanger';         Property = 'Header';  I18n = 'Exp.MoveDanger' },
        @{ Key = 'expCfgSort';            Property = 'Header';  I18n = 'Exp.CfgSort' },
        @{ Key = 'expCfgTools';           Property = 'Header';  I18n = 'Exp.CfgTools' },
        @{ Key = 'expToolsSetup';         Property = 'Header';  I18n = 'Exp.ToolsSetup' },
        @{ Key = 'expCfgDat';             Property = 'Header';  I18n = 'Exp.CfgDat' },
        @{ Key = 'expCfgProfiles';        Property = 'Header';  I18n = 'Exp.CfgProfiles' },
        @{ Key = 'expCfgGeneral';         Property = 'Header';  I18n = 'Exp.CfgGeneral' },
        @{ Key = 'expCfgAudit';           Property = 'Header';  I18n = 'Exp.CfgAudit' },
        @{ Key = 'expCfgAdvanced';        Property = 'Header';  I18n = 'Exp.CfgAdvanced' }
      )
      foreach ($target in $localizedTargets) {
        if (-not $Ctx.ContainsKey($target.Key) -or -not $Ctx[$target.Key]) { continue }
        $translated = [string](Get-UIString $target.I18n)
        if ([string]::IsNullOrWhiteSpace($translated) -or $translated -eq $target.I18n) { continue }
        try {
          if ($target.Property -eq 'Content') {
            $Ctx[$target.Key].Content = $translated
          } elseif ($target.Property -eq 'Header') {
            $Ctx[$target.Key].Header = $translated
          } elseif ($target.Property -eq 'Text') {
            $Ctx[$target.Key].Text = $translated
          }
        } catch { }
      }

      # ── StackPanel-Content buttons (nested TextBlock) ──────────────
      if ($Ctx.ContainsKey('btnRunGlobal') -and $Ctx['btnRunGlobal']) {
        $runLabel = [string](Get-UIString 'Start.RunButton')
        if ($Ctx['btnRunGlobal'].Content -is [System.Windows.Controls.StackPanel]) {
          $stack = [System.Windows.Controls.StackPanel]$Ctx['btnRunGlobal'].Content
          if ($stack.Children.Count -ge 2 -and $stack.Children[1] -is [System.Windows.Controls.TextBlock]) {
            $stack.Children[1].Text = $runLabel
          }
        } else {
          $Ctx['btnRunGlobal'].Content = $runLabel
        }
      }

      foreach ($spBtn in @(
        @{ Key = 'btnOpenReport';    I18n = 'Report.OpenReport' },
        @{ Key = 'btnOpenReportLog'; I18n = 'Report.OpenReportLog' }
      )) {
        if (-not $Ctx.ContainsKey($spBtn.Key) -or -not $Ctx[$spBtn.Key]) { continue }
        $label = [string](Get-UIString $spBtn.I18n)
        if ([string]::IsNullOrWhiteSpace($label) -or $label -eq $spBtn.I18n) { continue }
        try {
          if ($Ctx[$spBtn.Key].Content -is [System.Windows.Controls.StackPanel]) {
            $sp = [System.Windows.Controls.StackPanel]$Ctx[$spBtn.Key].Content
            if ($sp.Children.Count -ge 2 -and $sp.Children[1] -is [System.Windows.Controls.TextBlock]) {
              $sp.Children[1].Text = $label
            }
          } else {
            $Ctx[$spBtn.Key].Content = $label
          }
        } catch { }
      }
    }
  } catch { }

  # Only persist when settings have been fully loaded to avoid overwriting
  # saved tool paths with empty defaults during startup initialization.
  if ($Ctx.ContainsKey('_settingsInitialized') -and $Ctx['_settingsInitialized']) {
    Save-WpfToSettings -Ctx $Ctx
  }

  Set-WpfAdvancedStatus -Ctx $Ctx -Text ("Sprache gesetzt: {0}" -f $locale)
}

function Get-WpfMainSnapshot {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $vm = $null
  if (Get-Command Get-WpfViewModel -ErrorAction SilentlyContinue) {
    $vm = Get-WpfViewModel -Ctx $Ctx
  }

  $roots = New-Object System.Collections.Generic.List[string]
  $rootLookup = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
  $appendRoot = {
    param([object]$Candidate)
    $value = [string]$Candidate
    if ([string]::IsNullOrWhiteSpace($value)) { return }
    $normalized = $value.Trim()
    if ([string]::IsNullOrWhiteSpace($normalized)) { return }
    if ($rootLookup.Add($normalized)) {
      [void]$roots.Add($normalized)
    }
  }

  if ($vm -and $vm.PSObject.Properties['Roots']) {
    foreach ($root in @($vm.Roots)) {
      & $appendRoot $root
    }
  }

  if ($Ctx.ContainsKey('__rootsCollection') -and $Ctx['__rootsCollection']) {
    foreach ($root in @($Ctx['__rootsCollection'])) {
      & $appendRoot $root
    }
  }

  if ($Ctx.ContainsKey('__fallbackRoots') -and $Ctx['__fallbackRoots']) {
    foreach ($root in @($Ctx['__fallbackRoots'])) {
      & $appendRoot $root
    }
  }

  if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
    foreach ($item in @($Ctx['listRoots'].Items)) {
      $s = if ($item -is [string]) { [string]$item } elseif ($item -and $item.PSObject.Properties['Content']) { [string]$item.Content } else { [string]$item }
      & $appendRoot $s
    }
  }

  $getText = {
    param([string]$VmProperty, [string]$CtxKey)
    if ($vm -and $vm.PSObject.Properties[$VmProperty]) {
      return [string]$vm.$VmProperty
    }
    if ($Ctx.ContainsKey($CtxKey) -and $Ctx[$CtxKey] -and $Ctx[$CtxKey].PSObject.Properties['Text']) {
      return [string]$Ctx[$CtxKey].Text
    }
    return ''
  }

  $getBool = {
    param([string]$VmProperty, [string]$CtxKey)
    if ($vm -and $vm.PSObject.Properties[$VmProperty]) {
      return (ConvertTo-SafeBool $vm.$VmProperty)
    }
    return (Get-WpfIsChecked -Ctx $Ctx -Key $CtxKey)
  }

  $getCombo = {
    param([string]$VmProperty, [string]$CtxKey, [string]$Default = '')
    if ($vm -and $vm.PSObject.Properties[$VmProperty]) {
      $value = [string]$vm.$VmProperty
      if (-not [string]::IsNullOrWhiteSpace($value)) {
        return $value
      }
    }

    if ($Ctx.ContainsKey($CtxKey) -and $Ctx[$CtxKey] -and $Ctx[$CtxKey].SelectedItem -and $Ctx[$CtxKey].SelectedItem.Content) {
      return [string]$Ctx[$CtxKey].SelectedItem.Content
    }
    return [string]$Default
  }

  $selectionConfig = $null
  if (Get-Command Get-WpfAdvancedSelectionConfig -ErrorAction SilentlyContinue) {
    $selectionConfig = Get-WpfAdvancedSelectionConfig
  }

  $preferValues = @()
  $extensionValues = @()
  $consoleValues = @()
  if ($selectionConfig) {
    $preferValues = @(Get-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.PreferMap -DefaultValues $selectionConfig.PreferDefaults -FallbackTextControl 'txtPrefer')
    # Dateitypen und Konsolen: Checkbox-Listen (keine Auswahl = alle scannen)
    if ($selectionConfig.Contains('ExtensionsMap')) {
      $extensionValues = @(Get-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.ExtensionsMap -DefaultValues @())
    }
    if ($selectionConfig.Contains('ConsoleMap')) {
      $consoleValues = @(Get-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.ConsoleMap -DefaultValues @())
    }
  }

  $selectedDatHash = ''
  if (Get-Command Get-WpfDatHashType -ErrorAction SilentlyContinue) {
    $selectedDatHash = [string](Get-WpfDatHashType -Ctx $Ctx)
  }

  $jpKeepConsoles = @()
  $jpKeepRaw = (& $getText 'JpKeepConsoles' 'txtJpKeepConsoles')
  if (-not [string]::IsNullOrWhiteSpace([string]$jpKeepRaw)) {
    $jpKeepConsoles = @(ConvertTo-WpfStringList -Value $jpKeepRaw)
  }

  return [ordered]@{
    Roots       = $roots.ToArray()
    TrashRoot   = (& $getText 'TrashRoot' 'txtTrash')
    DatRoot     = (& $getText 'DatRoot' 'txtDatRoot')
    AuditRoot   = (& $getText 'AuditRoot' 'txtAuditRoot')
    Ps3DupesRoot= (& $getText 'Ps3DupesRoot' 'txtPs3Dupes')
    ToolChdman  = (& $getText 'ToolChdman' 'txtChdman')
    ToolDolphin = (& $getText 'ToolDolphin' 'txtDolphin')
    Tool7z      = (& $getText 'Tool7z' 'txt7z')
    ToolPsxtract= (& $getText 'ToolPsxtract' 'txtPsxtract')
    ToolCiso    = (& $getText 'ToolCiso' 'txtCiso')
    SortConsole = (& $getBool 'SortConsole' 'chkSortConsole')
    AliasKeying = (& $getBool 'AliasKeying' 'chkAliasKeying')
    UseDat      = (& $getBool 'UseDat' 'chkDatUse')
    DatFallback = (& $getBool 'DatFallback' 'chkDatFallback')
    DryRun      = (& $getBool 'DryRun' 'chkReportDryRun')
    ConvertChecked = (& $getBool 'ConvertEnabled' 'chkConvert')
    ConfirmMove = (& $getBool 'ConfirmMove' 'chkConfirmMove')
    AggressiveJunk = (& $getBool 'AggressiveJunk' 'chkJunkAggressive')
    CrcVerifyScan  = (& $getBool 'CrcVerifyScan' 'chkCrcVerifyScan')
    CrcVerifyDat   = (& $getBool 'CrcVerifyDat' 'chkCrcVerifyDat')
    SafetyStrict   = (& $getBool 'SafetyStrict' 'chkSafetyMode')
    SafetyPrompts  = (& $getBool 'SafetyPrompts' 'chkSafetyMode')
    JpOnlySelected = (& $getBool 'JpOnlySelected' 'chkJpOnlySelected')
    ProtectedPaths = (& $getText 'ProtectedPaths' 'txtSafetyScope')
    SafetySandbox  = (& $getText 'SafetySandbox' 'txtSafetyScope')
    JpKeepConsoles = $jpKeepConsoles
    LogLevel       = (& $getCombo 'LogLevel' 'cmbLogLevel' 'Info')
    Locale         = (& $getCombo 'Locale' 'cmbLocale' 'de')
    DatHashType    = $(if (-not [string]::IsNullOrWhiteSpace($selectedDatHash)) { $selectedDatHash } else { (& $getText 'DatHashType' 'cmbDatHash') })
    Prefer         = $preferValues
    Extensions     = $extensionValues
    ConsoleFilter  = $consoleValues
  }
}

function Get-WpfRunParameters {
  <#
  .SYNOPSIS
    Reads all UI control values and returns an operation-parameter hashtable
    compatible with Invoke-RegionDedupe / Start-BackgroundRunspace.
  #>
  param([Parameter(Mandatory)][hashtable]$Ctx)

  $main = Get-WpfMainSnapshot -Ctx $Ctx

  $selectionConfig = Get-WpfAdvancedSelectionConfig

  $prefer = @($main.Prefer)
  if ($prefer.Count -eq 0) {
    $prefer = @(Get-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.PreferMap -DefaultValues $selectionConfig.PreferDefaults -FallbackTextControl 'txtPrefer')
  }

  $extensions = @($main.Extensions)
  if ($extensions.Count -eq 0) { $extensions = $null }

  $consoleFilter = @($main.ConsoleFilter)
  if ($consoleFilter.Count -eq 0) { $consoleFilter = $null }

  $isDryRun = (ConvertTo-SafeBool $main.DryRun)
  $confirmMove = (ConvertTo-SafeBool $main.ConfirmMove)
  $mode = if ($isDryRun -or -not $confirmMove) { 'DryRun' } else { 'Move' }

  # DAT mappings from DataGrid
  $datMap = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
  foreach ($entry in @(Get-WpfDatMapEntries -Ctx $Ctx)) {
    try {
      if ($entry -and $entry.PSObject.Properties['console'] -and $entry.PSObject.Properties['path']) {
        $consKey = [string]$entry.PSObject.Properties['console'].Value
        $datFile = [string]$entry.PSObject.Properties['path'].Value
        if (-not [string]::IsNullOrWhiteSpace($consKey) -and -not [string]::IsNullOrWhiteSpace($datFile)) {
          $datMap[$consKey] = $datFile
        }
      }
    } catch { }
  }

  $selectedDatHash = [string]$main.DatHashType
  $jpKeepConsoles = @($main.JpKeepConsoles)

  $params = @{
    Roots            = @($main.Roots)
    TrashRoot        = [string]$main.TrashRoot
    Mode             = $mode
    Prefer           = $prefer
    Extensions       = $extensions
    ConsoleFilter    = $consoleFilter
    ConvertChecked   = (ConvertTo-SafeBool $main.ConvertChecked)
    SortConsole      = (ConvertTo-SafeBool $main.SortConsole)
    AggressiveJunk   = (ConvertTo-SafeBool $main.AggressiveJunk)
    AliasKeying      = (ConvertTo-SafeBool $main.AliasKeying)
    UseDat           = (ConvertTo-SafeBool $main.UseDat)
    DatRoot          = [string]$main.DatRoot
    DatHashType      = $selectedDatHash
    DatMap           = $datMap
    DatFallback      = (ConvertTo-SafeBool $main.DatFallback)
    CrcVerifyScan    = (ConvertTo-SafeBool $main.CrcVerifyScan)
    CrcVerifyDat     = (ConvertTo-SafeBool $main.CrcVerifyDat)
    CrcVerify        = ((ConvertTo-SafeBool $main.CrcVerifyScan) -or (ConvertTo-SafeBool $main.CrcVerifyDat))
    ProtectedPaths   = [string]$main.ProtectedPaths
    SafetySandbox    = [string]$main.SafetySandbox
    SafetyStrict     = (ConvertTo-SafeBool $main.SafetyStrict)
    AuditRoot        = [string]$main.AuditRoot
    Ps3DupesRoot     = [string]$main.Ps3DupesRoot
    ToolChdman       = [string]$main.ToolChdman
    ToolDolphin      = [string]$main.ToolDolphin
    Tool7z           = [string]$main.Tool7z
    ToolPsxtract     = [string]$main.ToolPsxtract
    ToolCiso         = [string]$main.ToolCiso
    JpOnlyForSelectedConsoles = (ConvertTo-SafeBool $main.JpOnlySelected)
    JpKeepConsoles   = $jpKeepConsoles
    SafetyPrompts    = (ConvertTo-SafeBool $main.SafetyPrompts)
  }

  # Einfach-Modus: override parameters from simplified controls
  $isEinfach = $false
  if ($Ctx.ContainsKey('rbModeEinfach') -and $Ctx['rbModeEinfach']) {
    $isEinfach = [bool]$Ctx['rbModeEinfach'].IsChecked
  }
  if ($isEinfach) {
    $regionChoice = ''
    if ($Ctx.ContainsKey('cmbEinfachRegion') -and $Ctx['cmbEinfachRegion'] -and $Ctx['cmbEinfachRegion'].SelectedItem) {
      $regionChoice = [string]$Ctx['cmbEinfachRegion'].SelectedItem.Content
    }
    $params.Prefer = switch -Wildcard ($regionChoice) {
      '*EU*'   { @('EU','US','DE','FR','IT','ES','WORLD') }
      '*US*'   { @('US','EU','WORLD') }
      '*JP*'   { @('JP','ASIA','WORLD') }
      '*Alle*' { @('EU','US','WORLD','JP','ASIA') }
      default  { @('EU','US','WORLD') }
    }
    $einfachSort = if ($Ctx.ContainsKey('chkEinfachSort') -and $Ctx['chkEinfachSort']) { [bool]$Ctx['chkEinfachSort'].IsChecked } else { $true }
    $einfachJunk = if ($Ctx.ContainsKey('chkEinfachJunk') -and $Ctx['chkEinfachJunk']) { [bool]$Ctx['chkEinfachJunk'].IsChecked } else { $true }
    $params.SortConsole = $einfachSort
    $params.AggressiveJunk = $einfachJunk
    # Einfach-Modus always uses DryRun for safety (unless Move was explicitly requested)
    if ($Ctx.ContainsKey('_forceMoveMode') -and $Ctx['_forceMoveMode']) {
      $params.Mode = 'Move'
    } else {
      $params.Mode = 'DryRun'
    }
    # Auto-derive TrashRoot from first root's parent if not already set
    if ([string]::IsNullOrWhiteSpace($params.TrashRoot) -and $params.Roots.Count -gt 0) {
      $firstRoot = [string]$params.Roots[0]
      if (-not [string]::IsNullOrWhiteSpace($firstRoot)) {
        $parentDir = Split-Path -Parent $firstRoot
        if (-not [string]::IsNullOrWhiteSpace($parentDir)) {
          $autoTrash = Join-Path $parentDir '_ROM_Cleanup_Trash'
          $params.TrashRoot = $autoTrash
          # Also update the hidden txtTrash for consistency
          if ($Ctx.ContainsKey('txtTrash') -and $Ctx['txtTrash']) {
            $Ctx['txtTrash'].Text = $autoTrash
          }
        }
      }
    }
  }

  return $params
}

Set-Alias -Name Collect-WpfRunParameters -Value Get-WpfRunParameters -Scope Script

function Update-WpfProfileCombo {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not (Get-Command Get-ConfigProfiles -ErrorAction SilentlyContinue)) { return }
  if (-not $Ctx.ContainsKey('cmbConfigProfile')) { return }

  $combo = $Ctx['cmbConfigProfile']
  $selectedBefore = [string]$combo.Text
  $combo.Items.Clear()

  try {
    $profiles = Get-ConfigProfiles
    foreach ($name in ($profiles.Keys | Sort-Object)) {
      [void]$combo.Items.Add([string]$name)
    }
  } catch { } # intentionally keep profile refresh best-effort

  if (-not [string]::IsNullOrWhiteSpace($selectedBefore) -and $combo.Items.Contains($selectedBefore)) {
    $combo.SelectedItem = $selectedBefore
  }
}

function Update-WpfToolSetupVisibility {
  param([Parameter(Mandatory)][hashtable]$Ctx)

  if (-not $Ctx.ContainsKey('expToolsSetup') -or -not $Ctx['expToolsSetup']) { return }

  $isExpert = $false
  if ($Ctx.ContainsKey('rbModeExperte') -and $Ctx['rbModeExperte']) {
    $isExpert = ([bool]$Ctx['rbModeExperte'].IsChecked)
  } elseif ($Ctx.ContainsKey('chkExpertMode') -and $Ctx['chkExpertMode']) {
    $isExpert = ([bool]$Ctx['chkExpertMode'].IsChecked)
  }
  if ($isExpert) {
    $Ctx['expToolsSetup'].IsExpanded = $true
    if ($Ctx.ContainsKey('lblToolsSetupHint') -and $Ctx['lblToolsSetupHint']) {
      $Ctx['lblToolsSetupHint'].Text = 'Expert-Mode: Tool-Setup dauerhaft sichtbar.'
    }
    return
  }

  $toolKeys = @('txtChdman','txtDolphin','txt7z','txtPsxtract','txtCiso')
  $allDetected = $true
  foreach ($toolKey in $toolKeys) {
    if (-not $Ctx.ContainsKey($toolKey) -or -not $Ctx[$toolKey]) {
      $allDetected = $false
      break
    }
    $raw = [string]$Ctx[$toolKey].Text
    if ([string]::IsNullOrWhiteSpace($raw) -or -not (Test-Path -LiteralPath $raw -PathType Leaf)) {
      $allDetected = $false
      break
    }
  }

  $Ctx['expToolsSetup'].IsExpanded = (-not $allDetected)
  if ($Ctx.ContainsKey('lblToolsSetupHint') -and $Ctx['lblToolsSetupHint']) {
    $Ctx['lblToolsSetupHint'].Text = if ($allDetected) {
      'Alle Tools erkannt – Bereich standardmäßig eingeklappt.'
    } else {
      'Nicht alle Tools gefunden – Bereich für Setup geöffnet.'
    }
  }
}

function Set-WpfThemePalette {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [bool]$Light = $false
  )

  if (Get-Command Set-WpfThemeResourceDictionary -ErrorAction SilentlyContinue) {
    Set-WpfThemeResourceDictionary -Window $Window -Light:$Light
  }

  if ($Ctx.ContainsKey('btnThemeToggle')) {
    $Ctx['btnThemeToggle'].Content = if ($Light) { '☀ Hell' } else { '☾ Dunkel' }
  }

  # ── Refresh all programmatically-set colors after theme switch ──────────
  # Controls whose Foreground/Fill was set via Resolve-WpfThemeBrush hold
  # a frozen SolidColorBrush instance that does not update when the
  # ResourceDictionary changes.  Re-run the status functions so they
  # pick up the new theme's brush values.
  try { Update-WpfStatusBar -Ctx $Ctx } catch { }
  if (Get-Command Update-WpfStepIndicator -ErrorAction SilentlyContinue) {
    try { Update-WpfStepIndicator -Ctx $Ctx } catch { }
  }
  try { Update-WpfDryRunBanner -Ctx $Ctx } catch { }
  try { Reset-WpfInlineValidationState -Ctx $Ctx } catch { }
  # Re-color tool status labels (✓ OK / ✗ Nicht gefunden)
  try { Update-WpfToolStatusLabels -Ctx $Ctx } catch { }
  # Re-color dashboard result labels based on current values
  try { Update-WpfDashboardColors -Ctx $Ctx } catch { }
}

function Update-WpfToolStatusLabels {
  <#
  .SYNOPSIS
    Re-applies theme-aware colors to the tool status labels.
    Called after theme switch to pick up new brush values.
  #>
  param([Parameter(Mandatory)][hashtable]$Ctx)

  foreach ($lblKey in @('lblChdmanStatus','lblDolphinStatus','lbl7zStatus','lblPsxtractStatus','lblCisoStatus')) {
    if (-not $Ctx.ContainsKey($lblKey) -or -not $Ctx[$lblKey]) { continue }
    $lblCtrl = $Ctx[$lblKey]
    $txt = [string]$lblCtrl.Text
    $brushKey = if ($txt -like '*OK*') { 'BrushSuccess' }
                elseif ($txt -like '*Nicht*' -or $txt -like '*Fehler*') { 'BrushDanger' }
                else { 'BrushTextMuted' }
    $lblCtrl.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $brushKey
  }
}

function Update-WpfDashboardColors {
  <#
  .SYNOPSIS
    Re-applies theme-aware colors to dashboard result labels.
    Called after theme switch when run results are already displayed.
  #>
  param([Parameter(Mandatory)][hashtable]$Ctx)

  foreach ($entry in @(
    @{ Key = 'lblDashWinners'; CondBrush = 'BrushSuccess' }
    @{ Key = 'lblDashDupes';   CondBrush = 'BrushWarning' }
    @{ Key = 'lblDashJunk';    CondBrush = 'BrushDanger'  }
  )) {
    if (-not $Ctx.ContainsKey($entry.Key) -or -not $Ctx[$entry.Key]) { continue }
    $ctrl = $Ctx[$entry.Key]
    $val = 0
    [void][int]::TryParse([string]$ctrl.Text, [ref]$val)
    $brushKey = if ($val -gt 0) { $entry.CondBrush } else { 'BrushTextMuted' }
    $ctrl.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $brushKey
  }
  # Health score
  if ($Ctx.ContainsKey('lblHealthScore') -and $Ctx['lblHealthScore'] -and
      (Get-Command Update-WpfHealthScore -ErrorAction SilentlyContinue)) {
    # Re-trigger health score to recalculate color from current theme
    $w = 0; $d = 0; $j = 0
    if ($Ctx.ContainsKey('lblDashWinners') -and $Ctx['lblDashWinners']) { [void][int]::TryParse([string]$Ctx['lblDashWinners'].Text, [ref]$w) }
    if ($Ctx.ContainsKey('lblDashDupes')   -and $Ctx['lblDashDupes'])   { [void][int]::TryParse([string]$Ctx['lblDashDupes'].Text,   [ref]$d) }
    if ($Ctx.ContainsKey('lblDashJunk')    -and $Ctx['lblDashJunk'])    { [void][int]::TryParse([string]$Ctx['lblDashJunk'].Text,    [ref]$j) }
    try { Update-WpfHealthScore -Ctx $Ctx -Winners $w -Dupes $d -Junk $j } catch { }
  }
}

function Invoke-WpfQuickOnboarding {
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][scriptblock]$BrowseFolder
  )

  $skipOnboardingByEnv = $false
  try {
    $skipFlagRaw = [string]$env:ROM_CLEANUP_SKIP_ONBOARDING
    if (-not [string]::IsNullOrWhiteSpace($skipFlagRaw)) {
      $skipOnboardingByEnv = @('1','true','yes','on') -contains $skipFlagRaw.Trim().ToLowerInvariant()
    }
  } catch { } # intentionally ignore malformed env values

  $skipOnboardingByState = $false
  try {
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      $skipOnboardingByState = [bool](Get-AppStateValue -Key 'SkipOnboardingWizard' -Default $false)
    }
  } catch { } # intentionally ignore unavailable/invalid persisted state

  $isAutomatedTestMode = $false
  try {
    if (Get-Command Test-RomCleanupAutomatedTestMode -ErrorAction SilentlyContinue) {
      $isAutomatedTestMode = [bool](Test-RomCleanupAutomatedTestMode)
    }
  } catch { } # intentionally ignore test-mode probe failures

  if ($skipOnboardingByEnv -or $skipOnboardingByState -or $isAutomatedTestMode) { return }

  $vm = $null
  if (Get-Command Get-WpfViewModel -ErrorAction SilentlyContinue) {
    $vm = Get-WpfViewModel -Ctx $Ctx
  }

  $rootList = @()
  if ($vm -and $vm.Roots) {
    $rootList = @($vm.Roots)
  } else {
    $rootList = @($Ctx['listRoots'].Items)
  }

  $hasRoots = $rootList.Count -gt 0
  $hasTrash = -not [string]::IsNullOrWhiteSpace([string]$Ctx['txtTrash'].Text)
  if ($hasRoots -and $hasTrash) { return }

  $startWizard = [System.Windows.MessageBox]::Show(
    "Willkommen bei ROM Cleanup.`n`nSollen wir die Einrichtung in 3 Schritten durchführen?`n`n1) ROM-Ordner wählen`n2) Papierkorb wählen`n3) Sortierung starten",
    'ROM Cleanup – Schnellstart',
    [System.Windows.MessageBoxButton]::YesNo,
    [System.Windows.MessageBoxImage]::Question
  )

  if ($startWizard -ne [System.Windows.MessageBoxResult]::Yes) { return }

  if (-not $hasRoots) {
    $rootPath = & $BrowseFolder 'Schritt 1/3: ROM-Hauptverzeichnis auswählen' ''
    if (-not [string]::IsNullOrWhiteSpace([string]$rootPath)) {
      $normalizedRootPath = [string]$rootPath
      $existing = $false
      if ($vm -and $vm.Roots) {
        foreach ($item in $vm.Roots) {
          if ([string]$item -ieq $normalizedRootPath) {
            $existing = $true
            break
          }
        }
        if (-not $existing) {
          $vm.Roots.Add($normalizedRootPath)
        }
      } else {
        $rootsCollection = Get-WpfRootsCollection -Ctx $Ctx
        if ($rootsCollection -and ($rootsCollection -is [System.Collections.IList])) {
          $already = $false
          foreach ($item in @($rootsCollection)) {
            if ([string]$item -ieq $normalizedRootPath) { $already = $true; break }
          }
          if (-not $already) { [void]$rootsCollection.Add($normalizedRootPath) }
        }
      }
    }
  }

  if ([string]::IsNullOrWhiteSpace([string]$Ctx['txtTrash'].Text)) {
    $defaultTrash = $null
    if ($rootList.Count -gt 0) {
      $firstRoot = [string]$rootList[0]
      if (-not [string]::IsNullOrWhiteSpace($firstRoot)) {
        $defaultTrash = Join-Path -Path $firstRoot -ChildPath '_trash'
      }
    }

    $trashPath = & $BrowseFolder 'Schritt 2/3: Papierkorb-Verzeichnis auswählen' $defaultTrash
    if (-not [string]::IsNullOrWhiteSpace([string]$trashPath)) {
      if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'TrashRoot' -Value ([string]$trashPath))) {
        $Ctx['txtTrash'].Text = $trashPath
      }
    } elseif (-not [string]::IsNullOrWhiteSpace([string]$defaultTrash)) {
      if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'TrashRoot' -Value ([string]$defaultTrash))) {
        $Ctx['txtTrash'].Text = $defaultTrash
      }
      Add-WpfLogLine -Ctx $Ctx -Line ("Onboarding: Standard-Papierkorb gesetzt: {0}" -f $defaultTrash) -Level 'INFO'
    }
  }

  if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'DryRun' -Value $true)) {
    $Ctx['chkReportDryRun'].IsChecked = $true
  }
  if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'SortConsole' -Value $true)) {
    $Ctx['chkSortConsole'].IsChecked = $true
  }
  if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'AliasKeying' -Value $true)) {
    $Ctx['chkAliasKeying'].IsChecked = $true
  }

  Update-WpfStatusBar -Ctx $Ctx
  Add-WpfLogLine -Ctx $Ctx -Line 'Schnellstart abgeschlossen. Schritt 3/3: Mit "Sortierung starten" beginnen.' -Level 'INFO'
}

function Update-WpfStepIndicator {
  <#
  .SYNOPSIS
    Updates the visual step indicator dots and labels based on current state.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [ValidateSet('idle','running','complete')]
    [string]$RunState = 'idle'
  )
  try {
    # Step 1: Roots
    $rootCount = 0
    if ($Ctx.ContainsKey('__rootsCollection') -and $Ctx['__rootsCollection']) {
      $rootCount = $Ctx['__rootsCollection'].Count
    } elseif ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
      $rootCount = $Ctx['listRoots'].Items.Count
    }
    $step1Done = $rootCount -gt 0
    if ($Ctx.ContainsKey('stepDot1') -and $Ctx['stepDot1']) {
      $Ctx['stepDot1'].Fill = if ($step1Done) {
        $Ctx['stepDot1'].TryFindResource('BrushSuccess')
      } else { [System.Windows.Media.Brushes]::Transparent }
      $Ctx['stepDot1'].Stroke = if ($step1Done) {
        $Ctx['stepDot1'].TryFindResource('BrushSuccess')
      } else { $Ctx['stepDot1'].TryFindResource('BrushBorder') }
    }
    if ($Ctx.ContainsKey('stepLabel1') -and $Ctx['stepLabel1']) {
      $Ctx['stepLabel1'].Text = if ($step1Done) { "$rootCount Ordner" } else { 'Keine Ordner' }
    }

    # Step 2: Options (always ready once step 1 is done)
    if ($Ctx.ContainsKey('stepDot2') -and $Ctx['stepDot2']) {
      $Ctx['stepDot2'].Fill = if ($step1Done) {
        $Ctx['stepDot2'].TryFindResource('BrushSuccess')
      } else { [System.Windows.Media.Brushes]::Transparent }
      $Ctx['stepDot2'].Stroke = if ($step1Done) {
        $Ctx['stepDot2'].TryFindResource('BrushSuccess')
      } else { $Ctx['stepDot2'].TryFindResource('BrushBorder') }
    }
    if ($Ctx.ContainsKey('stepLabel2') -and $Ctx['stepLabel2']) {
      $Ctx['stepLabel2'].Text = if ($step1Done) { 'Bereit' } else { 'Warte auf Ordner' }
    }

    # Step 3: Run state
    if ($Ctx.ContainsKey('stepDot3') -and $Ctx['stepDot3']) {
      switch ($RunState) {
        'running' {
          $Ctx['stepDot3'].Fill = $Ctx['stepDot3'].TryFindResource('BrushWarning')
          $Ctx['stepDot3'].Stroke = $Ctx['stepDot3'].TryFindResource('BrushWarning')
        }
        'complete' {
          $Ctx['stepDot3'].Fill = $Ctx['stepDot3'].TryFindResource('BrushSuccess')
          $Ctx['stepDot3'].Stroke = $Ctx['stepDot3'].TryFindResource('BrushSuccess')
        }
        default {
          $Ctx['stepDot3'].Fill = [System.Windows.Media.Brushes]::Transparent
          $Ctx['stepDot3'].Stroke = $Ctx['stepDot3'].TryFindResource('BrushBorder')
        }
      }
    }
    if ($Ctx.ContainsKey('stepLabel3') -and $Ctx['stepLabel3']) {
      switch ($RunState) {
        'running'  { $Ctx['stepLabel3'].Text = 'Läuft...' }
        'complete' { $Ctx['stepLabel3'].Text = 'Fertig!' }
        default    { $Ctx['stepLabel3'].Text = 'F5 drücken' }
      }
    }
  } catch {
    Write-Verbose ('[WPF] Update-WpfStepIndicator failed: {0}' -f $_.Exception.Message)
  }
}

function Register-WpfEventHandlers {
  <#
  .SYNOPSIS
    Registers all WPF event handlers for the main window.
  .PARAMETER Window
    The WPF Window object.
  .PARAMETER Ctx
    The named-element hashtable from Get-WpfNamedElements.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  $Ctx['Window'] = $Window
  # Mark settings as not-yet-initialized to prevent premature saves that
  # would overwrite persisted tool paths with empty defaults.
  $Ctx['_settingsInitialized'] = $false

  $vmBootstrap = $null
  if (Get-Command Get-WpfViewModel -ErrorAction SilentlyContinue) {
    $vmBootstrap = Get-WpfViewModel -Ctx $Ctx
  }
  if (-not $vmBootstrap -and (Get-Command New-WpfMainViewModel -ErrorAction SilentlyContinue) -and (Get-Command Set-WpfViewModel -ErrorAction SilentlyContinue)) {
    try {
      $vmBootstrap = New-WpfMainViewModel
      Set-WpfViewModel -Ctx $Ctx -ViewModel $vmBootstrap
      $Window.DataContext = $vmBootstrap
    } catch {
      $vmBootstrap = $null
    }
  }

  if ($vmBootstrap -and $Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
    try {
      $Ctx['listRoots'].ItemsSource = $vmBootstrap.Roots
    } catch { }
  }

  if (Get-Command Connect-WpfMainViewModelBindings -ErrorAction SilentlyContinue) {
    [void](Connect-WpfMainViewModelBindings -Window $Window -Ctx $Ctx)
  }

  # Resolve roots collection once and cache it for all root-management closures
  if (Get-Command Initialize-WpfRootsCollection -ErrorAction SilentlyContinue) {
    [void](Initialize-WpfRootsCollection -Ctx $Ctx)
  } else {
    $fallbackRootsCollection = New-Object 'System.Collections.ObjectModel.ObservableCollection[string]'
    $Ctx['__rootsCollection'] = $fallbackRootsCollection
    if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
      try { $Ctx['listRoots'].ItemsSource = $fallbackRootsCollection } catch { }
    }
  }

  # ── Helper: Open file/folder dialog ──────────────────────────────────────
  $browseFolder = {
    param([string]$Title, [string]$Initial)
    try {
      # WinForms FolderBrowserDialog – works reliably inside WPF ShowDialog loop
      Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
      $dlg = New-Object System.Windows.Forms.FolderBrowserDialog
      $dlg.Description = $Title
      if ($dlg.PSObject.Properties.Name -contains 'UseDescriptionForTitle') {
        $dlg.UseDescriptionForTitle = $true
      }
      if (-not [string]::IsNullOrWhiteSpace($Initial) -and (Test-Path -LiteralPath $Initial -PathType Container)) {
        $dlg.SelectedPath = $Initial
      }
      # Get WPF window HWND for proper parenting
      $ownerHandle = [System.IntPtr]::Zero
      try {
        $helper = [System.Windows.Interop.WindowInteropHelper]::new($Window)
        $ownerHandle = $helper.Handle
      } catch { }
      $ownerWin32 = $null
      if ($ownerHandle -ne [System.IntPtr]::Zero) {
        try {
          Add-Type -AssemblyName System.Windows.Forms -ErrorAction SilentlyContinue
          $ownerWin32 = [System.Windows.Forms.NativeWindow]::new()
          $ownerWin32.AssignHandle($ownerHandle)
        } catch { $ownerWin32 = $null }
      }
      $result = if ($ownerWin32) {
        $dlg.ShowDialog($ownerWin32)
      } else {
        $dlg.ShowDialog()
      }
      if ($ownerWin32) { try { $ownerWin32.ReleaseHandle() } catch { } }
      if ($result -eq [System.Windows.Forms.DialogResult]::OK -and -not [string]::IsNullOrWhiteSpace($dlg.SelectedPath)) {
        return [string]$dlg.SelectedPath
      }
    } catch { } # intentionally keep folder browse as non-fatal optional UX helper
    return $null
  }

  $browseFile = {
    param([string]$Title, [string]$Filter)
    return (Show-WpfOpenFileDialog -Title $Title -Filter $Filter -Owner $Window)
  }

  $saveFile = {
    param([string]$Title, [string]$Filter, [string]$DefaultName)
    try {
      $diag = New-Object Microsoft.Win32.SaveFileDialog
      $diag.Title = $Title
      $diag.Filter = if ($Filter) { $Filter } else { 'All Files (*.*)|*.*' }
      if (-not [string]::IsNullOrWhiteSpace($DefaultName)) {
        $diag.FileName = $DefaultName
      }
      $dialogResult = $diag.ShowDialog($Window)
      if ($dialogResult -eq $true) { return $diag.FileName }
    } catch { }
    return $null
  }

  # ── Status bar auto-refresh when roots/tool paths change ─────────────────
  $persistSettingsNow = {
    Save-WpfToSettings -Ctx $Ctx
  }.GetNewClosure()

  $refreshStatus = {
    try {
      Update-WpfStatusBar -Ctx $Ctx
      Update-WpfToolSetupVisibility -Ctx $Ctx
    } catch {
      Write-Verbose ('[WPF] refreshStatus failed: {0}' -f $_.Exception.Message)
    }
  }.GetNewClosure()

  # ── Slice 4: DAT Mapping (delegation) ────────────────────────────────────
  if (Get-Command Register-WpfDatMappingHandlers -ErrorAction SilentlyContinue) {
    Register-WpfDatMappingHandlers -Ctx $Ctx -BrowseFolder $browseFolder -PersistSettingsNow $persistSettingsNow -RefreshStatus $refreshStatus
  }

  $updateExpertModeUi = {
    try {
    $isExpert = $false
    # Support new radio button toggle (primary) and old checkbox (fallback)
    if ($Ctx.ContainsKey('rbModeExperte') -and $Ctx['rbModeExperte']) {
      $isExpert = [bool]$Ctx['rbModeExperte'].IsChecked
    } elseif ($Ctx.ContainsKey('chkExpertMode') -and $Ctx['chkExpertMode']) {
      $isExpert = [bool]$Ctx['chkExpertMode'].IsChecked
    }

    # Sync hidden chkExpertMode for backward compatibility
    if ($Ctx.ContainsKey('chkExpertMode') -and $Ctx['chkExpertMode']) {
      $Ctx['chkExpertMode'].IsChecked = $isExpert
    }

    # Einfach-Modus panel: visible when NOT expert
    if ($Ctx.ContainsKey('pnlEinfachModus') -and $Ctx['pnlEinfachModus']) {
      $Ctx['pnlEinfachModus'].Visibility = if ($isExpert) {
        [System.Windows.Visibility]::Collapsed
      } else {
        [System.Windows.Visibility]::Visible
      }
    }

    # Experte-Details panel: visible when expert
    if ($Ctx.ContainsKey('pnlExperteDetails') -and $Ctx['pnlExperteDetails']) {
      $Ctx['pnlExperteDetails'].Visibility = if ($isExpert) {
        [System.Windows.Visibility]::Visible
      } else {
        [System.Windows.Visibility]::Collapsed
      }
    }

    if ($Ctx.ContainsKey('tabConfig') -and $Ctx['tabConfig']) {
      $Ctx['tabConfig'].Visibility = [System.Windows.Visibility]::Visible
    }

    # Toggle "Erweitert" sub-tab visibility based on expert mode
    if ($Ctx.ContainsKey('tabConfigAdvanced') -and $Ctx['tabConfigAdvanced']) {
      $Ctx['tabConfigAdvanced'].Visibility = if ($isExpert) { [System.Windows.Visibility]::Visible } else { [System.Windows.Visibility]::Collapsed }
    }

    if ($Ctx.ContainsKey('expMoveDanger') -and $Ctx['expMoveDanger']) {
      $Ctx['expMoveDanger'].Visibility = if ($isExpert) { [System.Windows.Visibility]::Visible } else { [System.Windows.Visibility]::Collapsed }
      if (-not $isExpert) {
        $Ctx['expMoveDanger'].IsExpanded = $false
      }
    }

    foreach ($expanderName in @('expCfgSort','expCfgTools','expCfgDat','expCfgDatMap','expCfgProfiles','expCfgGeneral')) {
      if (-not $Ctx.ContainsKey($expanderName) -or -not $Ctx[$expanderName]) { continue }
      $Ctx[$expanderName].IsExpanded = $isExpert
    }

    if ($Ctx.ContainsKey('expToolsSetup') -and $Ctx['expToolsSetup']) {
      $Ctx['expToolsSetup'].IsExpanded = $isExpert
    }
    } catch {
      Write-Verbose ('[WPF] updateExpertModeUi failed: {0}' -f $_.Exception.Message)
    }
  }.GetNewClosure()

  $resolveLatestHtmlReport = {
    # Prefer the known report path from the last completed run
    if ($Ctx.ContainsKey('_lastReportHtmlPath') -and $Ctx['_lastReportHtmlPath'] -and (Test-Path -LiteralPath $Ctx['_lastReportHtmlPath'])) {
      return Get-Item -LiteralPath $Ctx['_lastReportHtmlPath']
    }
    $root = if ($script:_RomCleanupModuleRoot) {
      Split-Path -Parent (Split-Path -Parent $script:_RomCleanupModuleRoot)
    } elseif ($PSScriptRoot) {
      Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
    } else {
      (Get-Location).Path
    }
    $reportsDir = Join-Path $root 'reports'
    Get-ChildItem -LiteralPath $reportsDir -Filter '*.html' -ErrorAction SilentlyContinue |
      Sort-Object LastWriteTime -Descending |
      Select-Object -First 1
  }.GetNewClosure()

  $refreshReportPreview = {
    if (-not ($Ctx.ContainsKey('webReportPreview') -and $Ctx['webReportPreview'])) { return }
    try {
      $latest = & $resolveLatestHtmlReport
      if ($latest) {
        $Ctx['webReportPreview'].Navigate([Uri]$latest.FullName)
      } else {
        $bgHex  = (Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSurface'   -Fallback '#1E1E2E')
        $fgHex  = (Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushTextPrimary' -Fallback '#E0E0E0')
        $Ctx['webReportPreview'].NavigateToString("<html><body style=`"font-family:Segoe UI;background:$bgHex;color:$fgHex;padding:16px;`">Kein Bericht vorhanden. Starte einen Lauf, um eine Vorschau zu sehen.</body></html>")
      }
    } catch {
      Add-WpfLogLine -Ctx $Ctx -Line ("Report-Preview fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'WARN'
    }
  }.GetNewClosure()

  # Store refresh closure in Ctx so Start-WpfOperationAsync can call it after run
  $Ctx['_refreshReportPreview'] = $refreshReportPreview

  if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
    $Ctx['listRoots'].add_SelectionChanged({
      try {
        if (Get-Command Sync-WpfViewModelRootsFromControl -ErrorAction SilentlyContinue) {
          Sync-WpfViewModelRootsFromControl -Ctx $Ctx
        }
        & $refreshStatus
      } catch {
        Write-Verbose ('[WPF] listRoots.SelectionChanged failed: {0}' -f $_.Exception.Message)
      }
    }.GetNewClosure())
  }
  foreach ($toolCtrl in @('txtChdman','txt7z','txtDolphin','txtPsxtract','txtCiso')) {
    if ($Ctx.ContainsKey($toolCtrl) -and $Ctx[$toolCtrl]) {
      $Ctx[$toolCtrl].add_TextChanged($refreshStatus)
    }
  }

  # Persist tool paths when user finishes editing (LostFocus) so manually
  # typed or pasted paths survive across sessions.
  $persistOnLostFocus = {
    try { & $persistSettingsNow } catch { }
  }.GetNewClosure()
  foreach ($toolCtrl in @('txtChdman','txt7z','txtDolphin','txtPsxtract','txtCiso')) {
    if ($Ctx.ContainsKey($toolCtrl) -and $Ctx[$toolCtrl]) {
      $Ctx[$toolCtrl].add_LostFocus($persistOnLostFocus)
    }
  }
  foreach ($textCtrl in @('txtTrash','txtDatRoot','txtAuditRoot','txtPs3Dupes')) {
    if ($Ctx.ContainsKey($textCtrl) -and $Ctx[$textCtrl]) {
      $Ctx[$textCtrl].add_TextChanged($refreshStatus)
      $Ctx[$textCtrl].add_LostFocus($persistOnLostFocus)
    }
  }
  # Wire new radio button toggle
  foreach ($rb in @('rbModeEinfach', 'rbModeExperte')) {
    if ($Ctx.ContainsKey($rb) -and $Ctx[$rb]) {
      $Ctx[$rb].add_Checked($updateExpertModeUi)
    }
  }
  # Keep backward compatibility with old checkbox
  if ($Ctx.ContainsKey('chkExpertMode') -and $Ctx['chkExpertMode']) {
    $Ctx['chkExpertMode'].add_Checked($updateExpertModeUi)
    $Ctx['chkExpertMode'].add_Unchecked($updateExpertModeUi)
  }
  if ($Ctx.ContainsKey('txtSafetyScope') -and $Ctx['txtSafetyScope']) { $Ctx['txtSafetyScope'].add_TextChanged($refreshStatus) }

  if ($Ctx.ContainsKey('chkSafetyMode') -and $Ctx['chkSafetyMode']) {
    $Ctx['chkSafetyMode'].add_Checked($refreshStatus)
    $Ctx['chkSafetyMode'].add_Unchecked($refreshStatus)
  }
  foreach ($chkCtrl in @('chkSortConsole','chkAliasKeying')) {
    if ($Ctx.ContainsKey($chkCtrl) -and $Ctx[$chkCtrl]) {
      $Ctx[$chkCtrl].add_Checked($refreshStatus)
      $Ctx[$chkCtrl].add_Unchecked($refreshStatus)
    }
  }
  if ($Ctx.ContainsKey('cmbLogLevel') -and $Ctx['cmbLogLevel']) {
    $Ctx['cmbLogLevel'].add_SelectionChanged($refreshStatus)
  }

  if (Get-Command Register-WpfRootInputHandlers -ErrorAction SilentlyContinue) {
    Register-WpfRootInputHandlers -Ctx $Ctx -BrowseFolder $browseFolder -PersistSettingsNow $persistSettingsNow
  }

  # ── Trash browse ─────────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnBrowseTrash') -and $Ctx['btnBrowseTrash']) {
    $Ctx['btnBrowseTrash'].add_Click({
      try {
        $path = & $browseFolder 'Papierkorb-Verzeichnis' $Ctx['txtTrash'].Text
        if ($path) {
          if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'TrashRoot' -Value ([string]$path))) {
            $Ctx['txtTrash'].Text = $path
          }
          & $persistSettingsNow
        }
      } catch {
        Write-Verbose ('[WPF] btnBrowseTrash failed: {0}' -f $_.Exception.Message)
      }
    }.GetNewClosure())
  }

  # ── Trash text input: persist on LostFocus ──────────────────────────────
  if ($Ctx.ContainsKey('txtTrash') -and $Ctx['txtTrash']) {
    $Ctx['txtTrash'].add_LostFocus({
      try {
        & $persistSettingsNow
      } catch {
        Write-Verbose ('[WPF] txtTrash LostFocus persist failed: {0}' -f $_.Exception.Message)
      }
    }.GetNewClosure())
  }

  # ── Tool browse buttons ───────────────────────────────────────────────────
  function Register-ToolBrowseButton {
    param(
      [Parameter(Mandatory)][string]$ButtonName,
      [Parameter(Mandatory)][string]$TextBoxName,
      [Parameter(Mandatory)][string]$DialogTitle,
      [Parameter(Mandatory)][string]$DialogFilter
    )

    $btn = $Ctx[$ButtonName]
    $txt = $Ctx[$TextBoxName]
    $vmPropertyMap = @{
      'txtChdman' = 'ToolChdman'
      'txtDolphin' = 'ToolDolphin'
      'txt7z' = 'Tool7z'
      'txtPsxtract' = 'ToolPsxtract'
      'txtCiso' = 'ToolCiso'
    }
    $vmProperty = if ($vmPropertyMap.ContainsKey($TextBoxName)) { [string]$vmPropertyMap[$TextBoxName] } else { '' }
    $dialogTitleLocal    = $DialogTitle
    $dialogFilterLocal   = $DialogFilter
    # Explicitly capture outer-scope variables as locals so GetNewClosure works
    # reliably inside a nested function's scriptblock.
    $capturedCtx         = $Ctx
    $capturedTxt         = $txt
    $capturedVmProp      = $vmProperty
    $capturedPersist     = $persistSettingsNow
    if ($null -eq $btn -or $null -eq $capturedTxt) {
      throw ("WpfEventHandlers: Control nicht gefunden ({0} / {1})" -f $ButtonName, $TextBoxName)
    }

    $btn.add_Click({
      try {
        $f = Show-WpfOpenFileDialog -Title $dialogTitleLocal -Filter $dialogFilterLocal
        if (-not [string]::IsNullOrWhiteSpace($f)) {
          if ([string]::IsNullOrWhiteSpace($capturedVmProp) -or
              -not (Set-WpfViewModelProperty -Ctx $capturedCtx -Name $capturedVmProp -Value ([string]$f))) {
            $capturedTxt.Text = $f
          }
          & $capturedPersist
        }
        Update-WpfStatusBar -Ctx $capturedCtx
      } catch {
        try {
          Add-WpfLogLine -Ctx $capturedCtx -Line ("Tool-Auswahl fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'ERROR'
        } catch {
          Write-Warning ("Tool-Auswahl fehlgeschlagen: {0}" -f $_.Exception.Message)
        }
      }
    }.GetNewClosure())
  }

  Register-ToolBrowseButton -ButtonName 'btnBrowseChdman'  -TextBoxName 'txtChdman'   -DialogTitle 'chdman auswählen'      -DialogFilter 'chdman (*)|chdman.exe;chdman'
  Register-ToolBrowseButton -ButtonName 'btnBrowseDolphin' -TextBoxName 'txtDolphin'  -DialogTitle 'DolphinTool auswählen' -DialogFilter 'DolphinTool (*)|DolphinTool.exe;DolphinTool'
  Register-ToolBrowseButton -ButtonName 'btnBrowse7z'      -TextBoxName 'txt7z'       -DialogTitle '7-Zip auswählen'       -DialogFilter '7z (*)|7z.exe;7z'
  Register-ToolBrowseButton -ButtonName 'btnBrowsePsxtract'-TextBoxName 'txtPsxtract' -DialogTitle 'psxtract auswählen'    -DialogFilter 'psxtract (*)|psxtract.exe;psxtract'
  Register-ToolBrowseButton -ButtonName 'btnBrowseCiso'    -TextBoxName 'txtCiso'     -DialogTitle 'ciso auswählen'        -DialogFilter 'ciso (*)|ciso.exe;ciso'

  # ── Audit browse ──────────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnBrowseAudit') -and $Ctx['btnBrowseAudit']) {
    $Ctx['btnBrowseAudit'].add_Click({
      try {
        $path = & $browseFolder 'Audit-Verzeichnis auswählen' $Ctx['txtAuditRoot'].Text
        if ($path) {
          if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'AuditRoot' -Value ([string]$path))) {
            $Ctx['txtAuditRoot'].Text = $path
          }
          & $persistSettingsNow
        }
      } catch {
        Write-Verbose ('[WPF] btnBrowseAudit failed: {0}' -f $_.Exception.Message)
      }
    }.GetNewClosure())
  }

  # ── PS3 browse ────────────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnBrowsePs3') -and $Ctx['btnBrowsePs3']) {
    $Ctx['btnBrowsePs3'].add_Click({
      try {
        $path = & $browseFolder 'PS3 Dupes-Verzeichnis' $Ctx['txtPs3Dupes'].Text
        if ($path) {
          if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'Ps3DupesRoot' -Value ([string]$path))) {
            $Ctx['txtPs3Dupes'].Text = $path
          }
          & $persistSettingsNow
        }
      } catch {
        Write-Verbose ('[WPF] btnBrowsePs3 failed: {0}' -f $_.Exception.Message)
      }
    }.GetNewClosure())
  }

  # ── GameKey preview ──────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnGameKeyPreview') -and $Ctx['btnGameKeyPreview']) {
    $Ctx['btnGameKeyPreview'].add_Click({
      try {
        $previewKey = Invoke-WpfGameKeyPreview -Ctx $Ctx
        if (-not [string]::IsNullOrWhiteSpace([string]$previewKey)) {
          Add-WpfLogLine -Ctx $Ctx -Line ("GameKey-Vorschau: {0}" -f $previewKey) -Level 'INFO'
        }
      } catch {
        Add-WpfLogLine -Ctx $Ctx -Line ("GameKey-Vorschau fehlgeschlagen: {0}" -f $_.Exception.Message) -Level 'WARN'
      }
    }.GetNewClosure())
  }

  # ── Auto-find tools ───────────────────────────────────────────────────────
  if ($Ctx.ContainsKey('btnAutoFindTools') -and $Ctx['btnAutoFindTools']) {
    $Ctx['btnAutoFindTools'].add_Click({
    if (Get-Command Find-ConversionTool -ErrorAction SilentlyContinue) {
      foreach ($pair in @(
        ,@('chdman',      'txtChdman',   'lblChdmanStatus')
        ,@('dolphintool', 'txtDolphin',  'lblDolphinStatus')
        ,@('7z',          'txt7z',       'lbl7zStatus')
        ,@('psxtract',    'txtPsxtract', 'lblPsxtractStatus')
        ,@('ciso',        'txtCiso',     'lblCisoStatus')
      )) {
        $toolName  = [string]$pair[0]
        $txtCtrl   = if ($null -ne $pair[1] -and $Ctx.ContainsKey($pair[1])) { $Ctx[$pair[1]] } else { $null }
        $lblCtrl   = if ($null -ne $pair[2] -and $Ctx.ContainsKey($pair[2])) { $Ctx[$pair[2]] } else { $null }
        if (-not $txtCtrl) { continue }
        if ([string]::IsNullOrWhiteSpace([string]$txtCtrl.Text)) {
          try {
            $found = Find-ConversionTool -ToolName $toolName
            if ($found) {
              $vmProp = switch ($pair[1]) {
                'txtChdman' { 'ToolChdman' }
                'txtDolphin' { 'ToolDolphin' }
                'txt7z' { 'Tool7z' }
                'txtPsxtract' { 'ToolPsxtract' }
                'txtCiso' { 'ToolCiso' }
                default { '' }
              }
              if ([string]::IsNullOrWhiteSpace($vmProp) -or -not (Set-WpfViewModelProperty -Ctx $Ctx -Name $vmProp -Value ([string]$found))) {
                $txtCtrl.Text = $found
              }
              if ($lblCtrl) {
                $lblCtrl.Text       = '✓ OK'
                $lblCtrl.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSuccess' -Fallback '#00FF88'
              }
            } else {
              if ($lblCtrl) {
                $lblCtrl.Text       = '✗ Nicht gefunden'
                $lblCtrl.Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushDanger' -Fallback '#FF0044'
              }
            }
          } catch {
            if ($lblCtrl) { $lblCtrl.Text = '! Fehler' }
          }
        }
      }
      Update-WpfStatusBar -Ctx $Ctx
      Update-WpfToolSetupVisibility -Ctx $Ctx
      & $persistSettingsNow
    }
  }.GetNewClosure())
  }

  # ── Slice 5: Report / Preview / Export (delegation) ──────────────────────
  if (Get-Command Register-WpfReportPreviewHandlers -ErrorAction SilentlyContinue) {
    Register-WpfReportPreviewHandlers -Window $Window -Ctx $Ctx -SaveFile $saveFile -ResolveLatestHtmlReport $resolveLatestHtmlReport -RefreshReportPreview $refreshReportPreview
  }

  if (Get-Command Register-WpfSettingsProfileHandlers -ErrorAction SilentlyContinue) {
    Register-WpfSettingsProfileHandlers -Window $Window -Ctx $Ctx -BrowseFile $browseFile -SaveFile $saveFile -PersistSettingsNow $persistSettingsNow
  }

  # ── Slice 6: Advanced Features (delegation) ────────────────────────────
  if (Get-Command Register-WpfAdvancedFeatureHandlers -ErrorAction SilentlyContinue) {
    Register-WpfAdvancedFeatureHandlers -Window $Window -Ctx $Ctx -PersistSettingsNow $persistSettingsNow
  }

  if (Get-Command Register-WpfRunControlHandlers -ErrorAction SilentlyContinue) {
    Register-WpfRunControlHandlers -Window $Window -Ctx $Ctx -ResolveLatestHtmlReport $resolveLatestHtmlReport -RefreshReportPreview $refreshReportPreview
  }

  # ── Quick-Start Preset buttons ────────────────────────────────────────────
  # Helper: visual flash feedback on a button after Quick-Start click
  $quickStartFlash = {
    param([System.Windows.Controls.Button]$Btn, [string]$Label)
    try {
      $original = $Btn.BorderBrush
      $Btn.BorderBrush = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSuccess' -Fallback '#00FF88'
      $Btn.BorderThickness = [System.Windows.Thickness]::new(2)
      $timer = New-Object System.Windows.Threading.DispatcherTimer
      $timer.Interval = [TimeSpan]::FromMilliseconds(1500)
      $timer.add_Tick({
        $Btn.BorderBrush = $original
        $Btn.BorderThickness = [System.Windows.Thickness]::new(1)
        ($this -as [System.Windows.Threading.DispatcherTimer]).Stop()
      }.GetNewClosure())
      $timer.Start()
    } catch { }
  }

  if ($Ctx.ContainsKey('btnPresetSafeDryRun') -and $Ctx['btnPresetSafeDryRun']) {
    $Ctx['btnPresetSafeDryRun'].add_Click({
      try {
        # Expert-mode controls
        $Ctx['chkReportDryRun'].IsChecked = $true
        $Ctx['chkConvert'].IsChecked = $false
        if ($Ctx.ContainsKey('chkExpertMode') -and $Ctx['chkExpertMode']) { $Ctx['chkExpertMode'].IsChecked = $false }
        # Einfach-mode controls (sync)
        if ($Ctx.ContainsKey('chkEinfachDupes') -and $Ctx['chkEinfachDupes']) { $Ctx['chkEinfachDupes'].IsChecked = $true }
        if ($Ctx.ContainsKey('chkEinfachJunk') -and $Ctx['chkEinfachJunk']) { $Ctx['chkEinfachJunk'].IsChecked = $true }
        if ($Ctx.ContainsKey('chkEinfachSort') -and $Ctx['chkEinfachSort']) { $Ctx['chkEinfachSort'].IsChecked = $true }
        & $quickStartFlash $Ctx['btnPresetSafeDryRun'] 'Sicherer DryRun'
        Add-WpfLogLine -Ctx $Ctx -Line 'Quick-Start: Sicherer DryRun geladen.' -Level 'INFO'
      } catch { Add-WpfLogLine -Ctx $Ctx -Line ('[Quick-Start] Fehler: {0}' -f $_.Exception.Message) -Level 'ERROR' }
    }.GetNewClosure())
  }
  if ($Ctx.ContainsKey('btnPresetFullSort') -and $Ctx['btnPresetFullSort']) {
    $Ctx['btnPresetFullSort'].add_Click({
      try {
        # Expert-mode controls
        $Ctx['chkReportDryRun'].IsChecked = $true
        $Ctx['chkConvert'].IsChecked = $false
        foreach ($r in @('chkPreferEU','chkPreferUS','chkPreferWORLD','chkPreferJP')) {
          if ($Ctx.ContainsKey($r) -and $Ctx[$r]) { $Ctx[$r].IsChecked = $true }
        }
        # Einfach-mode controls (sync): all regions = Weltweit
        if ($Ctx.ContainsKey('cmbEinfachRegion') -and $Ctx['cmbEinfachRegion']) { $Ctx['cmbEinfachRegion'].SelectedIndex = 3 }
        if ($Ctx.ContainsKey('chkEinfachDupes') -and $Ctx['chkEinfachDupes']) { $Ctx['chkEinfachDupes'].IsChecked = $true }
        if ($Ctx.ContainsKey('chkEinfachJunk') -and $Ctx['chkEinfachJunk']) { $Ctx['chkEinfachJunk'].IsChecked = $true }
        if ($Ctx.ContainsKey('chkEinfachSort') -and $Ctx['chkEinfachSort']) { $Ctx['chkEinfachSort'].IsChecked = $true }
        & $quickStartFlash $Ctx['btnPresetFullSort'] 'Volle Sortierung'
        Add-WpfLogLine -Ctx $Ctx -Line 'Quick-Start: Volle Sortierung geladen.' -Level 'INFO'
      } catch { Add-WpfLogLine -Ctx $Ctx -Line ('[Quick-Start] Fehler: {0}' -f $_.Exception.Message) -Level 'ERROR' }
    }.GetNewClosure())
  }
  if ($Ctx.ContainsKey('btnPresetConvert') -and $Ctx['btnPresetConvert']) {
    $Ctx['btnPresetConvert'].add_Click({
      try {
        # Expert-mode controls
        $Ctx['chkReportDryRun'].IsChecked = $false
        $Ctx['chkConvert'].IsChecked = $true
        # Einfach-mode controls (sync)
        if ($Ctx.ContainsKey('chkEinfachSort') -and $Ctx['chkEinfachSort']) { $Ctx['chkEinfachSort'].IsChecked = $true }
        & $quickStartFlash $Ctx['btnPresetConvert'] 'Konvertierung'
        Add-WpfLogLine -Ctx $Ctx -Line 'Quick-Start: Konvertierung geladen.' -Level 'INFO'
      } catch { Add-WpfLogLine -Ctx $Ctx -Line ('[Quick-Start] Fehler: {0}' -f $_.Exception.Message) -Level 'ERROR' }
    }.GetNewClosure())
  }

  # ── Quick-Profile switch in top bar ──────────────────────────────────────
  if ($Ctx.ContainsKey('cmbQuickProfile') -and $Ctx['cmbQuickProfile']) {
    $Ctx['cmbQuickProfile'].add_SelectionChanged({
      try {
        $sel = $Ctx['cmbQuickProfile'].SelectedItem
        if (-not $sel -or [string]::IsNullOrWhiteSpace($sel)) { return }
        if ($Ctx.ContainsKey('cmbConfigProfile') -and $Ctx['cmbConfigProfile']) {
          $Ctx['cmbConfigProfile'].Text = [string]$sel
        }
        if (Get-Command Import-ConfigProfile -ErrorAction SilentlyContinue) {
          Import-ConfigProfile -Name ([string]$sel)
          Initialize-WpfFromSettings -Ctx $Ctx
          Add-WpfLogLine -Ctx $Ctx -Line ("Profil '{0}' geladen." -f $sel) -Level 'INFO'
        }
      } catch { Write-Verbose ('[WPF] cmbQuickProfile failed: {0}' -f $_.Exception.Message) }
    }.GetNewClosure())
  }

  # ── Initial status update ─────────────────────────────────────────────────
  $Window.add_Loaded({
    # TD-002: Silent catch erlaubt in WPF-Event-Handlern (verhindert UI-Thread-Absturz)
    $prevEap = $ErrorActionPreference
    $ErrorActionPreference = 'SilentlyContinue'
    try {
      Update-WpfStatusBar -Ctx $Ctx -Initial
      $theme = 'dark'
      if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
        try { $theme = [string](Get-AppStateValue -Key 'UITheme' -Default 'dark') } catch { }
      }
      Set-WpfThemePalette -Window $Window -Ctx $Ctx -Light:($theme -eq 'light')
      Set-WpfDefaultTooltips -Ctx $Ctx
      # Load saved settings into UI
      $Ctx['_settingsInitialized'] = $false
      Initialize-WpfFromSettings -Ctx $Ctx
      $Ctx['_settingsInitialized'] = $true
      Update-WpfDryRunBanner -Ctx $Ctx
      Update-WpfRuntimeStatus -Ctx $Ctx -Elapsed ([TimeSpan]::Zero) -Reset
      & $updateExpertModeUi
      Update-WpfToolSetupVisibility -Ctx $Ctx
      Update-WpfProfileCombo -Ctx $Ctx
      # UX-01: Initial step indicator state
      if (Get-Command Update-WpfStepIndicator -ErrorAction SilentlyContinue) {
        Update-WpfStepIndicator -Ctx $Ctx
      }
      # Sync quick-profile dropdown with main profile combobox
      if ($Ctx.ContainsKey('cmbQuickProfile') -and $Ctx['cmbQuickProfile'] -and $Ctx.ContainsKey('cmbConfigProfile') -and $Ctx['cmbConfigProfile']) {
        try {
          $Ctx['cmbQuickProfile'].Items.Clear()
          foreach ($item in $Ctx['cmbConfigProfile'].Items) { [void]$Ctx['cmbQuickProfile'].Items.Add($item) }
        } catch { }
      }
      try { Set-WpfLocale -Ctx $Ctx } catch { }
      & $refreshReportPreview
      # ISS-001: First-start wizard (replaces old 3-step MessageBox onboarding)
      if (Get-Command Invoke-WpfFirstStartWizard -ErrorAction SilentlyContinue) {
        Invoke-WpfFirstStartWizard -Window $Window -Ctx $Ctx -BrowseFolder $browseFolder
      } else {
        Invoke-WpfQuickOnboarding -Window $Window -Ctx $Ctx -BrowseFolder $browseFolder
      }
    } catch { }
    finally { $ErrorActionPreference = $prevEap }
  }.GetNewClosure())

  # ── Save settings on window close ──────────────────────────────────────
  $Window.add_Closing({
    try { Save-WpfToSettings -Ctx $Ctx } catch { }
  }.GetNewClosure())
}

function Initialize-WpfFromSettings {
  <#
  .SYNOPSIS
    Loads persisted user settings into the WPF controls on startup.
  #>
  param([Parameter(Mandatory)][hashtable]$Ctx)

  try {
    Initialize-WpfDatMapBinding -Ctx $Ctx

    if (-not (Get-Command Get-UserSettings -ErrorAction SilentlyContinue)) { return }
    $s = Get-UserSettings
    if (-not $s) { return }

    # Tool paths (data-driven)
    if ($s['toolPaths']) {
      $tp = $s['toolPaths']
      $toolMap = @(
        @{ Prop = 'chdman';      Ctrl = 'txtChdman';   VM = 'ToolChdman' }
        @{ Prop = 'dolphintool'; Ctrl = 'txtDolphin';  VM = 'ToolDolphin' }
        @{ Prop = '7z';          Ctrl = 'txt7z';       VM = 'Tool7z' }
        @{ Prop = 'psxtract';    Ctrl = 'txtPsxtract'; VM = 'ToolPsxtract' }
        @{ Prop = 'ciso';        Ctrl = 'txtCiso';     VM = 'ToolCiso' }
      )
      foreach ($entry in $toolMap) {
        $val = $tp[$entry.Prop]
        if ($val) {
          if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name $entry.VM -Value ([string]$val))) {
            $Ctx[$entry.Ctrl].Text = [string]$val
          }
        }
      }
    }

    # DAT settings
    if ($s['dat']) {
      $d = $s['dat']
      $loadedDatRoot = ''
      if ($d.ContainsKey('root') -and -not [string]::IsNullOrWhiteSpace([string]$d['root'])) {
        $loadedDatRoot = [string]$d['root']
      } elseif ($d.ContainsKey('datRoot') -and -not [string]::IsNullOrWhiteSpace([string]$d['datRoot'])) {
        $loadedDatRoot = [string]$d['datRoot']
      }
      if (-not [string]::IsNullOrWhiteSpace($loadedDatRoot)) {
        if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'DatRoot' -Value ([string]$loadedDatRoot))) {
          $Ctx['txtDatRoot'].Text = $loadedDatRoot
        }
      }

      $loadedDatHash = ''
      if ($d.ContainsKey('hashType') -and -not [string]::IsNullOrWhiteSpace([string]$d['hashType'])) {
        $loadedDatHash = [string]$d['hashType']
      } elseif ($d.ContainsKey('hash') -and -not [string]::IsNullOrWhiteSpace([string]$d['hash'])) {
        $loadedDatHash = [string]$d['hash']
      }
      if (-not [string]::IsNullOrWhiteSpace($loadedDatHash)) {
        $normalizedDatHash = $loadedDatHash.Trim().ToLowerInvariant()
        if ($normalizedDatHash -eq 'crc') { $normalizedDatHash = 'crc32' }
        foreach ($item in $Ctx['cmbDatHash'].Items) {
          $itemValue = if ($item.PSObject.Properties['Content']) { [string]$item.Content } else { [string]$item }
          if ($itemValue -eq $normalizedDatHash) {
            $Ctx['cmbDatHash'].SelectedItem = $item
            break
          }
        }
      }
      if (-not $Ctx['cmbDatHash'].SelectedItem) {
        foreach ($item in $Ctx['cmbDatHash'].Items) {
          $itemValue = if ($item.PSObject.Properties['Content']) { [string]$item.Content } else { [string]$item }
          if ($itemValue -eq 'sha1') {
            $Ctx['cmbDatHash'].SelectedItem = $item
            break
          }
        }
      }
      $datEnabled = ConvertTo-SafeBool $d['enabled']
      if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'UseDat' -Value $datEnabled)) {
        $Ctx['chkDatUse'].IsChecked = $datEnabled
      }
      $datFallback = ConvertTo-SafeBool $d['fallback']
      if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'DatFallback' -Value $datFallback)) {
        $Ctx['chkDatFallback'].IsChecked = $datFallback
      }

      if ($d.ContainsKey('map')) {
        Set-WpfDatMapFromSettings -Ctx $Ctx -DatMapEntries @($d['map'])
      }
    }

    # General
    if ($s['general']) {
      $g = $s['general']
      $selectionConfig = Get-WpfAdvancedSelectionConfig
      if ($g['logLevel']) {
        foreach ($item in $Ctx['cmbLogLevel'].Items) {
          if ([string]$item.Content -eq [string]$g['logLevel']) {
            $Ctx['cmbLogLevel'].SelectedItem = $item
            break
          }
        }
      }
      if ($g['auditRoot'])     { if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'AuditRoot' -Value ([string]$g['auditRoot']))) { $Ctx['txtAuditRoot'].Text = [string]$g['auditRoot'] } }
      if ($g['ps3DupesRoot'])  { if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'Ps3DupesRoot' -Value ([string]$g['ps3DupesRoot']))) { $Ctx['txtPs3Dupes'].Text = [string]$g['ps3DupesRoot'] } }
      if ($g.ContainsKey('trashRoot') -and -not [string]::IsNullOrWhiteSpace([string]$g['trashRoot'])) {
        if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'TrashRoot' -Value ([string]$g['trashRoot']))) {
          if ($Ctx.ContainsKey('txtTrash') -and $Ctx['txtTrash']) { $Ctx['txtTrash'].Text = [string]$g['trashRoot'] }
        }
      }

      $preferValues = if ($g.ContainsKey('prefer')) { @(ConvertTo-WpfStringList -Value $g['prefer']) } else { @() }
      if ($preferValues.Count -eq 0) {
        # Auto-detect region from system locale when no saved preference exists
        $localeRegionMap = @{
          'DE' = @('EU','DE')
          'FR' = @('EU','FR')
          'IT' = @('EU','IT')
          'ES' = @('EU','ES')
          'NL' = @('EU','NL')
          'EN' = @('EU','US','WORLD')
          'JA' = @('JP')
          'KO' = @('KR','ASIA')
          'ZH' = @('CN','ASIA')
          'PT' = @('EU','BR')
          'SV' = @('EU','SE')
        }
        $twoLetter = [CultureInfo]::CurrentCulture.TwoLetterISOLanguageName.ToUpperInvariant()
        $autoPrefer = $localeRegionMap[$twoLetter]
        if ($autoPrefer) { $preferValues = @($autoPrefer) }
        else { $preferValues = @($selectionConfig.PreferDefaults) }
      }
      Set-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.PreferMap -Values $preferValues

      if ($g.ContainsKey('extensions') -and $selectionConfig.Contains('ExtensionsMap')) {
        $extValues = @(ConvertTo-WpfStringList -Value $g['extensions'])
        Set-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.ExtensionsMap -Values $extValues
      }

      if ($g.ContainsKey('consolefilter') -and $selectionConfig.Contains('ConsoleMap')) {
        $consoleValues = @(ConvertTo-WpfStringList -Value $g['consolefilter'])
        Set-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.ConsoleMap -Values $consoleValues
      }

      if ($g.ContainsKey('jpkeepconsoles') -and $Ctx.ContainsKey('txtJpKeepConsoles') -and $Ctx['txtJpKeepConsoles']) {
        $jpKeepValue = (@(ConvertTo-WpfStringList -Value $g['jpkeepconsoles']) -join ',')
        if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'JpKeepConsoles' -Value $jpKeepValue)) {
          $Ctx['txtJpKeepConsoles'].Text = $jpKeepValue
        }
      }
      # ── Data-driven bool/string settings load (DUP-012 consolidation) ───
      $boolSettingsMap = @(
        @{ Prop = 'convertenabled'; Alt = 'convert';  Ctrl = 'chkConvert';        VM = 'ConvertEnabled' }
        @{ Prop = 'jponlyselected'; Alt = $null;      Ctrl = 'chkJpOnlySelected'; VM = 'JpOnlySelected' }
        @{ Prop = 'sortconsole';    Alt = $null;      Ctrl = 'chkSortConsole';    VM = 'SortConsole' }
        @{ Prop = 'aliaskeying';    Alt = $null;      Ctrl = 'chkAliasKeying';    VM = 'AliasKeying' }
        @{ Prop = 'aggressivejunk'; Alt = $null;      Ctrl = 'chkJunkAggressive'; VM = 'AggressiveJunk' }
        @{ Prop = 'safetystrict';   Alt = $null;      Ctrl = 'chkSafetyMode';     VM = 'SafetyStrict' }
        @{ Prop = 'dryrun';         Alt = $null;      Ctrl = 'chkReportDryRun';   VM = 'DryRun' }
        @{ Prop = 'confirmmove';    Alt = $null;      Ctrl = 'chkConfirmMove';    VM = 'ConfirmMove' }
      )
      foreach ($entry in $boolSettingsMap) {
        $propName = $entry.Prop
        $value = $null
        if ($g.ContainsKey($propName)) {
          $value = ConvertTo-SafeBool $g[$propName]
        } elseif ($entry.Alt -and $g.ContainsKey($entry.Alt)) {
          $value = ConvertTo-SafeBool $g[$entry.Alt]
        }
        if ($null -ne $value -and $Ctx.ContainsKey($entry.Ctrl) -and $Ctx[$entry.Ctrl]) {
          if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name $entry.VM -Value $value)) {
            $Ctx[$entry.Ctrl].IsChecked = $value
          }
        }
      }
      # ── CRC verify settings ─────────────────────────────────────────
      if ($g.ContainsKey('crcverifyscan') -and $Ctx.ContainsKey('chkCrcVerifyScan') -and $Ctx['chkCrcVerifyScan']) {
        $crcScanVal = ConvertTo-SafeBool $g['crcverifyscan']
        if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'CrcVerifyScan' -Value $crcScanVal)) {
          $Ctx['chkCrcVerifyScan'].IsChecked = $crcScanVal
        }
      }
      if ($g.ContainsKey('crcverifydat') -and $Ctx.ContainsKey('chkCrcVerifyDat') -and $Ctx['chkCrcVerifyDat']) {
        $crcDatVal = ConvertTo-SafeBool $g['crcverifydat']
        if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'CrcVerifyDat' -Value $crcDatVal)) {
          $Ctx['chkCrcVerifyDat'].IsChecked = $crcDatVal
        }
      }
      if ($g.ContainsKey('protectedpaths') -and $Ctx.ContainsKey('txtSafetyScope') -and $Ctx['txtSafetyScope']) {
        if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'ProtectedPaths' -Value ([string]$g['protectedpaths']))) {
          $Ctx['txtSafetyScope'].Text = [string]$g['protectedpaths']
        }
      }
      if ($g.ContainsKey('locale') -and $Ctx.ContainsKey('cmbLocale') -and $Ctx['cmbLocale']) {
        $targetLocale = [string]$g['locale']
        foreach ($item in $Ctx['cmbLocale'].Items) {
          if ([string]$item.Content -eq $targetLocale) {
            $Ctx['cmbLocale'].SelectedItem = $item
            break
          }
        }
      }
      if ($g.ContainsKey('theme')) {
        $themeValue = [string]$g['theme']
        if ($themeValue -in @('dark','light') -and (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue)) {
          try { [void](Set-AppStateValue -Key 'UITheme' -Value $themeValue) } catch { }
        }
      }
      # Restore expert mode toggle
      if ($g.ContainsKey('expertmode')) {
        $savedExpert = ConvertTo-SafeBool $g['expertmode']
        if ($Ctx.ContainsKey('rbModeExperte') -and $Ctx['rbModeExperte']) {
          $Ctx['rbModeExperte'].IsChecked = $savedExpert
          if ($Ctx.ContainsKey('rbModeEinfach') -and $Ctx['rbModeEinfach']) {
            $Ctx['rbModeEinfach'].IsChecked = -not $savedExpert
          }
        } elseif ($Ctx.ContainsKey('chkExpertMode') -and $Ctx['chkExpertMode']) {
          $Ctx['chkExpertMode'].IsChecked = $savedExpert
        }
      }
    }

    Update-WpfCrcVerifyOptions -Ctx $Ctx

    # Load persisted roots
    if ($g.ContainsKey('roots') -and -not [string]::IsNullOrWhiteSpace([string]$g['roots'])) {
      $savedRoots = @(([string]$g['roots']).Split('|') | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
      if ($savedRoots.Count -gt 0) {
        $rootsCollection = $null
        if ($Ctx.ContainsKey('__rootsCollection') -and $Ctx['__rootsCollection']) {
          $rootsCollection = $Ctx['__rootsCollection']
        } elseif (Get-Command Get-WpfRootsCollection -ErrorAction SilentlyContinue) {
          $rootsCollection = Get-WpfRootsCollection -Ctx $Ctx
        }
        if ($rootsCollection) {
          foreach ($savedRoot in $savedRoots) {
            $already = $false
            foreach ($existing in @($rootsCollection)) {
              if ([string]$existing -ieq [string]$savedRoot) { $already = $true; break }
            }
            if (-not $already) { [void]$rootsCollection.Add([string]$savedRoot) }
          }
        }
      }
    }

    if (Get-Command Sync-WpfViewModelRootsFromControl -ErrorAction SilentlyContinue) {
      Sync-WpfViewModelRootsFromControl -Ctx $Ctx
    }

    # Auto-detect trash folder if empty after loading settings
    $currentTrash = ''
    $vm = $null
    if (Get-Command Get-WpfViewModel -ErrorAction SilentlyContinue) { $vm = Get-WpfViewModel -Ctx $Ctx }
    if ($vm) { $currentTrash = [string]$vm.TrashRoot }
    if ([string]::IsNullOrWhiteSpace($currentTrash) -and $Ctx.ContainsKey('txtTrash') -and $Ctx['txtTrash']) {
      $currentTrash = [string]$Ctx['txtTrash'].Text
    }
    if ([string]::IsNullOrWhiteSpace($currentTrash)) {
      $firstRoot = $null
      if ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots'] -and $Ctx['listRoots'].Items.Count -gt 0) {
        $firstRoot = [string]($Ctx['listRoots'].Items[0])
      }
      if ($firstRoot -and (Test-Path -LiteralPath $firstRoot -PathType Container)) {
        $parentDir = Split-Path -Parent $firstRoot
        if ($parentDir) {
          $autoTrash = Join-Path $parentDir '_ROM_Cleanup_Trash'
          if (-not (Set-WpfViewModelProperty -Ctx $Ctx -Name 'TrashRoot' -Value $autoTrash)) {
            if ($Ctx.ContainsKey('txtTrash') -and $Ctx['txtTrash']) { $Ctx['txtTrash'].Text = $autoTrash }
          }
        }
      }
    }

    Update-WpfStatusBar -Ctx $Ctx
  } catch { } # intentionally keep startup robust when persisted settings are missing/invalid
}

function Save-WpfToSettings {
  <#
  .SYNOPSIS
    Persists current UI control values to user settings file.
  #>
  param([Parameter(Mandatory)][hashtable]$Ctx)

  try {
    # Guard: do not save before settings have been loaded into the UI.
    # Saving before _settingsInitialized is $true would overwrite persisted
    # values (e.g. tool paths) with empty defaults.
    if ($Ctx.ContainsKey('_settingsInitialized') -and $Ctx['_settingsInitialized'] -eq $false) { return }
    if (-not (Get-Command Set-UserSettings -ErrorAction SilentlyContinue)) { return }

    $main = Get-WpfMainSnapshot -Ctx $Ctx

    # Build settings structure
    $toolPaths = [ordered]@{
      chdman      = [string]$main.ToolChdman
      dolphintool = [string]$main.ToolDolphin
      '7z'        = [string]$main.Tool7z
      psxtract    = [string]$main.ToolPsxtract
      ciso        = [string]$main.ToolCiso
    }

    $selectedDatHash = [string]$main.DatHashType
    $selectedLogLevel = if (-not [string]::IsNullOrWhiteSpace([string]$main.LogLevel)) { [string]$main.LogLevel } else { 'Info' }

    $dat = [ordered]@{
      root     = [string]$main.DatRoot
      datRoot  = [string]$main.DatRoot
      hashType = $selectedDatHash
      enabled  = (ConvertTo-SafeBool $main.UseDat)
      fallback = (ConvertTo-SafeBool $main.DatFallback)
      map      = @(Get-WpfDatMapEntries -Ctx $Ctx)
    }

    $selectionConfig = Get-WpfAdvancedSelectionConfig

    $preferValues = @(Get-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.PreferMap -DefaultValues $selectionConfig.PreferDefaults -FallbackTextControl 'txtPrefer')
    $extValues     = if ($selectionConfig.Contains('ExtensionsMap')) { @(Get-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.ExtensionsMap -DefaultValues @()) } else { @() }
    $consoleValues = if ($selectionConfig.Contains('ConsoleMap'))    { @(Get-WpfCheckedValues -Ctx $Ctx -ControlMap $selectionConfig.ConsoleMap    -DefaultValues @()) } else { @() }
    $jpKeepValues = @($main.JpKeepConsoles)

    $general = [ordered]@{
      logLevel       = $selectedLogLevel
      auditRoot      = [string]$main.AuditRoot
      ps3DupesRoot   = [string]$main.Ps3DupesRoot
      trashRoot      = [string]$main.TrashRoot
      prefer         = ($preferValues -join ',')
      extensions     = ($extValues -join ',')
      consolefilter  = ($consoleValues -join ',')
      convertenabled = (ConvertTo-SafeBool $main.ConvertChecked)
      jpkeepconsoles = ($jpKeepValues -join ',')
      jponlyselected = (ConvertTo-SafeBool $main.JpOnlySelected)
      sortconsole    = (ConvertTo-SafeBool $main.SortConsole)
      aliaskeying    = (ConvertTo-SafeBool $main.AliasKeying)
      aggressivejunk = (ConvertTo-SafeBool $main.AggressiveJunk)
      safetystrict   = (ConvertTo-SafeBool $main.SafetyStrict)
      protectedpaths = [string]$main.ProtectedPaths
      dryrun         = (ConvertTo-SafeBool $main.DryRun)
      confirmmove    = (ConvertTo-SafeBool $main.ConfirmMove)
      crcverifyscan  = (ConvertTo-SafeBool $main.CrcVerifyScan)
      crcverifydat   = (ConvertTo-SafeBool $main.CrcVerifyDat)
      locale = $(if (-not [string]::IsNullOrWhiteSpace([string]$main.Locale)) { [string]$main.Locale } else { 'de' })
      theme = $(if ((Get-Command Get-AppStateValue -ErrorAction SilentlyContinue)) {
        try { [string](Get-AppStateValue -Key 'UITheme' -Default 'dark') } catch { 'dark' }
      } else { 'dark' })
      expertmode = $(if ($Ctx.ContainsKey('rbModeExperte') -and $Ctx['rbModeExperte']) {
        [bool]$Ctx['rbModeExperte'].IsChecked
      } elseif ($Ctx.ContainsKey('chkExpertMode') -and $Ctx['chkExpertMode']) {
        [bool]$Ctx['chkExpertMode'].IsChecked
      } else { $false })
    }

    # Persist roots
    $rootsList = @()
    if ($Ctx.ContainsKey('__rootsCollection') -and $Ctx['__rootsCollection'] -and $Ctx['__rootsCollection'].Count -gt 0) {
      $rootsList = @($Ctx['__rootsCollection'] | ForEach-Object { [string]$_ })
    } elseif ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots'] -and $Ctx['listRoots'].Items.Count -gt 0) {
      $rootsList = @($Ctx['listRoots'].Items | ForEach-Object { [string]$_ })
    }
    $general['roots'] = ($rootsList -join '|')

    Set-UserSettings -Settings @{ toolPaths = $toolPaths; dat = $dat; general = $general }
  } catch {
    if ($Ctx.ContainsKey('listLog')) {
      try { Add-WpfLogLine -Ctx $Ctx -Line ("Einstellungen konnten nicht gespeichert werden: {0}" -f $_.Exception.Message) -Level 'ERROR' } catch { }
    }
  }
}

function Show-WpfPreRunDialog {
  <#
  .SYNOPSIS
    Styled WPF dialog replacing the plain MessageBox before a run starts.
    Shows preflight status, mode, options summary, and Move-mode warning.
  .OUTPUTS
    [bool] $true if user confirmed, $false if cancelled.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Owner,
    [Parameter(Mandatory)][hashtable]$Params,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  $isMove = ($Params.Mode -eq 'Move')

  $dialogXaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ROM Cleanup – Lauf-Vorschau" SizeToContent="WidthAndHeight"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize"
        MinWidth="440" MaxWidth="560"
        Background="{DynamicResource BrushBackground}"
        Foreground="{DynamicResource BrushTextPrimary}"
        WindowStyle="ToolWindow" ShowInTaskbar="False">
  <Border Padding="24" Background="{DynamicResource BrushBackground}">
    <StackPanel>
      <TextBlock Text="LAUF-VORSCHAU" FontWeight="Bold" FontSize="16"
                 Foreground="{DynamicResource BrushAccentCyan}" Margin="0 0 0 16"/>

      <!-- Preflight Status -->
      <StackPanel Margin="0 0 0 12">
        <TextBlock x:Name="lblPfRoots"  Margin="0 2" TextWrapping="Wrap"/>
        <TextBlock x:Name="lblPfTools"  Margin="0 2" TextWrapping="Wrap"/>
        <TextBlock x:Name="lblPfDat"    Margin="0 2" TextWrapping="Wrap"/>
      </StackPanel>

      <Border Height="1" Background="{DynamicResource BrushBorder}" Margin="0 4 0 8"/>

      <!-- Options Summary -->
      <StackPanel Margin="0 0 0 12">
        <TextBlock x:Name="lblPfMode" FontWeight="SemiBold" FontSize="14" Margin="0 2"/>
        <TextBlock x:Name="lblPfTrash" Foreground="{DynamicResource BrushTextMuted}" Margin="0 2" TextWrapping="Wrap"/>
        <TextBlock x:Name="lblPfOpts"  Foreground="{DynamicResource BrushTextMuted}" Margin="0 2" TextWrapping="Wrap"/>
      </StackPanel>

      <!-- Move Warning (only visible for Move mode) -->
      <Border x:Name="brdPfMoveWarn" Visibility="Collapsed"
              Background="{DynamicResource BrushDangerBg}" BorderBrush="{DynamicResource BrushDanger}"
              BorderThickness="1" CornerRadius="6" Padding="12" Margin="0 0 0 12">
        <StackPanel>
          <TextBlock Text="&#xE7BA; ACHTUNG: DATEIEN WERDEN VERSCHOBEN" FontWeight="Bold"
                     Foreground="{DynamicResource BrushDanger}" Margin="0 0 0 6"/>
          <TextBlock x:Name="lblPfMoveInfo" Foreground="{DynamicResource BrushDanger}" TextWrapping="Wrap"/>
        </StackPanel>
      </Border>

      <!-- Buttons -->
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0 8 0 0">
        <Button x:Name="btnPfCancel" Content="Abbrechen" Padding="16 6" Margin="0 0 8 0" MinWidth="90"/>
        <Button x:Name="btnPfOk" Padding="16 6" FontWeight="SemiBold" MinWidth="120"
                Background="{DynamicResource BrushAccentCyan}" Foreground="{DynamicResource BrushBackground}"/>
      </StackPanel>
    </StackPanel>
  </Border>
</Window>
"@

  try {
    $dlg = [System.Windows.Markup.XamlReader]::Parse($dialogXaml)
  } catch {
    # Fallback to MessageBox if XAML parsing fails
    $fallback = [System.Windows.MessageBox]::Show(
      ('Modus: {0}  |  Ordner: {1}' -f $Params.Mode, @($Params.Roots).Count),
      'ROM Cleanup – Starten?',
      [System.Windows.MessageBoxButton]::OKCancel,
      [System.Windows.MessageBoxImage]::Information)
    return ($fallback -eq [System.Windows.MessageBoxResult]::OK)
  }

  # Merge theme resources from owner window
  try {
    if ($Owner.Resources.MergedDictionaries.Count -gt 0) {
      foreach ($rd in $Owner.Resources.MergedDictionaries) {
        [void]$dlg.Resources.MergedDictionaries.Add($rd)
      }
    } else {
      # Theme was injected inline — load fresh from file
      if (Get-Command Get-WpfThemeResourceDictionaryXaml -ErrorAction SilentlyContinue) {
        $thXaml = Get-WpfThemeResourceDictionaryXaml
        if (-not [string]::IsNullOrWhiteSpace($thXaml)) {
          $thDict = [System.Windows.Markup.XamlReader]::Parse($thXaml)
          if ($thDict -is [System.Windows.ResourceDictionary]) {
            [void]$dlg.Resources.MergedDictionaries.Add($thDict)
          }
        }
      }
    }
  } catch { } # dialog will still work with default WPF colors

  $dlg.Owner = $Owner

  # Populate preflight status
  $rootCount = if ($Params.Roots) { @($Params.Roots).Count } else { 0 }
  $dlg.FindName('lblPfRoots').Text = [string]::Format('{0} ROM-Ordner: {1}', $(if ($rootCount -gt 0) { [char]0x2705 } else { [char]0x26A0 }), $rootCount)

  # Tools status
  $toolsOk = $false
  try {
    if ($Ctx.ContainsKey('dotTools') -and $Ctx['dotTools'] -and $Ctx['dotTools'].Fill) {
      $fillStr = $Ctx['dotTools'].Fill.ToString()
      $toolsOk = ($fillStr -match '00FF|Success|Lime|Green')
    }
  } catch { }
  $dlg.FindName('lblPfTools').Text = if ($toolsOk) { "$([char]0x2705) Tools: verfügbar" } else { "$([char]0x26A0) Tools: nicht alle konfiguriert" }

  # DAT status
  $datActive = [bool]$Params.UseDat
  $dlg.FindName('lblPfDat').Text = if ($datActive) { "$([char]0x2705) DAT-Verifizierung: aktiviert" } else { "$([char]0x26A0) DAT: nicht konfiguriert (optional)" }

  # Mode
  if ($isMove) {
    $dlg.FindName('lblPfMode').Text = "$([char]0x26A0) Modus: Move (Dateien verschieben)"
    $dlg.FindName('lblPfMode').Foreground = $dlg.FindResource('BrushDanger')
  } else {
    $dlg.FindName('lblPfMode').Text = "$([char]0x2705) Modus: DryRun (Vorschau)"
    $dlg.FindName('lblPfMode').Foreground = $dlg.FindResource('BrushSuccess')
  }

  # Trash + options
  $trashDisplay = if ([string]::IsNullOrWhiteSpace($Params.TrashRoot)) { '(nicht gesetzt)' } else { $Params.TrashRoot }
  $dlg.FindName('lblPfTrash').Text = "Papierkorb: $trashDisplay"

  $optParts = @()
  if ($Params.SortConsole) { $optParts += 'Konsolen-Sort' }
  if ($Params.UseDat)      { $optParts += 'DAT-Abgleich' }
  if ($Params.ConvertChecked) { $optParts += 'Konvertierung' }
  $dlg.FindName('lblPfOpts').Text = 'Optionen: ' + $(if ($optParts.Count -gt 0) { $optParts -join ', ' } else { 'Standard' })

  # Move warning
  if ($isMove) {
    $dlg.FindName('brdPfMoveWarn').Visibility = [System.Windows.Visibility]::Visible
    $moveInfo = 'Duplikate und Junk werden physisch in den Papierkorb verschoben.'
    try {
      $lastDupes = 0; $lastJunk = 0
      if ($Ctx.ContainsKey('lblDashDupes') -and $Ctx['lblDashDupes']) { $lastDupes = [int]([string]$Ctx['lblDashDupes'].Text) }
      if ($Ctx.ContainsKey('lblDashJunk') -and $Ctx['lblDashJunk'])   { $lastJunk  = [int]([string]$Ctx['lblDashJunk'].Text) }
      if (($lastDupes + $lastJunk) -gt 0) {
        $moveInfo += "`nErwartete Verschiebungen: ~$($lastDupes + $lastJunk) Dateien"
      }
    } catch { }
    $dlg.FindName('lblPfMoveInfo').Text = $moveInfo
    $dlg.FindName('btnPfOk').Content = "$([char]0x26A1) Move starten"
    $dlg.FindName('btnPfOk').Background = $dlg.FindResource('BrushDanger')
    $dlg.FindName('btnPfOk').Foreground = [System.Windows.Media.Brushes]::White
  } else {
    $dlg.FindName('btnPfOk').Content = "$([char]0x25B6) DryRun starten"
  }

  # Button events
  $dlg.FindName('btnPfCancel').Add_Click({ $dlg.DialogResult = $false; $dlg.Close() }.GetNewClosure())
  $dlg.FindName('btnPfOk').Add_Click({ $dlg.DialogResult = $true; $dlg.Close() }.GetNewClosure())

  $result = $dlg.ShowDialog()
  return ($result -eq $true)
}

function Show-WpfMoveConfirmDialog {
  <#
  .SYNOPSIS
    Styled Move-confirmation dialog with checkbox gate.
    Shown when user clicks "Jetzt als Move ausführen" after a DryRun.
  .OUTPUTS
    [bool] $true if user confirmed Move, $false if cancelled.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Owner,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  # Gather last DryRun results
  $winners = 0; $dupes = 0; $junk = 0; $trashPath = ''
  try {
    if ($Ctx.ContainsKey('lblDashWinners') -and $Ctx['lblDashWinners']) { $winners = [int]([string]$Ctx['lblDashWinners'].Text) }
    if ($Ctx.ContainsKey('lblDashDupes') -and $Ctx['lblDashDupes'])     { $dupes   = [int]([string]$Ctx['lblDashDupes'].Text) }
    if ($Ctx.ContainsKey('lblDashJunk') -and $Ctx['lblDashJunk'])       { $junk    = [int]([string]$Ctx['lblDashJunk'].Text) }
  } catch { }
  try {
    if ($Ctx.ContainsKey('txtTrash') -and $Ctx['txtTrash']) { $trashPath = [string]$Ctx['txtTrash'].Text }
  } catch { }
  if ([string]::IsNullOrWhiteSpace($trashPath)) { $trashPath = '(nicht gesetzt)' }

  $dialogXaml = @"
<Window xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="ROM Cleanup – Move-Bestätigung" SizeToContent="WidthAndHeight"
        WindowStartupLocation="CenterOwner" ResizeMode="NoResize"
        MinWidth="460" MaxWidth="580"
        Background="{DynamicResource BrushBackground}"
        Foreground="{DynamicResource BrushTextPrimary}"
        WindowStyle="ToolWindow" ShowInTaskbar="False">
  <Border Padding="24" Background="{DynamicResource BrushBackground}">
    <StackPanel>
      <!-- Danger Header -->
      <Border Background="{DynamicResource BrushDangerBg}" BorderBrush="{DynamicResource BrushDanger}"
              BorderThickness="1" CornerRadius="6" Padding="12" Margin="0 0 0 16">
        <StackPanel>
          <TextBlock Text="&#xE7BA; MOVE-MODUS: DATEIEN WERDEN VERSCHOBEN" FontWeight="Bold" FontSize="14"
                     Foreground="{DynamicResource BrushDanger}" Margin="0 0 0 8"/>
          <TextBlock Text="Die folgenden Aktionen werden physisch durchgeführt:"
                     Foreground="{DynamicResource BrushDanger}" TextWrapping="Wrap"/>
        </StackPanel>
      </Border>

      <!-- DryRun Results Summary -->
      <StackPanel Margin="0 0 0 12">
        <TextBlock x:Name="lblMcWinners" Margin="0 3" TextWrapping="Wrap"/>
        <TextBlock x:Name="lblMcDupes"   Margin="0 3" TextWrapping="Wrap"/>
        <TextBlock x:Name="lblMcJunk"    Margin="0 3" TextWrapping="Wrap"/>
      </StackPanel>

      <Border Height="1" Background="{DynamicResource BrushBorder}" Margin="0 4 0 8"/>

      <TextBlock x:Name="lblMcTrash" Foreground="{DynamicResource BrushTextMuted}" Margin="0 0 0 16" TextWrapping="Wrap"/>

      <!-- Checkbox Gate -->
      <CheckBox x:Name="chkMcConfirm" Margin="0 0 0 16"
                Foreground="{DynamicResource BrushDanger}">
        <TextBlock Text="Ich habe die Vorschau geprüft und verstehe, dass Dateien verschoben werden."
                   TextWrapping="Wrap" Foreground="{DynamicResource BrushDanger}"/>
      </CheckBox>

      <!-- Buttons -->
      <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Margin="0 8 0 0">
        <Button x:Name="btnMcCancel" Content="Abbrechen" Padding="16 6" Margin="0 0 8 0" MinWidth="90"/>
        <Button x:Name="btnMcOk" Content="&#x26A1; Move ausführen" Padding="16 6" FontWeight="SemiBold" MinWidth="140"
                IsEnabled="False"
                Background="{DynamicResource BrushDanger}" Foreground="White"/>
      </StackPanel>
    </StackPanel>
  </Border>
</Window>
"@

  try {
    $dlg = [System.Windows.Markup.XamlReader]::Parse($dialogXaml)
  } catch {
    # Fallback to MessageBox
    $fb = [System.Windows.MessageBox]::Show(
      "ACHTUNG: Im Move-Modus werden Duplikate und Junk physisch in den Papierkorb verschoben.`n`nFortfahren?",
      'ROM Cleanup – Move-Modus',
      [System.Windows.MessageBoxButton]::OKCancel,
      [System.Windows.MessageBoxImage]::Warning)
    return ($fb -eq [System.Windows.MessageBoxResult]::OK)
  }

  # Merge theme resources
  try {
    if ($Owner.Resources.MergedDictionaries.Count -gt 0) {
      foreach ($rd in $Owner.Resources.MergedDictionaries) {
        [void]$dlg.Resources.MergedDictionaries.Add($rd)
      }
    } else {
      if (Get-Command Get-WpfThemeResourceDictionaryXaml -ErrorAction SilentlyContinue) {
        $thXaml = Get-WpfThemeResourceDictionaryXaml
        if (-not [string]::IsNullOrWhiteSpace($thXaml)) {
          $thDict = [System.Windows.Markup.XamlReader]::Parse($thXaml)
          if ($thDict -is [System.Windows.ResourceDictionary]) {
            [void]$dlg.Resources.MergedDictionaries.Add($thDict)
          }
        }
      }
    }
  } catch { }

  $dlg.Owner = $Owner

  # Populate summary
  $dlg.FindName('lblMcWinners').Text = "$([char]0x2705) $winners Winner bleiben an Ort"
  $dlg.FindName('lblMcDupes').Text   = "$([char]0x26A0) $dupes Duplikate $([char]0x2192) Papierkorb"
  $dlg.FindName('lblMcJunk').Text    = "$([char]0x1F5D1) $junk Junk-Dateien $([char]0x2192) Papierkorb"
  $dlg.FindName('lblMcTrash').Text   = "Papierkorb: $trashPath"

  # Checkbox gate — enable Move button only when checked
  $btnOk = $dlg.FindName('btnMcOk')
  $chk   = $dlg.FindName('chkMcConfirm')
  $chk.Add_Checked({  $btnOk.IsEnabled = $true  }.GetNewClosure())
  $chk.Add_Unchecked({ $btnOk.IsEnabled = $false }.GetNewClosure())

  # Button events
  $dlg.FindName('btnMcCancel').Add_Click({ $dlg.DialogResult = $false; $dlg.Close() }.GetNewClosure())
  $btnOk.Add_Click({ $dlg.DialogResult = $true; $dlg.Close() }.GetNewClosure())

  $result = $dlg.ShowDialog()
  return ($result -eq $true)
}

function Start-WpfOperationAsync {
  <#
  .SYNOPSIS
    Validates inputs, collects parameters, and starts the background engine.
    Uses a DispatcherTimer to poll the background job and update the UI.
  #>
  [Diagnostics.CodeAnalysis.SuppressMessageAttribute('PSUseDeclaredVarsMoreThanAssignments', '', Justification='Closure-captured state is shared between nested script blocks.')]
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [Parameter(Mandatory)][hashtable]$Ctx
  )

  # ── Validation ──────────────────────────────────────────────────────────
  if (Get-Command Sync-WpfViewModelRootsFromControl -ErrorAction SilentlyContinue) {
    try { Sync-WpfViewModelRootsFromControl -Ctx $Ctx } catch { }
  }

  Reset-WpfInlineValidationState -Ctx $Ctx

  $params = Get-WpfRunParameters -Ctx $Ctx

  if (-not $params.Roots -or $params.Roots.Count -eq 0) {
    Set-WpfInlineValidationState -Ctx $Ctx -ControlName 'listRoots' -IsValid $false
    [System.Windows.MessageBox]::Show(
      'Bitte mindestens ein ROM-Verzeichnis hinzufügen.',
      'ROM Cleanup – Validierung',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Warning) | Out-Null
    return
  }

  foreach ($root in $params.Roots) {
    if (-not (Test-Path -LiteralPath $root -PathType Container)) {
      Set-WpfInlineValidationState -Ctx $Ctx -ControlName 'listRoots' -IsValid $false
      [System.Windows.MessageBox]::Show(
        "Verzeichnis nicht gefunden:`n$root",
        'ROM Cleanup – Validierung',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Warning) | Out-Null
      return
    }
  }

  if ([string]::IsNullOrWhiteSpace($params.TrashRoot)) {
    Set-WpfInlineValidationState -Ctx $Ctx -ControlName 'txtTrash' -IsValid $false
    [System.Windows.MessageBox]::Show(
      'Bitte ein Papierkorb-Verzeichnis angeben.',
      'ROM Cleanup – Validierung',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Warning) | Out-Null
    return
  }

  # ── Pre-run summary dialog (UX-02/UX-06: styled WPF dialog) ─────────────
  $preRunConfirmed = Show-WpfPreRunDialog -Owner $Window -Params $params -Ctx $Ctx
  if (-not $preRunConfirmed) { return }

  # ── Switch to Log tab ────────────────────────────────────────────────────
  try { $Ctx['tabMain'].SelectedItem = $Ctx['tabProgress'] } catch { } # intentionally ignore tab switch failures on non-standard UI states

  # ── Enter busy state ─────────────────────────────────────────────────────
  if (Get-Command Set-AppRunState -ErrorAction SilentlyContinue) {
    try {
      [void](Set-AppRunState -State 'Starting')
    } catch {
      try { [void](Set-AppRunState -State 'Idle' -Force) } catch { }
      Add-WpfLogLine -Ctx $Ctx -Line ("RunState-Fehler (Starting): {0}" -f $_.Exception.Message) -Level 'ERROR'
      return
    }
  }

  Set-WpfBusyState -Ctx $Ctx -IsBusy $true -Hint '⟳ Läuft...'
  # Hide Move button and Rollback banner during run
  if ($Ctx.ContainsKey('btnStartMove') -and $Ctx['btnStartMove']) {
    $Ctx['btnStartMove'].Visibility = [System.Windows.Visibility]::Collapsed
  }
  if ($Ctx.ContainsKey('brdMoveCompleteBanner') -and $Ctx['brdMoveCompleteBanner']) {
    $Ctx['brdMoveCompleteBanner'].Visibility = [System.Windows.Visibility]::Collapsed
  }
  # UX-01: Step indicator → running state
  if (Get-Command Update-WpfStepIndicator -ErrorAction SilentlyContinue) {
    Update-WpfStepIndicator -Ctx $Ctx -RunState 'running'
  }
  Update-WpfResultDashboard -Ctx $Ctx -Mode $params.Mode -Winners 0 -Dupes 0 -Junk 0 -Duration '00:00'
  Update-WpfPerfDashboard -Ctx $Ctx -Percent 0 -Throughput '– grp/s' -Eta 'ETA –' -Cache 'Cache: –' -Parallel 'Parallel: –' -Phase 'Initialisierung' -File '–'
  $Ctx['listLog'].Items.Clear()
  if ($Ctx.ContainsKey('listErrorSummary') -and $Ctx['listErrorSummary']) { $Ctx['listErrorSummary'].Items.Clear() }
  Add-WpfLogLine -Ctx $Ctx -Line "=== ROM Cleanup gestartet: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss') ===" -Level 'INFO'
  Add-WpfLogLine -Ctx $Ctx -Line "Modus: $($params.Mode)  |  Roots: $($params.Roots.Count)" -Level 'INFO'

  # ── Run-Parameter Snapshot (Diagnose bei 0-Dateien-Problemen) ────────────
  foreach ($r in @($params.Roots)) {
    $rootExists = Test-Path -LiteralPath $r -PathType Container
    Add-WpfLogLine -Ctx $Ctx -Line ("  Root: {0}  [erreichbar={1}]" -f $r, $rootExists) -Level 'DEBUG'
  }
  $extInfo = if ($params.Extensions) { ($params.Extensions -join ', ') } else { '(alle)' }
  $cfInfo  = if ($params.ConsoleFilter) { ($params.ConsoleFilter -join ', ') } else { '(kein Filter)' }
  Add-WpfLogLine -Ctx $Ctx -Line ("  Extensions: {0}  |  ConsoleFilter: {1}  |  DAT: {2}  |  Convert: {3}" -f $extInfo, $cfInfo, $params.UseDat, $params.ConvertChecked) -Level 'DEBUG'

  # ── Reset cancel state ───────────────────────────────────────────────────
  $script:WpfCancelToken = New-Object System.Threading.CancellationTokenSource
  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    try { Set-AppStateValue -Key 'CancelRequested' -Value $false } catch { } # intentionally ignore transient app-state write failures
  }

  # ── Save settings before run ─────────────────────────────────────────────
  Save-WpfToSettings -Ctx $Ctx

  # ── Build log callback ────────────────────────────────────────────────────
  $runStopwatch = [System.Diagnostics.Stopwatch]::StartNew()
  # Shared mutable state between $logCallback and $timerTick.
  # Using a hashtable so that property mutations are visible across
  # .GetNewClosure() boundaries (the object reference is captured, not copied).
  $pState = @{
    Percent             = [double]0
    Phase               = 'Initialisierung'
    Item                = ''
    GroupDone           = 0
    CachePerfText       = 'Cache: –'
    ParallelWorkers     = 0
    ParallelChunksDone  = 0
    ParallelChunksTotal = 0
    ParallelPerfText    = 'Parallel: –'
    ParallelUtilPercent = [double]-1
    LastLogLineAt       = [System.Diagnostics.Stopwatch]::StartNew()
    QueueDrainErrorLogged = $false
  }

  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    try {
      [void](Set-AppStateValue -Key 'WpfOperationState' -Value ([pscustomobject]@{
        Status = 'Running'
        Phase = $pState.Phase
        ProgressPercent = [double]0
        StartedAtUtc = [DateTime]::UtcNow
      }))
    } catch { }
  }

  $logCallback = {
    param([string]$Line)
    $pState.LastLogLineAt.Restart()
    $lvl = 'INFO'
    if ($Line -match '^(\s*)ERROR:')   { $lvl = 'ERROR' }
    elseif ($Line -match '^(\s*)WARN:') { $lvl = 'WARN' }
    elseif ($Line -match '^(\s*)DEBUG:') { $lvl = 'DEBUG' }

    if ($Line -match '^===\s*(.+?)\s*===\s*$') {
      $pState.Phase = [string]$Matches[1]
    }

    # Scan-Root-Marker: zeigt aktuellen Root im Phasen-Label
    if ($Line -like '__SCANROOT__:*') {
      $scanRootPath = [string]$Line.Substring(13)
      try {
        $scanRootName = [System.IO.Path]::GetFileName($scanRootPath.TrimEnd('\','/'))
        if (-not [string]::IsNullOrWhiteSpace($scanRootName)) {
          $pState.Phase = ('Scan: {0}' -f $scanRootName)
        } else {
          $pState.Phase = 'Scan'
        }
      } catch { $pState.Phase = 'Scan' }
      $pState.Item = ''
      return
    }

    # Classify-File-Marker: zeigt aktuelle Datei im Dashboard
    if ($Line -like '__CLASSIFYFILE__:*') {
      $classifyPath = [string]$Line.Substring(17)
      try {
        $leaf = [System.IO.Path]::GetFileName($classifyPath)
        if (-not [string]::IsNullOrWhiteSpace($leaf)) { $pState.Item = $leaf }
      } catch { }
      return
    }

    if ($Line -like '__DATHASH__:*') {
      $payload = [string]$Line.Substring(12)
      if ($payload -match '^(\d+)\/(\d+)$') {
        $current = [int]$Matches[1]
        $total = [int]$Matches[2]
        if ($total -gt 0) {
          $datPercent = [Math]::Round((100.0 * $current / $total), 1)
          $phaseFloor = 5.0
          $phaseSpan = 80.0
          $pState.Percent = [Math]::Max($pState.Percent, [Math]::Min(95.0, $phaseFloor + (($datPercent / 100.0) * $phaseSpan)))
          $pState.Phase = ('DAT-Hashing {0}/{1}' -f $current, $total)
          $pState.Item = ('Hash-Fortschritt {0}%' -f $datPercent)
        }
      }
      return
    }

    if ($Line -match 'Dedupe-Fortschritt:\s*(\d+)\/(\d+)\s+Gruppen') {
      $pState.GroupDone = [int]$Matches[1]
      $pState.Phase = 'Auswahl'
    }

    # Gruppierung-Fortschritt parsen (zeigt aktuellen Game-Key)
    if ($Line -match 'Gruppierung:\s*\d+/\d+\s+\([0-9.,]+%\)\s+–\s+(.+)$') {
      $groupItem = [string]$Matches[1]
      if (-not [string]::IsNullOrWhiteSpace($groupItem) -and $groupItem -ne '–') {
        $pState.Item = $groupItem
      }
      $pState.Phase = 'Gruppierung'
    }

    # Verschiebe-Phase erkennen
    if ($Line -match 'Verschiebe\s+\d+\s+Eintraege') {
      $pState.Phase = 'Verschieben'
      $pState.Item = ''
    }

    # Move-Fortschritt mit Dateinamen parsen
    if ($Line -match '\.\.\.\s+\d+/\d+\s+\([0-9.,]+%\)\s+–\s+(.+)$') {
      $moveItem = [string]$Matches[1]
      if (-not [string]::IsNullOrWhiteSpace($moveItem) -and $moveItem -ne '–') {
        $pState.Item = $moveItem
      }
    }

    if ($Line -match 'Parallel:\s+\d+\s+Dateien\s+in\s+(\d+)\s+Chunks\s+.+\((\d+)\s+Worker\)') {
      $pState.ParallelChunksTotal = [int]$Matches[1]
      $pState.ParallelWorkers = [int]$Matches[2]
      $pState.ParallelChunksDone = 0
      $pState.ParallelPerfText = ('Parallel: {0} Worker • 0/{1} Chunks' -f $pState.ParallelWorkers, $pState.ParallelChunksTotal)
    }

    if ($Line -match 'Parallel:\s+Chunk\s+(\d+)\/(\d+)\s+fertig') {
      $pState.ParallelChunksDone = [int]$Matches[1]
      $pState.ParallelChunksTotal = [int]$Matches[2]
      if ($pState.ParallelWorkers -gt 0) {
        $pState.ParallelPerfText = ('Parallel: {0} Worker • {1}/{2} Chunks' -f $pState.ParallelWorkers, $pState.ParallelChunksDone, $pState.ParallelChunksTotal)
      } else {
        $pState.ParallelPerfText = ('Parallel: {0}/{1} Chunks' -f $pState.ParallelChunksDone, $pState.ParallelChunksTotal)
      }
    }

    if ($Line -match 'PERF:\s+(?:PreHash)?ParallelActive=(\d+)\/(\d+)\s+\(([0-9]+(?:[\.,][0-9]+)?)%\)\s+Queue=(\d+)\s+Completed=(\d+)\/(\d+)') {
      $active = [int]$Matches[1]
      $maxWorkers = [int]$Matches[2]
      $util = ([string]$Matches[3]).Replace(',', '.')
      $utilValue = [double]0
      if (-not [double]::TryParse($util, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$utilValue)) {
        $utilValue = 0
      }
      $queue = [int]$Matches[4]
      $done = [int]$Matches[5]
      $total = [int]$Matches[6]

      $pState.ParallelWorkers = $maxWorkers
      $pState.ParallelChunksDone = $done
      $pState.ParallelChunksTotal = $total
      $pState.ParallelUtilPercent = $utilValue
      $pState.ParallelPerfText = ('Parallel: {0}/{1} aktiv ({2}%) • Queue {3} • {4}/{5}' -f $active, $maxWorkers, $util, $queue, $done, $total)
    }

    if ($Line -match 'PERF:\s+CacheHitRate=([0-9]+(?:[\.,][0-9]+)?)%\s+Hits=(\d+)\s+Misses=(\d+)\s+Entries=(\d+)/(\d+)') {
      $rate = ([string]$Matches[1]).Replace(',', '.')
      $pState.CachePerfText = ('Cache: {0}% ({1}/{2})' -f $rate, [int]$Matches[4], [int]$Matches[5])
    }

    if ($Line -match '([A-Za-z]:\\[^\r\n\)]+)') {
      $pathCandidate = [string]$Matches[1]
      try {
        $leaf = [System.IO.Path]::GetFileName($pathCandidate)
        if (-not [string]::IsNullOrWhiteSpace($leaf)) {
          $pState.Item = $leaf
        }
      } catch { } # intentionally ignore invalid path-like substrings in log lines
    }

    if ($Line -match '\((\d+[\.,]?\d*)%\)') {
      $pctRaw = [string]$Matches[1]
      $pctRaw = $pctRaw.Replace(',', '.')
      $parsedPct = [double]0
      if ([double]::TryParse($pctRaw, [System.Globalization.NumberStyles]::Float, [System.Globalization.CultureInfo]::InvariantCulture, [ref]$parsedPct)) {
        if ($parsedPct -gt $pState.Percent) {
          $pState.Percent = [Math]::Min(99, $parsedPct)
        }
      }
    }

    # logCallback wird vom DispatcherTimer.Tick auf dem UI-Thread aufgerufen –
    # BeginInvoke ist nicht nötig und würde die PS-Variablen aus dem Scope verlieren.
    try {
      Add-WpfLogLine -Ctx $Ctx -Line $Line -Level $lvl
    } catch {
      Write-Warning ("Add-WpfLogLine-Fehler: {0}" -f $_.Exception.Message)
    }
  }.GetNewClosure()

  # ── Start background runspace ─────────────────────────────────────────────
  $job = $null
  $operationCompleted = $false
  try {
    if (Get-Command Start-BackgroundDedupe -ErrorAction SilentlyContinue) {
      $job = Start-BackgroundDedupe -DedupeParams $params
      # BUG GUI-001 FIX: Store job in Ctx so $requestCancel can signal its CancelEvent
      $Ctx['_activeBackgroundJob'] = $job
      if (Get-Command Set-AppRunState -ErrorAction SilentlyContinue) {
        try { [void](Set-AppRunState -State 'Running') } catch { }
      }

      # NOTE:
      # ThreadPool RegisterWaitForSingleObject with a PowerShell scriptblock callback
      # can crash pwsh (.NET Runtime 1026 / PSInvalidOperationException: no Runspace on thread).
      # Completion is therefore detected via the existing safe polling fallback below.
    } else {
      Add-WpfLogLine -Ctx $Ctx -Line 'Fehler: Start-BackgroundDedupe nicht gefunden.' -Level 'ERROR'
      if (Get-Command Set-AppRunState -ErrorAction SilentlyContinue) {
        try { [void](Set-AppRunState -State 'Failed') } catch { }
      }
      Set-WpfBusyState -Ctx $Ctx -IsBusy $false
      return
    }
  } catch {
    Add-WpfLogLine -Ctx $Ctx -Line "Fehler beim Starten: $($_.Exception.Message)" -Level 'ERROR'
    if (Get-Command Set-AppRunState -ErrorAction SilentlyContinue) {
      try { [void](Set-AppRunState -State 'Failed') } catch { }
    }
    Set-WpfBusyState -Ctx $Ctx -IsBusy $false
    return
  }

  # ── Poll via DispatcherTimer ──────────────────────────────────────────────
  $timer = New-Object System.Windows.Threading.DispatcherTimer
  $timer.Interval = [System.TimeSpan]::FromMilliseconds(250)

  $timerTick = {
    try {
      # BUG-018 FIX: Wrap Update-WpfRuntimeStatus in its own try/catch so failure
      # doesn't prevent log drain and completion detection below
      try {
        Update-WpfRuntimeStatus -Ctx $Ctx -Elapsed $runStopwatch.Elapsed
      } catch {
        # Non-fatal: status update failure should not block the rest of the tick
      }

    try {
      if ($job -and $job.PSObject.Properties['LogQueue'] -and $job.LogQueue) {
        $queuedLine = $null
        # BUG GUI-005 FIX: Limit drain to 200 entries per tick to prevent UI freeze
        $drainCount = 0
        $drainLimit = 200
        while ($drainCount -lt $drainLimit -and $job.LogQueue.TryDequeue([ref]$queuedLine)) {
          if (-not [string]::IsNullOrWhiteSpace([string]$queuedLine)) {
            & $logCallback ([string]$queuedLine)
          }
          $drainCount++
        }
      }
    } catch {
      if (-not $pState.QueueDrainErrorLogged) {
        $pState.QueueDrainErrorLogged = $true
        Add-WpfLogLine -Ctx $Ctx -Line ("Fehler beim Verarbeiten der Hintergrund-Logs: {0}" -f $_.Exception.Message) -Level 'WARN'
      }
    }

    # Check for completion (event-first, polling fallback)
    $isDone = [bool]$operationCompleted
    if (-not $isDone) {
      try {
        if ($job -and $job.PSObject.Properties['Handle'] -and $job.Handle) {
          $isDone = [bool]$job.Handle.IsCompleted
        } elseif ($job -and $job.PSObject.Properties['IsCompleted'] -and $job.IsCompleted) {
          $isDone = $true
        } elseif ($job -and $job.PSObject.Properties['Runspace'] -and
                  $job.Runspace.RunspaceAvailability -ne [System.Management.Automation.Runspaces.RunspaceAvailability]::Busy) {
          $isDone = $true
        } elseif (-not $job) {
          $isDone = $true
        }
      } catch {
        $isDone = $true
      }
    }

    if ($isDone) {
      $timer.Stop()
      $runStopwatch.Stop()

      # Collect result
      $success = $false
      $wasCanceled = $false
      $resultPayload = $null
      try {
        if ($job -and $job.PSObject.Properties['Handle'] -and $job.PSObject.Properties['PS']) {
          $resultPayload = $job.PS.EndInvoke($job.Handle)
          # Report any non-terminating background errors that didn't throw
          if ($job.PS.HadErrors) {
            $bgErrors = @($job.PS.Streams.Error)
            if ($bgErrors.Count -gt 0) {
              $success = $false
              foreach ($bgErr in ($bgErrors | Select-Object -First 8)) {
                $errMsg = if ($bgErr.Exception) { $bgErr.Exception.Message } else { [string]$bgErr }
                Add-WpfLogLine -Ctx $Ctx -Line ("Hintergrund-Fehler: {0}" -f $errMsg) -Level 'ERROR'
              }
            } else {
              $success = $true
            }
          } else {
            $success = $true
          }
        } elseif ($job -and $job.PSObject.Properties['AsyncResult']) {
          $resultPayload = $job.PowerShell.EndInvoke($job.AsyncResult)
          $success = $true
        } else {
          $success = $true
        }
      } catch {
        $wasCanceled = [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false)
        Add-WpfLogLine -Ctx $Ctx -Line "Fehler: $($_.Exception.Message)" -Level 'ERROR'
      }

      if (-not $wasCanceled) {
        try {
          $wasCanceled = [bool](Get-AppStateValue -Key 'CancelRequested' -Default $false)
        } catch { }
      }

      if ($wasCanceled) {
        Add-WpfLogLine -Ctx $Ctx -Line '=== Abgebrochen ===' -Level 'WARN'
      } else {
        $endMsg = if ($success) { '=== Abgeschlossen ===' } else { '=== Mit Fehler beendet ===' }
        Add-WpfLogLine -Ctx $Ctx -Line $endMsg -Level $(if ($success) { 'INFO' } else { 'ERROR' })
      }
      Set-WpfBusyState -Ctx $Ctx -IsBusy $false

      # UX-01: Step indicator → complete or idle
      if (Get-Command Update-WpfStepIndicator -ErrorAction SilentlyContinue) {
        $stepState = if ($wasCanceled) { 'idle' } elseif ($success) { 'complete' } else { 'idle' }
        Update-WpfStepIndicator -Ctx $Ctx -RunState $stepState
      }

      if ($wasCanceled) {
        # DUP-03: Consolidated dashboard update
        Update-WpfDashboardComplete -Ctx $Ctx -Status 'Canceled' -Phase 'Abgebrochen' -Mode $params.Mode `
          -Percent $pState.Percent -Duration (Format-WpfDuration -Span $runStopwatch.Elapsed) `
          -BusyHint ('Abgebrochen nach {0}' -f (Format-WpfDuration -Span $runStopwatch.Elapsed)) `
          -Cache $pState.CachePerfText -Parallel $pState.ParallelPerfText `
          -ParallelUtilPercent $pState.ParallelUtilPercent `
          -File $(if ([string]::IsNullOrWhiteSpace($pState.Item)) { '–' } else { $pState.Item })
      } elseif ($success) {
        try {
          $resultObj = if ($resultPayload -is [System.Array] -and $resultPayload.Count -gt 0) { $resultPayload[0] } else { $resultPayload }
          $rows = @()
          $winners = @()
          if ($resultObj -and ($resultObj.PSObject.Properties.Name -contains 'ReportRows')) { $rows = @($resultObj.ReportRows) }
          if ($resultObj -and ($resultObj.PSObject.Properties.Name -contains 'Winners')) { $winners = @($resultObj.Winners) }
          # Store actual report path from the result for preview/buttons
          if ($resultObj -and ($resultObj.PSObject.Properties.Name -contains 'HtmlPath') -and $resultObj.HtmlPath) {
            $Ctx['_lastReportHtmlPath'] = [string]$resultObj.HtmlPath
          }
          # Show Move button after successful DryRun
          if ($params.Mode -eq 'DryRun' -and $Ctx.ContainsKey('btnStartMove') -and $Ctx['btnStartMove']) {
            $Ctx['btnStartMove'].Visibility = [System.Windows.Visibility]::Visible
          }
          # UX-11: Show inline Rollback banner after successful Move
          if ($params.Mode -eq 'Move' -and $Ctx.ContainsKey('brdMoveCompleteBanner') -and $Ctx['brdMoveCompleteBanner']) {
            $moveCount = 0
            try {
              if ($Ctx.ContainsKey('lblDashDupes') -and $Ctx['lblDashDupes']) { $moveCount += [int]([string]$Ctx['lblDashDupes'].Text) }
              if ($Ctx.ContainsKey('lblDashJunk') -and $Ctx['lblDashJunk'])   { $moveCount += [int]([string]$Ctx['lblDashJunk'].Text) }
            } catch { }
            if ($Ctx.ContainsKey('lblMoveCompleteInfo') -and $Ctx['lblMoveCompleteInfo']) {
              $Ctx['lblMoveCompleteInfo'].Text = if ($moveCount -gt 0) {
                "Move abgeschlossen: $moveCount Dateien verschoben."
              } else {
                'Move abgeschlossen.'
              }
            }
            $Ctx['brdMoveCompleteBanner'].Visibility = [System.Windows.Visibility]::Visible
          }

          if ($rows.Count -gt 0) {
            $dupeActions = @('MOVE','SKIP_DRYRUN')
            $junkActions = @('JUNK','DRYRUN-JUNK')
            $dupCount = @($rows | Where-Object { $_.Category -eq 'GAME' -and $dupeActions -contains ([string]$_.Action).ToUpperInvariant() }).Count
            $junkCount = @($rows | Where-Object { $junkActions -contains ([string]$_.Action).ToUpperInvariant() }).Count
            $summary = ('Ergebnis: {0} Winner, {1} Duplikate, {2} Junk | Laufzeit {3}' -f $winners.Count, $dupCount, $junkCount, (Format-WpfDuration -Span $runStopwatch.Elapsed))
            Add-WpfLogLine -Ctx $Ctx -Line $summary -Level 'INFO'
            $throughputFinal = if ($runStopwatch.Elapsed.TotalSeconds -gt 0 -and $pState.GroupDone -gt 0) {
              ('{0:N1} grp/s' -f ($pState.GroupDone / $runStopwatch.Elapsed.TotalSeconds))
            } else { '– grp/s' }
            # DUP-03: Consolidated dashboard update
            Update-WpfDashboardComplete -Ctx $Ctx -Status 'Completed' -Phase 'Abgeschlossen' -Mode $params.Mode `
              -Percent 100 -Duration (Format-WpfDuration -Span $runStopwatch.Elapsed) -BusyHint $summary `
              -Winners $winners.Count -Dupes $dupCount -Junk $junkCount `
              -Throughput $throughputFinal -Eta 'ETA 00:00' `
              -Cache $pState.CachePerfText -Parallel $pState.ParallelPerfText `
              -ParallelUtilPercent $pState.ParallelUtilPercent `
              -File $(if ([string]::IsNullOrWhiteSpace($pState.Item)) { '–' } else { $pState.Item })
          } else {
            Update-WpfDashboardComplete -Ctx $Ctx -Status 'Completed' -Phase 'Abgeschlossen' -Mode $params.Mode `
              -Percent 100 -Duration (Format-WpfDuration -Span $runStopwatch.Elapsed) `
              -BusyHint ('Abgeschlossen in {0}' -f (Format-WpfDuration -Span $runStopwatch.Elapsed)) `
              -Cache $pState.CachePerfText -Parallel $pState.ParallelPerfText `
              -ParallelUtilPercent $pState.ParallelUtilPercent `
              -File $(if ([string]::IsNullOrWhiteSpace($pState.Item)) { '–' } else { $pState.Item })
          }
        } catch {
          Update-WpfDashboardComplete -Ctx $Ctx -Status 'Completed' -Phase 'Abgeschlossen' -Mode $params.Mode `
            -Percent 100 -Duration (Format-WpfDuration -Span $runStopwatch.Elapsed) `
            -BusyHint ('Abgeschlossen in {0}' -f (Format-WpfDuration -Span $runStopwatch.Elapsed)) `
            -Cache $pState.CachePerfText -Parallel $pState.ParallelPerfText `
            -ParallelUtilPercent $pState.ParallelUtilPercent `
            -File $(if ([string]::IsNullOrWhiteSpace($pState.Item)) { '–' } else { $pState.Item })
        }
      } else {
        # DUP-03: Consolidated dashboard update for failure
        Update-WpfDashboardComplete -Ctx $Ctx -Status 'Failed' -Phase 'Fehler' -Mode $params.Mode `
          -Percent $pState.Percent -Duration (Format-WpfDuration -Span $runStopwatch.Elapsed) `
          -BusyHint ('Mit Fehler beendet nach {0}' -f (Format-WpfDuration -Span $runStopwatch.Elapsed)) `
          -Cache $pState.CachePerfText -Parallel $pState.ParallelPerfText `
          -ParallelUtilPercent $pState.ParallelUtilPercent `
          -File $(if ([string]::IsNullOrWhiteSpace($pState.Item)) { '–' } else { $pState.Item })
      }

      # Enable Rollback button if audit files exist
      try {
        $auditDir = $params.AuditRoot
        if (-not [string]::IsNullOrWhiteSpace($auditDir) -and (Test-Path -LiteralPath $auditDir)) {
          $auditFiles = @(Get-ChildItem -LiteralPath $auditDir -Filter '*.csv' -ErrorAction SilentlyContinue)
          $Ctx['btnRollback'].IsEnabled = $auditFiles.Count -gt 0
          # Visual pulse on rollback button after Move run completes
          if ($params.Mode -eq 'Move' -and $Ctx['btnRollback'].IsEnabled) {
            try {
              $Ctx['btnRollback'].Background = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSurfaceLight' -Fallback '#3A1A4A'
              $pulseTimer = New-Object System.Windows.Threading.DispatcherTimer
              $pulseTimer.Interval = [TimeSpan]::FromSeconds(3)
              $pulseTimer.add_Tick({
                try {
                  $Ctx['btnRollback'].Background = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSurface' -Fallback '#1A0A20'
                  $this.Stop()
                } catch { $this.Stop() }
              }.GetNewClosure())
              $pulseTimer.Start()
            } catch { }
          }
        }
      } catch { } # intentionally keep rollback-button refresh non-fatal

      # DUP-04: Consolidated dispose
      Dispose-WpfBackgroundJob -Job $job
      # BUG GUI-001 FIX: Clear the background job reference
      if ($Ctx.ContainsKey('_activeBackgroundJob')) { $Ctx['_activeBackgroundJob'] = $null }

      # BUG GUI-002 FIX: Transition through the correct RunState before going to Idle.
      # Running → Completed/Failed/Canceled → Idle (direct Running → Idle is invalid)
      if (Get-Command Set-AppRunState -ErrorAction SilentlyContinue) {
        try {
          $intermediateState = if ($wasCanceled) { 'Canceled' } elseif ($success) { 'Completed' } else { 'Failed' }
          try { [void](Set-AppRunState -State $intermediateState) } catch { }
          [void](Set-AppRunState -State 'Idle')
        } catch {
          # Force-reset if transition chain fails
          try { [void](Set-AppRunState -State 'Idle' -Force) } catch { }
        }
      }
      # Reset CancelRequested so the next run can proceed
      if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
        try { Set-AppStateValue -Key 'CancelRequested' -Value $false } catch { }
      }

      Update-WpfProgress -Ctx $Ctx -Percent 100 -Detail 'Fertig'
      Update-WpfErrorSummaryFromLog -Ctx $Ctx
      Update-WpfRuntimeStatus -Ctx $Ctx -Elapsed $runStopwatch.Elapsed
      # Refresh report preview after run completes
      if ($Ctx.ContainsKey('_refreshReportPreview') -and $Ctx['_refreshReportPreview']) {
        try { & $Ctx['_refreshReportPreview'] } catch { }
      }
      return
    }

    $elapsed = $runStopwatch.Elapsed
    if ($pState.Percent -gt 0) {
      $etaText = ''
      $etaDash = 'ETA –'
      if ($pState.Percent -ge 1) {
        $estimatedTotalSeconds = $elapsed.TotalSeconds / ($pState.Percent / 100)
        $remainingSeconds = [Math]::Max(0, $estimatedTotalSeconds - $elapsed.TotalSeconds)
        $remaining = [TimeSpan]::FromSeconds($remainingSeconds)
        $etaText = (' • ETA {0}' -f (Format-WpfDuration -Span $remaining))
        $etaDash = ('ETA {0}' -f (Format-WpfDuration -Span $remaining))
      }
      $throughput = if ($elapsed.TotalSeconds -gt 0 -and $pState.GroupDone -gt 0) {
        ('{0:N1} grp/s' -f ($pState.GroupDone / $elapsed.TotalSeconds))
      } else { '– grp/s' }
      $itemText = if (-not [string]::IsNullOrWhiteSpace($pState.Item)) { (' • {0}' -f $pState.Item) } else { '' }
      $activityHint = if ($pState.LastLogLineAt.ElapsedMilliseconds -gt 5000) { ' • ⏳' } else { '' }
      $progressDetail = ('{0}% • {1}{2} • seit {3}{4}{5}' -f [Math]::Round($pState.Percent, 1), $pState.Phase, $itemText, (Format-WpfDuration -Span $elapsed), $etaText, $activityHint)
      if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
        try {
          [void](Set-AppStateValue -Key 'WpfOperationState' -Value ([pscustomobject]@{
            Status = 'Running'
            Phase = $pState.Phase
            ProgressPercent = [double]$pState.Percent
            ElapsedSeconds = [Math]::Round($elapsed.TotalSeconds, 1)
            UpdatedAtUtc = [DateTime]::UtcNow
          }))
        } catch { }
      }
      Update-WpfProgress -Ctx $Ctx -Percent ([int][Math]::Min(99, [Math]::Round($pState.Percent, 0))) -Detail $progressDetail
      $Ctx['lblBusyHint'].Text = ('{0}{1} • seit {2}{3}' -f $pState.Phase, $itemText, (Format-WpfDuration -Span $elapsed), $activityHint)
      Update-WpfPerfDashboard -Ctx $Ctx -Percent $pState.Percent -Throughput $throughput -Eta $etaDash -Cache $pState.CachePerfText -Parallel $pState.ParallelPerfText -ParallelUtilPercent $pState.ParallelUtilPercent -Phase $pState.Phase -File $(if ([string]::IsNullOrWhiteSpace($pState.Item)) { '–' } else { $pState.Item })
    } else {
      $activityHint = if ($pState.LastLogLineAt.ElapsedMilliseconds -gt 5000) { ' • ⏳ Verarbeitung läuft…' } else { '' }
      $itemText = if (-not [string]::IsNullOrWhiteSpace($pState.Item)) { (' • {0}' -f $pState.Item) } else { '' }
      Update-WpfProgress -Ctx $Ctx -Indeterminate -Detail ('{0}{1} • seit {2}{3}' -f $pState.Phase, $itemText, (Format-WpfDuration -Span $elapsed), $activityHint)
      $Ctx['lblBusyHint'].Text = ('{0}{1} • seit {2} • ETA wird berechnet…{3}' -f $pState.Phase, $itemText, (Format-WpfDuration -Span $elapsed), $activityHint)
      Update-WpfPerfDashboard -Ctx $Ctx -Percent 0 -Throughput '– grp/s' -Eta 'ETA wird berechnet…' -Cache $pState.CachePerfText -Parallel $pState.ParallelPerfText -ParallelUtilPercent $pState.ParallelUtilPercent -Phase $pState.Phase -File $(if ([string]::IsNullOrWhiteSpace($pState.Item)) { '–' } else { $pState.Item })
    }
    } catch {
      try { $timer.Stop() } catch { }
      try { $runStopwatch.Stop() } catch { }

      Add-WpfLogLine -Ctx $Ctx -Line ("Kritischer UI-Laufzeitfehler: {0}" -f $_.Exception.Message) -Level 'ERROR'
      if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
        try {
          [void](Set-AppStateValue -Key 'LastUnhandledWpfException' -Value ([string]$_.Exception.ToString()))
        } catch { }
      }
      # DUP-03: Consolidated dashboard update for critical error
      Update-WpfDashboardComplete -Ctx $Ctx -Status 'Failed' -Phase 'Fehler' `
        -Percent $pState.Percent `
        -BusyHint ('Mit Fehler beendet nach {0}' -f (Format-WpfDuration -Span $runStopwatch.Elapsed))

      # DUP-04: Consolidated dispose
      Dispose-WpfBackgroundJob -Job $job

      Set-WpfBusyState -Ctx $Ctx -IsBusy $false
    }
  }.GetNewClosure()

  $timer.add_Tick($timerTick)
  $timer.Start()
}
