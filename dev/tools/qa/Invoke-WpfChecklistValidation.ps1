[CmdletBinding()]
param()

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Add-Result {
    param(
        [System.Collections.Generic.List[object]]$List,
        [string]$Id,
        [bool]$Passed,
        [string]$Detail
    )
    $List.Add([pscustomobject]@{
        Id = $Id
        Passed = $Passed
        Detail = $Detail
    }) | Out-Null
}

$repoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
$moduleRoot = Join-Path $repoRoot 'dev\modules'

. (Join-Path $moduleRoot 'Settings.ps1')
. (Join-Path $moduleRoot 'WpfShims.ps1')
. (Join-Path $moduleRoot 'WpfXaml.ps1')
. (Join-Path $moduleRoot 'WpfHost.ps1')
. (Join-Path $moduleRoot 'WpfEventHandlers.ps1')
. (Join-Path $moduleRoot 'SimpleSort.WpfMain.ps1')

$results = [System.Collections.Generic.List[object]]::new()

# WPF-02: tabs vorhanden + count
try {
    $window = Start-WpfGui -Headless
    $ctx = Get-WpfNamedElements -Window $window
    $tabCount = 0
    if ($ctx.ContainsKey('tabMain') -and $ctx['tabMain']) {
        $tabCount = [int]$ctx['tabMain'].Items.Count
    }
    Add-Result -List $results -Id 'WPF-02' -Passed ($tabCount -ge 5) -Detail ("tabMain items: {0}" -f $tabCount)
} catch {
    Add-Result -List $results -Id 'WPF-02' -Passed $false -Detail $_.Exception.Message
}

# WPF-03: DryRun, wenn confirm move nicht gesetzt
try {
    if ($ctx -and $ctx.ContainsKey('chkConfirmMove') -and $ctx.ContainsKey('chkReportDryRun')) {
        $ctx['chkConfirmMove'].IsChecked = $false
        $ctx['chkReportDryRun'].IsChecked = $false
        $p = Get-WpfRunParameters -Ctx $ctx
        Add-Result -List $results -Id 'WPF-03' -Passed ($p.Mode -eq 'DryRun') -Detail ("mode={0}" -f $p.Mode)
    } else {
        Add-Result -List $results -Id 'WPF-03' -Passed $false -Detail 'required controls missing'
    }
} catch {
    Add-Result -List $results -Id 'WPF-03' -Passed $false -Detail $_.Exception.Message
}

# Static checks from source
$handlerPath = Join-Path $moduleRoot 'WpfEventHandlers.ps1'
$handlerText = Get-Content -Raw -LiteralPath $handlerPath

# WPF-04: DispatcherTimer + BeginInvoke vorhanden
$hasTimer = $handlerText -match 'DispatcherTimer'
$hasBeginInvoke = $handlerText -match 'BeginInvoke'
Add-Result -List $results -Id 'WPF-04' -Passed ($hasTimer -and $hasBeginInvoke) -Detail ("DispatcherTimer={0}; BeginInvoke={1}" -f $hasTimer, $hasBeginInvoke)

# WPF-05: Cancel token wiring
$hasCancelToken = ($handlerText -match 'WpfCancelToken') -and $handlerText -match 'btnCancelGlobal'
$hasCancelState = $handlerText.Contains("Set-AppStateValue -Key 'CancelRequested' -Value `$true")
Add-Result -List $results -Id 'WPF-05' -Passed ($hasCancelToken -and $hasCancelState) -Detail ("token={0}; appState={1}" -f $hasCancelToken, $hasCancelState)

# WPF-06: Rollback button wiring
$hasRollbackButton = $handlerText.Contains("btnRollback'].add_Click(")
$hasRollbackWizard = $handlerText -match 'Show-GuiRollbackWizard'
Add-Result -List $results -Id 'WPF-06' -Passed ($hasRollbackButton -and $hasRollbackWizard) -Detail ("button={0}; wizard={1}" -f $hasRollbackButton, $hasRollbackWizard)

# WPF-07: browse dialogs
$hasFolderDialog = ($handlerText -match 'BrowseForFolder') -or ($handlerText -match 'New-Object\s+-ComObject\s+Shell\.Application')
$hasOpenFileDialog = $handlerText -match 'Microsoft\.Win32\.OpenFileDialog'
Add-Result -List $results -Id 'WPF-07' -Passed ($hasFolderDialog -and $hasOpenFileDialog) -Detail ("FolderDialog(COM)={0}; OpenFileDialog(WPF)={1}" -f $hasFolderDialog, $hasOpenFileDialog)

# WPF-08: auto-find tools
$hasAutoFindButton = $handlerText.Contains("btnAutoFindTools'].add_Click(")
$hasFindConversion = $handlerText -match 'Find-ConversionTool'
Add-Result -List $results -Id 'WPF-08' -Passed ($hasAutoFindButton -and $hasFindConversion) -Detail ("button={0}; find={1}" -f $hasAutoFindButton, $hasFindConversion)

$passCount = @($results | Where-Object { $_.Passed }).Count
$failCount = @($results | Where-Object { -not $_.Passed }).Count

$out = [pscustomobject]@{
    Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    Total = $results.Count
    Passed = $passCount
    Failed = $failCount
    Results = @($results)
}

$outPath = Join-Path $repoRoot 'reports\wpf-checklist-validation-latest.json'
$out | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath $outPath -Encoding UTF8

Write-Output ("WPF checklist validation: passed={0}, failed={1}" -f $passCount, $failCount)
$out.Results | ForEach-Object {
    Write-Output ("[{0}] {1} - {2}" -f ($(if ($_.Passed) { 'OK' } else { 'FAIL' }), $_.Id, $_.Detail))
}
Write-Output ("Report: {0}" -f $outPath)

if ($failCount -gt 0) { exit 1 }
exit 0
