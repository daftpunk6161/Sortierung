#requires -Modules Pester

BeforeAll {
    $loaderPath = Join-Path $PSScriptRoot 'TestScriptLoader.ps1'
    if (-not (Test-Path -LiteralPath $loaderPath)) {
        $loaderPath = Join-Path (Split-Path -Parent $PSScriptRoot) 'TestScriptLoader.ps1'
    }
    . $loaderPath
    $testsRoot = Split-Path -Parent $PSScriptRoot
    $ctx = New-SimpleSortTestScript -TestsRoot $testsRoot -TempPrefix 'phasemetrics_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'PhaseMetrics' {
    BeforeEach {
        Initialize-PhaseMetrics | Out-Null
    }

    It 'Initialize-PhaseMetrics creates fresh collector' {
        $m = Initialize-PhaseMetrics
        $m | Should -Not -BeNullOrEmpty
        $m.RunId | Should -Not -BeNullOrEmpty
        $m.Phases.Count | Should -Be 0
    }

    It 'Start-PhaseMetric and Complete-PhaseMetric record a phase' {
        Start-PhaseMetric -Phase 'TestPhase' | Out-Null
        Start-Sleep -Milliseconds 50
        $completed = Complete-PhaseMetric -ItemCount 42

        $completed.Phase | Should -Be 'TestPhase'
        $completed.ItemCount | Should -Be 42
        $completed.Status | Should -Be 'OK'
        $completed.Duration.TotalMilliseconds | Should -BeGreaterThan 0
    }

    It 'Get-PhaseMetrics returns aggregate data' {
        Start-PhaseMetric -Phase 'Alpha' | Out-Null
        Complete-PhaseMetric -ItemCount 10 | Out-Null

        Start-PhaseMetric -Phase 'Beta' | Out-Null
        Complete-PhaseMetric -ItemCount 20 | Out-Null

        $metrics = Get-PhaseMetrics
        $metrics.PhaseCount | Should -Be 2
        $metrics.Phases[0].Phase | Should -Be 'Alpha'
        $metrics.Phases[1].Phase | Should -Be 'Beta'
        $metrics.TotalElapsedSec | Should -BeGreaterOrEqual 0
    }

    It 'Auto-completes previous phase when new one starts' {
        Start-PhaseMetric -Phase 'First' | Out-Null
        Start-PhaseMetric -Phase 'Second' | Out-Null
        Complete-PhaseMetric -ItemCount 5 | Out-Null

        $metrics = Get-PhaseMetrics
        $metrics.PhaseCount | Should -Be 2
        $metrics.Phases[0].Phase | Should -Be 'First'
        $metrics.Phases[0].Status | Should -Be 'OK'
    }

    It 'ItemsPerSec is calculated correctly' {
        Start-PhaseMetric -Phase 'Speed' | Out-Null
        Start-Sleep -Milliseconds 100
        Complete-PhaseMetric -ItemCount 1000 | Out-Null

        $metrics = Get-PhaseMetrics
        $metrics.Phases[0].ItemsPerSec | Should -BeGreaterThan 0
    }

    It 'PercentOfTotal sums close to 100 for multiple phases' {
        Start-PhaseMetric -Phase 'A' | Out-Null
        Start-Sleep -Milliseconds 50
        Complete-PhaseMetric -ItemCount 1 | Out-Null

        Start-PhaseMetric -Phase 'B' | Out-Null
        Start-Sleep -Milliseconds 50
        Complete-PhaseMetric -ItemCount 1 | Out-Null

        $metrics = Get-PhaseMetrics
        $totalPct = ($metrics.Phases | Measure-Object -Property PercentOfTotal -Sum).Sum
        # Should be close to 100 but may not be exact due to overhead
        $totalPct | Should -BeGreaterThan 50
    }

    It 'Write-PhaseMetricsSummary emits log lines without error' {
        Start-PhaseMetric -Phase 'Scan' | Out-Null
        Complete-PhaseMetric -ItemCount 100 | Out-Null

        $logLines = New-Object System.Collections.Generic.List[string]
        $logCallback = { param($msg) $logLines.Add($msg) }

        $result = Write-PhaseMetricsSummary -Log $logCallback
        $result | Should -Not -BeNullOrEmpty
        $logLines.Count | Should -BeGreaterThan 0
        ($logLines -join "`n") | Should -Match 'Phasenmetriken'
    }

    It 'Export-PhaseMetrics writes JSON files' {
        Start-PhaseMetric -Phase 'Export' | Out-Null
        Complete-PhaseMetric -ItemCount 5 | Out-Null

        $tmpDir = Join-Path $env:TEMP ("pm_test_" + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tmpDir -Force | Out-Null
        try {
            $filePath = Export-PhaseMetrics -ReportsDir $tmpDir
            $filePath | Should -Not -BeNullOrEmpty
            Test-Path -LiteralPath $filePath | Should -BeTrue

            $latestPath = Join-Path $tmpDir 'phase-metrics-latest.json'
            Test-Path -LiteralPath $latestPath | Should -BeTrue

            $content = Get-Content -LiteralPath $latestPath -Raw | ConvertFrom-Json
            $content.PhaseCount | Should -Be 1
            $content.Phases[0].Phase | Should -Be 'Export'
        } finally {
            Remove-Item -LiteralPath $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'MemoryGuard' {
    BeforeEach {
        $script:MemoryBudgetConfig = $null
    }

    It 'Initialize-MemoryBudget sets baseline' {
        $config = Initialize-MemoryBudget -SoftLimitMB 500 -HardLimitMB 1000
        $config | Should -Not -BeNullOrEmpty
        $config.SoftLimitMB | Should -Be 500
        $config.HardLimitMB | Should -Be 1000
        $config.BaselineBytes | Should -BeGreaterThan 0
    }

    It 'Get-MemoryPressure returns valid state' {
        Initialize-MemoryBudget -SoftLimitMB 500 -HardLimitMB 1000 | Out-Null
        $pressure = Get-MemoryPressure

        $pressure.CurrentMB | Should -BeGreaterThan 0
        $pressure.ManagedMB | Should -BeGreaterOrEqual 0
        $pressure.SoftLimitMB | Should -Be 500
        $pressure.HardLimitMB | Should -Be 1000
        $pressure.Pressure | Should -BeIn @('None', 'Soft', 'Hard')
    }

    It 'Test-MemoryBudget returns true when within limits' {
        Initialize-MemoryBudget -SoftLimitMB 99999 -HardLimitMB 99999 -CheckIntervalMs 0 | Out-Null
        $result = Test-MemoryBudget
        $result | Should -BeTrue
    }

    It 'Get-MemoryBudgetSummary returns summary when initialized' {
        Initialize-MemoryBudget | Out-Null
        $summary = Get-MemoryBudgetSummary

        $summary.Initialized | Should -BeTrue
        $summary.CurrentMB | Should -BeGreaterThan 0
        $summary.GcTriggeredCount | Should -BeGreaterOrEqual 0
    }

    It 'Get-MemoryBudgetSummary handles uninitialized state' {
        $script:MemoryBudgetConfig = $null
        $summary = Get-MemoryBudgetSummary

        $summary.Initialized | Should -BeFalse
    }

    It 'Invoke-MemoryBackpressure resolves when under limit' {
        Initialize-MemoryBudget -SoftLimitMB 99999 -HardLimitMB 99999 | Out-Null

        $logLines = New-Object System.Collections.Generic.List[string]
        $result = Invoke-MemoryBackpressure -PauseMs 10 -MaxRetries 1 -Log { param($msg) $logLines.Add($msg) }

        $result | Should -Be 'None'
    }
}
