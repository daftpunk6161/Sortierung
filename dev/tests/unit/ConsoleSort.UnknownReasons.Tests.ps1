#requires -Modules Pester

<#
  TEST-11: Unknown-Reasons Statistik Test
  Validiert, dass Invoke-ConsoleSort die UnknownReasons korrekt aggregiert
  und Get-ConsoleUnknownReasonLabel die richtigen Labels liefert.
#>

BeforeAll {
    $testsRoot = $PSScriptRoot
    while ($testsRoot -and -not (Test-Path (Join-Path $testsRoot 'TestScriptLoader.ps1'))) {
        $testsRoot = Split-Path -Parent $testsRoot
    }
    . (Join-Path $testsRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $testsRoot -TempPrefix 'unknown_reasons_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'TEST-11: Unknown-Reasons Statistik' {

    Context 'UnknownReasons Hashtable Struktur' {

        It 'ARCHIVE_TOOL_MISSING wird korrekt gezaehlt' {
            $root = Join-Path $TestDrive "ur_archive_tool_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            # ZIP ohne 7z-Tool
            'dummy' | Out-File (Join-Path $root 'game1.zip') -Encoding ascii
            'dummy' | Out-File (Join-Path $root 'game2.zip') -Encoding ascii

            Mock -CommandName Find-ConversionTool -MockWith { return $null }
            Mock -CommandName Get-ConsoleType -MockWith { return 'UNKNOWN' }

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.zip') -Log { param($m) } -Mode 'DryRun'
            $result | Should -Not -BeNullOrEmpty
            $result.Unknown | Should -BeGreaterOrEqual 2
            $result.UnknownReasons.ContainsKey('ARCHIVE_TOOL_MISSING') | Should -BeTrue
            $result.UnknownReasons['ARCHIVE_TOOL_MISSING'] | Should -BeGreaterOrEqual 2
        }

        It 'HEURISTIC_NO_MATCH wird korrekt gezaehlt' {
            $root = Join-Path $TestDrive "ur_heuristic_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            # Nicht-gemappte Extension
            'dummy' | Out-File (Join-Path $root 'file1.xyz') -Encoding ascii
            'dummy' | Out-File (Join-Path $root 'file2.xyz') -Encoding ascii
            'dummy' | Out-File (Join-Path $root 'file3.xyz') -Encoding ascii

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.xyz') -Log { param($m) } -Mode 'DryRun'
            $result | Should -Not -BeNullOrEmpty
            $result.Unknown | Should -BeGreaterOrEqual 3
            $result.UnknownReasons.ContainsKey('HEURISTIC_NO_MATCH') | Should -BeTrue
            $result.UnknownReasons['HEURISTIC_NO_MATCH'] | Should -BeGreaterOrEqual 3
        }

        It 'UnknownReasons ist ein Hashtable' {
            $root = Join-Path $TestDrive "ur_type_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            'dummy' | Out-File (Join-Path $root 'file.xyz') -Encoding ascii

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.xyz') -Log { param($m) } -Mode 'DryRun'
            $result.UnknownReasons | Should -BeOfType [hashtable]
        }

        It 'Leeres Verzeichnis -> 0 Unknown, leere UnknownReasons' {
            $root = Join-Path $TestDrive "ur_empty_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -Log { param($m) } -Mode 'DryRun'
            $result | Should -Not -BeNullOrEmpty
            $result.Unknown | Should -Be 0
            $result.UnknownReasons.Count | Should -Be 0
        }
    }

    Context 'Get-ConsoleUnknownReasonLabel Mapping' {

        It 'Alle bekannten Reason-Codes haben Labels' {
            # Get-ConsoleUnknownReasonLabel ist nested in Invoke-ConsoleSort,
            # daher pruefen wir den Funktions-Body direkt
            $funcBody = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()

            $knownCodes = @(
                'ARCHIVE_AMBIGUOUS_EXT',
                'ARCHIVE_TOOL_MISSING',
                'ARCHIVE_DISC_HEADER_MISSING',
                'DOLPHIN_DISC_ID_MISSING',
                'HEURISTIC_NO_MATCH',
                'DISC_HEADER_IO_ERROR'
            )

            foreach ($code in $knownCodes) {
                $funcBody | Should -Match $code -Because "Reason-Code '$code' sollte in Invoke-ConsoleSort definiert sein"
            }
        }

        It 'Reason-Label Funktion ist in Invoke-ConsoleSort vorhanden' {
            $funcBody = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $funcBody | Should -Match 'Get-ConsoleUnknownReasonLabel' -Because 'Label-Funktion sollte existieren'
        }
    }

    Context 'Ergebnis-Struktur Validierung' {

        It 'Invoke-ConsoleSort gibt Total, Moved, Skipped, Unknown zurueck' {
            $root = Join-Path $TestDrive "ur_struct_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            'dummy' | Out-File (Join-Path $root 'game.gba') -Encoding ascii

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.gba') -Log { param($m) } -Mode 'DryRun'
            $result.PSObject.Properties.Name | Should -Contain 'Total'
            $result.PSObject.Properties.Name | Should -Contain 'Moved'
            $result.PSObject.Properties.Name | Should -Contain 'Skipped'
            $result.PSObject.Properties.Name | Should -Contain 'Unknown'
            $result.PSObject.Properties.Name | Should -Contain 'UnknownReasons'
        }

        It 'Total ist konsistent mit Moved/Skipped/Unknown' {
            $root = Join-Path $TestDrive "ur_total_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            'dummy' | Out-File (Join-Path $root 'game.gba') -Encoding ascii
            'dummy' | Out-File (Join-Path $root 'file.xyz') -Encoding ascii

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.gba', '.xyz') -Log { param($m) } -Mode 'DryRun'
            $result.Total | Should -BeGreaterOrEqual 0
            $result.Moved | Should -BeGreaterOrEqual 0
            $result.Unknown | Should -BeGreaterOrEqual 0
            $result.Skipped | Should -BeGreaterOrEqual 0
            # Unknown ist Teilmenge von Total
            $result.Unknown | Should -BeLessOrEqual $result.Total
        }

        It 'Alle Zaehler sind nicht-negativ' {
            $root = Join-Path $TestDrive "ur_nonneg_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -Log { param($m) } -Mode 'DryRun'
            $result.Total | Should -BeGreaterOrEqual 0
            $result.Moved | Should -BeGreaterOrEqual 0
            $result.Skipped | Should -BeGreaterOrEqual 0
            $result.Unknown | Should -BeGreaterOrEqual 0
        }
    }
}
