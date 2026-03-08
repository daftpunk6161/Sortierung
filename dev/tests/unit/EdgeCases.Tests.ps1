#Requires -Modules Pester
<#
  TEST-EDGE-03/04/06: Edge-Case Tests für Classification und Parsing
  Prüft: Multi-Platform-Signatures, GameKey-Collisions, Malformed CUE/GDI/M3U.
#>

BeforeAll {
  . "$PSScriptRoot/../../modules/RomCleanupLoader.ps1"
}

Describe 'Edge-Case Tests' {

  Context 'TEST-EDGE-03: Multiple Platform-Signaturen – deterministische Auflösung' {

    It 'Get-DiscHeaderConsole ist deterministisch bei gleichen Inputs' {
      if (-not (Get-Command Get-DiscHeaderConsole -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-DiscHeaderConsole nicht verfügbar'
        return
      }
      $tmpFile = Join-Path ([System.IO.Path]::GetTempPath()) "edge_multi_sig.bin"
      try {
        # Leere Datei – nicht erkennbar
        [System.IO.File]::WriteAllBytes($tmpFile, [byte[]]::new(2048))
        $result1 = Get-DiscHeaderConsole -FilePath $tmpFile
        $result2 = Get-DiscHeaderConsole -FilePath $tmpFile
        $result1 | Should -Be $result2 -Because 'gleiche Inputs müssen gleichen Output liefern'
      } finally {
        Remove-Item $tmpFile -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-EDGE-04: GameKey Collisions' {

    It 'unterschiedliche Titel mit sehr ähnlichen Namen erzeugen verschiedene Keys' {
      if (-not (Get-Command ConvertTo-GameKey -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'ConvertTo-GameKey nicht verfügbar'
        return
      }
      $key1 = ConvertTo-GameKey -BaseName 'Final Fantasy VII (Europe) (Disc 1)'
      $key2 = ConvertTo-GameKey -BaseName 'Final Fantasy VIII (Europe) (Disc 1)'
      $key1 | Should -Not -Be $key2 -Because 'VII und VIII sind verschiedene Spiele'
    }

    It 'gleicher Titel verschiedene Regionen → gleicher GameKey' {
      if (-not (Get-Command ConvertTo-GameKey -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'ConvertTo-GameKey nicht verfügbar'
        return
      }
      $keyEU = ConvertTo-GameKey -BaseName 'Crash Bandicoot (Europe)'
      $keyUS = ConvertTo-GameKey -BaseName 'Crash Bandicoot (USA)'
      $keyEU | Should -Be $keyUS -Because 'Region-Tags werden entfernt'
    }
  }

  Context 'TEST-EDGE-06: Malformed CUE/GDI/M3U Files' {

    BeforeAll {
      $script:tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "edge_malformed_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      $null = New-Item -ItemType Directory -Path $script:tmpDir -Force
    }

    AfterAll {
      if ($script:tmpDir -and (Test-Path $script:tmpDir)) {
        Remove-Item $script:tmpDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }

    It 'leere CUE-Datei wirft keine Exception bei Get-CueRelatedFiles' {
      if (-not (Get-Command Get-CueRelatedFiles -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-CueRelatedFiles nicht verfügbar'
        return
      }
      $cue = Join-Path $script:tmpDir 'empty.cue'
      '' | Set-Content -LiteralPath $cue -Encoding UTF8
      { Get-CueRelatedFiles -CuePath $cue } | Should -Not -Throw
    }

    It 'CUE mit fehlender Track-Referenz behandelt graceful' {
      if (-not (Get-Command Get-CueRelatedFiles -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-CueRelatedFiles nicht verfügbar'
        return
      }
      $cue = Join-Path $script:tmpDir 'broken.cue'
      'FILE "nonexistent_track.bin" BINARY' | Set-Content -LiteralPath $cue -Encoding UTF8
      { Get-CueRelatedFiles -CuePath $cue } | Should -Not -Throw
    }

    It 'M3U mit Kommentaren und Leerzeilen wirft nicht' {
      if (-not (Get-Command Get-M3URelatedFiles -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-M3URelatedFiles nicht verfügbar'
        return
      }
      $m3u = Join-Path $script:tmpDir 'comments.m3u'
      @('# This is a comment', '', '  ', '# Another comment') | Set-Content -LiteralPath $m3u -Encoding UTF8
      { Get-M3URelatedFiles -M3UPath $m3u } | Should -Not -Throw
    }
  }
}
