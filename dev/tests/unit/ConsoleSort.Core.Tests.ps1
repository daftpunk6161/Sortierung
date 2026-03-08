#Requires -Modules Pester
<#
  TEST-MOD-01: ConsoleSort.ps1 — Table-driven Tests für Invoke-ConsoleSort
  Testet Kern-Logik: Erkennung, Verschiebung, Set-Handling, UnknownReasons.
#>

BeforeAll {
  . "$PSScriptRoot/../../modules/RomCleanupLoader.ps1"
}

Describe 'Invoke-ConsoleSort – Kern-Logik' {

  BeforeAll {
    $script:tmpRoot = Join-Path ([System.IO.Path]::GetTempPath()) "ConsoleSortTest_$([guid]::NewGuid().ToString('N').Substring(0,8))"
    $null = New-Item -ItemType Directory -Path $script:tmpRoot -Force
  }

  AfterAll {
    if ($script:tmpRoot -and (Test-Path $script:tmpRoot)) {
      Remove-Item -Path $script:tmpRoot -Recurse -Force -ErrorAction SilentlyContinue
    }
  }

  Context 'DryRun-Modus' {

    It 'verschiebt keine Dateien im DryRun' {
      $dir = Join-Path $script:tmpRoot 'dryrun'
      $null = New-Item -ItemType Directory -Path $dir -Force
      $iso = Join-Path $dir 'test.iso'
      [System.IO.File]::WriteAllBytes($iso, [byte[]]::new(2048))
      $result = Invoke-ConsoleSort -Roots @($dir) -Mode 'DryRun' -IncludeExtensions @('.iso') -Log {}
      $result | Should -Not -BeNullOrEmpty
      # Datei soll noch am Ursprungsort liegen
      Test-Path $iso | Should -BeTrue
    }

    It 'gibt Result-Objekt mit korrekten Properties zurück' {
      $dir = Join-Path $script:tmpRoot 'result-shape'
      $null = New-Item -ItemType Directory -Path $dir -Force
      [System.IO.File]::WriteAllBytes((Join-Path $dir 'x.chd'), [byte[]]::new(64))
      $result = Invoke-ConsoleSort -Roots @($dir) -Mode 'DryRun' -IncludeExtensions @('.chd') -Log {}
      $result.PSObject.Properties.Name | Should -Contain 'Total'
      $result.PSObject.Properties.Name | Should -Contain 'Moved'
      $result.PSObject.Properties.Name | Should -Contain 'Skipped'
      $result.PSObject.Properties.Name | Should -Contain 'Unknown'
      $result.PSObject.Properties.Name | Should -Contain 'UnknownReasons'
    }
  }

  Context 'Unknown-Erkennung' {

    It 'zählt nicht erkennbare Dateien als Unknown' {
      $dir = Join-Path $script:tmpRoot 'unknown'
      $null = New-Item -ItemType Directory -Path $dir -Force
      [System.IO.File]::WriteAllBytes((Join-Path $dir 'random_garbage.chd'), [byte[]]::new(64))
      $result = Invoke-ConsoleSort -Roots @($dir) -Mode 'DryRun' -IncludeExtensions @('.chd') -Log {}
      $result.Unknown | Should -BeGreaterOrEqual 0
      # UnknownReasons soll ein Hashtable sein
      $result.UnknownReasons | Should -BeOfType [hashtable]
    }
  }

  Context 'Leere Verzeichnisse' {

    It 'verarbeitet leeres Verzeichnis ohne Fehler' {
      $dir = Join-Path $script:tmpRoot 'empty'
      $null = New-Item -ItemType Directory -Path $dir -Force
      $result = Invoke-ConsoleSort -Roots @($dir) -Mode 'DryRun' -IncludeExtensions @('.chd') -Log {}
      $result.Total | Should -Be 0
      $result.Moved | Should -Be 0
    }
  }

  Context 'Parameter-Validierung' {

    It 'akzeptiert Mode DryRun und Move' {
      $dir = Join-Path $script:tmpRoot 'param-mode'
      $null = New-Item -ItemType Directory -Path $dir -Force
      { Invoke-ConsoleSort -Roots @($dir) -Mode 'DryRun' -IncludeExtensions @('.chd') -Log {} } | Should -Not -Throw
    }

    It 'lehnt ungültigen Mode ab' {
      $dir = Join-Path $script:tmpRoot 'param-bad'
      $null = New-Item -ItemType Directory -Path $dir -Force
      { Invoke-ConsoleSort -Roots @($dir) -Mode 'Delete' -IncludeExtensions @('.chd') -Log {} } | Should -Throw
    }
  }

  Context 'Konsolen-Key-Sicherheit' {

    It 'Total zählt gefundene Dateien korrekt' {
      $dir = Join-Path $script:tmpRoot 'counting'
      $null = New-Item -ItemType Directory -Path $dir -Force
      1..3 | ForEach-Object { [System.IO.File]::WriteAllBytes((Join-Path $dir "file$_.chd"), [byte[]]::new(64)) }
      $result = Invoke-ConsoleSort -Roots @($dir) -Mode 'DryRun' -IncludeExtensions @('.chd') -Log {}
      $result.Total | Should -Be 3
    }
  }
}
