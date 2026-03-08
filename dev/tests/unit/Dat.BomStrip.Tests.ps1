#requires -Modules Pester
# ================================================================
#  Dat.BomStrip.Tests.ps1  –  F-05
#  Verifies DAT XML parsing works when content starts with UTF-8 BOM
#  character U+FEFF before the XML prolog/root element.
# ================================================================

Describe 'F-05: DAT BOM-Strip Erkennung' {

    BeforeAll {
        $root = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..\..\..'))

        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\AppState.ps1')
        . (Join-Path $root 'dev\modules\Core.ps1')
        . (Join-Path $root 'dev\modules\Classification.ps1')
        . (Join-Path $root 'dev\modules\Dat.ps1')
    }

    It 'laedt und indiziert XML mit fuehrendem BOM-Zeichen' {
        $tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("dat-bom-{0}" -f [Guid]::NewGuid().ToString('N'))
        $ps1Dir = Join-Path $tmpRoot 'PS1'
        $datPath = Join-Path $ps1Dir 'ps1.dat'

        try {
            New-Item -ItemType Directory -Path $ps1Dir -Force | Out-Null

            $xml = @'
<?xml version="1.0"?>
<datafile>
  <game name="Bom Game">
    <rom name="bom.bin" sha1="1111111111111111111111111111111111111111"/>
  </game>
</datafile>
'@

            $contentWithBomChar = ([char]0xFEFF) + $xml
            $utf8NoBom = New-Object System.Text.UTF8Encoding($false)
            [System.IO.File]::WriteAllText($datPath, $contentWithBomChar, $utf8NoBom)

            $logFn = { param($msg) }
            $index = Get-DatIndex -DatRoot $tmpRoot -HashType 'SHA1' -Log $logFn

            $index.ContainsKey('PS1') | Should -BeTrue
            $index['PS1'].ContainsKey('1111111111111111111111111111111111111111') | Should -BeTrue
            $index['PS1']['1111111111111111111111111111111111111111'] | Should -Be 'Bom Game'
        }
        finally {
            Remove-Item -LiteralPath $tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
