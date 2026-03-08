function Get-SimpleSortTestScript {
    [CmdletBinding()]
    param(
        [Parameter(Mandatory = $true)][string]$TestsRoot,
        [Parameter(Mandatory = $true)][string]$TempPrefix,
        [switch]$IncludeGui
    )

    $scriptPath = Join-Path $TestsRoot '..\..\simple_sort.ps1'
    $scriptContent = Get-Content -LiteralPath $scriptPath -Raw
    $moduleDir = Join-Path (Split-Path -Parent $scriptPath) 'dev\modules'

    $modifiedContent = $scriptContent -replace '\[void\]\$form\.ShowDialog\(\)', '$null = $null'
    $modifiedContent = $modifiedContent -replace '\[System\.Windows\.Forms\.Application\]::EnableVisualStyles\(\)', '$null = $null'
    $modifiedContent = $modifiedContent -replace '\[System\.Windows\.Forms\.Application\]::SetCompatibleTextRenderingDefault\([^)]+\)', '$null = $null'
    $modifiedContent = $modifiedContent -replace 'if \(\[System\.Threading\.Thread\]::CurrentThread\.ApartmentState -ne \[System\.Threading\.ApartmentState\]::STA\) \{', 'if ($false -and [System.Threading.Thread]::CurrentThread.ApartmentState -ne [System.Threading.ApartmentState]::STA) {'

    # Strip the entire GUI construction block (~2,600 lines of WinForms control
    # creation) unless the caller explicitly needs it.  Creating thousands of
    # Win32 handles per test file freezes VS Code's integrated terminal.
    if (-not $IncludeGui) {
        $modifiedContent = $modifiedContent -replace '(?s)# GUI-BLOCK-BEGIN[^\r\n]*\r?\n.*?# GUI-BLOCK-END[^\r\n]*', '# GUI block stripped for tests'
        # Force module profile to 'core' so WPF modules (WpfShims.ps1 etc.)
        # are never loaded.  WpfShims.ps1 unconditionally calls
        # Add-Type -AssemblyName PresentationFramework at module level,
        # which freezes VS Code's integrated terminal.
        $modifiedContent = $modifiedContent -replace '\$script:ROMCLEANUP_MODULE_PROFILE\s*=\s*''all''', '$script:ROMCLEANUP_MODULE_PROFILE = ''core'''
    }

    $tempScript = Join-Path $env:TEMP ("{0}_{1}.ps1" -f $TempPrefix, (Get-Random))
    $moduleOverride = '$env:ROM_CLEANUP_MODULE_DIR = ''{0}''' -f $moduleDir
    $onboardingSkipOverride = '$env:ROM_CLEANUP_SKIP_ONBOARDING = ''1'''
    $testModeOverride = '$env:ROMCLEANUP_TESTMODE = ''1'''
    $injectedOverrides = @($moduleOverride, $onboardingSkipOverride)
    if (-not $IncludeGui) {
        $injectedOverrides += $testModeOverride
    }
    $overrideBlock = ($injectedOverrides -join "`r`n")

    # Keep the top-level CmdletBinding/param block intact and inject the module
    # override directly after it. If no param block exists, prepend override.
    $headerPattern = '(?s)(\[CmdletBinding\([^\]]*\)\]\s*param\s*\(.*?\n\)\s*)'
    if ([regex]::IsMatch($modifiedContent, $headerPattern)) {
        $modifiedContent = [regex]::Replace($modifiedContent, $headerPattern, ('$1' + $overrideBlock + "`r`n"), 1)
    } else {
        $modifiedContent = $overrideBlock + "`r`n" + $modifiedContent
    }
    # Remove the headless delegation block (if ($Headless) { ... exit ... })
    $modifiedContent = $modifiedContent -replace '(?s)#\s*──\s*Headless delegation[^\r\n]*\r?\n.*?exit \$LASTEXITCODE\s*\}\s*', ''
    # Remove the "GUI Mode" marker comment
    $modifiedContent = $modifiedContent -replace '#\s*──\s*GUI Mode[^\r\n]*', ''

    $portBootstrap = @'
# ── Test Module Loader ──────────────────────────────────────────────
# The GUI-BLOCK strip removed the module loading code from simple_sort.ps1.
# Load core modules explicitly via RomCleanupLoader.ps1 with profile 'core'
# to avoid loading WPF modules (which freeze VS Code's terminal).
$script:ROMCLEANUP_MODULE_PROFILE = 'core'
$env:ROMCLEANUP_MODULE_PROFILE = 'core'
$_testModuleDir = $env:ROM_CLEANUP_MODULE_DIR
if ($_testModuleDir -and (Test-Path -LiteralPath $_testModuleDir -PathType Container)) {
    $script:RomCleanupModuleRoot = (Resolve-Path -LiteralPath $_testModuleDir).Path
    $_loaderPath = Join-Path $script:RomCleanupModuleRoot 'RomCleanupLoader.ps1'
    if (Test-Path -LiteralPath $_loaderPath -PathType Leaf) {
        . $_loaderPath
    }
}
if (Get-Command New-OperationPorts -ErrorAction SilentlyContinue) {
    try {
        $script:TestOperationPorts = New-OperationPorts
        if (Get-Command Set-AppStateValue -ErrorAction SilentlyContinue) {
            [void](Set-AppStateValue -Key 'OperationPorts' -Value $script:TestOperationPorts)
        }
    } catch {
    }
}
Remove-Variable -Name _testModuleDir, _loaderPath -ErrorAction SilentlyContinue
'@
    $modifiedContent += "`r`n" + $portBootstrap + "`r`n"

    $modifiedContent | Out-File -LiteralPath $tempScript -Encoding utf8 -Force

    return [pscustomobject]@{
        ScriptPath = $scriptPath
        TempScript = $tempScript
    }
}

function Clear-SimpleSortTestTempScript {
    [CmdletBinding()]
    param([string]$TempScript)

    if ($TempScript -and (Test-Path -LiteralPath $TempScript)) {
        Remove-Item -LiteralPath $TempScript -Force -ErrorAction SilentlyContinue
    }
}

Set-Alias -Name New-SimpleSortTestScript -Value Get-SimpleSortTestScript -Scope Script
Set-Alias -Name Remove-SimpleSortTestTempScript -Value Clear-SimpleSortTestTempScript -Scope Script
