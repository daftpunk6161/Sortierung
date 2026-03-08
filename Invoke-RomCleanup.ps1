<#
.SYNOPSIS
    ROM Cleanup & Region Dedupe — Headless CLI entry point.

.DESCRIPTION
    Performs ROM region deduplication, junk removal, console sorting,
    and format conversion without the GUI.  All options are controlled
    via parameters.  Designed for automation, CI pipelines, and scripted
    batch processing.

    Exit codes:
      0  Success (operation completed)
      1  Runtime error (unhandled exception)
      2  Cancelled by user
      3  Validation / preflight error

.PARAMETER Roots
    One or more ROM root folder paths to process.  Mandatory.

.PARAMETER Mode
    Operation mode: DryRun (report only, no files moved) or Move.
    Default: DryRun.

.PARAMETER Prefer
    Region priority order as comma-separated string or array.
    Example: 'EU','US','WORLD','JP'
    Default: 'EU','US','WORLD','JP'

.PARAMETER Extensions
    File extensions to include.  Dot-prefixed.
    Default: common ROM extensions (.zip, .7z, .chd, .iso, .bin, .cue, etc.)

.PARAMETER TrashRoot
    Custom trash folder for moved duplicates/junk.
    Default: _TRASH subfolder in each root.

.PARAMETER AuditRoot
    Folder for audit CSV logs.  Default: audit-logs/ in workspace.

.PARAMETER RemoveJunk
    Remove junk ROMs (demos, samples, betas).  Default: $true.

.PARAMETER AggressiveJunk
    Use aggressive junk detection patterns.  Default: $false.

.PARAMETER AliasEditionKeying
    Normalize game keys with alias/edition awareness.  Default: $false.

.PARAMETER SeparateBios
    Separate BIOS files into dedicated folder.  Default: $false.

.PARAMETER ConsoleFilter
    Limit processing to specific console types.  Empty = all.

.PARAMETER SortConsole
    Sort files into console subfolders before dedupe (Move mode only).
    Default: $false.

.PARAMETER UseDat
    Enable DAT-based hash verification.  Default: $false.

.PARAMETER DatRoot
    Path to DAT file storage root.

.PARAMETER DatHashType
    Hash algorithm for DAT matching: SHA1, MD5, or CRC32.  Default: SHA1.

.PARAMETER DatFallback
    Fall back to name matching when DAT hash misses.  Default: $true.

.PARAMETER DatMap
    Hashtable mapping console names to DAT file paths.

.PARAMETER Convert
    Convert winners to best format after dedupe.  Default: $false.

.PARAMETER ToolOverrides
    Hashtable of tool path overrides: @{ chdman='...'; '7z'='...' }.

.PARAMETER Use1G1R
    Enable 1G1R (parent/clone) deduplication.  Default: $false.

.PARAMETER GenerateReports
    Generate HTML/CSV reports even in DryRun mode.  Default: $true.

.PARAMETER SkipConfirm
    Skip interactive confirmations (for automation).  Default: $false.

.PARAMETER JpKeepConsoles
    Console names where JP ROMs are kept even without preferred region.

.PARAMETER JpOnlyForSelected
    Apply JP-keep policy only to consoles in JpKeepConsoles.

.PARAMETER Quiet
    Suppress informational output.  Errors still shown.

.EXAMPLE
    .\Invoke-RomCleanup.ps1 -Roots 'D:\ROMs\SNES','D:\ROMs\Genesis' -Mode DryRun

.EXAMPLE
    .\Invoke-RomCleanup.ps1 -Roots 'D:\ROMs' -Mode Move -Prefer EU,US -RemoveJunk -SkipConfirm

.EXAMPLE
    .\Invoke-RomCleanup.ps1 -Roots 'D:\ROMs' -Mode Move -UseDat -DatRoot 'D:\DATs' -DatMap @{ 'SNES'='D:\DATs\SNES.dat' }
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory, Position = 0)]
    [string[]]$Roots,

    [ValidateSet('DryRun', 'Move')]
    [string]$Mode = 'DryRun',

    [string[]]$Prefer = @('EU', 'US', 'WORLD', 'JP'),

    [string[]]$Extensions = @('.zip', '.7z', '.chd', '.iso', '.bin', '.cue', '.gdi',
                               '.ccd', '.img', '.sub', '.mds', '.mdf', '.pbp', '.gcm',
                               '.gcz', '.rvz', '.wbfs', '.nsp', '.xci', '.cia', '.3ds',
                               '.nds', '.gba', '.gb', '.gbc', '.nes', '.sfc', '.smc',
                               '.md', '.sms', '.gg', '.pce', '.ngp', '.ngc', '.ws',
                               '.wsc', '.col', '.sg', '.vb', '.vec', '.a26', '.a78',
                               '.lnx', '.j64', '.n64', '.z64', '.v64'),

    [string]$TrashRoot,

    [string]$AuditRoot,

    [switch]$RemoveJunk,

    [switch]$AggressiveJunk,

    [switch]$AliasEditionKeying,

    [switch]$SeparateBios,

    [string[]]$ConsoleFilter = @(),

    [switch]$SortConsole,

    [switch]$UseDat,

    [string]$DatRoot,

    [ValidateSet('SHA1', 'MD5', 'CRC32')]
    [string]$DatHashType = 'SHA1',

    [switch]$DatFallback,

    [hashtable]$DatMap = @{},

    [switch]$Convert,

    [hashtable]$ToolOverrides = @{},

    [switch]$Use1G1R,

    [switch]$GenerateReports,

    [switch]$SkipConfirm,

    [string[]]$JpKeepConsoles = @(),

    [switch]$JpOnlyForSelected,

    [switch]$Quiet,

    [switch]$NotifyAfterRun,

    [switch]$EmitJsonSummary,

    [string]$SummaryJsonPath,

    [hashtable]$Ports = @{}
)

# ── Bootstrap ────────────────────────────────────────────────────────────────
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$_scriptDir = $PSScriptRoot
if (-not $_scriptDir) { $_scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition }

# Locate module directory
$_moduleDir = Join-Path $_scriptDir 'dev' 'modules'
if (-not (Test-Path -LiteralPath $_moduleDir -PathType Container)) {
    # Maybe running from dev/ or from a packaged layout
    $_moduleDir = Join-Path $_scriptDir 'modules'
}
if (-not (Test-Path -LiteralPath $_moduleDir -PathType Container)) {
    Write-Error "Module directory not found.  Expected: dev/modules/ relative to script."
    exit 3
    return
}

$_loaderPath = Join-Path $_moduleDir 'RomCleanupLoader.ps1'
if (-not (Test-Path -LiteralPath $_loaderPath -PathType Leaf)) {
    Write-Error "RomCleanupLoader.ps1 not found in $_moduleDir"
    exit 3
    return
}

$script:_RomCleanupModuleRoot = $_moduleDir
. $_loaderPath

# Initialize application state (required by many module functions)
Initialize-AppState

try {
    if (Get-Command Import-ConsolePlugins -ErrorAction SilentlyContinue) {
        [void](Import-ConsolePlugins)
    }
} catch {
    # Plugin loading is optional in headless mode.
}

try {
    $updateRepo = if ($env:ROMCLEANUP_UPDATE_REPO) { [string]$env:ROMCLEANUP_UPDATE_REPO } else { $null }
    if (-not [string]::IsNullOrWhiteSpace($updateRepo) -and (Get-Command Test-RomCleanupUpdateAvailable -ErrorAction SilentlyContinue)) {
        $currentVer = if (Get-Command Get-RomCleanupVersion -ErrorAction SilentlyContinue) { Get-RomCleanupVersion } else { '1.0.0' }
        $updateResult = Test-RomCleanupUpdateAvailable -Repo $updateRepo -CurrentVersion $currentVer
        if ($updateResult.UpdateAvailable -and -not $Quiet) {
            Write-Output ("Update verfügbar: {0} -> {1} ({2})" -f $updateResult.CurrentVersion, $updateResult.LatestTag, $updateResult.LatestUrl)
        }
    }
} catch {
    # Update check is best-effort.
}

# ── Exit Codes ───────────────────────────────────────────────────────────────
$EXIT_OK              = 0
$EXIT_ERROR           = 1
$EXIT_CANCELLED       = 2
$EXIT_VALIDATION      = 3

# ── Logging ──────────────────────────────────────────────────────────────────
$_logLines = New-Object System.Collections.Generic.List[string]
$script:LastCliPreflight = [pscustomobject]@{ IsOk = $true; Errors = @(); Warnings = @() }

$LogFn = {
    param([string]$Message)
    $_logLines.Add($Message)
    if (-not $Quiet) {
        Write-Output $Message
    }
}

function Write-CliJsonSummary {
    param(
        [Parameter(Mandatory=$true)][string]$Status,
        [Parameter(Mandatory=$true)][int]$ExitCode,
        [object]$Result,
        [int]$RunErrorCount = 0,
        [string]$ErrorMessage,
        [string]$ScriptStackTrace
    )

    if (-not $EmitJsonSummary -and [string]::IsNullOrWhiteSpace($SummaryJsonPath)) { return }

    $payload = [ordered]@{
        schemaVersion = 'romcleanup-cli-result-v1'
        timestampUtc  = (Get-Date).ToUniversalTime().ToString('o')
        status        = [string]$Status
        exitCode      = [int]$ExitCode
        mode          = [string]$Mode
        roots         = @($Roots)
        preflight     = [ordered]@{
            isOk     = [bool]$script:LastCliPreflight.IsOk
            errors   = @($script:LastCliPreflight.Errors)
            warnings = @($script:LastCliPreflight.Warnings)
        }
        runErrors     = [int]$RunErrorCount
        reports       = [ordered]@{
            csvPath  = if ($Result -and $Result.PSObject.Properties.Name -contains 'CsvPath') { [string]$Result.CsvPath } else { $null }
            htmlPath = if ($Result -and $Result.PSObject.Properties.Name -contains 'HtmlPath') { [string]$Result.HtmlPath } else { $null }
            launchBoxXmlPath = if ($Result -and $Result.PSObject.Properties.Name -contains 'LaunchBoxXmlPath') { [string]$Result.LaunchBoxXmlPath } else { $null }
            emulationStationXmlPath = if ($Result -and $Result.PSObject.Properties.Name -contains 'EmulationStationXmlPath') { [string]$Result.EmulationStationXmlPath } else { $null }
        }
        error         = if ([string]::IsNullOrWhiteSpace($ErrorMessage)) { $null } else { [ordered]@{ message = [string]$ErrorMessage; stack = [string]$ScriptStackTrace } }
    }

    $json = $payload | ConvertTo-Json -Depth 8

    if (-not [string]::IsNullOrWhiteSpace($SummaryJsonPath)) {
        $targetDir = Split-Path -Parent $SummaryJsonPath
        if (-not [string]::IsNullOrWhiteSpace($targetDir) -and -not (Test-Path -LiteralPath $targetDir -PathType Container)) {
            [void](New-Item -ItemType Directory -Path $targetDir -Force)
        }
        $json | Out-File -LiteralPath $SummaryJsonPath -Encoding UTF8 -Force
    }

    if ($EmitJsonSummary) {
        Write-Output $json
    }
}

function Invoke-CliScheduledNotification {
    <#
    .SYNOPSIS
      Sends a notification after a completed run (CLI only).
    .DESCRIPTION
      Called automatically when -NotifyAfterRun is specified on the CLI.
      Requires Send-ScheduledRunNotification (from Notifications.ps1) to be loaded.
      Supported channels depend on configuration: Toast, E-Mail, Webhook, Discord, Telegram.
      Not available in the GUI -- use the GUI's built-in status indicators instead.
    .EXAMPLE
      .\Invoke-RomCleanup.ps1 -Roots C:\ROMs -Mode region-dedupe -NotifyAfterRun
    #>
    param(
        [object]$Result,
        [int]$RunErrorCount
    )

    if (-not $NotifyAfterRun) { return }
    if (-not (Get-Command Send-ScheduledRunNotification -ErrorAction SilentlyContinue)) { return }

    try {
        $filesScanned = if ($Result -and $Result.PSObject.Properties.Name -contains 'FilesScanned') { [int]$Result.FilesScanned } else { 0 }
        $groups       = if ($Result -and $Result.PSObject.Properties.Name -contains 'Groups') { [int]$Result.Groups } else { 0 }
        $moves        = if ($Result -and $Result.PSObject.Properties.Name -contains 'Moves') { [int]$Result.Moves } else { 0 }
        $reportPath   = if ($Result -and $Result.PSObject.Properties.Name -contains 'HtmlPath') { [string]$Result.HtmlPath } else { '' }
        $summary = "Mode=$Mode | Roots=$($Roots.Count) | Files=$filesScanned | Groups=$groups | Moves=$moves | Errors=$RunErrorCount"
        [void](Send-ScheduledRunNotification -Summary $summary -ReportPath $reportPath)
    } catch {
        if (-not $Quiet) {
            Write-Warning ("Scheduled notification failed: {0}" -f $_.Exception.Message)
        }
    }
}

# ── Preflight (non-GUI) ─────────────────────────────────────────────────────
function Invoke-CliPreflight {
    param(
        [string[]]$Roots,
        [string[]]$Exts,
        [bool]$UseDat,
        [string]$DatRoot,
        [hashtable]$DatMap,
        [bool]$DoConvert,
        [hashtable]$ToolOverrides,
        [string]$AuditRoot
    )

    $errors   = [System.Collections.Generic.List[string]]::new()
    $warnings = [System.Collections.Generic.List[string]]::new()

    $sharedPreflightPath = Join-Path $PSScriptRoot 'dev' 'tools' 'preflight' 'Invoke-RomCleanupPreflight.ps1'
    if (Test-Path -LiteralPath $sharedPreflightPath -PathType Leaf) {
        $sharedResult = & {
            param(
                [string]$ScriptPath,
                [string[]]$RootsArg,
                [string]$AuditRootArg,
                [string]$DatRootArg,
                [hashtable]$DatMapArg,
                [hashtable]$ToolPathsArg
            )

            . $ScriptPath

            Set-Variable -Name 'Roots' -Value @($RootsArg) -Scope Local
            Set-Variable -Name 'TrashRoot' -Value $null -Scope Local
            Set-Variable -Name 'AuditRoot' -Value $AuditRootArg -Scope Local
            Set-Variable -Name 'DatRoot' -Value $DatRootArg -Scope Local
            Set-Variable -Name 'DatMap' -Value $(if ($DatMapArg) { $DatMapArg } else { @{} }) -Scope Local
            Set-Variable -Name 'ToolPaths' -Value $(if ($ToolPathsArg) { $ToolPathsArg } else { @{} }) -Scope Local

            $innerErrors = [System.Collections.Generic.List[string]]::new()
            $innerWarnings = [System.Collections.Generic.List[string]]::new()
            $checks = @('Test-RootsFolders','Test-TrashRoot','Test-AuditRoot','Test-DatConfiguration','Test-ConversionTools','Test-ReparsePointsInRoots','Test-PathSubstringEdgeCases')
            foreach ($check in $checks) {
                if (-not (Get-Command $check -ErrorAction SilentlyContinue)) { continue }
                if (Get-Command Reset-PreflightState -ErrorAction SilentlyContinue) { Reset-PreflightState }
                & $check
                if (Get-Command Get-PreflightState -ErrorAction SilentlyContinue) {
                    $state = Get-PreflightState
                    foreach ($entry in @($state.Errors)) { if (-not [string]::IsNullOrWhiteSpace([string]$entry)) { [void]$innerErrors.Add([string]$entry) } }
                    foreach ($entry in @($state.Warnings)) { if (-not [string]::IsNullOrWhiteSpace([string]$entry)) { [void]$innerWarnings.Add([string]$entry) } }
                }
            }

            return [pscustomobject]@{
                Errors = @($innerErrors)
                Warnings = @($innerWarnings)
            }
        } -ScriptPath $sharedPreflightPath -RootsArg $Roots -AuditRootArg $AuditRoot -DatRootArg $DatRoot -DatMapArg $DatMap -ToolPathsArg $ToolOverrides

        foreach ($entry in @($sharedResult.Errors)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$entry)) { [void]$errors.Add([string]$entry) }
        }
        foreach ($entry in @($sharedResult.Warnings)) {
            if (-not [string]::IsNullOrWhiteSpace([string]$entry)) { [void]$warnings.Add([string]$entry) }
        }

        if (-not $Exts -or $Exts.Count -eq 0) {
            [void]$errors.Add('No file extensions specified.')
        }
        if ($DoConvert) {
            $anyTool = $false
            foreach ($tool in @('chdman', 'dolphintool', '7z')) {
                $p = $null
                if ($ToolOverrides -and $ToolOverrides.ContainsKey($tool)) { $p = [string]$ToolOverrides[$tool] }
                if (-not $p) { $p = Find-ConversionTool -ToolName $tool }
                if ($p) { $anyTool = $true }
            }
            if (-not $anyTool) { [void]$warnings.Add('No conversion tools found.  Conversion will be skipped.') }
        }
    } else {
        if (-not $Roots -or $Roots.Count -eq 0) {
            [void]$errors.Add('No ROM root folders specified.')
        }
        foreach ($r in $Roots) {
            if (-not (Test-Path -LiteralPath $r -PathType Container)) {
                [void]$errors.Add("Root folder not found: $r")
            }
        }
    }

    return [pscustomobject]@{
        IsOk = ($errors.Count -eq 0)
        Errors = @($errors | Select-Object -Unique)
        Warnings = @($warnings | Select-Object -Unique)
    }
}

# ── Main ─────────────────────────────────────────────────────────────────────
try {
    # --- Banner ---
    if (-not $Quiet) {
        Write-Output ""
        Write-Output "================================================================"
        Write-Output "  ROM Cleanup & Region Dedupe  —  CLI Mode"
        Write-Output "================================================================"
        Write-Output "  Mode:    $Mode"
        Write-Output "  Roots:   $($Roots -join ', ')"
        Write-Output "  Prefer:  $($Prefer -join ', ')"
        Write-Output "  Junk:    $(if ($RemoveJunk) { 'Yes' } else { 'No' })$(if ($AggressiveJunk) { ' (aggressive)' } else { '' })"
        Write-Output "  DAT:     $(if ($UseDat) { 'Yes' } else { 'No' })"
        Write-Output "  Convert: $(if ($Convert) { 'Yes' } else { 'No' })"
        Write-Output "  1G1R:    $(if ($Use1G1R) { 'Yes' } else { 'No' })"
        Write-Output "================================================================"
        Write-Output ""
    }

    # --- Preflight ---
    $preflight = Invoke-CliPreflight `
        -Roots $Roots `
        -Exts  $Extensions `
        -UseDat ([bool]$UseDat) `
        -DatRoot $DatRoot `
        -DatMap  $DatMap `
        -DoConvert ([bool]$Convert) `
        -ToolOverrides $ToolOverrides `
        -AuditRoot $AuditRoot

    $script:LastCliPreflight = $preflight

    if ($preflight.Errors -and @($preflight.Errors).Count -gt 0) {
        Write-Output ""
        Write-Output "=== Preflight ERRORS ==="
        foreach ($entry in @($preflight.Errors)) { Write-Output ("  ERROR: {0}" -f [string]$entry) }
    }
    if ($preflight.Warnings -and @($preflight.Warnings).Count -gt 0) {
        Write-Output ""
        Write-Output "=== Preflight WARNINGS ==="
        foreach ($entry in @($preflight.Warnings)) { Write-Output ("  WARN: {0}" -f [string]$entry) }
    }

    if (-not [bool]$preflight.IsOk) {
        Write-CliJsonSummary -Status 'validation_failed' -ExitCode $EXIT_VALIDATION
        exit $EXIT_VALIDATION
        return
    }

    # --- Move confirmation ---
    if ($Mode -eq 'Move' -and -not $SkipConfirm) {
        Write-Output ""
        Write-Output "WARNING: Move mode will permanently relocate duplicate/junk files."
        Write-Output "Roots: $($Roots -join ', ')"
        Write-Output ""
        $answer = Read-Host "Type MOVE to confirm, or anything else to cancel"
        if ($answer -ne 'MOVE') {
            Write-Output "Cancelled."
            Write-CliJsonSummary -Status 'cancelled' -ExitCode $EXIT_CANCELLED
            exit $EXIT_CANCELLED
            return
        }
    }

    # --- Initialize run ---
    Reset-RunErrors

    if (-not $Quiet) {
        & $LogFn "Starting $Mode run..."
        & $LogFn ""
    }

    # --- ConfirmMove scriptblock for Invoke-RegionDedupe ---
    $confirmMoveBlock = $null
    if ($Mode -eq 'Move') {
        if ($SkipConfirm) {
            $confirmMoveBlock = { param($msg) return $true }
        } else {
            $confirmMoveBlock = {
                param($msg)
                Write-Output ""
                Write-Output $msg
                $r = Read-Host "Type YES to proceed"
                return ($r -eq 'YES')
            }
        }
    }

    # --- Core: Region Dedupe ---
    $dedupeParams = @{
        Roots                     = $Roots
        Mode                      = $Mode
        PreferOrder               = $Prefer
        IncludeExtensions         = $Extensions
        RemoveJunk                = [bool]$RemoveJunk
        AggressiveJunk            = [bool]$AggressiveJunk
        AliasEditionKeying        = [bool]$AliasEditionKeying
        SeparateBios              = [bool]$SeparateBios
        ConsoleFilter             = $ConsoleFilter
        JpOnlyForSelectedConsoles = [bool]$JpOnlyForSelected
        JpKeepConsoles            = $JpKeepConsoles
        GenerateReportsInDryRun   = [bool]$GenerateReports
        UseDat                    = [bool]$UseDat
        DatHashType               = $DatHashType
        DatFallback               = [bool]$DatFallback
        ToolOverrides             = $ToolOverrides
        Log                       = $LogFn
        RequireConfirmMove        = (-not [bool]$SkipConfirm)
        ConfirmMove               = $confirmMoveBlock
        ConvertChecked            = [bool]$Convert
        Use1G1R                   = [bool]$Use1G1R
    }

    # Optional parameters (only pass if specified)
    if ($TrashRoot)  { $dedupeParams['TrashRoot']  = $TrashRoot }
    if ($AuditRoot)  { $dedupeParams['AuditRoot']  = $AuditRoot }
    if ($DatRoot)    { $dedupeParams['DatRoot']     = $DatRoot }
    if ($DatMap -and $DatMap.Count -gt 0) { $dedupeParams['DatMap'] = $DatMap }
    $result = Invoke-CliRunAdapter `
        -SortConsole ([bool]$SortConsole) `
        -Mode $Mode `
        -Roots $Roots `
        -Extensions $Extensions `
        -UseDat ([bool]$UseDat) `
        -DatRoot $DatRoot `
        -DatHashType $DatHashType `
        -DatMap $DatMap `
        -ToolOverrides $ToolOverrides `
        -Log $LogFn `
        -Convert ([bool]$Convert) `
        -DedupeParams $dedupeParams `
        -Ports $Ports

    # --- Error summary ---
    Write-RunErrorSummary -Log $LogFn

    # --- Summary ---
    if (-not $Quiet) {
        Write-Output ""
        Write-Output "================================================================"
        Write-Output "  Run completed ($Mode)"
        if ($result) {
            if ($result.PSObject.Properties.Name -contains 'CsvPath' -and -not [string]::IsNullOrWhiteSpace([string]$result.CsvPath)) {
                Write-Output "  CSV Report:  $($result.CsvPath)"
            }
            if ($result.PSObject.Properties.Name -contains 'HtmlPath' -and -not [string]::IsNullOrWhiteSpace([string]$result.HtmlPath)) {
                Write-Output "  HTML Report: $($result.HtmlPath)"
            }
        }
        Write-Output "================================================================"
    }

    $errCount = Get-RunErrorCount
    if ($errCount -gt 0) {
        Invoke-CliScheduledNotification -Result $result -RunErrorCount $errCount
        Write-CliJsonSummary -Status 'completed_with_errors' -ExitCode $EXIT_ERROR -Result $result -RunErrorCount $errCount
        exit $EXIT_ERROR
        return
    }
    Invoke-CliScheduledNotification -Result $result -RunErrorCount 0
    Write-CliJsonSummary -Status 'completed' -ExitCode $EXIT_OK -Result $result -RunErrorCount 0
    exit $EXIT_OK
    return

} catch [System.OperationCanceledException] {
    if (-not $Quiet) { Write-Output "Operation cancelled." }
    Write-CliJsonSummary -Status 'cancelled' -ExitCode $EXIT_CANCELLED -ErrorMessage $_.Exception.Message -ScriptStackTrace $_.ScriptStackTrace
    exit $EXIT_CANCELLED
    return

} catch {
    Write-Output ""
    Write-Output "FATAL ERROR: $($_.Exception.Message)"
    Write-Output $_.ScriptStackTrace
    Write-CliJsonSummary -Status 'failed' -ExitCode $EXIT_ERROR -ErrorMessage $_.Exception.Message -ScriptStackTrace $_.ScriptStackTrace
    exit $EXIT_ERROR
    return
}
