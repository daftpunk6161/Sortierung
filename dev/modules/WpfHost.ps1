# ================================================================
#  WpfHost.ps1  –  Bootstrap the WPF main window
#  Exports: Start-WpfMainWindow (creates, wires, returns window)
# ================================================================

function Initialize-WpfAssemblies {
  <# Load WPF assemblies into the current PS session. #>
  foreach ($asm in @(
    'PresentationCore',
    'PresentationFramework',
    'WindowsBase',
    'System.Xaml'
  )) {
    try {
      Add-Type -AssemblyName $asm -ErrorAction SilentlyContinue
    } catch { } # intentionally ignore optional/duplicate assembly load failures
  }
}

function New-WpfWindowFromXaml {
  <#
  .SYNOPSIS
    Parses a XAML string and returns the Window object.
  .PARAMETER Xaml
    Full XAML string for the window (no x:Class attribute needed).
  .NOTES
    Kompatibilität: Vor dem Parsen wird das Legacy-Attribut `Grid.Col`
    automatisch auf `Grid.Column` normalisiert. Dadurch bleiben alte
    oder gecachte XAML-Varianten lauffähig, ohne den Start zu blockieren.
  .OUTPUTS
    [System.Windows.Window]
  #>
  param([Parameter(Mandatory)][string]$Xaml)

  # Compatibility guard: normalize legacy typo from older/stale XAML payloads
  # so runtime startup does not crash in long-lived shells.
  if (-not [string]::IsNullOrWhiteSpace($Xaml)) {
    $Xaml = [System.Text.RegularExpressions.Regex]::Replace(
      $Xaml,
      '(?i)\bGrid\.Col\b(?=\s*=)',
      'Grid.Column'
    )
  }

  # XamlReader.Parse is the simplest way to load XAML without
  # requiring a code-behind class (no x:Class → no .NET type needed).
  try {
    return [System.Windows.Markup.XamlReader]::Parse($Xaml)
  } catch {
    throw ('WpfHost: XAML-Parsing fehlgeschlagen: {0}' -f $_.Exception.Message)
  }
}

function Get-WpfNamedElements {
  <#
  .SYNOPSIS
    Builds a hashtable of all named elements in the Window for easy access.
    Equivalent to the $ctx hashtable used in the WinForms GuiHandlers.
  .PARAMETER Window
    The root Window object.
  .OUTPUTS
    [hashtable]  Name → FrameworkElement
  #>
  param([Parameter(Mandatory)][System.Windows.Window]$Window)

  $map = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)

  # Walk the logical tree and collect named elements
  $queue = New-Object System.Collections.Generic.Queue[System.Windows.DependencyObject]
  [void]$queue.Enqueue($Window)

  while ($queue.Count -gt 0) {
    $node = $queue.Dequeue()
    if ($null -eq $node) { continue }

    # Collect name if set
    $name = $null
    try {
      $fe = $node -as [System.Windows.FrameworkElement]
      if ($fe -and -not [string]::IsNullOrWhiteSpace($fe.Name)) {
        $name = $fe.Name
      }
    } catch { } # intentionally ignore non-framework logical nodes
    if ($name) { $map[$name] = $node }

    # Enqueue logical children
    try {
      foreach ($child in [System.Windows.LogicalTreeHelper]::GetChildren($node)) {
        $dep = $child -as [System.Windows.DependencyObject]
        if ($dep) { [void]$queue.Enqueue($dep) }
      }
    } catch { } # intentionally ignore nodes without logical children
  }

  return $map
}

function Resolve-WpfThemeBrush {
  <#
  .SYNOPSIS
    Resolves a theme brush from the Window's resources, falling back to a hardcoded value.
    Ensures UI colors follow the active theme (Dark/Light).
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Ctx,
    [Parameter(Mandatory)][string]$BrushKey,
    [string]$Fallback = '#888888'
  )
  $brush = $null
  if ($Ctx.ContainsKey('Window') -and $Ctx['Window']) {
    $brush = $Ctx['Window'].TryFindResource($BrushKey)
  }
  if ($brush -is [System.Windows.Media.SolidColorBrush]) { return $brush }
  return [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString($Fallback))
}

function Update-WpfStatusBar {
  <#
  .SYNOPSIS
    Refreshes the top status-bar indicators based on current UI state.
    Includes a combined "overall readiness" traffic-light indicator.
  #>
  param(
    [hashtable]$Ctx,
    [switch]$Initial
  )

  try {
    $rootCount = 0
    if ($Ctx.ContainsKey('__rootsCollection') -and $Ctx['__rootsCollection'] -and $Ctx['__rootsCollection'].Count -gt 0) {
      $rootCount = $Ctx['__rootsCollection'].Count
    } elseif ($Ctx.ContainsKey('listRoots') -and $Ctx['listRoots']) {
      $rootCount = @($Ctx['listRoots'].Items).Count
    }
    $rootsOk = ($rootCount -gt 0)
    $Ctx['dotRoots'].Fill    = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $(if ($rootsOk) { 'BrushSuccess' } else { 'BrushDanger' })
    $Ctx['lblStatusRoots'].Text = "Roots: $rootCount"

    # Tools status: check if chdman or 7z is configured and valid
    $toolOk = -not [string]::IsNullOrWhiteSpace([string]$Ctx['txtChdman'].Text) -or
              -not [string]::IsNullOrWhiteSpace([string]$Ctx['txt7z'].Text)
    $toolValid = $true
    foreach ($tc in @('txtChdman','txt7z','txtDolphin','txtPsxtract','txtCiso')) {
      if (-not $Ctx.ContainsKey($tc) -or -not $Ctx[$tc]) { continue }
      $tp = [string]$Ctx[$tc].Text
      if (-not [string]::IsNullOrWhiteSpace($tp) -and -not (Test-Path -LiteralPath $tp -PathType Leaf)) {
        $toolValid = $false
      }
    }
    $Ctx['dotTools'].Fill    = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $(if ($toolOk -and $toolValid) { 'BrushSuccess' } else { 'BrushWarning' })
    $Ctx['lblStatusTools'].Text = if ($toolOk -and $toolValid) { 'Tools: OK' } elseif ($toolOk) { 'Tools: ?' } else { 'Tools: –' }

    # DAT status
    $datEnabled = ($Ctx['chkDatUse'].IsChecked -eq $true)
    $datOk = $datEnabled -and -not [string]::IsNullOrWhiteSpace([string]$Ctx['txtDatRoot'].Text)
    $Ctx['dotDat'].Fill     = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $(if (-not $datEnabled) { 'BrushTextMuted' } elseif ($datOk) { 'BrushSuccess' } else { 'BrushDanger' })
    $Ctx['lblStatusDat'].Text   = if (-not $datEnabled) { 'DAT: aus' } elseif ($datOk) { 'DAT: OK' } else { 'DAT: –' }

    # Overall readiness indicator (traffic light)
    if ($Ctx.ContainsKey('dotReady') -and $Ctx['dotReady']) {
      $overallBrushKey = 'BrushDanger'
      $statusText = 'Blockiert'
      $toolsResolved = $toolOk -and $toolValid
      $datResolved = (-not $datEnabled) -or $datOk
      if ($rootsOk -and $toolsResolved -and $datResolved) {
        $overallBrushKey = 'BrushSuccess'
        $statusText = 'Bereit'
      } elseif ($rootsOk) {
        $overallBrushKey = 'BrushWarning'
        $statusText = 'Warnung'
      }
      $Ctx['dotReady'].Fill = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $overallBrushKey
      if ($Ctx.ContainsKey('lblStatusReady') -and $Ctx['lblStatusReady']) {
        $Ctx['lblStatusReady'].Text = $statusText
        $Ctx['lblStatusReady'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $overallBrushKey
      }
    }
  } catch { } # intentionally keep status refresh non-fatal if controls are not yet available
}

function Add-WpfLogLine {
  <#
  .SYNOPSIS
    Appends a line to the log ListBox and scrolls to the bottom.
    Thread-safe: uses Dispatcher.BeginInvoke if called from another thread.
  #>
  param(
    [hashtable]$Ctx,
    [string]$Line,
    [string]$Level = 'INFO'
  )

  $listLog = $Ctx['listLog']
  if (-not $listLog) { return }

  $lineText = [string]$Line
  $levelText = if ([string]::IsNullOrWhiteSpace([string]$Level)) { 'INFO' } else { [string]$Level }

  $addSb = {
    $brushKey = switch ($levelText.ToUpperInvariant()) {
      'ERROR' { 'BrushDanger' }
      'WARN'  { 'BrushWarning' }
      'DEBUG' { 'BrushTextMuted' }
      default { 'BrushAccentCyan' }
    }
    $fallbackColor = switch ($levelText.ToUpperInvariant()) {
      'ERROR' { '#FF0044' }
      'WARN'  { '#FFB700' }
      'DEBUG' { '#9999CC' }
      default { '#00F5FF' }
    }
    if (-not $listLog.ItemTemplate) {
      # DUP-05: Defensive fallback — canonical LogEntryTemplate is in MainWindow.xaml.
      # This only activates when the inline XAML skeleton (no external file) is used.
      $templateXaml = @"
<DataTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation'
              xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml'>
  <StackPanel Orientation='Horizontal'>
    <TextBlock Text='• ' Foreground='{Binding Brush}'/>
    <TextBlock Text='{Binding Text}' Foreground='{Binding Brush}' FontFamily='Consolas' FontSize='11'/>
  </StackPanel>
</DataTemplate>
"@
      try {
        $listLog.ItemTemplate = [System.Windows.Markup.XamlReader]::Parse($templateXaml)
      } catch { }
    }

    $brush = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey $brushKey -Fallback $fallbackColor
    $item = [pscustomobject]@{
      Text = $lineText
      Level = $levelText.ToUpperInvariant()
      Brush = $brush
    }
    [void]$listLog.Items.Add($item)

    $maxLines = 2000
    $trimTo = 1500
    $trimBatch = 20
    if (Get-Command Get-AppStateValue -ErrorAction SilentlyContinue) {
      try {
        $cfgMax = Get-AppStateValue -Key 'LogMaxLines' -Default $maxLines
        if ($cfgMax -is [int] -or $cfgMax -is [long]) {
          $maxLines = [int][Math]::Max(500, [int64]$cfgMax)
        }
        $cfgTrim = Get-AppStateValue -Key 'LogTrimTo' -Default $trimTo
        if ($cfgTrim -is [int] -or $cfgTrim -is [long]) {
          $trimTo = [int][Math]::Max(200, [Math]::Min([int64]$cfgTrim, [int64]$maxLines - 1))
        }
        $cfgTrimBatch = Get-AppStateValue -Key 'LogTrimBatchSize' -Default $trimBatch
        if ($cfgTrimBatch -is [int] -or $cfgTrimBatch -is [long]) {
          $trimBatch = [int][Math]::Max(1, [Math]::Min([int64]$cfgTrimBatch, 100))
        }
      } catch { }
    }

    if ($listLog.Items.Count -gt $maxLines) {
      $removeCount = [Math]::Min($trimBatch, [Math]::Max(0, ($listLog.Items.Count - $trimTo)))
      for ($idx = 0; $idx -lt $removeCount; $idx++) {
        $listLog.Items.RemoveAt(0)
      }
    }

    $listLog.ScrollIntoView($item)
  }.GetNewClosure()
  $addAction = [System.Action]$addSb

  if ($listLog.Dispatcher.CheckAccess()) {
    $addAction.Invoke()
  } else {
    [void]$listLog.Dispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background, $addAction)
  }
}

function Set-WpfDefaultTooltips {
  <#
  .SYNOPSIS
    Adds fallback tooltips for interactive controls that do not yet define one.
    Existing explicit tooltips in XAML are preserved.
  #>
  param([hashtable]$Ctx)

  if (-not $Ctx) { return }

  foreach ($entry in $Ctx.GetEnumerator()) {
    $name = [string]$entry.Key
    $ctrl = $entry.Value
    if (-not $ctrl) { continue }

    $isInteractive = (
      ($ctrl -is [System.Windows.Controls.Button]) -or
      ($ctrl -is [System.Windows.Controls.TextBox]) -or
      ($ctrl -is [System.Windows.Controls.CheckBox]) -or
      ($ctrl -is [System.Windows.Controls.ComboBox]) -or
      ($ctrl -is [System.Windows.Controls.ListBox]) -or
      ($ctrl -is [System.Windows.Controls.DataGrid]) -or
      ($ctrl -is [System.Windows.Controls.TabItem])
    )
    if (-not $isInteractive) { continue }

    try {
      if ($ctrl.ToolTip -and -not [string]::IsNullOrWhiteSpace([string]$ctrl.ToolTip)) { continue }

      $label = $null
      if ($ctrl -is [System.Windows.Controls.TabItem]) {
        if ($ctrl.Header) { $label = [string]$ctrl.Header }
      } elseif ($ctrl -is [System.Windows.Controls.Button] -or $ctrl -is [System.Windows.Controls.CheckBox]) {
        if ($ctrl.Content) { $label = [string]$ctrl.Content }
      }

      if ([string]::IsNullOrWhiteSpace($label)) {
        $label = $name
      }

      if (-not [string]::IsNullOrWhiteSpace($label)) {
        $ctrl.ToolTip = ('{0}' -f $label)
      }
    } catch { } # intentionally ignore controls that do not expose ToolTip/Content as expected
  }
}

Set-Alias -Name Ensure-WpfDefaultTooltips -Value Set-WpfDefaultTooltips -Scope Script

function Set-WpfBusyState {
  <#
  .SYNOPSIS
    Toggles the UI into busy (running) or idle state.
  #>
  param(
    [hashtable]$Ctx,
    [bool]$IsBusy,
    [string]$Hint = ''
  )

  try {
    # GUI-15: Dispatcher-Check — UI-Zugriff nur vom UI-Thread
    $wnd = $Ctx['Window']
    if ($wnd -and $wnd.Dispatcher -and -not $wnd.Dispatcher.CheckAccess()) {
      $busySb = { Set-WpfBusyState -Ctx $Ctx -IsBusy $IsBusy -Hint $Hint }.GetNewClosure()
      [void]$wnd.Dispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background, [System.Action]$busySb)
      return
    }
    $Ctx['btnRunGlobal'].IsEnabled    = -not $IsBusy
    $Ctx['btnCancelGlobal'].IsEnabled = $IsBusy
    $Ctx['tabMain'].IsEnabled         = -not $IsBusy
    $Ctx['lblBusyHint'].Text          = if ($IsBusy) { $Hint } else { '' }
    if (-not $IsBusy) {
      # Flash progress bar green briefly on completion
      if ($Ctx['progGlobal'].Value -ge 100) {
        try {
          $Ctx['progGlobal'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushSuccess' -Fallback '#00FF88'
          $resetTimer = New-Object System.Windows.Threading.DispatcherTimer
          $resetTimer.Interval = [TimeSpan]::FromSeconds(2)
          $resetTimer.add_Tick({
            try {
              $Ctx['progGlobal'].Foreground = Resolve-WpfThemeBrush -Ctx $Ctx -BrushKey 'BrushAccentCyan' -Fallback '#00F5FF'
              $this.Stop()
            } catch { $this.Stop() }
          }.GetNewClosure())
          $resetTimer.Start()
        } catch { }
      }
      $Ctx['progGlobal'].Value     = 0
      $Ctx['lblProgressPct'].Text  = ''
    }
  } catch { } # intentionally avoid throwing from transient UI state changes
}

function Update-WpfProgress {
  <#
  .SYNOPSIS
    Updates the global progress bar. Thread-safe.
  #>
  param(
    [hashtable]$Ctx,
    [int]$Percent,
    [string]$Detail = '',
    [switch]$Indeterminate
  )

  $updateSb = {
    $bar = $Ctx['progGlobal']
    if ($Indeterminate) {
      $bar.IsIndeterminate = $true
    } else {
      $bar.IsIndeterminate = $false
      $bar.Value = [System.Math]::Max(0, [System.Math]::Min(100, $Percent))
    }
    $Ctx['lblProgressPct'].Text = if ($Detail) { $Detail } else { "$Percent%" }
  }.GetNewClosure()
  $updateAction = [System.Action]$updateSb

  $prog = $Ctx['progGlobal']
  if ($prog -and $prog.Dispatcher.CheckAccess()) {
    $updateAction.Invoke()
  } elseif ($prog) {
    [void]$prog.Dispatcher.BeginInvoke([System.Windows.Threading.DispatcherPriority]::Background, $updateAction)
  }
}

function Add-WpfResourceDictionary {
  <#
  .SYNOPSIS
    Parses a ResourceDictionary XAML string and merges it into the target window.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [string]$ResourceDictionaryXaml
  )

  if ([string]::IsNullOrWhiteSpace($ResourceDictionaryXaml)) {
    return
  }

  try {
    $dictionary = [System.Windows.Markup.XamlReader]::Parse($ResourceDictionaryXaml)
    if ($dictionary -is [System.Windows.ResourceDictionary]) {
      [void]$Window.Resources.MergedDictionaries.Add($dictionary)
    }
  } catch {
    throw ('WpfHost: ResourceDictionary-Parsing fehlgeschlagen: {0}' -f $_.Exception.Message)
  }
}

function Set-WpfThemeResourceDictionary {
  <#
  .SYNOPSIS
    Replaces the active theme ResourceDictionary via MergedDictionaries swap.
  #>
  param(
    [Parameter(Mandatory)][System.Windows.Window]$Window,
    [bool]$Light = $false
  )

  $themeName = if ($Light) { 'Light.xaml' } else { 'SynthwaveDark.xaml' }
  $themeXaml = $null

  if (Get-Command Resolve-WpfComponentAssetPath -ErrorAction SilentlyContinue) {
    $path = Resolve-WpfComponentAssetPath -Category 'Themes' -FileName $themeName
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      $themeXaml = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    }
  }
  if (-not $themeXaml -and (Get-Command Resolve-WpfAssetPath -ErrorAction SilentlyContinue)) {
    $path = Resolve-WpfAssetPath -FileName 'Theme.Resources.xaml'
    if (Test-Path -LiteralPath $path -PathType Leaf) {
      $themeXaml = Get-Content -LiteralPath $path -Raw -Encoding UTF8
    }
  }

  if (-not [string]::IsNullOrWhiteSpace($themeXaml)) {
    try {
      $newDict = [System.Windows.Markup.XamlReader]::Parse($themeXaml)
      if ($newDict -is [System.Windows.ResourceDictionary]) {
        # Clear existing theme dictionaries and load the new one
        $Window.Resources.MergedDictionaries.Clear()
        [void]$Window.Resources.MergedDictionaries.Add($newDict)
        return
      }
    } catch { }
  }

  # Fallback: direct palette swap if theme file is unavailable
  $palette = if ($Light) {
    @{
      BrushBackground   = '#F4F6FF'
      BrushSurface      = '#FFFFFF'
      BrushSurfaceAlt   = '#ECEFFC'
      BrushSurfaceLight = '#F7F8FF'
      BrushAccentCyan   = '#005A9E'
      BrushAccentPurple = '#6E3FD6'
      BrushDanger       = '#C62828'
      BrushSuccess      = '#1B8A5A'
      BrushWarning      = '#B26A00'
      BrushTextPrimary  = '#12142A'
      BrushTextMuted    = '#4C5478'
      BrushBorder       = '#C9CFE8'
    }
  } else {
    @{
      BrushBackground   = '#0D0D1F'
      BrushSurface      = '#1A1A3A'
      BrushSurfaceAlt   = '#13133A'
      BrushSurfaceLight = '#252550'
      BrushAccentCyan   = '#00F5FF'
      BrushAccentPurple = '#BF00FF'
      BrushDanger       = '#FF0044'
      BrushSuccess      = '#00FF88'
      BrushWarning      = '#FFB700'
      BrushTextPrimary  = '#E8E8F8'
      BrushTextMuted    = '#9999CC'  # WCAG AA 4.56:1 on #0A0A1E
      BrushBorder       = '#2D2D5A'
    }
  }
  foreach ($k in $palette.Keys) {
    try {
      $Window.Resources[$k] = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString([string]$palette[$k]))
    } catch { }
  }
}

function Initialize-DesignSystem {
  <#
  .SYNOPSIS
    Initializes persisted UI theme state and optionally applies it to a window.
  #>
  param(
    [string]$Theme = 'Dark',
    [System.Windows.Window]$Window,
    [hashtable]$Ctx
  )

  $normalizedTheme = if ([string]::IsNullOrWhiteSpace([string]$Theme)) { 'dark' } else { [string]$Theme.Trim().ToLowerInvariant() }
  if ($normalizedTheme -notin @('dark', 'light')) {
    $normalizedTheme = 'dark'
  }

  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    try { [void](Set-AppStateValue -Key 'UITheme' -Value $normalizedTheme) } catch { }
  }

  if ($Window -and (Get-Command Set-WpfThemeResourceDictionary -ErrorAction SilentlyContinue)) {
    try { Set-WpfThemeResourceDictionary -Window $Window -Light:($normalizedTheme -eq 'light') } catch { }
  }

  if ($Ctx -and $Ctx.ContainsKey('btnThemeToggle') -and $Ctx['btnThemeToggle']) {
    try {
      $Ctx['btnThemeToggle'].Content = if ($normalizedTheme -eq 'light') { '☀ Hell' } else { '☾ Dunkel' }
    } catch { }
  }

  return $normalizedTheme
}

function New-WpfStyledDialog {
  <#
  .SYNOPSIS
    Factory for theme-aware WPF dialog windows (DUP-110).
    Returns a pre-styled Window so callers only need to set Content.
  .PARAMETER Owner
    Parent window for centering.
  .PARAMETER Title
    Dialog title.
  .PARAMETER Width
    Dialog width. Omit for SizeToContent.
  .PARAMETER Height
    Dialog height. Omit for SizeToContent.
  .PARAMETER ResizeMode
    Resize mode (default CanResize).
  .PARAMETER SizeToContent
    SizeToContent mode (default Manual, set to WidthAndHeight for auto-size).
  #>
  param(
    [System.Windows.Window]$Owner,
    [string]$Title = '',
    [int]$Width = 0,
    [int]$Height = 0,
    [System.Windows.ResizeMode]$ResizeMode = [System.Windows.ResizeMode]::CanResize,
    [System.Windows.SizeToContent]$SizeToContent = [System.Windows.SizeToContent]::Manual
  )

  $dlg = New-Object System.Windows.Window
  $dlg.Title = $Title
  $dlg.WindowStartupLocation = [System.Windows.WindowStartupLocation]::CenterOwner
  $dlg.ResizeMode = $ResizeMode
  $bgBrush = $null; $fgBrush = $null
  if ($Owner) {
    $bgBrush = $Owner.TryFindResource('BrushBackground')
    $fgBrush = $Owner.TryFindResource('BrushTextPrimary')
  }
  if (-not ($bgBrush -is [System.Windows.Media.SolidColorBrush])) {
    $bgBrush = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString('#0D0D1F'))
  }
  if (-not ($fgBrush -is [System.Windows.Media.SolidColorBrush])) {
    $fgBrush = [System.Windows.Media.SolidColorBrush]([System.Windows.Media.ColorConverter]::ConvertFromString('#E8E8F8'))
  }
  $dlg.Background = $bgBrush
  $dlg.Foreground = $fgBrush
  if ($Owner) { $dlg.Owner = $Owner }
  if ($Width -gt 0) { $dlg.Width = $Width }
  if ($Height -gt 0) { $dlg.Height = $Height }
  if ($SizeToContent -ne [System.Windows.SizeToContent]::Manual) {
    $dlg.SizeToContent = $SizeToContent
  }
  return $dlg
}
