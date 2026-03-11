[CmdletBinding()]
param(
    [ValidateSet('all','unit','integration','e2e')]
    [string]$Stage = 'all',
    [ValidateSet('None','Normal','Detailed','Diagnostic')]
    [string]$PesterOutput = 'None',
    [switch]$FailFast,
    [switch]$Coverage,
    [ValidateRange(1,100)]
    [int]$CoverageTarget = 34,
    [switch]$BenchmarkGate,
    [switch]$BenchmarkGateNightly,
    [ValidateRange(0,3)]
    [int]$FlakyRetries = 0
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Write-Stage {
    param(
        [string]$Message,
        [ValidateSet('Info','Success','Warning','Error')]
        [string]$Type = 'Info'
    )
    Write-Output ("[{0}] {1}" -f $Type, $Message)
}

$repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$testsRoot = Join-Path $repoRoot 'dev\tests'
$reportsRoot = Join-Path $repoRoot 'reports'
if (-not (Test-Path -LiteralPath $reportsRoot)) {
    [void](New-Item -ItemType Directory -Path $reportsRoot -Force)
}

$stages = [ordered]@{
    unit = @(
        (Join-Path $testsRoot 'Startup.Tests.ps1'),
        (Join-Path $testsRoot 'ErrorContracts.Tests.ps1'),
        (Join-Path $testsRoot 'Preflight.Tests.ps1'),
        (Join-Path $testsRoot 'EventBus.Tests.ps1'),
        (Join-Path $testsRoot 'Convert.Strategy.Tests.ps1'),
        (Join-Path $testsRoot 'OperationPlugins.Tests.ps1'),
        (Join-Path $testsRoot 'PluginContractValidation.Tests.ps1'),
        (Join-Path $testsRoot 'Api.OpenApiDrift.Tests.ps1'),
        (Join-Path $testsRoot 'ReportBuilder.Export.Tests.ps1'),
        (Join-Path $testsRoot 'GuiPreflight.InputValidation.Tests.ps1'),
        (Join-Path $testsRoot 'GuiProgress.Readiness.Tests.ps1'),
        (Join-Path $testsRoot 'GameKey.Tests.ps1'),
        (Join-Path $testsRoot 'GameKey.Fuzz.Tests.ps1'),
        (Join-Path $testsRoot 'LruCache.Tests.ps1'),
        (Join-Path $testsRoot 'Settings.BugFinder.Tests.ps1'),
        (Join-Path $testsRoot 'OneGameOneRom.Tests.ps1'),
        (Join-Path $testsRoot 'DatSources.Tests.ps1'),
        (Join-Path $testsRoot 'RuleRegressionPack.Tests.ps1'),
        (Join-Path $testsRoot 'LogExport.Tests.ps1'),
        (Join-Path $testsRoot 'PortInterfaces.Unit.Tests.ps1'),
        (Join-Path $testsRoot 'OperationAdapters.Ports.Tests.ps1'),
        (Join-Path $testsRoot 'UiCommands.Unit.Tests.ps1'),
        (Join-Path $testsRoot 'WpfEventHandlers.Coverage.Tests.ps1'),
        (Join-Path $testsRoot 'Convert.Coverage.Tests.ps1'),
        (Join-Path $testsRoot 'Dedupe.Coverage.Tests.ps1'),
        (Join-Path $testsRoot 'unit\LruCache.Perf.Tests.ps1'),
        (Join-Path $testsRoot 'unit\ChdHeaderCache.Tests.ps1'),
        (Join-Path $testsRoot 'unit\Dat.BomStrip.Tests.ps1'),
        (Join-Path $testsRoot 'unit\WpfXaml.Validation.Tests.ps1'),
        (Join-Path $testsRoot 'OpsBundle.Tests.ps1'),
        (Join-Path $testsRoot 'unit\ConsoleSort.Core.Tests.ps1'),
        (Join-Path $testsRoot 'unit\ConfigMerge.Tests.ps1'),
        (Join-Path $testsRoot 'unit\ConfigProfiles.Tests.ps1'),
        (Join-Path $testsRoot 'unit\Sets.Core.Tests.ps1'),
        (Join-Path $testsRoot 'unit\ConsoleDetection.Fuzz.Tests.ps1'),
        (Join-Path $testsRoot 'unit\EdgeCases.Tests.ps1'),
        (Join-Path $testsRoot 'unit\BackgroundOps.Tests.ps1'),
        (Join-Path $testsRoot 'unit\MemoryGuard.Tests.ps1'),
        (Join-Path $testsRoot 'unit\SafetyToolsService.Tests.ps1'),
        (Join-Path $testsRoot 'unit\RunHelpers.Execution.Tests.ps1'),
        (Join-Path $testsRoot 'unit\Determinism.Tests.ps1'),
        (Join-Path $testsRoot 'unit\NegativeTests.Tests.ps1'),
        (Join-Path $testsRoot 'unit\PipelineStages.Tests.ps1'),
        (Join-Path $testsRoot 'BugRegression.Tests.ps1'),
        (Join-Path $testsRoot 'BugRegression2.Tests.ps1')
    )
    integration = @(
        (Join-Path $testsRoot 'integration\WpfSmoke.Tests.ps1'),
        (Join-Path $testsRoot 'integration\PluginIntegration.Tests.ps1'),
        (Join-Path $testsRoot 'ApiServer.Integration.Tests.ps1'),
        (Join-Path $testsRoot 'Modules.Tests.ps1'),
        (Join-Path $testsRoot 'RegionDedupe.Tests.ps1'),
        (Join-Path $testsRoot 'Security.Tests.ps1'),
        (Join-Path $testsRoot 'FaultInjection.Tests.ps1'),
        (Join-Path $testsRoot 'RomCleanup.Tests.ps1'),
        (Join-Path $testsRoot 'Phase2Smoke.Tests.ps1'),
        (Join-Path $testsRoot 'Phase3Smoke.Tests.ps1'),
        (Join-Path $testsRoot 'RunHelpers.Features.Tests.ps1'),
        (Join-Path $testsRoot 'ParallelConvert.Tests.ps1')
    )
    e2e = @(
        (Join-Path $testsRoot 'E2E.Tests.ps1'),
        (Join-Path $testsRoot 'UiSmoke.Tests.ps1'),
        (Join-Path $testsRoot 'Benchmark.Tests.ps1')
    )
}

$runOrder = if ($Stage -eq 'all') { @('unit','integration','e2e') } else { @($Stage) }

$coveragePaths = @((Join-Path $repoRoot 'dev\modules\*.ps1'))
$allRunFiles = New-Object System.Collections.Generic.List[string]

$benchmarkEnvBackup = $env:ROM_BENCHMARK
$benchmarkNightlyEnvBackup = $env:ROM_BENCHMARK_NIGHTLY
$testModeEnvBackup = $env:ROMCLEANUP_TESTMODE
$skipOnboardingEnvBackup = $env:ROM_CLEANUP_SKIP_ONBOARDING
$env:ROMCLEANUP_TESTMODE = '1'
$env:ROM_CLEANUP_SKIP_ONBOARDING = '1'
if ($BenchmarkGate) {
    $env:ROM_BENCHMARK = '1'
    Write-Stage "Benchmark gate enabled (ROM_BENCHMARK=1)."
}
if ($BenchmarkGateNightly) {
    $env:ROM_BENCHMARK = '1'
    $env:ROM_BENCHMARK_NIGHTLY = '1'
    Write-Stage "Nightly benchmark gate enabled (ROM_BENCHMARK=1, ROM_BENCHMARK_NIGHTLY=1)."
}

$summary = New-Object System.Collections.Generic.List[psobject]
$failedStages = New-Object System.Collections.Generic.List[string]
$failureDetails = New-Object System.Collections.Generic.List[psobject]

foreach ($name in $runOrder) {
    $files = @($stages[$name] | Where-Object { Test-Path -LiteralPath $_ })
    if ($files.Count -eq 0) {
        Write-Stage ("Stage '{0}' skipped (no files found)." -f $name) 'Warning'
        continue
    }

    Write-Stage ("Stage '{0}' running ({1} file(s))..." -f $name, $files.Count)
    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $stageTotal = 0
    $stagePassed = 0
    $stageFailed = 0
    $stageSkipped = 0

    foreach ($file in $files) {
        [void]$allRunFiles.Add($file)
        $fileResult = Invoke-Pester -Path $file -PassThru -Output $PesterOutput

        # Flaky-test retry: re-run failed tests up to $FlakyRetries times
        $retryAttempt = 0
        while ([int]$fileResult.FailedCount -gt 0 -and $retryAttempt -lt $FlakyRetries) {
            $retryAttempt++
            Write-Stage ("Retrying '{0}' (attempt {1}/{2})..." -f (Split-Path -Leaf $file), $retryAttempt, $FlakyRetries) 'Warning'
            $fileResult = Invoke-Pester -Path $file -PassThru -Output $PesterOutput
        }
        if ($retryAttempt -gt 0 -and [int]$fileResult.FailedCount -eq 0) {
            Write-Stage ("Flaky test passed on retry {0}: {1}" -f $retryAttempt, (Split-Path -Leaf $file)) 'Warning'
        }
        $stageTotal += [int]$fileResult.TotalCount
        $stagePassed += [int]$fileResult.PassedCount
        $stageFailed += [int]$fileResult.FailedCount
        $stageSkipped += [int]$fileResult.SkippedCount

        if ([int]$fileResult.FailedCount -gt 0) {
            $failedTests = @()
            if ($fileResult.PSObject.Properties.Name -contains 'Failed') {
                $failedTests = @($fileResult.Failed)
            }

            if ($failedTests.Count -eq 0) {
                [void]$failureDetails.Add([pscustomobject]@{
                    Stage = $name
                    File = [string]$file
                    Test = '<unknown>'
                    Message = ('{0} failed test(s)' -f [int]$fileResult.FailedCount)
                })
            } else {
                foreach ($failedTest in $failedTests) {
                    $testName = if ($failedTest.PSObject.Properties.Name -contains 'ExpandedPath' -and -not [string]::IsNullOrWhiteSpace([string]$failedTest.ExpandedPath)) {
                        [string]$failedTest.ExpandedPath
                    } elseif ($failedTest.PSObject.Properties.Name -contains 'Name') {
                        [string]$failedTest.Name
                    } else {
                        '<unknown>'
                    }

                    $testMessage = $null
                    if ($failedTest.PSObject.Properties.Name -contains 'ErrorRecord' -and $failedTest.ErrorRecord) {
                        if ($failedTest.ErrorRecord.Exception -and $failedTest.ErrorRecord.Exception.Message) {
                            $testMessage = [string]$failedTest.ErrorRecord.Exception.Message
                        } elseif ($failedTest.ErrorRecord.ToString()) {
                            $testMessage = [string]$failedTest.ErrorRecord.ToString()
                        }
                    }
                    if ([string]::IsNullOrWhiteSpace($testMessage) -and $failedTest.PSObject.Properties.Name -contains 'ErrorRecord' -and $failedTest.ErrorRecord) {
                        $testMessage = [string]$failedTest.ErrorRecord
                    }
                    if ([string]::IsNullOrWhiteSpace($testMessage)) {
                        $testMessage = 'Test fehlgeschlagen (keine Detailnachricht verfügbar).'
                    }

                    [void]$failureDetails.Add([pscustomobject]@{
                        Stage = $name
                        File = [string]$file
                        Test = $testName
                        Message = $testMessage
                    })
                }
            }
        }
    }
    $sw.Stop()

    $stageResult = [pscustomobject]@{
        Stage = $name
        Total = [int]$stageTotal
        Passed = [int]$stagePassed
        Failed = [int]$stageFailed
        Skipped = [int]$stageSkipped
        DurationMs = [math]::Round($sw.Elapsed.TotalMilliseconds, 2)
    }
    [void]$summary.Add($stageResult)

    if ($stageFailed -gt 0) {
        [void]$failedStages.Add($name)
        Write-Stage ("Stage '{0}' FAILED: {1} failed" -f $name, $stageFailed) 'Error'
        if ($FailFast) { break }
    } else {
        Write-Stage ("Stage '{0}' passed" -f $name) 'Success'
    }
}

$coverageResult = $null
if ($Coverage -and $allRunFiles.Count -gt 0) {
    Write-Stage ('Coverage pass running ({0} file(s))...' -f $allRunFiles.Count)
    $coverageHelper = Join-Path $PSScriptRoot 'New-CoverageConfiguration.ps1'
    if (Test-Path -LiteralPath $coverageHelper) {
        . $coverageHelper
        $covCfg = New-CoverageConfiguration -TestPaths @($allRunFiles) -CoveragePaths @($coveragePaths) -CoverageTarget $CoverageTarget
    } else {
        $covCfg = New-PesterConfiguration
        $covCfg.Run.Path = @($allRunFiles)
        $covCfg.Run.PassThru = $true
        $covCfg.Output.Verbosity = 'None'
        $covCfg.CodeCoverage.Enabled = $true
        $covCfg.CodeCoverage.Path = @($coveragePaths)
    }
    $coverageResult = Invoke-Pester -Configuration $covCfg
}

$timestamp = Get-Date -Format 'yyyyMMdd-HHmmss'
$jsonPath = Join-Path $reportsRoot ("test-pipeline-{0}.json" -f $timestamp)
$mdPath = Join-Path $reportsRoot ("test-pipeline-{0}.md" -f $timestamp)
$latestJsonPath = Join-Path $reportsRoot 'test-pipeline-latest.json'
$latestMdPath = Join-Path $reportsRoot 'test-pipeline-latest.md'

$payload = [pscustomobject]@{
    Timestamp = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss')
    Stage = $Stage
    FailedStages = @($failedStages)
    Summary = @($summary)
    FailureDetails = @($failureDetails)
}

if ($coverageResult -and $coverageResult.CodeCoverage) {
    $payload | Add-Member -NotePropertyName Coverage -NotePropertyValue ([pscustomobject]@{
        CoveragePercent = [decimal]$coverageResult.CodeCoverage.CoveragePercent
        CommandsAnalyzedCount = [int64]$coverageResult.CodeCoverage.CommandsAnalyzedCount
        CommandsExecutedCount = [int64]$coverageResult.CodeCoverage.CommandsExecutedCount
        CommandsMissedCount = [int64]$coverageResult.CodeCoverage.CommandsMissedCount
        FilesAnalyzedCount = [int64]$coverageResult.CodeCoverage.FilesAnalyzedCount
        TargetPercent = [int]$CoverageTarget
    })
}
$payload | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $jsonPath -Encoding utf8 -Force
Copy-Item -LiteralPath $jsonPath -Destination $latestJsonPath -Force

$lines = New-Object System.Collections.Generic.List[string]
$lines.Add('# Test Pipeline Snapshot') | Out-Null
$lines.Add('') | Out-Null
$lines.Add(("Timestamp: {0}" -f $payload.Timestamp)) | Out-Null
$lines.Add(("Stage: {0}" -f $Stage)) | Out-Null
$lines.Add('') | Out-Null
$lines.Add('| Stage | Total | Passed | Failed | Skipped | Duration (ms) |') | Out-Null
$lines.Add('|---|---:|---:|---:|---:|---:|') | Out-Null
foreach ($row in $summary) {
    $lines.Add(("| {0} | {1} | {2} | {3} | {4} | {5} |" -f $row.Stage, $row.Total, $row.Passed, $row.Failed, $row.Skipped, $row.DurationMs)) | Out-Null
}

if ($failureDetails.Count -gt 0) {
    $lines.Add('') | Out-Null
    $lines.Add('## Fehlgeschlagene Tests') | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add('| Stage | Datei | Test | Meldung |') | Out-Null
    $lines.Add('|---|---|---|---|') | Out-Null
    foreach ($entry in $failureDetails) {
        $msg = [string]$entry.Message
        if ($msg.Length -gt 220) { $msg = $msg.Substring(0, 220) + '…' }
        $msg = $msg.Replace("`r", ' ').Replace("`n", ' ').Replace('|', '\|')
        $test = ([string]$entry.Test).Replace('|', '\|')
        $fileName = Split-Path -Leaf ([string]$entry.File)
        $fileName = $fileName.Replace('|', '\|')
        $lines.Add(("| {0} | {1} | {2} | {3} |" -f $entry.Stage, $fileName, $test, $msg)) | Out-Null
    }
}

if ($payload.PSObject.Properties.Name -contains 'Coverage') {
    $lines.Add('') | Out-Null
    $lines.Add('## Coverage') | Out-Null
    $lines.Add('') | Out-Null
    $lines.Add(("- Coverage: {0}%" -f $payload.Coverage.CoveragePercent)) | Out-Null
    $lines.Add(("- Commands: {0}/{1} (missed: {2})" -f $payload.Coverage.CommandsExecutedCount, $payload.Coverage.CommandsAnalyzedCount, $payload.Coverage.CommandsMissedCount)) | Out-Null
    $lines.Add(("- Files analyzed: {0}" -f $payload.Coverage.FilesAnalyzedCount)) | Out-Null
    $lines.Add(("- Target: {0}%" -f $payload.Coverage.TargetPercent)) | Out-Null
}
$lines.Add('') | Out-Null
$lines.Add(("JSON: {0}" -f $jsonPath)) | Out-Null
$lines.Add(("MD: {0}" -f $mdPath)) | Out-Null
$lines | Out-File -LiteralPath $mdPath -Encoding utf8 -Force
Copy-Item -LiteralPath $mdPath -Destination $latestMdPath -Force

Write-Stage ("Report JSON: {0}" -f $jsonPath)
Write-Stage ("Report MD:   {0}" -f $mdPath)
Write-Stage ("Latest JSON: {0}" -f $latestJsonPath)
Write-Stage ("Latest MD:   {0}" -f $latestMdPath)

if ($BenchmarkGate) {
    $env:ROM_BENCHMARK = $benchmarkEnvBackup
}
if ($BenchmarkGateNightly) {
    $env:ROM_BENCHMARK_NIGHTLY = $benchmarkNightlyEnvBackup
}

$env:ROMCLEANUP_TESTMODE = $testModeEnvBackup
$env:ROM_CLEANUP_SKIP_ONBOARDING = $skipOnboardingEnvBackup

if ($payload.PSObject.Properties.Name -contains 'Coverage') {
    if ([decimal]$payload.Coverage.CoveragePercent -lt [decimal]$CoverageTarget) {
        Write-Stage ("Coverage gate FAILED: {0}% < {1}%" -f $payload.Coverage.CoveragePercent, $CoverageTarget) 'Error'
        exit 1
    }
    Write-Stage ("Coverage gate passed: {0}% >= {1}%" -f $payload.Coverage.CoveragePercent, $CoverageTarget) 'Success'
}

if ($failedStages.Count -gt 0) {
    Write-Stage ("Pipeline failed in stage(s): {0}" -f ($failedStages -join ', ')) 'Error'
    exit 1
}

Write-Stage 'Pipeline completed successfully.' 'Success'
exit 0
