#requires -Modules Pester

Describe 'Plugin Integration - End-to-End Hooking' {
    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }

        . (Join-Path $root 'dev\modules\RunHelpers.ps1')
    }

    It 'führt post-run Operation-Plugins mit Kontext aus' {
        $context = @{
            Mode = 'DryRun'
            ReportRows = @(
                [pscustomobject]@{ Action='SKIP_DRYRUN'; Category='GAME'; GameKey='sample' }
            )
        }

        $results = @(Invoke-OperationPlugins -Phase 'post-run' -Context $context -Log { param($m) })

        @($results).Count | Should -BeGreaterThan 0
        @($results | Where-Object { $_.Status -eq 'OK' }).Count | Should -BeGreaterThan 0
    }
}
