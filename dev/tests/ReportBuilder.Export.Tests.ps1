#requires -Modules Pester

Describe 'ReportBuilder XML exports' {
    BeforeAll {
        $repoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $modulesRoot = Join-Path $repoRoot 'dev\modules'

        . (Join-Path $modulesRoot 'Settings.ps1')
        . (Join-Path $modulesRoot 'AppState.ps1')
        . (Join-Path $modulesRoot 'FileOps.ps1')
        . (Join-Path $modulesRoot 'Classification.ps1')
        . (Join-Path $modulesRoot 'Report.ps1')
        . (Join-Path $modulesRoot 'ReportBuilder.ps1')
    }

    It 'Write-Reports should export LaunchBox and EmulationStation XML files' {
        $report = [System.Collections.Generic.List[psobject]]::new()
        $report.Add([pscustomobject]@{
            Category = 'GAME'
            GameKey = 'test-game'
            Action = 'KEEP'
            Region = 'EU'
            WinnerRegion = 'EU'
            VersionScore = 10
            FormatScore = 10
            Type = 'FILE'
            DatMatch = $false
            IsCorrupt = $false
            MainPath = 'C:\ROMs\test-game.chd'
            Root = 'C:\ROMs'
            SizeBytes = 1024
            Winner = 'C:\ROMs\test-game.chd'
        }) | Out-Null

        $result = Write-Reports -Report $report -DupeGroups 0 -TotalDupes 0 -SavedBytes 0 -JunkCount 0 -JunkBytes 0 -BiosCount 0 -UniqueGames 1 -TotalScanned 1 -Mode 'DryRun' -UseDat $false -DatIndex @{} -ConsoleSortUnknownReasons @{} -GenerateReports $true -Log { param($m) }

        [string]$result.LaunchBoxXmlPath | Should -Not -BeNullOrEmpty
        [string]$result.EmulationStationXmlPath | Should -Not -BeNullOrEmpty
        (Test-Path -LiteralPath $result.LaunchBoxXmlPath) | Should -BeTrue
        (Test-Path -LiteralPath $result.EmulationStationXmlPath) | Should -BeTrue
    }
}
