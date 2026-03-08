#requires -Modules Pester

Describe 'EventBus module' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $modulesRoot = Join-Path $repoRoot 'dev\modules'

        function global:Get-AppStateValue {
            param([string]$Key, $Default = $null)
            if (-not $script:__state) { $script:__state = @{} }
            if ($script:__state.ContainsKey($Key)) { return $script:__state[$Key] }
            return $Default
        }

        function global:Set-AppStateValue {
            param([string]$Key, $Value)
            if (-not $script:__state) { $script:__state = @{} }
            $script:__state[$Key] = $Value
            return $Value
        }

        . (Join-Path $modulesRoot 'EventBus.ps1')
        . (Join-Path $modulesRoot 'FileOps.ps1')
        . (Join-Path $modulesRoot 'Logging.ps1')
    }

    BeforeEach {
        Initialize-RomEventBus
        $script:__state = @{}
    }

    It 'should register and publish to subscriber' {
        $script:received = @()
        [void](Register-RomEventSubscriber -Topic 'unit.topic' -Handler {
            param($payload)
            $script:received += @([string]$payload.Data.value)
        })

        $result = Publish-RomEvent -Topic 'unit.topic' -Data @{ value = 'ok' }

        [int]$result.Delivered | Should -Be 1
        [int]$result.Failed | Should -Be 0
        @($script:received).Count | Should -Be 1
        $script:received[0] | Should -Be 'ok'
    }

    It 'should continue on handler failure when ContinueOnError is set' {
        [void](Register-RomEventSubscriber -Topic 'unit.fault' -Handler { param($payload) throw 'boom' })
        [void](Register-RomEventSubscriber -Topic 'unit.fault' -Handler { param($payload) $script:afterFault = $true })

        $script:afterFault = $false
        $result = Publish-RomEvent -Topic 'unit.fault' -Data @{} -ContinueOnError

        [int]$result.Delivered | Should -Be 1
        [int]$result.Failed | Should -Be 1
        [bool]$script:afterFault | Should -BeTrue
    }
}
