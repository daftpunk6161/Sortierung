#Requires -Modules Pester
<#
  T-11: MDF/MDS Set-Handling — Tests for MISS-CS-02 (Alcohol 120% disc images)
  Verifies Get-MdsRelatedFiles and Get-MdsMissingFiles.
#>

Describe 'T-11: MDF/MDS Set-Parsing' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\SetParsing.ps1')

        $script:tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) "MdsTest_$([guid]::NewGuid().ToString('N').Substring(0,8))"
        New-Item -ItemType Directory -Path $script:tmpRoot -Force | Out-Null
    }

    AfterAll {
        if ($script:tmpRoot -and (Test-Path $script:tmpRoot)) {
            Remove-Item -Path $script:tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context 'Get-MdsRelatedFiles' {

        It 'sammelt MDS + MDF wenn beide vorhanden' {
            $mds = Join-Path $script:tmpRoot 'game.mds'
            $mdf = Join-Path $script:tmpRoot 'game.mdf'
            [System.IO.File]::WriteAllBytes($mds, [byte[]]::new(64))
            [System.IO.File]::WriteAllBytes($mdf, [byte[]]::new(4096))

            $result = @(Get-MdsRelatedFiles -MdsPath $mds -RootPath $script:tmpRoot)
            $result.Count | Should -Be 2
            $result | Should -Contain $mds
            $result | Should -Contain $mdf
        }

        It 'gibt nur MDS zurueck wenn MDF fehlt' {
            $sub = Join-Path $script:tmpRoot 'sub_nomdf'
            New-Item -ItemType Directory -Path $sub -Force | Out-Null
            $mds = Join-Path $sub 'lonely.mds'
            [System.IO.File]::WriteAllBytes($mds, [byte[]]::new(64))

            $result = @(Get-MdsRelatedFiles -MdsPath $mds -RootPath $sub)
            $result.Count | Should -Be 1
            $result[0] | Should -Be $mds
        }

        It 'MDS-Pfad ist immer erstes Element' {
            $sub = Join-Path $script:tmpRoot 'sub_order'
            New-Item -ItemType Directory -Path $sub -Force | Out-Null
            $mds = Join-Path $sub 'disc.mds'
            $mdf = Join-Path $sub 'disc.mdf'
            [System.IO.File]::WriteAllBytes($mds, [byte[]]::new(32))
            [System.IO.File]::WriteAllBytes($mdf, [byte[]]::new(1024))

            $result = @(Get-MdsRelatedFiles -MdsPath $mds -RootPath $sub)
            $result[0] | Should -Be $mds
        }

        It 'keine Duplikate im Ergebnis' {
            $sub = Join-Path $script:tmpRoot 'sub_dedup'
            New-Item -ItemType Directory -Path $sub -Force | Out-Null
            $mds = Join-Path $sub 'dup.mds'
            $mdf = Join-Path $sub 'dup.mdf'
            [System.IO.File]::WriteAllBytes($mds, [byte[]]::new(32))
            [System.IO.File]::WriteAllBytes($mdf, [byte[]]::new(512))

            $result = @(Get-MdsRelatedFiles -MdsPath $mds -RootPath $sub)
            ($result | Select-Object -Unique).Count | Should -Be $result.Count
        }
    }

    Context 'Get-MdsMissingFiles' {

        It 'meldet fehlende MDF-Datei' {
            $sub = Join-Path $script:tmpRoot 'sub_miss'
            New-Item -ItemType Directory -Path $sub -Force | Out-Null
            $mds = Join-Path $sub 'incomplete.mds'
            [System.IO.File]::WriteAllBytes($mds, [byte[]]::new(64))

            $missing = @(Get-MdsMissingFiles -MdsPath $mds -RootPath $sub)
            $missing.Count | Should -Be 1
            $missing[0] | Should -Match '\.mdf$'
        }

        It 'leere Liste wenn MDF vorhanden' {
            $sub = Join-Path $script:tmpRoot 'sub_complete'
            New-Item -ItemType Directory -Path $sub -Force | Out-Null
            $mds = Join-Path $sub 'full.mds'
            $mdf = Join-Path $sub 'full.mdf'
            [System.IO.File]::WriteAllBytes($mds, [byte[]]::new(32))
            [System.IO.File]::WriteAllBytes($mdf, [byte[]]::new(2048))

            $missing = @(Get-MdsMissingFiles -MdsPath $mds -RootPath $sub)
            $missing.Count | Should -Be 0
        }

        It 'fehlende MDF hat gleichen Basisnamen wie MDS' {
            $sub = Join-Path $script:tmpRoot 'sub_basename'
            New-Item -ItemType Directory -Path $sub -Force | Out-Null
            $mds = Join-Path $sub 'MyGame (EU).mds'
            [System.IO.File]::WriteAllBytes($mds, [byte[]]::new(32))

            $missing = @(Get-MdsMissingFiles -MdsPath $mds -RootPath $sub)
            $missing.Count | Should -Be 1
            $missing[0] | Should -Match 'MyGame \(EU\)\.mdf$'
        }
    }
}
