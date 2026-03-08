#Requires -Modules Pester
<#
  TEST-MOD-03: ConfigProfiles.ps1 — Tests für Profile CRUD
  Testet Create/Load/Delete/Import/Export.
#>

BeforeAll {
  . "$PSScriptRoot/../../modules/RomCleanupLoader.ps1"
}

Describe 'ConfigProfiles – CRUD-Operationen' {

  BeforeAll {
    $script:tmpDir = Join-Path ([System.IO.Path]::GetTempPath()) "ProfilesTest_$([guid]::NewGuid().ToString('N').Substring(0,8))"
    $null = New-Item -ItemType Directory -Path $script:tmpDir -Force
    $script:storePath = Join-Path $script:tmpDir 'profiles.json'
  }

  AfterAll {
    if ($script:tmpDir -and (Test-Path $script:tmpDir)) {
      Remove-Item -Path $script:tmpDir -Recurse -Force -ErrorAction SilentlyContinue
    }
  }

  Context 'Get-ConfigProfiles' {

    It 'liefert leeres Hashtable wenn Datei nicht existiert' {
      $nonExistent = Join-Path $script:tmpDir 'nope.json'
      $profiles = Get-ConfigProfiles -StorePath $nonExistent
      $profiles | Should -BeOfType [hashtable]
      $profiles.Count | Should -Be 0
    }
  }

  Context 'Set-ConfigProfile (Create/Update)' {

    It 'erstellt neues Profil' {
      $settings = @{ mode = 'DryRun'; logLevel = 'Info' }
      $result = Set-ConfigProfile -Name 'TestProfile' -Settings $settings -StorePath $script:storePath
      $result | Should -Not -BeNullOrEmpty
      # Profil sollte nun in der Datei stehen
      $profiles = Get-ConfigProfiles -StorePath $script:storePath
      $profiles.ContainsKey('TestProfile') | Should -BeTrue
    }

    It 'aktualisiert bestehendes Profil' {
      $settings = @{ mode = 'Move'; logLevel = 'Debug' }
      Set-ConfigProfile -Name 'TestProfile' -Settings $settings -StorePath $script:storePath
      $profiles = Get-ConfigProfiles -StorePath $script:storePath
      $profiles['TestProfile'].mode | Should -Be 'Move'
    }

    It 'lehnt leeren Profilnamen ab' {
      { Set-ConfigProfile -Name '   ' -Settings @{ mode = 'DryRun' } -StorePath $script:storePath } | Should -Throw
    }
  }

  Context 'Remove-ConfigProfile (Delete)' {

    It 'löscht bestehendes Profil' {
      Set-ConfigProfile -Name 'ToDelete' -Settings @{ mode = 'DryRun' } -StorePath $script:storePath
      $result = Remove-ConfigProfile -Name 'ToDelete' -StorePath $script:storePath
      $result | Should -BeTrue
      $profiles = Get-ConfigProfiles -StorePath $script:storePath
      $profiles.ContainsKey('ToDelete') | Should -BeFalse
    }

    It 'gibt false zurück für nicht-existierendes Profil' {
      $result = Remove-ConfigProfile -Name 'DoesNotExist' -StorePath $script:storePath
      $result | Should -BeFalse
    }
  }

  Context 'Export-ConfigProfile' {

    It 'exportiert Profil als JSON-Datei' {
      Set-ConfigProfile -Name 'ExportMe' -Settings @{ mode = 'Move'; logLevel = 'Warning' } -StorePath $script:storePath
      $exportPath = Join-Path $script:tmpDir 'exported.json'
      Export-ConfigProfile -Name 'ExportMe' -Path $exportPath -StorePath $script:storePath
      Test-Path $exportPath | Should -BeTrue
      $exported = Get-Content -LiteralPath $exportPath -Raw | ConvertFrom-Json
      $exported.name | Should -Be 'ExportMe'
    }

    It 'wirft Fehler für nicht-existierendes Profil' {
      $path = Join-Path $script:tmpDir 'fail-export.json'
      { Export-ConfigProfile -Name 'GhostProfile' -Path $path -StorePath $script:storePath } | Should -Throw
    }
  }

  Context 'Import-ConfigProfile' {

    It 'importiert exportiertes Profil' {
      # Setup: Exportiere
      $importStore = Join-Path $script:tmpDir 'import-store.json'
      Set-ConfigProfile -Name 'ImportSource' -Settings @{ mode = 'DryRun' } -StorePath $script:storePath
      $exportFile = Join-Path $script:tmpDir 'for-import.json'
      Export-ConfigProfile -Name 'ImportSource' -Path $exportFile -StorePath $script:storePath

      # Import in neuen Store
      $imported = Import-ConfigProfile -Path $exportFile -StorePath $importStore
      $imported | Should -Not -BeNullOrEmpty
    }
  }
}
