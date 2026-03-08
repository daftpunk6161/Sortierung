#Requires -Modules Pester
<#
  TEST-MOD-08: Sets.ps1 — Tests für SetItem-Builder und Score-Berechnung
  Testet New-SetItem, New-FileItem, Score-Konsistenz.
#>

BeforeAll {
  . "$PSScriptRoot/../../modules/RomCleanupLoader.ps1"
}

Describe 'Sets – SetItem-Builder' {

  BeforeAll {
    $script:tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) "SetsTest_$([guid]::NewGuid().ToString('N').Substring(0,8))"
    $null = New-Item -ItemType Directory -Path $script:tmpRoot -Force
    # Testdateien anlegen
    $script:testFile = Join-Path $script:tmpRoot 'game.iso'
    [System.IO.File]::WriteAllBytes($script:testFile, [byte[]]::new(4096))
  }

  AfterAll {
    if ($script:tmpRoot -and (Test-Path $script:tmpRoot)) {
      Remove-Item -Path $script:tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
  }

  Context 'New-FileItem – Grundfunktion' {

    It 'erstellt FileItem mit Typ FILE' {
      $item = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'GAME' -Region 'EU' -PreferOrder @('EU','US') `
        -VersionScore 100 -GameKey 'TestGame'
      $item | Should -Not -BeNullOrEmpty
      $item.Type | Should -Be 'FILE'
      $item.Category | Should -Be 'GAME'
      $item.Region | Should -Be 'EU'
      $item.GameKey | Should -Be 'TestGame'
    }

    It 'setzt IsComplete auf true für einzelne Dateien' {
      $item = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'GAME' -Region 'US' -PreferOrder @('US') `
        -VersionScore 0 -GameKey 'SingleFile'
      $item.IsComplete | Should -BeTrue
      $item.MissingCount | Should -Be 0
    }
  }

  Context 'New-SetItem – Score-Berechnung' {

    It 'berechnet RegionScore basierend auf PreferOrder' {
      $itemEU = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'GAME' -Region 'EU' -PreferOrder @('EU','US','JP') `
        -VersionScore 0 -GameKey 'ScoreTest'
      $itemUS = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'GAME' -Region 'US' -PreferOrder @('EU','US','JP') `
        -VersionScore 0 -GameKey 'ScoreTest'
      # EU soll höheren RegionScore haben (erste Position)
      $itemEU.RegionScore | Should -BeGreaterThan $itemUS.RegionScore
    }

    It 'berechnet FormatScore – CHD > ISO > ZIP' {
      $chdFile = Join-Path $script:tmpRoot 'test.chd'
      $isoFile = Join-Path $script:tmpRoot 'test.iso'
      $zipFile = Join-Path $script:tmpRoot 'test.zip'
      @($chdFile, $isoFile, $zipFile) | ForEach-Object {
        [System.IO.File]::WriteAllBytes($_, [byte[]]::new(64))
      }
      $itemChd = New-FileItem -Root $script:tmpRoot -MainPath $chdFile `
        -Category 'GAME' -Region 'EU' -PreferOrder @('EU') `
        -VersionScore 0 -GameKey 'FormatTest'
      $itemIso = New-FileItem -Root $script:tmpRoot -MainPath $isoFile `
        -Category 'GAME' -Region 'EU' -PreferOrder @('EU') `
        -VersionScore 0 -GameKey 'FormatTest'
      $itemZip = New-FileItem -Root $script:tmpRoot -MainPath $zipFile `
        -Category 'GAME' -Region 'EU' -PreferOrder @('EU') `
        -VersionScore 0 -GameKey 'FormatTest'
      $itemChd.FormatScore | Should -BeGreaterThan $itemIso.FormatScore
      $itemIso.FormatScore | Should -BeGreaterThan $itemZip.FormatScore
    }

    It 'setzt DatMatch-Flag korrekt' {
      $item = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'GAME' -Region 'EU' -PreferOrder @('EU') `
        -VersionScore 0 -GameKey 'DatTest' -DatMatch $true
      $item.DatMatch | Should -BeTrue

      $itemNo = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'GAME' -Region 'EU' -PreferOrder @('EU') `
        -VersionScore 0 -GameKey 'DatTest' -DatMatch $false
      $itemNo.DatMatch | Should -BeFalse
    }
  }

  Context 'New-SetItem – Kategorie-Handling' {

    It 'behandelt JUNK-Kategorie' {
      $item = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'JUNK' -Region 'EU' -PreferOrder @('EU') `
        -VersionScore 0 -GameKey 'JunkGame'
      $item.Category | Should -Be 'JUNK'
    }

    It 'behandelt BIOS-Kategorie' {
      $item = New-FileItem -Root $script:tmpRoot -MainPath $script:testFile `
        -Category 'BIOS' -Region 'US' -PreferOrder @('US') `
        -VersionScore 0 -GameKey '[BIOS]'
      $item.Category | Should -Be 'BIOS'
    }
  }

  Context 'Score-Determinismus' {

    It 'gleiche Inputs erzeugen gleichen Score' {
      $params = @{
        Root         = $script:tmpRoot
        MainPath     = $script:testFile
        Category     = 'GAME'
        Region       = 'EU'
        PreferOrder  = @('EU', 'US', 'JP')
        VersionScore = 42
        GameKey      = 'DeterminismTest'
      }
      $item1 = New-FileItem @params
      $item2 = New-FileItem @params
      $item1.RegionScore | Should -Be $item2.RegionScore
      $item1.FormatScore | Should -Be $item2.FormatScore
      $item1.VersionScore | Should -Be $item2.VersionScore
    }
  }
}
