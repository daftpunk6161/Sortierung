#requires -Modules Pester

Describe 'Operation plugin system' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $modulesRoot = Join-Path $repoRoot 'dev\modules'
        . (Join-Path $modulesRoot 'FileOps.ps1')
        . (Join-Path $modulesRoot 'Settings.ps1')
        . (Join-Path $modulesRoot 'LruCache.ps1')
        . (Join-Path $modulesRoot 'Tools.ps1')
        . (Join-Path $modulesRoot 'RunHelpers.ps1')
        . (Join-Path $modulesRoot 'ZipSort.ps1')
        . (Join-Path $modulesRoot 'FolderDedupe.ps1')
    }

    It 'Invoke-OperationPlugins should execute operation plugins and return results' {
        $results = @(Invoke-OperationPlugins -Phase 'post-run' -Context @{ Mode = 'DryRun'; ReportRows = @() } -Log { param($m) })
        @($results).Count | Should -BeGreaterThan 0
        @($results | Where-Object { $_.Plugin -eq 'example.operation-plugin.ps1' -and $_.Status -eq 'OK' }).Count | Should -Be 1

        $first = @($results | Select-Object -First 1)[0]
        $first.PSObject.Properties.Name | Should -Contain 'PluginId'
        $first.PSObject.Properties.Name | Should -Contain 'Version'
    }

    It 'Invoke-ZipSort should prefer operation plugin result when plugin handles phase' {
        Mock -CommandName Invoke-OperationPlugins -MockWith {
            @(
                [pscustomobject]@{
                    Plugin = 'zipsort.operation-plugin.ps1'
                    Status = 'OK'
                    Result = [pscustomobject]@{
                        PluginHandled = $true
                        Total = 5
                        Moved = 2
                        Skipped = 3
                        Errors = 0
                    }
                }
            )
        }

        $result = Invoke-ZipSort -Roots @('C:\dummy') -Strategy 'PS1PS2' -Log { param($m) }
        $result | Should -Not -BeNullOrEmpty
        [bool]$result.PluginHandled | Should -BeTrue
        [int]$result.Total | Should -Be 5
    }

    It 'Invoke-Ps3Dedupe should prefer operation plugin result when plugin handles phase' {
        Mock -CommandName Invoke-OperationPlugins -MockWith {
            @(
                [pscustomobject]@{
                    Plugin = 'ps3-dedupe.operation-plugin.ps1'
                    Status = 'OK'
                    Result = [pscustomobject]@{
                        PluginHandled = $true
                        Total = 4
                        Dupes = 1
                        Moved = 1
                        Skipped = 0
                    }
                }
            )
        }

        $result = Invoke-Ps3Dedupe -Roots @('C:\dummy') -DupeRoot 'C:\dupes' -Log { param($m) }
        $result | Should -Not -BeNullOrEmpty
        [bool]$result.PluginHandled | Should -BeTrue
        [int]$result.Dupes | Should -Be 1
    }

    It 'Invoke-OperationPlugins should skip unsigned plugins in signed-only mode' {
        $results = @(Invoke-OperationPlugins -Phase 'post-run' -TrustMode 'signed-only' -Context @{ Mode = 'DryRun'; ReportRows = @() } -Log { param($m) })
        @($results | Where-Object { $_.Status -eq 'SKIPPED' }).Count | Should -BeGreaterThan 0
    }

    It 'Invoke-OperationPlugins should skip untrusted plugins in trusted-only mode' {
        $results = @(Invoke-OperationPlugins -Phase 'post-run' -TrustMode 'trusted-only' -Context @{ Mode = 'DryRun'; ReportRows = @() } -Log { param($m) })
        @($results | Where-Object { $_.Status -eq 'SKIPPED' -and [string]$_.Error -match 'trusted-only' }).Count | Should -BeGreaterThan 0
    }

    It 'Invoke-OperationPlugins should isolate untrusted plugins in compat mode' {
        $results = @(Invoke-OperationPlugins -Phase 'post-run' -TrustMode 'compat' -PluginTimeoutMs 10000 -Context @{ Mode = 'DryRun'; ReportRows = @() } -Log { param($m) })
        $isolated = @($results | Where-Object { $_.Plugin -eq 'untrusted-demo.operation-plugin.ps1' -and [string]$_.ExecutionMode -eq 'isolated' })
        @($isolated).Count | Should -Be 1
        [string]$isolated[0].Status | Should -Be 'OK'
    }
}
