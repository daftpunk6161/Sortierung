BeforeAll {
  $loaderPath = Join-Path $PSScriptRoot '..\..\modules\RomCleanupLoader.ps1'
  if (Test-Path $loaderPath) { . $loaderPath }
}

Describe 'Determinismus – Property-Tests' {

  Context 'TEST-DET-01: DatMatch → gleiche Console' {

    It 'DatMatch-basierte Konsolen-Erkennung ist stabil über 50 Durchläufe' {
      if (-not (Get-Command Get-ConsoleType -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-ConsoleType nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "det01_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $testFile = Join-Path $tempDir 'test.chd'
      [System.IO.File]::WriteAllBytes($testFile, [byte[]]@(0))
      try {
        $results = 1..50 | ForEach-Object {
          Get-ConsoleType -FilePath $testFile -RootPath $tempDir
        }
        $unique = $results | Select-Object -Unique
        @($unique).Count | Should -Be 1 -Because 'gleiche Inputs müssen gleiche Console ergeben'
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-DET-02: Select-Winner Determinismus' {

    It 'Select-Winner gibt immer gleichen Winner für gleiche Gruppe' {
      if (-not (Get-Command Select-Winner -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Select-Winner nicht verfügbar'
        return
      }
      # Erstelle Testgruppe mit vorberechneten Score-Properties
      $items = @(
        [pscustomobject]@{ BaseName = 'Game (Europe)'; RegionScore = 1000; HeaderScore = 0; FormatScore = 700; VersionScore = 0; SizeBytes = 1000; MainPath = 'a.iso'; Category = 'GAME' }
        [pscustomobject]@{ BaseName = 'Game (USA)'; RegionScore = 999; HeaderScore = 0; FormatScore = 850; VersionScore = 0; SizeBytes = 1200; MainPath = 'b.chd'; Category = 'GAME' }
        [pscustomobject]@{ BaseName = 'Game (Japan)'; RegionScore = 998; HeaderScore = 0; FormatScore = 500; VersionScore = 0; SizeBytes = 800; MainPath = 'c.zip'; Category = 'GAME' }
      )

      $results = 1..100 | ForEach-Object {
        $winner = Select-Winner -Items $items
        if ($winner) { $winner.BaseName } else { 'NULL' }
      }
      $unique = $results | Select-Object -Unique
      @($unique).Count | Should -Be 1 -Because 'gleiche Inputs müssen gleichen Winner ergeben'
    }
  }

  Context 'ConvertTo-GameKey Determinismus' {

    It 'gleiche BaseName erzeugt immer gleichen Key über 100 Durchläufe' {
      if (-not (Get-Command ConvertTo-GameKey -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'ConvertTo-GameKey nicht verfügbar'
        return
      }
      $name = 'Super Mario World (Europe) (Rev 1)'
      $results = 1..100 | ForEach-Object { ConvertTo-GameKey -BaseName $name }
      $unique = $results | Select-Object -Unique
      @($unique).Count | Should -Be 1
    }
  }
}
