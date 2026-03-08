Describe 'OperationAdapters port pass-through' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $scriptPath = Join-Path $repoRoot 'dev\modules\OperationAdapters.ps1'
        . $scriptPath
        
            if (-not (Get-Command Invoke-RunSortService -ErrorAction SilentlyContinue)) {
                function Invoke-RunSortService { param() return $null }
            }
            if (-not (Get-Command Invoke-RunDedupeService -ErrorAction SilentlyContinue)) {
                function Invoke-RunDedupeService { param() return $null }
            }
            if (-not (Get-Command Invoke-RunConversionService -ErrorAction SilentlyContinue)) {
                function Invoke-RunConversionService { param() return $null }
            }
    }

    It 'forwards Ports to service facades' {
        $script:capturedSortPorts = $null
        $script:capturedDedupePorts = $null
        $script:capturedConvertPorts = $null

        Mock -CommandName Invoke-RunSortService -MockWith {
            param($Enabled, $Mode, $Roots, $Extensions, $UseDat, $DatRoot, $DatHashType, $DatMap, $ToolOverrides, $Log, $Ports)
            $script:capturedSortPorts = $Ports
            return [pscustomobject]@{ Value = @{ unknown = 1 } }
        }

        Mock -CommandName Invoke-RunDedupeService -MockWith {
            param($Parameters, $Ports)
            $script:capturedDedupePorts = $Ports
            return [pscustomobject]@{ CsvPath = 'report.csv'; HtmlPath = 'report.html' }
        }

        Mock -CommandName Invoke-RunConversionService -MockWith {
            param($Operation, $Parameters, $Ports)
            $script:capturedConvertPorts = $Ports
            return $null
        }

        $ports = @{
            FileSystem = [pscustomobject]@{ Name = 'fs' }
            ToolRunner = [pscustomobject]@{ Name = 'tools' }
            DatRepository = [pscustomobject]@{ Name = 'dat' }
            AuditStore = [pscustomobject]@{ Name = 'audit' }
        }

        $result = Invoke-CliRunAdapter -SortConsole $true -Mode 'Move' -Roots @('C:\roms') -Extensions @('.zip') -UseDat $false -DatRoot '' -DatHashType 'SHA1' -DatMap @{} -ToolOverrides @{} -Log { param($m) } -Convert $true -DedupeParams @{} -Ports $ports

        $result | Should -Not -BeNullOrEmpty
        $script:capturedSortPorts | Should -Be $ports
        $script:capturedDedupePorts | Should -Be $ports
        $script:capturedConvertPorts | Should -Be $ports
    }
}
