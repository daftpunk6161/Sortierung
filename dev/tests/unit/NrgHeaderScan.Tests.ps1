#Requires -Modules Pester
<#
  T-18: NRG Disc-Header-Scan — Tests for MISS-CS-03 (Nero disc images)
  Verifies that Get-DiscHeaderConsole detects NER5/NERO footer signatures
  and sets diagnostic info (NRG_NO_CONSOLE).
#>

Describe 'T-18: NRG Footer-Scan' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\RomCleanupLoader.ps1')

        $script:tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) "NrgTest_$([guid]::NewGuid().ToString('N').Substring(0,8))"
        New-Item -ItemType Directory -Path $script:tmpRoot -Force | Out-Null
    }

    AfterAll {
        if ($script:tmpRoot -and (Test-Path $script:tmpRoot)) {
            Remove-Item -Path $script:tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    Context 'NER5-Signatur (Nero v5.5+)' {

        It 'erkennt NER5 in den letzten 12 Bytes' {
            $nrg = Join-Path $script:tmpRoot 'test_ner5.nrg'
            # Create file: 256 bytes padding + "NER5" at offset 0 of last 12 bytes
            $data = [byte[]]::new(256)
            $sig = [System.Text.Encoding]::ASCII.GetBytes('NER5')
            [Array]::Copy($sig, 0, $data, ($data.Length - 12), 4)
            [System.IO.File]::WriteAllBytes($nrg, $data)

            $result = Get-DiscHeaderConsole -Path $nrg
            $result | Should -BeNullOrEmpty  # NRG alone cannot determine console

            $script:LAST_DISC_HEADER_ERROR | Should -Not -BeNullOrEmpty
            $script:LAST_DISC_HEADER_ERROR.Type | Should -Be 'NRG_NO_CONSOLE'
        }

        It 'erkennt NER5 an Position 4 der letzten 12 Bytes' {
            $nrg = Join-Path $script:tmpRoot 'test_ner5_pos2.nrg'
            $data = [byte[]]::new(128)
            $sig = [System.Text.Encoding]::ASCII.GetBytes('NER5')
            [Array]::Copy($sig, 0, $data, ($data.Length - 8), 4)
            [System.IO.File]::WriteAllBytes($nrg, $data)

            $result = Get-DiscHeaderConsole -Path $nrg
            $result | Should -BeNullOrEmpty

            $script:LAST_DISC_HEADER_ERROR | Should -Not -BeNullOrEmpty
            $script:LAST_DISC_HEADER_ERROR.Type | Should -Be 'NRG_NO_CONSOLE'
        }
    }

    Context 'NERO-Signatur (aeltere Versionen)' {

        It 'erkennt NERO in den letzten 12 Bytes' {
            $nrg = Join-Path $script:tmpRoot 'test_nero.nrg'
            $data = [byte[]]::new(200)
            $sig = [System.Text.Encoding]::ASCII.GetBytes('NERO')
            [Array]::Copy($sig, 0, $data, ($data.Length - 12), 4)
            [System.IO.File]::WriteAllBytes($nrg, $data)

            $result = Get-DiscHeaderConsole -Path $nrg
            $result | Should -BeNullOrEmpty

            $script:LAST_DISC_HEADER_ERROR | Should -Not -BeNullOrEmpty
            $script:LAST_DISC_HEADER_ERROR.Type | Should -Be 'NRG_NO_CONSOLE'
            $script:LAST_DISC_HEADER_ERROR.Path | Should -Be $nrg
        }
    }

    Context 'Keine NRG-Signatur' {

        It 'setzt keinen NRG-Fehler bei normaler ISO-Datei' {
            $iso = Join-Path $script:tmpRoot 'normal.iso'
            $data = [byte[]]::new(64)
            [System.IO.File]::WriteAllBytes($iso, $data)

            $result = Get-DiscHeaderConsole -Path $iso
            # Should not have NRG_NO_CONSOLE type
            if ($script:LAST_DISC_HEADER_ERROR) {
                $script:LAST_DISC_HEADER_ERROR.Type | Should -Not -Be 'NRG_NO_CONSOLE'
            }
        }

        It 'Datei zu klein fuer Footer-Scan (< 12 Bytes)' {
            $tiny = Join-Path $script:tmpRoot 'tiny.nrg'
            [System.IO.File]::WriteAllBytes($tiny, [byte[]]::new(8))

            $result = Get-DiscHeaderConsole -Path $tiny
            $result | Should -BeNullOrEmpty

            if ($script:LAST_DISC_HEADER_ERROR) {
                $script:LAST_DISC_HEADER_ERROR.Type | Should -Not -Be 'NRG_NO_CONSOLE'
            }
        }
    }

    Context 'SEC-CS-06: State-Reset zwischen Aufrufen' {

        It 'LAST_DISC_HEADER_ERROR wird bei neuem Aufruf zurueckgesetzt' {
            # First call: NRG file → sets error
            $nrg = Join-Path $script:tmpRoot 'reset_test.nrg'
            $data = [byte[]]::new(64)
            $sig = [System.Text.Encoding]::ASCII.GetBytes('NER5')
            [Array]::Copy($sig, 0, $data, ($data.Length - 12), 4)
            [System.IO.File]::WriteAllBytes($nrg, $data)

            Get-DiscHeaderConsole -Path $nrg | Out-Null
            $script:LAST_DISC_HEADER_ERROR | Should -Not -BeNullOrEmpty
            $script:LAST_DISC_HEADER_ERROR.Type | Should -Be 'NRG_NO_CONSOLE'

            # Second call: normal file → error should be reset
            $plain = Join-Path $script:tmpRoot 'reset_plain.bin'
            [System.IO.File]::WriteAllBytes($plain, [byte[]]::new(64))

            Get-DiscHeaderConsole -Path $plain | Out-Null
            if ($script:LAST_DISC_HEADER_ERROR) {
                $script:LAST_DISC_HEADER_ERROR.Type | Should -Not -Be 'NRG_NO_CONSOLE'
            }
        }
    }
}
