# ================================================================
#  RomCleanup PowerShell Module  (RomCleanup.psm1)
# ================================================================
#  Single entry point for all ROM Cleanup & Region Dedupe functionality.
#  Dot-sources all component modules in dependency order.
#
#  Usage:  Import-Module .\dev\modules\RomCleanup.psd1
#
#  IMPORTANT: Do NOT dot-source this file.  PowerShell creates an implicit
#  module scope for .psm1 files, so functions will NOT be visible in the
#  caller's scope.  For dot-source loading, use RomCleanupLoader.ps1 instead.
# ================================================================

# When dot-sourced from simple_sort.ps1, $PSScriptRoot may be empty for .psm1
# files in PS 5.1.  The caller pre-sets RomCleanupModuleRoot in that case.
if (-not (Get-Variable -Name RomCleanupModuleRoot -Scope Script -ErrorAction SilentlyContinue) -and
    (Get-Variable -Name _RomCleanupModuleRoot -Scope Script -ErrorAction SilentlyContinue)) {
    $script:RomCleanupModuleRoot = $script:_RomCleanupModuleRoot
}

if (-not $script:RomCleanupModuleRoot) {
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
# 19. BackgroundOps      – Background runspace ops  (needs Settings)
# 20. Gui                – WinForms design system  (needs Settings)
# 21. GuiState           – GUI state management    (needs Gui, FileOps, Core)
# 22. GuiHandlers        – Event handler wiring    (needs GuiState, Core)
# 23. GuiHandlersOps*    – Operation handler split (needs RunHelpers, GuiState)

$moduleListPath = Join-Path $script:RomCleanupModuleRoot 'ModuleFileList.ps1'
if (Test-Path -LiteralPath $moduleListPath -PathType Leaf) {
    . $moduleListPath
}

$_moduleFiles = if (Get-Command Get-RomCleanupModuleFiles -ErrorAction SilentlyContinue) {
    @(Get-RomCleanupModuleFiles)
} else {
    throw 'ModuleFileList.ps1 fehlt oder Get-RomCleanupModuleFiles ist nicht verfügbar.'
}

foreach ($_mf in $_moduleFiles) {
    $_mfPath = Join-Path $script:RomCleanupModuleRoot $_mf
    if (Test-Path -LiteralPath $_mfPath -PathType Leaf) {
        . $_mfPath
    }
}

Remove-Variable -Name moduleListPath, _moduleFiles, _mf, _mfPath -ErrorAction SilentlyContinue

# --- Public API definition ---------------------------------------------------
# Export policy is manifest-driven; wildcard avoids manual function list drift.
$_publicFunctions = '*'

# Export only when loaded as a proper module (Import-Module).
# When dot-sourced directly, this block is skipped gracefully.
$_publicAliases = '*'

$manifestPath = Join-Path $script:RomCleanupModuleRoot 'RomCleanup.psd1'
if (Test-Path -LiteralPath $manifestPath -PathType Leaf) {
    try {
        $manifestData = Import-PowerShellDataFile -Path $manifestPath
        if ($manifestData -and $manifestData.FunctionsToExport -and @($manifestData.FunctionsToExport).Count -gt 0) {
            $_publicFunctions = @($manifestData.FunctionsToExport)
        }
        if ($manifestData -and $manifestData.AliasesToExport -and @($manifestData.AliasesToExport).Count -gt 0) {
            $_publicAliases = @($manifestData.AliasesToExport)
        }
    } catch { }
}

if ($_publicFunctions -eq '*') {
    $approvedVerbs = [System.Collections.Generic.HashSet[string]]::new([System.StringComparer]::OrdinalIgnoreCase)
    foreach ($verbInfo in @(Get-Verb)) {
        [void]$approvedVerbs.Add([string]$verbInfo.Verb)
    }

    $moduleName = if ($MyInvocation.MyCommand.ScriptBlock.Module) {
        [string]$MyInvocation.MyCommand.ScriptBlock.Module.Name
    } else {
        ''
    }

    $moduleFunctions = @(
        Get-ChildItem Function:\ -ErrorAction SilentlyContinue |
            Where-Object {
                $_.ScriptBlock -and $_.ScriptBlock.Module -and
                (-not [string]::IsNullOrWhiteSpace($moduleName)) -and
                ([string]$_.ScriptBlock.Module.Name -eq $moduleName)
            } |
            ForEach-Object { [string]$_.Name }
    )

    $_publicFunctions = @(
        $moduleFunctions |
            Where-Object {
                $name = [string]$_
                if ([string]::IsNullOrWhiteSpace($name) -or -not $name.Contains('-')) { return $false }
                $verb = $name.Split('-', 2)[0]
                return $approvedVerbs.Contains($verb)
            } |
            Sort-Object -Unique
    )
}

if ($_publicAliases -eq '*') {
    $_publicAliases = @(
        'Remove-DatFromInventory',
        'Update-DatBatch'
    )
}

if ($MyInvocation.MyCommand.ScriptBlock.Module) {
    Export-ModuleMember -Function $_publicFunctions -Alias $_publicAliases
}

Remove-Variable -Name _publicFunctions -ErrorAction SilentlyContinue
Remove-Variable -Name _publicAliases -ErrorAction SilentlyContinue
Remove-Variable -Name manifestPath, manifestData -ErrorAction SilentlyContinue
