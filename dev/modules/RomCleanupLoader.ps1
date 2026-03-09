# ================================================================
#  RomCleanup Dot-Source Loader  (RomCleanupLoader.ps1)
# ================================================================
#  Loads all ROM Cleanup & Region Dedupe component files into the
#  caller's scope via dot-sourcing.  All functions (public AND
#  internal) become visible - $script: state variables are shared.
#
#  Usage:  $script:RomCleanupModuleRoot = $moduleDir
#          . "$moduleDir\RomCleanupLoader.ps1"
#
#  NOTE: For proper module isolation (restricted exports), use
#        Import-Module .\dev\modules\RomCleanup.psd1
# ================================================================

Set-StrictMode -Version Latest

# Resolve the module root directory.
# The caller should pre-set $script:RomCleanupModuleRoot before dot-sourcing.
if (-not (Get-Variable -Name RomCleanupModuleRoot -Scope Script -ErrorAction SilentlyContinue) -and
    (Get-Variable -Name _RomCleanupModuleRoot -Scope Script -ErrorAction SilentlyContinue)) {
    $script:RomCleanupModuleRoot = $script:_RomCleanupModuleRoot
}

if (-not (Get-Variable -Name RomCleanupModuleRoot -Scope Script -ErrorAction SilentlyContinue) -or
    -not $script:RomCleanupModuleRoot) {
    if ($PSScriptRoot) {
        $script:RomCleanupModuleRoot = $PSScriptRoot
    } else {
        $script:RomCleanupModuleRoot = Split-Path -Parent $MyInvocation.MyCommand.Definition
    }
}

# Legacy compatibility shim for older callers.
$script:_RomCleanupModuleRoot = $script:RomCleanupModuleRoot

# --- Component loading order (respects inter-module dependencies) ------------
#  1. Settings          – AppState, user prefs  (no deps)
#  2. Tools             – External tool wrappers (needs Settings)
#  3. FileOps           – FS operations          (needs Settings)
#  4. Report            – HTML/CSV reports       (no runtime deps)
#  5. FormatScoring     – Format/Region scoring  (no deps)
#  6. SetParsing        – CUE/GDI/CCD/M3U parse (no deps)
#  7. Ps3Dedupe         – PS3 folder dedupe      (needs FileOps)
#  8. ZipSort           – ZIP sort helpers        (needs Tools)
#  9. Sets              – Set/FileItem builders   (needs SetParsing)
# 10. Core              – Region, GameKey, Winner (no deps)
# 11. Classification    – File/Console classify   (needs Core, Tools)
# 12. Convert           – Format conversion       (needs Tools, Settings)
# 13. Dedupe            – Region dedupe engine     (needs Core, Sets, Classification, FormatScoring)
# 14. Dat               – DAT index/hash          (needs Tools, Core)
# 15. DatSources        – DAT download/install    (needs Dat)
# 16. RunHelpers         – Orchestration helpers   (needs many)
# 17. ConsoleSort        – Console sort            (needs Classification, FileOps)
# 18. Logging            – JSON/text logging       (needs Settings, FileOps)
# 19. Gui                – WinForms design system  (needs Settings)
# 20. GuiState           – GUI state management    (needs Gui, FileOps, Core)
# 21. GuiHandlers        – Event handler wiring    (needs GuiState, Core)
# 22. GuiHandlersOps*    – Operation handler split (needs RunHelpers, GuiState)

$moduleListPath = Join-Path $script:RomCleanupModuleRoot 'ModuleFileList.ps1'
if (Test-Path -LiteralPath $moduleListPath -PathType Leaf) {
    . $moduleListPath
}

$_moduleFiles = if (Get-Command Get-RomCleanupModuleFiles -ErrorAction SilentlyContinue) {
    $_loaderProfile = [string]$env:ROMCLEANUP_MODULE_PROFILE
    if ([string]::IsNullOrWhiteSpace($_loaderProfile) -and (Get-Variable -Scope Script -Name ROMCLEANUP_MODULE_PROFILE -ErrorAction SilentlyContinue)) {
        $_loaderProfile = [string]$script:ROMCLEANUP_MODULE_PROFILE
    }
    if ([string]::IsNullOrWhiteSpace($_loaderProfile)) {
        $_loaderProfile = 'all'
    }
    @(Get-RomCleanupModuleFiles -Profile $_loaderProfile)
} else {
    throw 'ModuleFileList.ps1 fehlt oder Get-RomCleanupModuleFiles ist nicht verfügbar.'
}

if (Get-Command Resolve-RomCleanupModuleOrder -ErrorAction SilentlyContinue) {
    $_moduleFiles = @(Resolve-RomCleanupModuleOrder -ModuleFiles @($_moduleFiles))
}

if (-not (Get-Variable -Name RomCleanupLoadedModules -Scope Script -ErrorAction SilentlyContinue)) {
    $script:RomCleanupLoadedModules = [System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
}

foreach ($_mf in $_moduleFiles) {
    if ($script:RomCleanupLoadedModules.Contains([string]$_mf)) { continue }
    $_mfPath = Join-Path $script:RomCleanupModuleRoot $_mf
    # BUG LOADER-003 FIX: Validate resolved path stays within module root (prevent path traversal)
    $_mfResolved = try { [System.IO.Path]::GetFullPath($_mfPath) } catch { '' }
    if (-not $_mfResolved -or -not $_mfResolved.StartsWith($script:RomCleanupModuleRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        Write-Warning ('[Loader] Modul-Pfad ausserhalb Root ignoriert: {0}' -f $_mf)
        continue
    }
    if (Test-Path -LiteralPath $_mfResolved -PathType Leaf) {
        # LOADER-004 FIX: Per-file error handling so one broken module doesn't block all others
        try {
            . $_mfResolved
            [void]$script:RomCleanupLoadedModules.Add([string]$_mf)
        } catch {
            Write-Warning ('[Loader] Modul konnte nicht geladen werden: {0} — {1}' -f $_mf, $_.Exception.Message)
        }
    }
}

# Promote module functions to global scope so that .GetNewClosure() closures can
# resolve them.  PowerShell closures created via .GetNewClosure() run inside a
# dynamic module whose command lookup only reaches global: scope — functions
# defined in an intermediate script scope (e.g. when simple_sort.ps1 is invoked
# via & from VSCode terminal) are invisible.  This promotion is idempotent and
# harmless when the script scope already IS the global scope.
# Skip during automated tests to preserve Pester mock isolation.
# BUG LOADER-002 FIX: Parse TESTMODE consistently — only 1/true/yes/on = active
$_testModeFlag = [string]$env:ROMCLEANUP_TESTMODE
$_isTestMode = (-not [string]::IsNullOrWhiteSpace($_testModeFlag)) -and (@('1','true','yes','on') -contains $_testModeFlag.Trim().ToLowerInvariant())
if (-not $_isTestMode) {
    foreach ($_fn in (Get-ChildItem Function:)) {
        if ($_fn.ScriptBlock -and $_fn.ScriptBlock.File -and
            $_fn.ScriptBlock.File.StartsWith($script:RomCleanupModuleRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
            Set-Item -Path "Function:global:$($_fn.Name)" -Value $_fn.ScriptBlock -ErrorAction SilentlyContinue
        }
    }
    Remove-Variable -Name _fn -ErrorAction SilentlyContinue
}

function Import-RomCleanupFeatureModules {
    param([Parameter(Mandatory)][string]$Feature)

    if (-not (Get-Command Get-RomCleanupFeatureModuleFiles -ErrorAction SilentlyContinue)) {
        return @()
    }

    $loadedNow = New-Object System.Collections.Generic.List[string]
    $featureFiles = @(Get-RomCleanupFeatureModuleFiles -Feature $Feature)
    if (Get-Command Resolve-RomCleanupModuleOrder -ErrorAction SilentlyContinue) {
        $featureFiles = @(Resolve-RomCleanupModuleOrder -ModuleFiles @($featureFiles))
    }
    foreach ($featureFile in $featureFiles) {
        if ($script:RomCleanupLoadedModules.Contains([string]$featureFile)) { continue }
        $featurePath = Join-Path $script:RomCleanupModuleRoot $featureFile
        if (Test-Path -LiteralPath $featurePath -PathType Leaf) {
            . $featurePath
            [void]$script:RomCleanupLoadedModules.Add([string]$featureFile)
            [void]$loadedNow.Add([string]$featureFile)
        }
    }
    # Promote newly loaded functions to global scope (same rationale as main load loop above)
    # BUG LOADER-002 FIX: Parse TESTMODE consistently — only 1/true/yes/on = active
    $_tmFlag = [string]$env:ROMCLEANUP_TESTMODE
    $_isTM = (-not [string]::IsNullOrWhiteSpace($_tmFlag)) -and (@('1','true','yes','on') -contains $_tmFlag.Trim().ToLowerInvariant())
    if (-not $_isTM) {
        foreach ($fn in (Get-ChildItem Function:)) {
            if ($fn.ScriptBlock -and $fn.ScriptBlock.File -and
                $fn.ScriptBlock.File.StartsWith($script:RomCleanupModuleRoot, [System.StringComparison]::OrdinalIgnoreCase) -and
                -not (Get-Item "Function:global:$($fn.Name)" -ErrorAction SilentlyContinue)) {
                Set-Item -Path "Function:global:$($fn.Name)" -Value $fn.ScriptBlock -ErrorAction SilentlyContinue
            }
        }
    }
    return @($loadedNow)
}

# LOADER-005 FIX: Include $_loaderProfile and $_mfResolved in variable cleanup
Remove-Variable -Name moduleListPath, _moduleFiles, _mf, _mfPath, _mfResolved, _loaderProfile, _testModeFlag, _isTestMode -ErrorAction SilentlyContinue
