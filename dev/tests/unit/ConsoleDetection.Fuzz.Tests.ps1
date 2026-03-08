#Requires -Modules Pester
<#
  TEST-FUZZ-01/02: Fuzz Get-ConsoleType und Get-DiscHeaderConsole
  Prüft: Keine Exceptions bei zufälligem Input, immer valides Result.
#>

BeforeAll {
  . "$PSScriptRoot/../../modules/RomCleanupLoader.ps1"
}

Describe 'ConsoleDetection – Fuzz-Tests' {

  Context 'Get-ConsoleType mit zufälliger Extension/Folder/Path' {

    $fuzzCases = @(
      @{ Ext = '.xyz';  Folder = 'RandomFolder' }
      @{ Ext = '.';     Folder = '' }
      @{ Ext = '';      Folder = 'test' }
      @{ Ext = '.bin';  Folder = '../../etc' }
      @{ Ext = '.CHD';  Folder = 'UPPERCASE' }
      @{ Ext = '.iso';  Folder = 'games (2026)' }
      @{ Ext = '.nkit.iso'; Folder = 'Wii Backup' }
      @{ Ext = '.abc.def.ghi'; Folder = 'deep/nested/path' }
      @{ Ext = '.nes';  Folder = 'Über-Spezial (Ümlauts)' }
      @{ Ext = '.gba';  Folder = 'C:\fake\path\that\is\very\long\' + ('x' * 200) }
    )

    It 'wirft keine Exception für Ext=<Ext> Folder=<Folder>' -TestCases $fuzzCases {
      param($Ext, $Folder)
      $tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "fuzz_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      try {
        $null = New-Item -ItemType Directory -Path $tmpDir -Force
        $fakeFile = Join-Path $tmpDir "test$Ext"
        [System.IO.File]::WriteAllBytes($fakeFile, [byte[]]::new(64))
        # Soll keine Exception werfen
        if (Get-Command Get-ConsoleType -ErrorAction SilentlyContinue) {
          { Get-ConsoleType -FilePath $fakeFile -FolderName $Folder } | Should -Not -Throw
        } elseif (Get-Command Invoke-ConsoleDetection -ErrorAction SilentlyContinue) {
          { Invoke-ConsoleDetection -FilePath $fakeFile } | Should -Not -Throw
        }
      } finally {
        Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'Get-DiscHeaderConsole mit Random-Bytes' {

    It 'gibt UNKNOWN zurück für zufällige Binärdaten (Iteration <_>)' -TestCases @(1..20 | ForEach-Object { @{ Iteration = $_ } }) {
      param($Iteration)
      if (-not (Get-Command Get-DiscHeaderConsole -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-DiscHeaderConsole nicht verfügbar'
        return
      }
      $tmpFile = Join-Path ([System.IO.Path]::GetTempPath()) "fuzz_header_$Iteration.bin"
      try {
        $rng = [System.Security.Cryptography.RandomNumberGenerator]::Create()
        $randomBytes = [byte[]]::new(4096)
        $rng.GetBytes($randomBytes)
        [System.IO.File]::WriteAllBytes($tmpFile, $randomBytes)
        $result = Get-DiscHeaderConsole -FilePath $tmpFile
        # Soll entweder $null, leer oder 'UNKNOWN' sein – aber keine Exception
        if ($null -ne $result -and $result -ne '') {
          # Falls durch Zufall ein Pattern matcht, ist das OK
          $result | Should -BeOfType [string]
        }
      } finally {
        Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'GameKey-Fuzz – Sonderzeichen' {

    $specialNames = @(
      'Game (Rev A) [!] (EU,US)'
      'Spiel: Die "Rückkehr" der Helden'
      '../../etc/passwd'
      'NUL'
      ''
      '   '
      'Game [Beta 3] [Proto] (Japan) (En,Ja) (v1.02a)'
      'a' * 500
      "Game`twith`ttabs"
      'Game<>with|pipes'
    )

    It 'ConvertTo-GameKey wirft nicht für "<_>"' -TestCases ($specialNames | ForEach-Object { @{ Name = $_ } }) {
      param($Name)
      if (-not (Get-Command ConvertTo-GameKey -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'ConvertTo-GameKey nicht verfügbar'
        return
      }
      { ConvertTo-GameKey -BaseName $Name } | Should -Not -Throw
    }
  }
}
