#requires -Modules Pester

<#
  TEST-12: Pipeline Coverage Test
  Validiert, dass jede Detection-Stufe (DAT, ArchiveExt, ArchiveHeader,
  DolphinTool, DiscID-Filename, Heuristic/Get-ConsoleType) als "Gewinner"
  der Pipeline erreichbar ist.
#>

BeforeAll {
    $testsRoot = $PSScriptRoot
    while ($testsRoot -and -not (Test-Path (Join-Path $testsRoot 'TestScriptLoader.ps1'))) {
        $testsRoot = Split-Path -Parent $testsRoot
    }
    . (Join-Path $testsRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $testsRoot -TempPrefix 'pipeline_cov_test'
    $script:ScriptPath = $ctx.ScriptPath
    $script:TempScript = $ctx.TempScript
    . $script:TempScript
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'TEST-12: Detection Pipeline Coverage' {

    Context 'Stufe 5: Heuristic/Get-ConsoleType als Gewinner' {
        It 'Extension-basiert (.gba) via Get-ConsoleType -> GBA (DryRun)' {
            $root = Join-Path $TestDrive "pc_ext_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            'dummy' | Out-File (Join-Path $root 'game.gba') -Encoding ascii

            $logs = [System.Collections.Generic.List[string]]::new()
            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.gba') -Log { param($m) $logs.Add($m) } -Mode 'DryRun'

            $result.Total | Should -BeGreaterOrEqual 1
            $result.Unknown | Should -Be 0
            # GBA erkannt = kein UNKNOWN
            $result.Moved | Should -BeGreaterOrEqual 1
        }

        It 'Folder-basiert (ps1/) via Get-ConsoleType -> PS1 (DryRun)' {
            $root = Join-Path $TestDrive "pc_folder_$(Get-Random)"
            $subDir = Join-Path $root 'ps1'
            New-Item -ItemType Directory -Path $subDir -Force | Out-Null
            'dummy' | Out-File (Join-Path $subDir 'game.bin') -Encoding ascii

            $logs = [System.Collections.Generic.List[string]]::new()
            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.bin') -Log { param($m) $logs.Add($m) } -Mode 'DryRun'

            $result.Total | Should -BeGreaterOrEqual 1
            $result.Unknown | Should -Be 0
        }
    }

    Context 'Stufe: UNKNOWN Fallback' {
        It 'Unbekannte Extension ohne Folder-Context -> UNKNOWN + HEURISTIC_NO_MATCH' {
            $root = Join-Path $TestDrive "pc_unk_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            'dummy' | Out-File (Join-Path $root 'mystery.xyz') -Encoding ascii

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.xyz') -Log { param($m) } -Mode 'DryRun'
            $result.Unknown | Should -Be 1
            $result.UnknownReasons.ContainsKey('HEURISTIC_NO_MATCH') | Should -BeTrue
        }

        It 'ZIP ohne 7z -> UNKNOWN + ARCHIVE_TOOL_MISSING' {
            $root = Join-Path $TestDrive "pc_no7z_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            'dummy' | Out-File (Join-Path $root 'archive.zip') -Encoding ascii

            Mock -CommandName Find-ConversionTool -MockWith { return $null }
            Mock -CommandName Get-ConsoleType -MockWith { return 'UNKNOWN' }

            $result = Invoke-ConsoleSort -Roots @($root) -UseDat $false -IncludeExtensions @('.zip') -Log { param($m) } -Mode 'DryRun'
            $result.Unknown | Should -Be 1
            $result.UnknownReasons.ContainsKey('ARCHIVE_TOOL_MISSING') | Should -BeTrue
        }
    }

    Context 'Pipeline-Stufen Existenz' {

        It 'Invoke-ConsoleSort enthaelt DAT-Hash-Lookup' {
            $body = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $body | Should -Match 'datHashToConsole' -Because 'DAT-Hash-Lookup sollte in der Pipeline sein'
        }

        It 'Invoke-ConsoleSort enthaelt Archive-Entry-Pruefung' {
            $body = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $body | Should -Match 'Get-ConsoleFromArchiveEntries' -Because 'Archive-Entry-Pruefung sollte in der Pipeline sein'
        }

        It 'Invoke-ConsoleSort enthaelt Archive-Disc-Header-Pruefung' {
            $body = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $body | Should -Match 'Get-ArchiveDiscHeaderConsole' -Because 'Archive-Disc-Header-Pruefung sollte in der Pipeline sein'
        }

        It 'Invoke-ConsoleSort enthaelt DolphinTool-Check' {
            $body = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $body | Should -Match 'Get-ConsoleFromDolphinTool' -Because 'DolphinTool-Check sollte in der Pipeline sein'
        }

        It 'Invoke-ConsoleSort enthaelt Disc-ID Filename Fallback' {
            $body = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $body | Should -Match 'Get-ConsoleFromDiscIdInFileName' -Because 'Disc-ID Filename Fallback sollte in der Pipeline sein'
        }

        It 'Invoke-ConsoleSort enthaelt Get-ConsoleType Heuristik' {
            $body = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $body | Should -Match 'Get-ConsoleType' -Because 'Get-ConsoleType Heuristik sollte in der Pipeline sein'
        }

        It 'Pipeline hat 6 Reason-Codes definiert' {
            $body = (Get-Command Invoke-ConsoleSort).ScriptBlock.ToString()
            $codes = @(
                'ARCHIVE_AMBIGUOUS_EXT',
                'ARCHIVE_TOOL_MISSING',
                'ARCHIVE_DISC_HEADER_MISSING',
                'DOLPHIN_DISC_ID_MISSING',
                'HEURISTIC_NO_MATCH',
                'DISC_HEADER_IO_ERROR'
            )
            foreach ($code in $codes) {
                $body | Should -Match $code -Because "Reason-Code '$code' sollte definiert sein"
            }
        }
    }

    Context 'Get-ConsoleType Pipeline-Stufen Coverage' {

        BeforeEach {
            $script:CONSOLE_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $script:CONSOLE_FOLDER_TYPE_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            if (Get-Variable -Name ISO_HEADER_CACHE -Scope Script -ErrorAction SilentlyContinue) {
                $script:ISO_HEADER_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            }
        }

        It 'Stufe 1: Folder-Map Match (exakt)' {
            $root = Join-Path $env:TEMP "pester_cov_folder_$(Get-Random)"
            $sub = Join-Path $root 'snes'
            New-Item -ItemType Directory -Path $sub -Force | Out-Null
            try {
                $result = Get-ConsoleType -RootPath $root -FilePath (Join-Path $sub 'game.bin') -Extension '.bin'
                $result | Should -Be 'SNES'
            } finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Stufe 2: Disc-Header Match (.iso mit GC magic)' {
            $root = Join-Path $env:TEMP "pester_cov_header_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            $path = Join-Path $root 'disc.iso'
            # PERF-01: Datei >= 1 MB damit Disc-Header-Probing greift
            $buffer = New-Object byte[] 1048576
            $buffer[0x1C] = 0xC2; $buffer[0x1D] = 0x33; $buffer[0x1E] = 0x9F; $buffer[0x1F] = 0x3D
            [System.IO.File]::WriteAllBytes($path, $buffer)
            try {
                $result = Get-ConsoleType -RootPath $root -FilePath $path -Extension '.iso'
                $result | Should -Be 'GC'
            } finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Stufe 3: UNIQUE Extension-Map Match (.gba)' {
            $root = Join-Path $env:TEMP "pester_cov_ext_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            try {
                $result = Get-ConsoleType -RootPath $root -FilePath (Join-Path $root 'game.gba') -Extension '.gba'
                $result | Should -Be 'GBA'
            } finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Stufe 5: AMBIG Extension-Map Match (.cso)' {
            $root = Join-Path $env:TEMP "pester_cov_ambig_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            try {
                $result = Get-ConsoleType -RootPath $root -FilePath (Join-Path $root 'game.cso') -Extension '.cso'
                $result | Should -Be 'PSP'
            } finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'Stufe UNKNOWN: Keine passende Detection' {
            $root = Join-Path $env:TEMP "pester_cov_unk_$(Get-Random)"
            New-Item -ItemType Directory -Path $root -Force | Out-Null
            try {
                $result = Get-ConsoleType -RootPath $root -FilePath (Join-Path $root 'mystery.dat') -Extension '.dat'
                $result | Should -Be 'UNKNOWN'
            } finally {
                Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}
