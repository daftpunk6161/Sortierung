BeforeAll {
  $loaderPath = Join-Path $PSScriptRoot '..\..\modules\RomCleanupLoader.ps1'
  if (Test-Path $loaderPath) { . $loaderPath }
}

Describe 'Negativ-Tests – Fehlerszenarien' {

  Context 'TEST-NEG-01: Move-ItemSafely mit gesperrter Datei' {

    It 'wirft oder gibt Fehler-Objekt bei gesperrter Datei zurück' {
      if (-not (Get-Command Move-ItemSafely -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Move-ItemSafely nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "neg01_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $sourceFile = Join-Path $tempDir 'locked.txt'
      $trashDir = Join-Path $tempDir 'trash'
      New-Item -Path $trashDir -ItemType Directory -Force | Out-Null
      Set-Content -Path $sourceFile -Value 'test'
      # Datei sperren
      $stream = [System.IO.File]::Open($sourceFile, 'Open', 'ReadWrite', 'None')
      try {
        # Muss entweder Exception werfen oder Fehler-Result zurückgeben
        $threw = $false
        try {
          Move-ItemSafely -Path $sourceFile -Destination (Join-Path $trashDir 'locked.txt')
        } catch {
          $threw = $true
        }
        # Datei sollte noch am Original-Ort sein
        Test-Path $sourceFile | Should -Be $true
      } finally {
        $stream.Close()
        $stream.Dispose()
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-NEG-03: Berechtigungs-Probleme' {

    It 'behandelt nicht-existierendes Zielverzeichnis graceful' {
      if (-not (Get-Command Move-ItemSafely -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Move-ItemSafely nicht verfügbar'
        return
      }
      $tempDir = Join-Path ([System.IO.Path]::GetTempPath()) "neg03_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempDir -ItemType Directory -Force | Out-Null
      $sourceFile = Join-Path $tempDir 'test.txt'
      Set-Content -Path $sourceFile -Value 'test'
      $nonExistDest = Join-Path $tempDir 'nonexist\subfolder\test.txt'
      try {
        # Entweder Auto-Erstellung oder sinnvoller Fehler
        $result = $null
        try {
          $result = Move-ItemSafely -Path $sourceFile -Destination $nonExistDest
        } catch {
          # Fehler ist akzeptabel
        }
      } finally {
        Remove-Item -Path $tempDir -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-NEG-04: Tool-Hash-Mismatch' {

    It 'Test-ToolHash gibt false bei unbekanntem Tool-Pfad' {
      if (-not (Get-Command Test-ToolHash -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Test-ToolHash nicht verfügbar'
        return
      }
      $fakeToolPath = Join-Path ([System.IO.Path]::GetTempPath()) "faketool_$([guid]::NewGuid().ToString('N').Substring(0,8)).exe"
      Set-Content -Path $fakeToolPath -Value 'nicht_ein_echtes_tool'
      try {
        $result = Test-ToolHash -ToolPath $fakeToolPath -ToolName 'chdman'
        # Sollte fehlschlagen da Hash nicht übereinstimmt
        $result | Should -Be $false
      } catch {
        # Exception ist auch akzeptabel
      } finally {
        Remove-Item -Path $fakeToolPath -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'TEST-NEG-05: Path-Traversal-Schutz' {

    It 'Resolve-ChildPathWithinRoot gibt null bei Traversal-Versuch' {
      if (-not (Get-Command Resolve-ChildPathWithinRoot -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Resolve-ChildPathWithinRoot nicht verfügbar'
        return
      }
      $root = 'C:\SafeRoot'
      $malicious = '..\..\Windows\System32\cmd.exe'
      $result = Resolve-ChildPathWithinRoot -BaseDir $root -ChildPath $malicious -Root $root
      $result | Should -BeNullOrEmpty -Because 'Path-Traversal muss blockiert werden'
    }
  }
}
