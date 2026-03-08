#requires -Modules Pester

Describe 'Silent Catch Gate' {
    It 'Invoke-SilentCatchGate.ps1 exists and is valid PowerShell' {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
        $gatePath = Join-Path $repoRoot 'dev\tools\Invoke-SilentCatchGate.ps1'
        Test-Path -LiteralPath $gatePath | Should -BeTrue

        $errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($gatePath, [ref]$null, [ref]$errors)
        $errors.Count | Should -Be 0
    }

    It 'gate runs in WarnOnly mode and writes report' {
        $repoRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..\..')
        $gatePath = Join-Path $repoRoot 'dev\tools\Invoke-SilentCatchGate.ps1'
        $modulesDir = Join-Path $repoRoot 'dev\modules'
        $tmpReports = Join-Path $env:TEMP ("silent_catch_gate_" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tmpReports -Force | Out-Null

        try {
            $output = & pwsh -NoProfile -File $gatePath -ModulesDir $modulesDir -ReportsDir $tmpReports -WarnOnly 2>&1
            $LASTEXITCODE | Should -BeIn @(0, $null)

            $reportPath = Join-Path $tmpReports 'silent-catch-gate-latest.json'
            Test-Path -LiteralPath $reportPath | Should -BeTrue

            $report = Get-Content -LiteralPath $reportPath -Raw | ConvertFrom-Json
            $report.FilesScanned | Should -BeGreaterThan 0
            # TEST-ALIBI-02: Pruefe echte Violation-Compliance statt immer-wahre Assertion
            # WPF-EventHandler duerfen silent catches haben (UI-Thread-Absturz verhindern),
            # aber der Rest der Codebase soll compliant sein.
            # Erlaube maximal die bekannten WPF-Ausnahmen (TD-002).
            [int]$report.ViolationCount | Should -BeLessOrEqual 15 -Because 'nur WPF-EventHandler-Ausnahmen sind erlaubt'
        } finally {
            Remove-Item -LiteralPath $tmpReports -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
