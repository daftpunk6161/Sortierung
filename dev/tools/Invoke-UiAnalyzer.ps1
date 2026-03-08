[CmdletBinding()]
param(
    [string[]]$Paths = @(
        'dev/modules/GuiHandlers.ps1',
        'dev/modules/GuiHandlersOps.ps1',
        'dev/modules/GuiHandlersRules.ps1',
        'dev/modules/GuiHandlersAdvanced.ps1',
        'dev/modules/GuiHandlersStart.ps1',
        'dev/modules/UiCommands.ps1',
        'dev/modules/UiContext.ps1'
    )
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$settingsPath = Join-Path $repoRoot 'PSScriptAnalyzerSettings.psd1'
if (-not (Test-Path -LiteralPath $settingsPath -PathType Leaf)) {
    throw "PSScriptAnalyzerSettings.psd1 nicht gefunden im Root: $settingsPath"
}

if (-not (Get-Command Invoke-ScriptAnalyzer -ErrorAction SilentlyContinue)) {
    throw 'Invoke-ScriptAnalyzer ist nicht verfügbar. Bitte Modul PSScriptAnalyzer installieren.'
}

$findings = New-Object System.Collections.Generic.List[object]
foreach ($entry in @($Paths)) {
    if ([string]::IsNullOrWhiteSpace([string]$entry)) { continue }
    $target = Join-Path $repoRoot $entry
    if (-not (Test-Path -LiteralPath $target -PathType Leaf)) { continue }
    foreach ($f in @(Invoke-ScriptAnalyzer -Path $target -Settings $settingsPath -Recurse:$false)) {
        [void]$findings.Add($f)
    }
}

if ($findings.Count -eq 0) {
    Write-Output '[Info] ScriptAnalyzer: keine Findings in UI-Modulen.'
    exit 0
}

$findings |
    Select-Object RuleName, Severity, ScriptName, Line, Message |
    Format-Table -AutoSize | Out-String | Write-Output

Write-Output ("[Error] ScriptAnalyzer: {0} Finding(s)." -f $findings.Count)
exit 1
