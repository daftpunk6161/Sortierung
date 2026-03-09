#requires -version 5.1
<#
  ROM Cleanup & Region Dedupe GUI
  ================================
  - Entfernt Demos, Betas, Protos, Software, Hacks, Bad Dumps, Trainer etc.
  - BIOS/Firmware optional in separaten Ordner
  - Region-Dedupe: EU > US > JP (konfigurierbar)

  CLI Mode:
    .\simple_sort.ps1 -Headless -Roots 'D:\ROMs' [-Mode DryRun|Move] [-Prefer EU,US] ...
    Delegates to Invoke-RomCleanup.ps1 for headless operation.
#>
[CmdletBinding(DefaultParameterSetName = 'GUI')]
param(
    # ── Headless / CLI ───────────────────────────────────────────────────────
    [Parameter(ParameterSetName = 'CLI', Mandatory)]
    [switch]$Headless,

    [Parameter(ParameterSetName = 'CLI', Mandatory)]
    [string[]]$Roots,

    [Parameter(ParameterSetName = 'CLI')]
    [ValidateSet('DryRun', 'Move')]
    [string]$Mode = 'DryRun',

    [Parameter(ParameterSetName = 'CLI')]
    [string[]]$Prefer = @('EU', 'US', 'WORLD', 'JP'),

    [Parameter(ParameterSetName = 'CLI')]
    [string[]]$Extensions,

    [Parameter(ParameterSetName = 'CLI')]
    [string]$TrashRoot,

    [Parameter(ParameterSetName = 'CLI')]
    [string]$AuditRoot,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$RemoveJunk,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$AggressiveJunk,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$SortConsole,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$Convert,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$UseDat,

    [Parameter(ParameterSetName = 'CLI')]
    [string]$DatRoot,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$Use1G1R,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$SkipConfirm,

    [Parameter(ParameterSetName = 'CLI')]
    [switch]$Quiet,

    [Parameter(ParameterSetName = 'GUI')]
    [switch]$StaRelaunched,

    [Parameter(ParameterSetName = 'GUI')]
    [switch]$GuiDetached
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Test-RomCleanupAutomatedTestMode {
  <#
    Returns $true when the script runs inside automated tests and GUI startup
    should be skipped.
  #>
  $flag = [string]$env:ROMCLEANUP_TESTMODE
  if (-not [string]::IsNullOrWhiteSpace($flag)) {
    switch ($flag.Trim().ToLowerInvariant()) {
      '1' { return $true }
      'true' { return $true }
      'yes' { return $true }
      'on' { return $true }
    }
  }
  return $false
}

# Security invariant (M11): generated output folders must never be treated as
# normal scan input roots. Reserved names: _TRASH and _BIOS.

# ── Headless delegation ─────────────────────────────────────────────────────
if ($Headless) {
    # ENTRY-006 FIX: Enforce safety invariant M11 — TrashDir/BiosDir must not be a Root
    foreach ($rootPath in @($Roots)) {
        if ([string]::IsNullOrWhiteSpace($rootPath)) { continue }
        $normalizedRootM11 = $null
        try { $normalizedRootM11 = [System.IO.Path]::GetFullPath($rootPath).TrimEnd('\','/') } catch { $normalizedRootM11 = $rootPath }
        $rootLeaf = [System.IO.Path]::GetFileName($normalizedRootM11)
        if ($rootLeaf -ieq '_TRASH' -or $rootLeaf -ieq '_BIOS') {
            Write-Warning ("Safety invariant M11 violated: Root path '{0}' uses reserved name '{1}'. Aborting." -f $rootPath, $rootLeaf)
            exit 3
        }
        if (-not [string]::IsNullOrWhiteSpace($TrashRoot)) {
            $normalizedTrash = $null
            try { $normalizedTrash = [System.IO.Path]::GetFullPath($TrashRoot).TrimEnd('\','/') } catch { $normalizedTrash = $TrashRoot }
            if ($normalizedTrash -ieq $normalizedRootM11) {
                Write-Warning ("Safety invariant M11 violated: TrashRoot '{0}' is the same as Root '{1}'. Aborting." -f $TrashRoot, $rootPath)
                exit 3
            }
        }
    }
    $cliScript = Join-Path $PSScriptRoot 'Invoke-RomCleanup.ps1'
    if (-not (Test-Path -LiteralPath $cliScript -PathType Leaf)) {
        Write-Error "Invoke-RomCleanup.ps1 not found at: $cliScript"
        exit 3
    }
    # Forward all CLI parameters
    $fwd = @{ Roots = $Roots; Mode = $Mode; Prefer = $Prefer }
    if ($Extensions)    { $fwd['Extensions']    = $Extensions }
    if ($TrashRoot)     { $fwd['TrashRoot']     = $TrashRoot }
    if ($AuditRoot)     { $fwd['AuditRoot']     = $AuditRoot }
    if ($RemoveJunk)    { $fwd['RemoveJunk']    = $true }
    if ($AggressiveJunk){ $fwd['AggressiveJunk']= $true }
    if ($SortConsole)   { $fwd['SortConsole']   = $true }
    if ($Convert)       { $fwd['Convert']       = $true }
    if ($UseDat)        { $fwd['UseDat']        = $true }
    if ($DatRoot)       { $fwd['DatRoot']       = $DatRoot }
    if ($Use1G1R)       { $fwd['Use1G1R']       = $true }
    if ($SkipConfirm)   { $fwd['SkipConfirm']   = $true }
    if ($Quiet)         { $fwd['Quiet']         = $true }
    & $cliScript @fwd
    $exitCode = if ($null -ne $LASTEXITCODE) { $LASTEXITCODE } else { if ($?) { 0 } else { 1 } }
    exit $exitCode
}

# GUI-BLOCK-BEGIN – stripped by TestScriptLoader for unit tests
# ── GUI Mode ─────────────────────────────────────────────────────────────────

# GUI starts directly in the current process.
# STA handling below performs a relaunch only when required.

Add-Type -AssemblyName PresentationFramework

# ── DPI Awareness ────────────────────────────────────────────────────────────
# Ensure the process declares System-DPI-Awareness explicitly so WinForms
# AutoScaleMode=Dpi works consistently across all Windows versions.
try {
  if (-not ([System.Management.Automation.PSTypeName]'RomCleanupDpiHelper').Type) {
    Add-Type -TypeDefinition @'
public class RomCleanupDpiHelper {
    [System.Runtime.InteropServices.DllImport("shcore.dll", SetLastError = true)]
    private static extern int SetProcessDpiAwareness(int awareness);
    // 0 = Unaware, 1 = System, 2 = PerMonitor
    public static void SetSystemDpiAware() {
        try { SetProcessDpiAwareness(1); } catch { }
    }
}
'@ -ErrorAction SilentlyContinue
  }
  [RomCleanupDpiHelper]::SetSystemDpiAware()
} catch { <# Pre-Win8.1 or already set — safe to ignore #> }

$script:SingleInstanceMutex = $null
$script:SingleInstanceMutexName = 'Local\RomCleanupGuiSingleInstance'
$script:SingleInstanceActivationEventName = 'Local\RomCleanupGuiActivate'

try {
  Add-Type -Name 'OleInit' -Namespace 'RomCleanup' -MemberDefinition @'
  [System.Runtime.InteropServices.DllImport("ole32.dll")]
  public static extern int OleInitialize(System.IntPtr pvReserved);
'@ -ErrorAction SilentlyContinue | Out-Null
  [void][RomCleanup.OleInit]::OleInitialize([IntPtr]::Zero)
} catch {
  Write-Verbose ("OLE-Initialisierung übersprungen: {0}" -f $_.Exception.Message)
}

function Write-StartupTrace {
  param(
    [Parameter(Mandatory)]
    [string]$Message
  )

  try {
    $base = if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) { $PSScriptRoot } else { (Get-Location).Path }
    $reportsDir = Join-Path $base 'reports'
    if (-not (Test-Path -LiteralPath $reportsDir -PathType Container)) {
      New-Item -ItemType Directory -Path $reportsDir -Force | Out-Null
    }
    $line = "[{0}] {1}" -f (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff'), $Message
    Add-Content -LiteralPath (Join-Path $reportsDir 'startup-diagnostic-latest.log') -Value $line
  } catch { }
}

Write-StartupTrace -Message 'Startup entered.'

function Resolve-ModuleDirectory {
  <# Resolve dev/modules directory robustly across File-run, dot-source, and editor selection modes. #>
  param(
    [string]$EnvOverride,
    [string]$ScriptOverride
  )

  $probe = New-Object System.Collections.Generic.List[string]

  if (-not [string]::IsNullOrWhiteSpace($EnvOverride)) { [void]$probe.Add($EnvOverride) }
  if (-not [string]::IsNullOrWhiteSpace($ScriptOverride)) { [void]$probe.Add($ScriptOverride) }
  if (-not [string]::IsNullOrWhiteSpace($PSScriptRoot)) { [void]$probe.Add($PSScriptRoot) }

  if (-not [string]::IsNullOrWhiteSpace($PSCommandPath)) {
    [void]$probe.Add((Split-Path -Parent $PSCommandPath))
  }

  $invPath = $null
  if ($MyInvocation -and $MyInvocation.MyCommand) {
    $pathProp = $MyInvocation.MyCommand.PSObject.Properties['Path']
    if ($pathProp -and -not [string]::IsNullOrWhiteSpace([string]$pathProp.Value)) {
      $invPath = [string]$pathProp.Value
    }
  }
  if (-not [string]::IsNullOrWhiteSpace($invPath)) {
    [void]$probe.Add((Split-Path -Parent $invPath))
  }

  try {
    $cwd = (Get-Location).Path
    if (-not [string]::IsNullOrWhiteSpace($cwd)) { [void]$probe.Add($cwd) }
  } catch { }

  foreach ($entry in $probe) {
    if ([string]::IsNullOrWhiteSpace($entry)) { continue }

    $candidates = @($entry)
    try {
      $devModules = Join-Path $entry 'dev\modules'
      $candidates += $devModules
    } catch { }

    foreach ($candidate in $candidates) {
      if ([string]::IsNullOrWhiteSpace($candidate)) { continue }
      if (-not (Test-Path -LiteralPath $candidate -PathType Container)) { continue }

      $markerFiles = @('ModuleFileList.ps1','RomCleanupLoader.ps1')
      $allPresent = $true
      foreach ($marker in $markerFiles) {
        if (-not (Test-Path -LiteralPath (Join-Path $candidate $marker) -PathType Leaf)) {
          $allPresent = $false
          break
        }
      }
      if ($allPresent) { return $candidate }
    }
  }

  return $null
}

# Optional modular overlays (loaded when running in repository layout).
# Loads the RomCleanup module by dot-sourcing RomCleanupLoader.ps1 into script scope
# so that all $script: state variables remain shared.
# When ARCH-16/17 (state management) are completed, this can switch to Import-Module.
$scriptOverrideDir = $null
try {
  $script:ROMCLEANUP_MODULE_PROFILE = 'all'
  $previousModuleProfileEnv = $env:ROMCLEANUP_MODULE_PROFILE
  $env:ROMCLEANUP_MODULE_PROFILE = $script:ROMCLEANUP_MODULE_PROFILE
  $scriptOverrideVar = Get-Variable -Scope Script -Name ROM_CLEANUP_MODULE_DIR -ErrorAction SilentlyContinue
  if ($scriptOverrideVar -and -not [string]::IsNullOrWhiteSpace([string]$scriptOverrideVar.Value)) {
    $scriptOverrideDir = [string]$scriptOverrideVar.Value
  }

  $moduleDir = Resolve-ModuleDirectory -EnvOverride $env:ROM_CLEANUP_MODULE_DIR -ScriptOverride $scriptOverrideDir
  if ($moduleDir) {
    Remove-Variable -Scope Script -Name RomCleanupLoadedModules -ErrorAction SilentlyContinue
    $loaderPath = Join-Path $moduleDir 'RomCleanupLoader.ps1'
    if (Test-Path -LiteralPath $loaderPath -PathType Leaf) {
      # Dot-source the .ps1 loader so all functions + $script: state stay in our scope.
      # NOTE: .psm1 cannot be dot-sourced — PS creates an implicit module scope for
      #       .psm1 files, trapping functions.  Use Import-Module for .psm1/.psd1.
      $script:_RomCleanupModuleRoot = $moduleDir
      . $loaderPath
    } else {
      # Fallback: load individual module files (standalone/packaged deployment)
      $moduleListPath = Join-Path $moduleDir 'ModuleFileList.ps1'
      if (Test-Path -LiteralPath $moduleListPath -PathType Leaf) {
        . $moduleListPath
      }
      if (-not (Get-Command Get-RomCleanupModuleFiles -ErrorAction SilentlyContinue)) {
        throw 'ModuleFileList.ps1 loaded, but Get-RomCleanupModuleFiles is not available.'
      }
      $fallbackModuleNames = @(Get-RomCleanupModuleFiles -Profile $script:ROMCLEANUP_MODULE_PROFILE)
      foreach ($moduleName in $fallbackModuleNames) {
        $modulePath = Join-Path $moduleDir $moduleName
        if (Test-Path -LiteralPath $modulePath -PathType Leaf) {
          . $modulePath
        }
      }
    }
  }
} catch {
  $script:ModuleLoadError = $_.Exception.Message
  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    [void](Set-AppStateValue -Key 'ModuleLoadError' -Value $_.Exception.Message)
  }
} finally {
  if ($null -eq $previousModuleProfileEnv) {
    Remove-Item Env:\ROMCLEANUP_MODULE_PROFILE -ErrorAction SilentlyContinue
  } else {
    $env:ROMCLEANUP_MODULE_PROFILE = $previousModuleProfileEnv
  }
}

try {
  if (Get-Command Import-ConsolePlugins -ErrorAction SilentlyContinue) {
    $pluginResult = Import-ConsolePlugins
    [void](Set-AppStateValue -Key 'ConsolePluginResult' -Value $pluginResult)
  }
} catch {
  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    [void](Set-AppStateValue -Key 'ConsolePluginError' -Value $_.Exception.Message)
  }
}

# --- Load persisted theme preference -----------------------------------------
$startupSettings = $null
try {
  $startupSettings = Get-UserSettings
  if ($startupSettings -and $startupSettings['general'] -and $startupSettings['general'] -is [hashtable] -and $startupSettings['general'].ContainsKey('theme') -and $startupSettings['general']['theme']) {
    $savedTheme = [string]$startupSettings['general']['theme']
    if (($savedTheme -eq 'Dark' -or $savedTheme -eq 'Light') -and (Get-Command Initialize-DesignSystem -ErrorAction SilentlyContinue)) {
      [void](Initialize-DesignSystem -Theme $savedTheme)
    }
  }
} catch {
  Write-Warning ("Theme-Laden fehlgeschlagen: {0}" -f $_.Exception.Message)
}

try {
  $updateRepo = $null
  if ($startupSettings -and $startupSettings['general'] -and $startupSettings['general'] -is [hashtable] -and $startupSettings['general'].ContainsKey('updateRepo') -and $startupSettings['general']['updateRepo']) {
    $updateRepo = [string]$startupSettings['general']['updateRepo']
  }
  if ([string]::IsNullOrWhiteSpace($updateRepo) -and $env:ROMCLEANUP_UPDATE_REPO) {
    $updateRepo = [string]$env:ROMCLEANUP_UPDATE_REPO
  }
  if (-not [string]::IsNullOrWhiteSpace($updateRepo) -and (Get-Command Test-RomCleanupUpdateAvailable -ErrorAction SilentlyContinue)) {
    $currentVer = if (Get-Command Get-RomCleanupVersion -ErrorAction SilentlyContinue) { Get-RomCleanupVersion } else { '1.0.0' }
    $updateResult = Test-RomCleanupUpdateAvailable -Repo $updateRepo -CurrentVersion $currentVer
    [void](Set-AppStateValue -Key 'UpdateCheckResult' -Value $updateResult)
  }
} catch {
  if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
    [void](Set-AppStateValue -Key 'UpdateCheckError' -Value $_.Exception.Message)
  }
}

function Test-RequiredModuleFunctions {
  <# Ensures modularized helper functions are available before heavy UI/actions start. #>
  param([scriptblock]$Log)

  $required = @(
    'Find-ConversionTool',
    'Expand-ArchiveToTemp',
    'ConvertTo-SafeCsvValue',
    'Write-Reports',
    'Invoke-MovePhase',
    'Invoke-AuditRollback',
    'Resolve-GameKey',
    'Get-DatIndex',
    'New-SetItem'
  )

  $missing = New-Object System.Collections.Generic.List[string]
  foreach ($name in $required) {
    if (-not (Get-Command -Name $name -ErrorAction SilentlyContinue)) {
      [void]$missing.Add($name)
    }
  }

  if ($missing.Count -gt 0) {
    $cwd = $null
    try { $cwd = (Get-Location).Path } catch { }
    $cwdHint = if ([string]::IsNullOrWhiteSpace($cwd)) { '(unbekannt)' } else { $cwd }
    $moduleLoadError = $null
    $moduleLoadErrorVar = Get-Variable -Scope Script -Name ModuleLoadError -ErrorAction SilentlyContinue
    if ($moduleLoadErrorVar -and -not [string]::IsNullOrWhiteSpace([string]$moduleLoadErrorVar.Value)) {
      $moduleLoadError = [string]$moduleLoadErrorVar.Value
    }

    $msg = "Fehlende Modul-Funktionen: {0}`n`nPrüfe dev/modules oder ROM_CLEANUP_MODULE_DIR.`nCWD: {1}" -f ($missing -join ', '), $cwdHint
    if ($moduleLoadError) {
      $msg += "`nLoad-Fehler: " + $moduleLoadError
    }
    if ($Log) { & $Log $msg }
    try {
      [System.Windows.MessageBox]::Show(
        $msg,
        'ROM Cleanup - Startup Fehler',
        [System.Windows.MessageBoxButton]::OK,
        [System.Windows.MessageBoxImage]::Error) | Out-Null
    } catch { }
    return $false
  }

  return $true
}

if (-not (Test-RequiredModuleFunctions)) {
  Write-Error 'Startup abgebrochen: Pflichtfunktionen konnten nicht geladen werden. Details in reports/startup-diagnostic-latest.log.'
  Write-StartupTrace -Message 'Startup aborted: missing required module functions.'
  return
}

# Automated tests: load modules, but skip GUI startup to avoid modal dialogs
# and interactive WinForms windows during Pester runs.
$script:IsAutomatedTestMode = Test-RomCleanupAutomatedTestMode
if ($script:IsAutomatedTestMode) {
  Write-Warning 'Automated Test Mode aktiv (ROMCLEANUP_TESTMODE) - GUI-Start wird übersprungen.'
  Write-StartupTrace -Message 'Startup exited: automated test mode active.'
  try {
    if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
      [void](Set-AppStateValue -Key 'IsAutomatedTestMode' -Value $true)
    }
  } catch {
    Write-Verbose ("Set-AppStateValue im TestMode fehlgeschlagen: {0}" -f $_.Exception.Message)
  }
  return
}

# VS Code PowerShell extension can terminate the integrated terminal process
# under certain host/runtime faults. In that case, the GUI dies with the host.
# Detached relaunch is therefore supported, but kept opt-in because some
# environments hide/suppress the child process window and appear as "does not start".
$isVsCodeHost = $false
try {
  if ([string]$env:TERM_PROGRAM -eq 'vscode') { $isVsCodeHost = $true }
  elseif ($Host -and [string]$Host.Name -match 'Visual Studio Code') { $isVsCodeHost = $true }
} catch { }

$enableVsCodeDetach = $false
try {
  $detachFlag = [string]$env:ROMCLEANUP_VSCODE_DETACH
  if (-not [string]::IsNullOrWhiteSpace($detachFlag)) {
    switch ($detachFlag.Trim().ToLowerInvariant()) {
      '1' { $enableVsCodeDetach = $true }
      'true' { $enableVsCodeDetach = $true }
      'yes' { $enableVsCodeDetach = $true }
      'on' { $enableVsCodeDetach = $true }
    }
  }
} catch { }

# ── Centralized PowerShell Executable Discovery ──────────────────────────────
function Resolve-PowerShellExe {
  <# Finds the best available PowerShell executable.
     Prefers powershell.exe (Windows PowerShell) because pwsh.exe (PS7) does not support -STA. #>
  $candidates = @(
    (Get-Command -Name 'powershell.exe' -ErrorAction SilentlyContinue),
    (Get-Command -Name 'pwsh.exe' -ErrorAction SilentlyContinue)
  )
  foreach ($cmd in $candidates) {
    if ($cmd -and $cmd.Source) { return [string]$cmd.Source }
  }
  $legacyPath = Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
  if (Test-Path -LiteralPath $legacyPath -PathType Leaf) { return $legacyPath }
  return $null
}

# ── Centralized STA Process Launch ───────────────────────────────────────────
function Start-StaProcess {
  <# Launches a new PowerShell process with -STA flag and verifies it stays alive.
     Returns $true if child process started successfully, $false otherwise. #>
  param(
    [Parameter(Mandatory)][string]$ScriptPath,
    [string[]]$ExtraArgs = @(),
    [string]$WindowStyle = 'Normal'
  )

  $exe = Resolve-PowerShellExe
  if (-not $exe -or -not $ScriptPath) { return $false }

  try {
    # pwsh.exe (PS7) does not support -STA; only add it for powershell.exe
    $isPwsh = [System.IO.Path]::GetFileNameWithoutExtension($exe) -ieq 'pwsh'
    # ENTRY-004: -ExecutionPolicy Bypass is required because the script is unsigned and
    # users may have restrictive default policies. This is intentional for usability.
    # The child process runs our own script, so the risk is limited to the same trust boundary.
    $argsList = @('-NoProfile', '-ExecutionPolicy', 'Bypass')
    if (-not $isPwsh) { $argsList += '-STA' }
    $argsList += @('-File', $ScriptPath) + $ExtraArgs
    if ($WindowStyle -eq 'Hidden') {
      # Use .NET ProcessStartInfo with CreateNoWindow for reliable console hiding.
      # Start-Process -WindowStyle Hidden is unreliable for console applications.
      $psi = New-Object System.Diagnostics.ProcessStartInfo
      $psi.FileName = $exe
      # ENTRY-003 FIX: Use proper argument quoting to prevent injection.
      # Escape embedded double quotes and wrap arguments containing spaces or special chars.
      $psi.Arguments = ($argsList | ForEach-Object {
        $arg = [string]$_
        if ($arg -eq '') { return '""' }
        if ($arg -match '[\s"&|<>^]') {
          # Escape embedded double quotes by doubling them, then wrap in double quotes
          return '"{0}"' -f ($arg -replace '"', '\"')
        }
        return $arg
      }) -join ' '
      $psi.WorkingDirectory = $PSScriptRoot
      $psi.CreateNoWindow = $true
      $psi.UseShellExecute = $false
      $psi.WindowStyle = [System.Diagnostics.ProcessWindowStyle]::Hidden
      $proc = [System.Diagnostics.Process]::Start($psi)
    } else {
      $startParams = @{
        FilePath = $exe
        ArgumentList = $argsList
        WorkingDirectory = $PSScriptRoot
        PassThru = $true
      }
      $proc = Start-Process @startParams
    }
    Start-Sleep -Milliseconds 900
    $alive = -not ($proc -and $proc.HasExited)
    if (-not $alive -and $proc) {
      $exitCodeText = if ($proc) { [string]$proc.ExitCode } else { '?' }
      Write-StartupTrace -Message ("STA child exited early (ExitCode={0})." -f $exitCodeText)
    }
    return $alive
  } catch {
    Write-StartupTrace -Message ("STA process start failed: {0}" -f $_.Exception.Message)
    return $false
  }
}

if ($isVsCodeHost -and $enableVsCodeDetach -and -not $GuiDetached) {
  Write-StartupTrace -Message 'VSCode host detected: detached opt-in path entered.'
  $scriptPath = $PSCommandPath
  if (-not $scriptPath) { $scriptPath = $MyInvocation.MyCommand.Path }

  if ($scriptPath) {
    $detachedAlive = Start-StaProcess -ScriptPath $scriptPath -ExtraArgs @('-StaRelaunched', '-GuiDetached') -WindowStyle 'Hidden'
    if ($detachedAlive) {
      Write-StartupTrace -Message 'Detached child alive after probe; parent exits.'
      return
    }
    Write-Warning 'Detached GUI-Prozess beendet sich sofort; fallback auf Start im aktuellen Host.'
  }
}

# Single-instance pre-check disabled to avoid silent startup aborts caused by stale hidden instances.

# WinForms Drag&Drop requires a real STA main thread.
# Relaunch only when current thread is not STA.
$needsStaRelaunch = ([System.Threading.Thread]::CurrentThread.ApartmentState -ne [System.Threading.ApartmentState]::STA)

if ($needsStaRelaunch) {
  Write-StartupTrace -Message 'Current thread is not STA; relaunch path entered.'
  if (-not $StaRelaunched) {
    $scriptPath = $PSCommandPath
    if (-not $scriptPath) { $scriptPath = $MyInvocation.MyCommand.Path }

    $relaunched = $false
    if ($scriptPath) {
      $relaunched = Start-StaProcess -ScriptPath $scriptPath -ExtraArgs @('-StaRelaunched') -WindowStyle 'Hidden'
    }

    if ($relaunched) {
      Write-StartupTrace -Message 'STA relaunch child alive after probe; parent exits.'
      return
    }

    Write-StartupTrace -Message 'No STA process could be started; showing warning dialog.'
    [System.Windows.MessageBox]::Show(
      'Konnte keinen STA-Prozess starten. Bitte manuell mit powershell -STA -File simple_sort.ps1 ausführen.',
      'ROM Cleanup',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Warning) | Out-Null
  } else {
    Write-StartupTrace -Message 'StaRelaunched set but thread still non-STA; showing warning dialog.'
    [System.Windows.MessageBox]::Show(
      'Drag&Drop requires STA. Please start with: powershell -STA -File simple_sort.ps1',
      'ROM Cleanup',
      [System.Windows.MessageBoxButton]::OK,
      [System.Windows.MessageBoxImage]::Warning) | Out-Null
  }
  return
}

try {
  $createdNew = $false
  $script:SingleInstanceMutex = New-Object System.Threading.Mutex($true, $script:SingleInstanceMutexName, [ref]$createdNew)
  if (-not $createdNew) {
    # Continue startup even when mutex already exists.
    # This avoids permanent lockout after stale/hidden process artifacts.
    try { $script:SingleInstanceMutex.Dispose() } catch { }
    $script:SingleInstanceMutex = $null
  }

  try {
    $script:SingleInstanceActivationEvent = New-Object System.Threading.EventWaitHandle(
      $false,
      [System.Threading.EventResetMode]::AutoReset,
      $script:SingleInstanceActivationEventName)
  } catch {
    $script:SingleInstanceActivationEvent = $null
  }
} catch { }

try {
  Write-StartupTrace -Message 'Entering WPF bootstrap/start block.'
  if (Get-Command Import-RomCleanupFeatureModules -ErrorAction SilentlyContinue) {
    [void](Import-RomCleanupFeatureModules -Feature 'wpf')
  }

  if (-not (Get-Command Start-WpfGui -ErrorAction SilentlyContinue)) {
    $wpfModuleNames = @(
      'WpfXaml.ps1',
      'WpfHost.ps1',
      'WpfSelectionConfig.ps1',
      'WpfMainViewModel.ps1',
      'WpfSlice.Roots.ps1',
      'WpfSlice.RunControl.ps1',
      'WpfSlice.Settings.ps1',
      'WpfSlice.DatMapping.ps1',
      'WpfSlice.ReportPreview.ps1',
      'WpfSlice.AdvancedFeatures.ps1',
      'WpfEventHandlers.ps1',
      'SimpleSort.WpfMain.ps1',
      'WpfApp.ps1'
    )

    if (Get-Command Get-RomCleanupFeatureModuleFiles -ErrorAction SilentlyContinue) {
      $fromFeature = @(Get-RomCleanupFeatureModuleFiles -Feature 'wpf')
      if ($fromFeature.Count -gt 0) { $wpfModuleNames = $fromFeature }
    } elseif (Get-Command Get-RomCleanupModuleFiles -ErrorAction SilentlyContinue) {
      $fromProfile = @(Get-RomCleanupModuleFiles -Profile 'wpf')
      if ($fromProfile.Count -gt 0) { $wpfModuleNames = $fromProfile }
    }

    $resolvedModuleDir = $script:_RomCleanupModuleRoot
    if ([string]::IsNullOrWhiteSpace([string]$resolvedModuleDir)) {
      $resolvedModuleDir = Resolve-ModuleDirectory -EnvOverride $env:ROM_CLEANUP_MODULE_DIR -ScriptOverride $scriptOverrideDir
    }

    if (-not [string]::IsNullOrWhiteSpace([string]$resolvedModuleDir)) {
      foreach ($moduleName in $wpfModuleNames) {
        $modulePath = Join-Path $resolvedModuleDir $moduleName
        if (Test-Path -LiteralPath $modulePath -PathType Leaf) {
          . $modulePath
        }
      }
      # Promote dot-sourced functions to global scope for .GetNewClosure() compatibility
      foreach ($fn in (Get-ChildItem Function:)) {
        if ($fn.ScriptBlock -and $fn.ScriptBlock.File -and
            $fn.ScriptBlock.File.StartsWith($resolvedModuleDir, [System.StringComparison]::OrdinalIgnoreCase)) {
          Set-Item -Path "Function:global:$($fn.Name)" -Value $fn.ScriptBlock -ErrorAction SilentlyContinue
        }
      }
    }
  }

  if (-not (Get-Command Start-WpfGui -ErrorAction SilentlyContinue)) {
    throw 'Start-WpfGui ist nach Modul-Initialisierung nicht verfügbar.'
  }

  if (Get-Command Start-WpfApp -ErrorAction SilentlyContinue) {
    Write-StartupTrace -Message 'Calling Start-WpfApp.'
    Start-WpfApp
  } else {
    Write-StartupTrace -Message 'Calling Start-WpfGui.'
    Start-WpfGui
  }
} catch {
  $startupError = $_.Exception
  if (-not $startupError) { $startupError = [System.Exception]::new('Unbekannter Fehler beim GUI-Start') }

  try {
    if (Get-Command Invoke-WpfUnhandledExceptionReport -ErrorAction SilentlyContinue) {
      Invoke-WpfUnhandledExceptionReport -Exception $startupError -Source 'simple_sort.Startup' -ShowDialog $true
    }
  } catch { }

  Write-Warning ("GUI-Start fehlgeschlagen: {0}" -f $startupError.Message)
  Write-StartupTrace -Message ("GUI startup failed: {0}" -f $startupError.Message)
} finally {
  Write-StartupTrace -Message 'Startup finally block entered.'
  if ($script:SingleInstanceActivationEvent) {
    try { $script:SingleInstanceActivationEvent.Dispose() } catch { Write-Verbose ("Activation-Event Dispose Warnung: {0}" -f $_.Exception.Message) }
    $script:SingleInstanceActivationEvent = $null
  }

  if ($script:SingleInstanceMutex) {
    try { $script:SingleInstanceMutex.ReleaseMutex() } catch { Write-Verbose ("Mutex Release Warnung: {0}" -f $_.Exception.Message) }
    try { $script:SingleInstanceMutex.Dispose() } catch { Write-Verbose ("Mutex Dispose Warnung: {0}" -f $_.Exception.Message) }
    $script:SingleInstanceMutex = $null
  }
}
# GUI-BLOCK-END
