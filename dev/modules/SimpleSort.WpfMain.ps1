# ================================================================
#  SimpleSort.WpfMain.ps1  –  WPF Entry Point
#  Provides: Start-WpfGui
# ================================================================

function Invoke-WpfUnhandledExceptionReport {
  param(
    [Parameter(Mandatory)][System.Exception]$Exception,
    [string]$Source = 'Unhandled',
    [bool]$ShowDialog = $true
  )

  $errorText = "[{0}] {1}" -f $Source, $Exception.ToString()

  try {
    if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
      [void](Set-AppStateValue -Key 'LastUnhandledWpfException' -Value $errorText)
      [void](Set-AppStateValue -Key 'LastUnhandledWpfExceptionAtUtc' -Value ([DateTime]::UtcNow.ToString('o')))
    }
  } catch { }

  try {
    if (Get-Command Write-Log -ErrorAction SilentlyContinue) {
      Write-Log -Level Error -Message ("[WPF-UNHANDLED] {0}" -f $errorText)
    }
  } catch { }

  try {
    $crashLogPath = Join-Path ([System.IO.Path]::GetTempPath()) 'romcleanup-wpf-crash.log'
    Add-Content -LiteralPath $crashLogPath -Value ("{0} {1}" -f (Get-Date -Format 'o'), $errorText) -Encoding UTF8
  } catch { }

  try { Write-Warning ("WPF-Unhandled ({0}): {1}" -f $Source, $Exception.Message) } catch { }

  $canShowDialog = $ShowDialog
  try {
    if (Get-Command Test-RomCleanupAutomatedTestMode -ErrorAction SilentlyContinue) {
      if (Test-RomCleanupAutomatedTestMode) { $canShowDialog = $false }
    }
  } catch { }
  try {
    $testMode = [string]$env:ROMCLEANUP_TESTMODE
    if (-not [string]::IsNullOrWhiteSpace($testMode) -and @('1','true','yes','on') -contains $testMode.Trim().ToLowerInvariant()) {
      $canShowDialog = $false
    }
  } catch { }

  if ($canShowDialog) {
    try {
      if (Test-WpfSuppressExceptionDialog -Exception $Exception -Source $Source) {
        $canShowDialog = $false
      }
    } catch { }
  }

  if ($canShowDialog) {
    try {
      [System.Windows.MessageBox]::Show(
        [string]$Exception.ToString(),
        'ROM Cleanup - Unerwarteter Fehler',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error) | Out-Null
    } catch {
      try { Write-Warning ([string]$Exception.Message) } catch { }
    }
  }
}

function Get-WpfExceptionFingerprint {
  param(
    [Parameter(Mandatory)][System.Exception]$Exception,
    [string]$Source = 'Unhandled'
  )

  $typeName = [string]$Exception.GetType().FullName
  $message = [string]$Exception.Message
  $stackHead = ''
  try {
    $stack = [string]$Exception.StackTrace
    if (-not [string]::IsNullOrWhiteSpace($stack)) {
      $stackHead = (($stack -split "`r?`n") | Select-Object -First 1)
    }
  } catch { }

  return ('{0}|{1}|{2}|{3}' -f [string]$Source, $typeName, $message, [string]$stackHead).Trim().ToLowerInvariant()
}

function Test-WpfSuppressExceptionDialog {
  param(
    [Parameter(Mandatory)][System.Exception]$Exception,
    [string]$Source = 'Unhandled'
  )

  if ($Exception -is [System.ComponentModel.Win32Exception]) {
    try {
      if ([int]$Exception.NativeErrorCode -eq 1816) {
        return $true
      }
    } catch { }
  }

  $debounceSeconds = 20
  try {
    $raw = [string]$env:ROMCLEANUP_UNHANDLED_DIALOG_DEBOUNCE_SECONDS
    if (-not [string]::IsNullOrWhiteSpace($raw)) {
      $parsed = 0
      if ([int]::TryParse($raw, [ref]$parsed)) {
        $debounceSeconds = [Math]::Max(1, [Math]::Min(300, $parsed))
      }
    }
  } catch { }

  $fingerprint = Get-WpfExceptionFingerprint -Exception $Exception -Source $Source
  $now = [DateTime]::UtcNow

  $lastFp = $null
  $lastAt = $null
  try { $lastFp = Get-Variable -Name WPF_LAST_UNHANDLED_FINGERPRINT -Scope Script -ValueOnly -ErrorAction SilentlyContinue } catch { }
  try { $lastAt = Get-Variable -Name WPF_LAST_UNHANDLED_AT_UTC -Scope Script -ValueOnly -ErrorAction SilentlyContinue } catch { }

  if ($lastFp -and $lastAt -and ([string]$lastFp -eq [string]$fingerprint)) {
    try {
      $elapsed = ($now - [DateTime]$lastAt).TotalSeconds
      if ($elapsed -lt $debounceSeconds) {
        return $true
      }
    } catch { }
  }

  $script:WPF_LAST_UNHANDLED_FINGERPRINT = $fingerprint
  $script:WPF_LAST_UNHANDLED_AT_UTC = $now
  return $false
}

function Test-RomCleanupEnableGlobalUnhandledHooks {
  try {
    $forceRaw = [string]$env:ROMCLEANUP_ENABLE_GLOBAL_UNHANDLED_HOOKS
    if (-not [string]::IsNullOrWhiteSpace($forceRaw)) {
      return (@('1','true','yes','on') -contains $forceRaw.Trim().ToLowerInvariant())
    }
  } catch { }

  try {
    if ([string]$env:TERM_PROGRAM -eq 'vscode') { return $false }
  } catch { }
  try {
    if ($Host -and [string]$Host.Name -match 'Visual Studio Code') { return $false }
  } catch { }

  return $false
}

function Register-GlobalWpfDispatcherExceptionHandler {
  $already = Get-Variable -Name WPF_DISPATCHER_UNHANDLED_HANDLER -Scope Script -ErrorAction SilentlyContinue
  $alreadyAppDomain = Get-Variable -Name WPF_APPDOMAIN_UNHANDLED_HANDLER -Scope Script -ErrorAction SilentlyContinue
  $alreadyTask = Get-Variable -Name WPF_TASK_UNOBSERVED_HANDLER -Scope Script -ErrorAction SilentlyContinue

  if ($already -and $already.Value -and $alreadyAppDomain -and $alreadyAppDomain.Value -and $alreadyTask -and $alreadyTask.Value) { return }

  $app = [System.Windows.Application]::Current
  $dispatcher = if ($app) { $app.Dispatcher } else { [System.Windows.Threading.Dispatcher]::CurrentDispatcher }
  if (-not $dispatcher) { return }

  if (-not ($already -and $already.Value)) {
    $handler = [System.Windows.Threading.DispatcherUnhandledExceptionEventHandler]{
    param($sender, $eventArgs)

    $ex = $null
    if ($eventArgs) { $ex = $eventArgs.Exception }
    if (-not $ex) { return }

    if ($ex -is [System.Management.Automation.PipelineStoppedException]) {
      if ($eventArgs) { $eventArgs.Handled = $true }
      return
    }

    Invoke-WpfUnhandledExceptionReport -Exception $ex -Source 'DispatcherUnhandledException' -ShowDialog $true

    if ($eventArgs) { $eventArgs.Handled = $true }
  }

    try {
      if ($app) {
        $app.add_DispatcherUnhandledException($handler)
      } else {
        $dispatcher.add_UnhandledException($handler)
      }
      $script:WPF_DISPATCHER_UNHANDLED_HANDLER = $handler
    } catch {
      try { Write-Warning ("Dispatcher-Exception-Handler konnte nicht registriert werden: {0}" -f $_.Exception.Message) } catch { }
    }
  }

  if (-not (Test-RomCleanupEnableGlobalUnhandledHooks)) {
    return
  }

  if (-not ($alreadyAppDomain -and $alreadyAppDomain.Value)) {
    $appDomainHandler = [System.UnhandledExceptionEventHandler]{
      param($sender, $eventArgs)

      $ex = $null
      if ($eventArgs -and $eventArgs.ExceptionObject -is [System.Exception]) {
        $ex = [System.Exception]$eventArgs.ExceptionObject
      } else {
        $ex = [System.Exception]::new('Unbekannte AppDomain-Ausnahme ohne ExceptionObject')
      }
      Invoke-WpfUnhandledExceptionReport -Exception $ex -Source 'AppDomainUnhandledException' -ShowDialog $false
    }
    try {
      [AppDomain]::CurrentDomain.add_UnhandledException($appDomainHandler)
      $script:WPF_APPDOMAIN_UNHANDLED_HANDLER = $appDomainHandler
    } catch {
      try { Write-Warning ("AppDomain-Exception-Handler konnte nicht registriert werden: {0}" -f $_.Exception.Message) } catch { }
    }
  }

  if (-not ($alreadyTask -and $alreadyTask.Value)) {
    $taskHandler = [System.EventHandler[System.Threading.Tasks.UnobservedTaskExceptionEventArgs]]{
      param($sender, $eventArgs)

      $ex = $null
      if ($eventArgs -and $eventArgs.Exception) {
        $ex = $eventArgs.Exception
      } else {
        $ex = [System.Exception]::new('Unbeobachtete Task-Ausnahme ohne Exception-Objekt')
      }

      Invoke-WpfUnhandledExceptionReport -Exception $ex -Source 'TaskSchedulerUnobservedTaskException' -ShowDialog $false
      if ($eventArgs) { $eventArgs.SetObserved() }
    }
    try {
      [System.Threading.Tasks.TaskScheduler]::add_UnobservedTaskException($taskHandler)
      $script:WPF_TASK_UNOBSERVED_HANDLER = $taskHandler
    } catch {
      try { Write-Warning ("TaskScheduler-Exception-Handler konnte nicht registriert werden: {0}" -f $_.Exception.Message) } catch { }
    }
  }
}

function Start-WpfGui {
  <#
  .SYNOPSIS
    Initialises the WPF window, wires all event handlers and shows it.
    Must be called from an STA thread.
  .PARAMETER Headless
    When $true the window is not shown (smoke-test mode).
    Returns the Window object for assertion in tests.
  #>
  param(
    [switch]$Headless
  )

  try {
    # ── Ensure WPF assemblies are loaded ────────────────────────────────────
    Initialize-WpfAssemblies
    Register-GlobalWpfDispatcherExceptionHandler

    # ── Build window from XAML ───────────────────────────────────────────────
    $window = New-WpfWindowFromXaml -Xaml $script:RC_XAML_MAIN

    if (Get-Command Add-WpfResourceDictionary -ErrorAction SilentlyContinue) {
      $themeXaml = $null
      if (Get-Command Get-WpfThemeResourceDictionaryXaml -ErrorAction SilentlyContinue) {
        $themeXaml = Get-WpfThemeResourceDictionaryXaml
      } elseif (Get-Variable -Name RC_XAML_THEME -Scope Script -ErrorAction SilentlyContinue) {
        $themeXaml = [string]$script:RC_XAML_THEME
      }
      Add-WpfResourceDictionary -Window $window -ResourceDictionaryXaml $themeXaml
    }

    # ── Named element index ───────────────────────────────────────────────────
    $ctx = Get-WpfNamedElements -Window $window

    if (Get-Command Connect-WpfMainViewModelBindings -ErrorAction SilentlyContinue) {
      [void](Connect-WpfMainViewModelBindings -Window $window -Ctx $ctx)
    }

    # ── Wire all UI events ────────────────────────────────────────────────────
    Register-WpfEventHandlers -Window $window -Ctx $ctx

    # ── Smoke-test short-circuit ──────────────────────────────────────────────
    if ($Headless) {
      return $window
    }

    # ── Show modal (blocks until window closes) ──────────────────────────────
    $null = $window.ShowDialog()
  } catch {
    $ex = $_.Exception
    if (-not $ex) { $ex = [System.Exception]::new('Unbekannter Fehler in Start-WpfGui') }
    if (Get-Command Invoke-WpfUnhandledExceptionReport -ErrorAction SilentlyContinue) {
      Invoke-WpfUnhandledExceptionReport -Exception $ex -Source 'Start-WpfGui' -ShowDialog $true
      return $null
    }
    throw
  }
}
