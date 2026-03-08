#requires -Modules Pester

Describe 'Port Contract Validation' {
    BeforeAll {
        $script:RepoRoot = Split-Path -Parent (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
        $script:ModulesRoot = Join-Path $script:RepoRoot 'dev\modules'

        # Load minimal required modules for Port creation
        . (Join-Path $script:ModulesRoot 'Settings.ps1')
        . (Join-Path $script:ModulesRoot 'LruCache.ps1')
        . (Join-Path $script:ModulesRoot 'ErrorContracts.ps1')
        . (Join-Path $script:ModulesRoot 'DataContracts.ps1')
        . (Join-Path $script:ModulesRoot 'EventBus.ps1')
        . (Join-Path $script:ModulesRoot 'Logging.ps1')
        . (Join-Path $script:ModulesRoot 'CatchGuard.ps1')
        . (Join-Path $script:ModulesRoot 'FileOps.ps1')
        . (Join-Path $script:ModulesRoot 'Tools.ps1')
        . (Join-Path $script:ModulesRoot 'Dat.ps1')
        . (Join-Path $script:ModulesRoot 'RunHelpers.ps1')
        . (Join-Path $script:ModulesRoot 'RunHelpers.Audit.ps1')
        . (Join-Path $script:ModulesRoot 'PortInterfaces.ps1')
        . (Join-Path $script:ModulesRoot 'ApplicationServices.ps1')
    }

    Context 'FileSystem Port contract matches target functions' {
        It 'New-FileSystemPort creates all expected port members' {
            $port = New-FileSystemPort
            $port.Name | Should -Be 'FileSystem'
            $port.PSObject.Properties.Name | Should -Contain 'TestPath'
            $port.PSObject.Properties.Name | Should -Contain 'EnsureDirectory'
            $port.PSObject.Properties.Name | Should -Contain 'GetFilesSafe'
            $port.PSObject.Properties.Name | Should -Contain 'MoveItemSafely'
            $port.PSObject.Properties.Name | Should -Contain 'ResolveChildPathWithinRoot'
        }

        It 'ResolveChildPathWithinRoot port delegates correctly to Resolve-ChildPathWithinRoot' {
            # Verify the target function exists with expected parameters
            $cmd = Get-Command Resolve-ChildPathWithinRoot -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $params = $cmd.Parameters
            $params.ContainsKey('BaseDir') | Should -BeTrue -Because 'target function must accept BaseDir'
            $params.ContainsKey('ChildPath') | Should -BeTrue -Because 'target function must accept ChildPath'
            $params.ContainsKey('Root') | Should -BeTrue -Because 'target function must accept Root'
        }

        It 'GetFilesSafe port delegates correctly to Get-FilesSafe' {
            $cmd = Get-Command Get-FilesSafe -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $params = $cmd.Parameters
            $params.ContainsKey('Root') | Should -BeTrue
        }

        It 'MoveItemSafely port delegates correctly to Move-ItemSafely' {
            $cmd = Get-Command Move-ItemSafely -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $params = $cmd.Parameters
            $params.ContainsKey('Source') | Should -BeTrue
            $params.ContainsKey('Dest') | Should -BeTrue
        }
    }

    Context 'ToolRunner Port contract matches target functions' {
        It 'New-ToolRunnerPort creates all expected port members' {
            $port = New-ToolRunnerPort
            $port.Name | Should -Be 'ToolRunner'
            $port.PSObject.Properties.Name | Should -Contain 'FindTool'
            $port.PSObject.Properties.Name | Should -Contain 'InvokeProcess'
            $port.PSObject.Properties.Name | Should -Contain 'Invoke7z'
        }

        It 'InvokeProcess port delegates correctly to Invoke-ExternalToolProcess' {
            $cmd = Get-Command Invoke-ExternalToolProcess -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $params = $cmd.Parameters
            $params.ContainsKey('ToolPath') | Should -BeTrue -Because 'target function must accept ToolPath'
            $params.ContainsKey('ToolArgs') | Should -BeTrue -Because 'target function must accept ToolArgs'
        }

        It 'FindTool port delegates correctly to Find-ConversionTool' {
            $cmd = Get-Command Find-ConversionTool -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $params = $cmd.Parameters
            $params.ContainsKey('ToolName') | Should -BeTrue
        }
    }

    Context 'DatRepository Port contract matches target functions' {
        It 'New-DatRepositoryPort creates all expected port members' {
            $port = New-DatRepositoryPort
            $port.Name | Should -Be 'DatRepository'
            $port.PSObject.Properties.Name | Should -Contain 'GetDatIndex'
            $port.PSObject.Properties.Name | Should -Contain 'GetDatGameKey'
            $port.PSObject.Properties.Name | Should -Contain 'GetDatParentCloneIndex'
            $port.PSObject.Properties.Name | Should -Contain 'ResolveParentName'
        }

        It 'GetDatIndex port delegates correctly to Get-DatIndex' {
            $cmd = Get-Command Get-DatIndex -ErrorAction SilentlyContinue
            $cmd | Should -Not -BeNullOrEmpty
            $params = $cmd.Parameters
            $params.ContainsKey('DatRoot') | Should -BeTrue
            $params.ContainsKey('ConsoleMap') | Should -BeTrue
            $params.ContainsKey('HashType') | Should -BeTrue
        }
    }

    Context 'AuditStore Port contract matches target functions' {
        It 'New-AuditStorePort creates all expected port members' {
            $port = New-AuditStorePort
            $port.Name | Should -Be 'AuditStore'
            $port.PSObject.Properties.Name | Should -Contain 'WriteMetadataSidecar'
            $port.PSObject.Properties.Name | Should -Contain 'TestMetadataSidecar'
            $port.PSObject.Properties.Name | Should -Contain 'Rollback'
        }
    }

    Context 'AppState Port contract matches target functions' {
        It 'New-AppStatePort creates all expected port members' {
            $port = New-AppStatePort
            $port.Name | Should -Be 'AppState'
            $port.PSObject.Properties.Name | Should -Contain 'Get'
            $port.PSObject.Properties.Name | Should -Contain 'Set'
            $port.PSObject.Properties.Name | Should -Contain 'Watch'
            $port.PSObject.Properties.Name | Should -Contain 'Undo'
            $port.PSObject.Properties.Name | Should -Contain 'Redo'
        }
    }

    Context 'New-OperationPorts composite factory' {
        It 'creates all required port keys' {
            $ports = New-OperationPorts
            $ports | Should -Not -BeNullOrEmpty
            $ports.ContainsKey('FileSystem') | Should -BeTrue
            $ports.ContainsKey('ToolRunner') | Should -BeTrue
            $ports.ContainsKey('DatRepository') | Should -BeTrue
            $ports.ContainsKey('AuditStore') | Should -BeTrue
            $ports.ContainsKey('AppState') | Should -BeTrue
            $ports.ContainsKey('RegionDedupe') | Should -BeTrue
        }
    }
}
