#requires -Modules Pester

BeforeAll {
    $loaderPath = Join-Path $PSScriptRoot 'TestScriptLoader.ps1'
    if (-not (Test-Path -LiteralPath $loaderPath)) {
        $loaderPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'TestScriptLoader.ps1'
    }
    . $loaderPath
    $testsRoot = Split-Path -Parent $PSScriptRoot
    $ctx = New-SimpleSortTestScript -TestsRoot $testsRoot -TempPrefix 'governance_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript

    $script:RepoRoot = $testsRoot
    while ($script:RepoRoot -and -not (Test-Path -LiteralPath (Join-Path $script:RepoRoot 'simple_sort.ps1'))) {
        $script:RepoRoot = Split-Path -Parent $script:RepoRoot
    }
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'Governance Gate' {
    It 'Invoke-GovernanceGate.ps1 exists and is valid PowerShell' {
        $gatePath = Join-Path $script:RepoRoot 'dev' 'tools' 'Invoke-GovernanceGate.ps1'
        Test-Path -LiteralPath $gatePath | Should -BeTrue

        $errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($gatePath, [ref]$null, [ref]$errors)
        $errors.Count | Should -Be 0
    }

    It 'Governance gate runs in WarnOnly mode without error' {
        $gatePath = Join-Path $script:RepoRoot 'dev' 'tools' 'Invoke-GovernanceGate.ps1'
        $modulesDir = Join-Path $script:RepoRoot 'dev' 'modules'
        $tmpReports = Join-Path $env:TEMP ("gov_test_" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tmpReports -Force | Out-Null

        try {
            $output = & pwsh -NoProfile -File $gatePath -ModulesDir $modulesDir -ReportsDir $tmpReports -WarnOnly 2>&1
            # Should not throw (exit 0 in WarnOnly)
            $LASTEXITCODE | Should -BeIn @(0, $null)

            # Report should be created
            $reportPath = Join-Path $tmpReports 'governance-gate-latest.json'
            Test-Path -LiteralPath $reportPath | Should -BeTrue

            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            $report.FilesScanned | Should -BeGreaterThan 0
            $report.Limits.MaxFileLinesHard | Should -Be 4000
        } finally {
            Remove-Item -LiteralPath $tmpReports -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'PSScriptAnalyzerSettings.psd1 exists and includes required rules' {
        $settingsPath = Join-Path $script:RepoRoot 'PSScriptAnalyzerSettings.psd1'
        Test-Path -LiteralPath $settingsPath | Should -BeTrue

        $settings = Import-PowerShellDataFile -LiteralPath $settingsPath
        $settings.IncludeRules | Should -Contain 'PSUseDeclaredVarsMoreThanAssignments'
        $settings.IncludeRules | Should -Contain 'PSAvoidUsingInvokeExpression'
    }
}
