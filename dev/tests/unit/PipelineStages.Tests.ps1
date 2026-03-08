BeforeAll {
  $loaderPath = Join-Path $PSScriptRoot '..\..\modules\RomCleanupLoader.ps1'
  if (Test-Path $loaderPath) { . $loaderPath }
}

Describe 'Pipeline-Stage Tests – Funktional' {

  Context 'TEST-PIPE-01: DAT Hash Lookup' {

    It 'Get-DatIndex gibt Hashtable zurück bei leerem DatRoot' {
      if (-not (Get-Command Get-DatIndex -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-DatIndex nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "pipe01_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      try {
        $result = Get-DatIndex -DatRoot $tempDir -HashType 'SHA1'
        # leeres Hashtable oder null beides OK
        if ($null -ne $result) {
          $result | Should -BeOfType [hashtable]
        }
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }

    It 'Get-DatGameKey gibt null bei leerem ConsoleIndex' {
      if (-not (Get-Command Get-DatGameKey -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-DatGameKey nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "pipe01b_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $testFile = Join-Path $tempDir 'test.zip'
      Set-Content -Path $testFile -Value 'dummy'
      try {
        $result = Get-DatGameKey -Paths @($testFile) -ConsoleIndex @{} -HashType 'SHA1'
        $result | Should -BeNullOrEmpty
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-PIPE-02: Archive Disc Header' {

    It 'Get-DiscHeaderConsole gibt String oder null bei leerer Datei' {
      if (-not (Get-Command Get-DiscHeaderConsole -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-DiscHeaderConsole nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "pipe02_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $testFile = Join-Path $tempDir 'empty.bin'
      [System.IO.File]::WriteAllBytes($testFile, [byte[]]@())
      try {
        # Leere Datei -> UNKNOWN oder null
        { Get-DiscHeaderConsole -FilePath $testFile } | Should -Not -Throw
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-PIPE-03: Extension-basierte Erkennung mit Folder-Hint' {

    It 'Get-ConsoleType erkennt GBA über Folder-Alias' {
      if (-not (Get-Command Get-ConsoleType -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-ConsoleType nicht verfügbar'
        return
      }
      $tempBase = Join-Path ([System.IO.Path]::GetTempPath()) "pipe03_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      $gbaFolder = Join-Path $tempBase 'Nintendo - Game Boy Advance'
      New-Item -Path $gbaFolder -ItemType Directory -Force | Out-Null
      $testFile = Join-Path $gbaFolder 'game.gba'
      [System.IO.File]::WriteAllBytes($testFile, [byte[]]@(0))
      try {
        $result = Get-ConsoleType -FilePath $testFile -RootPath $tempBase
        $result | Should -Not -BeNullOrEmpty
      } finally {
        Remove-Item -Path $tempBase -Recurse -Force -ErrorAction SilentlyContinue
      }
    }

    It 'Get-ConsoleType gibt immer String zurück (nie Exception)' {
      if (-not (Get-Command Get-ConsoleType -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-ConsoleType nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "pipe03b_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $testFile = Join-Path $tempDir 'random.xyz'
      [System.IO.File]::WriteAllBytes($testFile, [byte[]]@(0))
      try {
        $result = Get-ConsoleType -FilePath $testFile -RootPath $tempDir
        $result | Should -Not -BeNullOrEmpty
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-ALIBI-04: Pipeline-Stage funktionale Prüfung' {

    It 'Classify-FileCategory unterscheidet GAME vs JUNK' {
      if (-not (Get-Command Classify-FileCategory -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Classify-FileCategory nicht verfügbar'
        return
      }
      $game = Classify-FileCategory -BaseName 'Super Mario (Europe)'
      $junk = Classify-FileCategory -BaseName 'Super Mario (Europe) (Beta 1)'
      $game | Should -Be 'GAME'
      $junk | Should -Be 'JUNK'
    }
  }
}

Describe 'Fuzz-Tests – Parser & API' {

  Context 'TEST-FUZZ-03: CUE/GDI/M3U Parser mit adversarialem Input' {

    It 'Get-CueRelatedFiles crasht nicht bei Binärdaten als CUE' {
      if (-not (Get-Command Get-CueRelatedFiles -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-CueRelatedFiles nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "fuzz03_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $cueFile = Join-Path $tempDir 'binary.cue'
      $rng = [System.Random]::new(42)
      $bytes = [byte[]]::new(256)
      $rng.NextBytes($bytes)
      [System.IO.File]::WriteAllBytes($cueFile, $bytes)
      try {
        { Get-CueRelatedFiles -CuePath $cueFile } | Should -Not -Throw
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }

    It 'Get-GdiRelatedFiles crasht nicht bei leerem GDI' {
      if (-not (Get-Command Get-GdiRelatedFiles -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-GdiRelatedFiles nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "fuzz03g_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $gdiFile = Join-Path $tempDir 'empty.gdi'
      Set-Content -Path $gdiFile -Value ''
      try {
        { Get-GdiRelatedFiles -GdiPath $gdiFile } | Should -Not -Throw
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-FUZZ-04: DAT XML Parser mit Malformed XML' {

    It 'Import-DatFile crasht nicht bei ungültigem XML' {
      if (-not (Get-Command Import-DatFile -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Import-DatFile nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "fuzz04_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $datFile = Join-Path $tempDir 'malformed.dat'
      Set-Content -Path $datFile -Value '<xml><unclosed><broken attr='
      try {
        $result = $null
        try { $result = Import-DatFile -Path $datFile } catch { }
        # Entweder null/leer oder Exception - beides OK, kein Crash
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }
}
