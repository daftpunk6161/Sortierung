#requires -Modules Pester

Describe 'Port Interfaces Unit' {
    BeforeAll {
        $script:RepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $script:ModulesRoot = Join-Path $script:RepoRoot 'dev\modules'

        . (Join-Path $script:ModulesRoot 'FileOps.ps1')
        . (Join-Path $script:ModulesRoot 'Settings.ps1')
        . (Join-Path $script:ModulesRoot 'RunHelpers.ps1')
        . (Join-Path $script:ModulesRoot 'PortInterfaces.ps1')
        . (Join-Path $script:ModulesRoot 'ApplicationServices.ps1')
    }

    It 'creates default operation ports with required keys' {
        $ports = New-OperationPorts

        $ports.ContainsKey('FileSystem') | Should -BeTrue
        $ports.ContainsKey('ToolRunner') | Should -BeTrue
        $ports.ContainsKey('DatRepository') | Should -BeTrue
        $ports.ContainsKey('AuditStore') | Should -BeTrue
    }

    It 'uses injected AuditStore rollback port in rollback service' {
        $called = $false
        $ports = @{
            AuditStore = [pscustomobject]@{
                Rollback = {
                    param(
                        [string]$AuditCsvPath,
                        [string[]]$AllowedRestoreRoots,
                        [string[]]$AllowedCurrentRoots,
                        [switch]$DryRun,
                        [scriptblock]$Log
                    )
                    $script:called = $true
                    return [pscustomobject]@{
                        RolledBack = 1
                        Failed = 0
                        SkippedUnsafe = 0
                        SkippedMissingDest = 0
                        SkippedCollision = 0
                        DryRun = [bool]$DryRun
                        AuditCsvPath = $AuditCsvPath
                    }
                }
            }
        }

        $result = Invoke-RunRollbackService -Parameters @{
            AuditCsvPath = 'C:\temp\audit.csv'
            AllowedRestoreRoots = @('C:\roms')
            AllowedCurrentRoots = @('C:\trash')
            DryRun = $true
            Log = { param($m) }
        } -Ports $ports

        $script:called | Should -BeTrue
        [int]$result.RolledBack | Should -Be 1
        [bool]$result.DryRun | Should -BeTrue
    }
}
