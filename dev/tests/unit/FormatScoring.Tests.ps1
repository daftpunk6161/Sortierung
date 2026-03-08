#requires -Modules Pester

<#
  FormatScoring Unit Tests
  MISS-CS-07: Validiert, dass alle relevanten Extensions korrekte Scores haben.
#>

BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
        $root = Split-Path -Parent $root
    }
    . (Join-Path $root 'dev\modules\FormatScoring.ps1')
}

Describe 'Get-FormatScore' {

    Context 'Disc-Image Formate (hohe Scores)' {
        $testCases = @(
            @{ Ext = '.chd';  ExpMin = 800; ExpMax = 900; Name = 'CHD' }
            @{ Ext = '.iso';  ExpMin = 650; ExpMax = 750; Name = 'ISO' }
            @{ Ext = '.cso';  ExpMin = 650; ExpMax = 700; Name = 'CSO' }
            @{ Ext = '.pbp';  ExpMin = 650; ExpMax = 700; Name = 'PBP' }
            @{ Ext = '.gcz';  ExpMin = 650; ExpMax = 700; Name = 'GCZ' }
            @{ Ext = '.rvz';  ExpMin = 650; ExpMax = 700; Name = 'RVZ' }
            @{ Ext = '.wia';  ExpMin = 640; ExpMax = 700; Name = 'WIA' }
            @{ Ext = '.wbf1'; ExpMin = 630; ExpMax = 700; Name = 'WBF1' }
            @{ Ext = '.wbfs'; ExpMin = 600; ExpMax = 700; Name = 'WBFS' }
        )

        It '<Name> (<Ext>) Score zwischen <ExpMin> und <ExpMax>' -TestCases $testCases {
            param($Ext, $ExpMin, $ExpMax, $Name)
            $score = Get-FormatScore -Extension $Ext
            $score | Should -BeGreaterOrEqual $ExpMin
            $score | Should -BeLessOrEqual $ExpMax
        }
    }

    Context 'Cartridge-Formate (mittlere Scores)' {
        $testCases = @(
            @{ Ext = '.gba'; ExpMin = 550; ExpMax = 650; Name = 'GBA' }
            @{ Ext = '.nes'; ExpMin = 550; ExpMax = 650; Name = 'NES' }
            @{ Ext = '.sfc'; ExpMin = 550; ExpMax = 650; Name = 'SNES' }
            @{ Ext = '.n64'; ExpMin = 550; ExpMax = 650; Name = 'N64' }
            @{ Ext = '.nds'; ExpMin = 550; ExpMax = 650; Name = 'NDS' }
            @{ Ext = '.md';  ExpMin = 550; ExpMax = 650; Name = 'MD' }
        )

        It '<Name> (<Ext>) Score zwischen <ExpMin> und <ExpMax>' -TestCases $testCases {
            param($Ext, $ExpMin, $ExpMax, $Name)
            $score = Get-FormatScore -Extension $Ext
            $score | Should -BeGreaterOrEqual $ExpMin
            $score | Should -BeLessOrEqual $ExpMax
        }
    }

    Context 'Komprimierte Formate' {
        It 'ZIP Score 500' {
            Get-FormatScore -Extension '.zip' | Should -Be 500
        }
        It '7Z Score 480' {
            Get-FormatScore -Extension '.7z' | Should -Be 480
        }
        It 'RAR Score 400' {
            Get-FormatScore -Extension '.rar' | Should -Be 400
        }
    }

    Context 'Set-Typen' {
        It 'M3USET hat hoechsten Score' {
            Get-FormatScore -Extension '.m3u' -Type 'M3USET' | Should -BeGreaterOrEqual 850
        }
        It 'CUESET Score >= 750' {
            Get-FormatScore -Extension '.cue' -Type 'CUESET' | Should -BeGreaterOrEqual 750
        }
        It 'GDISET Score >= 750' {
            Get-FormatScore -Extension '.gdi' -Type 'GDISET' | Should -BeGreaterOrEqual 750
        }
    }

    Context 'Score-Ordnung (Determinismus)' {
        It 'CHD > ISO > ZIP > RAR > Unbekannt' {
            $chd  = Get-FormatScore -Extension '.chd'
            $iso  = Get-FormatScore -Extension '.iso'
            $zip  = Get-FormatScore -Extension '.zip'
            $rar  = Get-FormatScore -Extension '.rar'
            $unk  = Get-FormatScore -Extension '.xyz'

            $chd | Should -BeGreaterThan $iso
            $iso | Should -BeGreaterThan $zip
            $zip | Should -BeGreaterThan $rar
            $rar | Should -BeGreaterThan $unk
        }

        It 'WIA > WBF1 (gleiche Plattform, WIA ist neuer)' {
            $wia  = Get-FormatScore -Extension '.wia'
            $wbf1 = Get-FormatScore -Extension '.wbf1'
            $wia | Should -BeGreaterOrEqual $wbf1
        }
    }

    Context 'Unbekannte Extensions -> Fallback-Score' {
        It 'Unbekannte Extension gibt niedrigen Score' {
            $score = Get-FormatScore -Extension '.xyz'
            $score | Should -BeLessOrEqual 400
            $score | Should -BeGreaterThan 0
        }
    }
}
