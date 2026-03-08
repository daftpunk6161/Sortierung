#requires -Modules Pester

Describe 'DAT Index Cache Persistenz' {
    BeforeAll {
        $repoRoot = $PSScriptRoot
        while ($repoRoot -and -not (Test-Path (Join-Path $repoRoot 'simple_sort.ps1'))) {
            $repoRoot = Split-Path -Parent $repoRoot
        }

        . (Join-Path $repoRoot 'dev\modules\FileOps.ps1')
        . (Join-Path $repoRoot 'dev\modules\Settings.ps1')
        . (Join-Path $repoRoot 'dev\modules\Dat.ps1')
        if (-not (Get-Variable -Name CONSOLE_FOLDER_MAP -Scope Script -ErrorAction SilentlyContinue) -or -not $script:CONSOLE_FOLDER_MAP) {
            $script:CONSOLE_FOLDER_MAP = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        }
        if (-not (Get-Variable -Name BEST_FORMAT -Scope Script -ErrorAction SilentlyContinue) -or -not $script:BEST_FORMAT) {
            $script:BEST_FORMAT = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        }
        if (-not $script:BEST_FORMAT.ContainsKey('PS1')) {
            $script:BEST_FORMAT['PS1'] = 'chd'
        }
    }

    It 'speichert und lädt Dat-Index per Fingerprint' {
        Push-Location $TestDrive
        try {
            $fingerprint = Get-DatIndexCacheFingerprint -DatRoot $TestDrive -HashType 'SHA1' -ConsoleMap @{}
            $index = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $index['PS1'] = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $index['PS1']['abc123'] = 'Game A'

            Save-DatIndexCache -Root $TestDrive -Fingerprint $fingerprint -Index $index
            $loaded = Import-DatIndexCache -Root $TestDrive -ExpectedFingerprint $fingerprint

            $loaded | Should -Not -BeNullOrEmpty
            $loaded.ContainsKey('PS1') | Should -BeTrue
            $loaded['PS1']['abc123'] | Should -Be 'Game A'
        } finally {
            Pop-Location
        }
    }

        It 'Get-DatIndex verarbeitet DAT mit DOCTYPE sicher (DTD ignoriert) und indexiert Hashes' {
                Push-Location $TestDrive
                try {
                        $datRoot = Join-Path $TestDrive 'dat'
                        New-Item -ItemType Directory -Path $datRoot -Force | Out-Null

                        $datFile = Join-Path $datRoot 'sony_psx.dat'
                        @'
<?xml version="1.0" encoding="utf-8"?>
<!DOCTYPE datafile [
    <!ELEMENT datafile ANY>
    <!ENTITY xxe SYSTEM "file:///c:/windows/win.ini">
]>
<datafile>
    <game name="Game A">
        <rom name="gamea.bin" sha1="ABCDEF0123456789ABCDEF0123456789ABCDEF01" />
    </game>
</datafile>
'@ | Set-Content -LiteralPath $datFile -Encoding UTF8

                        $index = Get-DatIndex -DatRoot $datRoot -HashType 'SHA1' -ConsoleMap @{ PS1 = $datRoot } -Log { param($m) }

                        $index | Should -Not -BeNullOrEmpty
                        $index.ContainsKey('PS1') | Should -BeTrue
                        $index['PS1'].ContainsKey('abcdef0123456789abcdef0123456789abcdef01') | Should -BeTrue
                        $index['PS1']['abcdef0123456789abcdef0123456789abcdef01'] | Should -Be 'Game A'
                } finally {
                        Pop-Location
                }
        }
}
