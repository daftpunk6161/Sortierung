# DatSources.Tests.ps1  –  Unit tests for DatSources.ps1 module
# Tests catalog, inventory, download, and utility functions.

BeforeAll {
  $projectRoot = Resolve-Path (Join-Path $PSScriptRoot '..\..')
  . (Join-Path $projectRoot 'dev\modules\FileOps.ps1')
  . (Join-Path $projectRoot 'dev\modules\DatSources.ps1')
}

Describe 'Get-BuiltinDatCatalog' {
  It 'returns a non-empty array of catalog entries' {
    $catalog = Get-BuiltinDatCatalog
    $catalog.Count | Should -BeGreaterThan 30
  }

  It 'each entry has required keys' {
    $catalog = Get-BuiltinDatCatalog
    foreach ($entry in $catalog) {
      $entry.Id         | Should -Not -BeNullOrEmpty
      $entry.Group      | Should -Not -BeNullOrEmpty
      $entry.System     | Should -Not -BeNullOrEmpty
      $entry.Format     | Should -BeIn @('zip-dat','raw-dat','nointro-pack','7z-dat')
      $entry.ConsoleKey | Should -Not -BeNullOrEmpty
    }
  }

  It 'has unique IDs' {
    $catalog = Get-BuiltinDatCatalog
    $ids = $catalog | ForEach-Object { $_.Id }
    $ids.Count | Should -Be ($ids | Select-Object -Unique).Count
  }

  It 'includes Redump entries with URLs' {
    $catalog = Get-BuiltinDatCatalog
    $redump = $catalog | Where-Object { $_.Group -eq 'Redump' }
    $redump.Count | Should -BeGreaterThan 10
    foreach ($r in $redump) {
      $r.Url | Should -Not -BeNullOrEmpty
      $r.Format | Should -Be 'zip-dat'
    }
  }

  It 'includes No-Intro entries with nointro-pack format and PackMatch' {
    $catalog = Get-BuiltinDatCatalog
    $nointro = $catalog | Where-Object { $_.Group -eq 'No-Intro' }
    $nointro.Count | Should -BeGreaterThan 5
    foreach ($n in $nointro) {
      $n.Format | Should -Be 'nointro-pack'
      $n.PackMatch | Should -Not -BeNullOrEmpty
    }
  }

  It 'includes FBNEO entries with GitHub URLs' {
    $catalog = Get-BuiltinDatCatalog
    $fbneo = $catalog | Where-Object { $_.Group -eq 'FBNEO' }
    $fbneo.Count | Should -BeGreaterThan 5
    foreach ($f in $fbneo) {
      $f.Url | Should -Match 'github'
    }
  }
}

Describe 'Get-DatCatalogView' {
  It 'returns merged view with Status column' {
    $view = Get-DatCatalogView -Inventory @{}
    $view.Count | Should -BeGreaterThan 0
    $view[0].Status | Should -Not -BeNullOrEmpty
  }

  It 'marks entries as Available when not installed' {
    $view = Get-DatCatalogView -Inventory @{}
    $redump = $view | Where-Object { $_.Group -eq 'Redump' }
    foreach ($r in $redump) {
      $r.Status | Should -Be 'Available'
    }
  }

  It 'marks entries as Installed when in inventory' {
    $inv = @{
      'redump-ps1' = @{
        localPath    = 'C:\DATs\Redump\Sony - PlayStation.dat'
        downloadDate = '2026-02-20T10:00:00Z'
        fileSize     = 123456
        fileHash     = 'ABC123'
      }
    }
    $view = Get-DatCatalogView -Inventory $inv
    $ps1 = $view | Where-Object { $_.Id -eq 'redump-ps1' }
    $ps1.Status | Should -Be 'Installed'
    $ps1.LocalPath | Should -Be 'C:\DATs\Redump\Sony - PlayStation.dat'
    $ps1.InstalledSize | Should -Be 123456
  }

  It 'marks No-Intro nointro-pack as Available (not CustomURL)' {
    $view = Get-DatCatalogView -Inventory @{}
    $nointro = $view | Where-Object { $_.Group -eq 'No-Intro' }
    foreach ($n in $nointro) {
      $n.Status | Should -Be 'Available'
      $n.PackMatch | Should -Not -BeNullOrEmpty
    }
  }

  It 'supports custom sources as hashtable without PackMatch key' {
    $baseCount = @(Get-DatCatalogView -Inventory @{}).Count
    $custom = @{
      Id = 'custom-psobj'
      Group = 'Custom'
      System = 'PSObj Console'
      Url = 'https://example.org/test.dat'
      Format = 'raw-dat'
      ConsoleKey = 'PSOBJ'
    }

    { Get-DatCatalogView -Inventory @{} -CustomSources @($custom) | Out-Null } | Should -Not -Throw
    $view = @(Get-DatCatalogView -Inventory @{} -CustomSources @($custom))
    @($view).Count | Should -BeGreaterThan $baseCount
  }
}

Describe 'Inventory persistence' {
  BeforeAll {
    $script:origInvPath = $script:DAT_INVENTORY_PATH
    $script:DAT_INVENTORY_PATH = Join-Path $env:TEMP "test-dat-inventory-$(Get-Random).json"
  }
  AfterAll {
    if (Test-Path -LiteralPath $script:DAT_INVENTORY_PATH) {
      Remove-Item -LiteralPath $script:DAT_INVENTORY_PATH -Force -ErrorAction SilentlyContinue
    }
    $script:DAT_INVENTORY_PATH = $script:origInvPath
  }

  It 'returns empty hashtable when file does not exist' {
    if (Test-Path -LiteralPath $script:DAT_INVENTORY_PATH) {
      Remove-Item -LiteralPath $script:DAT_INVENTORY_PATH -Force
    }
    $inv = Get-DatInventory
    $inv | Should -BeOfType [hashtable]
    $inv.Count | Should -Be 0
  }

  It 'round-trips save and load' {
    $inv = @{
      'redump-dc' = @{
        localPath = 'C:\DATs\Redump\dc.dat'
        downloadDate = '2026-02-20T12:00:00'
        fileSize = 999
        fileHash = 'DEADBEEF'
      }
    }
    Save-DatInventory -Inventory $inv
    $loaded = Get-DatInventory
    $loaded.Count | Should -Be 1
    $loaded['redump-dc'].localPath | Should -Be 'C:\DATs\Redump\dc.dat'
    $loaded['redump-dc'].fileHash | Should -Be 'DEADBEEF'
  }
}

Describe 'Format-DatFileSize' {
  It 'formats bytes' {
    Format-DatFileSize -Bytes 500 | Should -Be '500 B'
  }

  It 'formats KB' {
    Format-DatFileSize -Bytes 2048 | Should -Be '2.0 KB'
  }

  It 'formats MB' {
    Format-DatFileSize -Bytes (5 * 1MB) | Should -Be '5.0 MB'
  }

  It 'formats GB' {
    Format-DatFileSize -Bytes (2.5 * 1GB) | Should -Be '2.50 GB'
  }
}

Describe 'Test-DownloadedDatSignature' {
  It 'returns true when ExpectedSha256 matches local file hash' {
    $tempFile = Join-Path $env:TEMP ("datsig-{0}.txt" -f [guid]::NewGuid().ToString('N'))
    try {
      'abc123' | Out-File -LiteralPath $tempFile -Encoding ascii -Force
      $expected = [string](Get-FileHash -LiteralPath $tempFile -Algorithm SHA256).Hash
      Test-DownloadedDatSignature -SourceUrl 'https://example.com/file.dat' -LocalPath $tempFile -ExpectedSha256 $expected | Should -BeTrue
    } finally {
      if (Test-Path -LiteralPath $tempFile) {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
      }
    }
  }

  It 'returns false when ExpectedSha256 does not match local file hash' {
    $tempFile = Join-Path $env:TEMP ("datsig-{0}.txt" -f [guid]::NewGuid().ToString('N'))
    try {
      'abc123' | Out-File -LiteralPath $tempFile -Encoding ascii -Force
      Test-DownloadedDatSignature -SourceUrl 'https://example.com/file.dat' -LocalPath $tempFile -ExpectedSha256 ('0' * 64) | Should -BeFalse
    } finally {
      if (Test-Path -LiteralPath $tempFile) {
        Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
      }
    }
  }
}

Describe 'Invoke-DatDownload' {
  It 'returns error when URL is empty' {
    $result = Invoke-DatDownload -Id 'test' -Url '' -Format 'raw-dat' -TargetDir $env:TEMP -System 'Test'
    $result.Success | Should -Be $false
    $result.Error | Should -Match 'Keine URL'
  }

  It 'returns error for unknown format' {
    $result = Invoke-DatDownload -Id 'test' -Url 'https://example.com' -Format 'unknown' -TargetDir $env:TEMP -System 'Test'
    $result.Success | Should -Be $false
    $result.Error | Should -Match 'Unbekanntes Format'
  }

  It 'returns error for nointro-pack without PackMatch' {
    $result = Invoke-DatDownload -Id 'test' -Url '' -Format 'nointro-pack' -TargetDir $env:TEMP -System 'Test'
    $result.Success | Should -Be $false
    $result.Error | Should -Match 'PackMatch'
  }

  It 'returns error for empty URL when not nointro-pack' {
    $result = Invoke-DatDownload -Id 'test' -Url '' -Format 'zip-dat' -TargetDir $env:TEMP -System 'Test'
    $result.Success | Should -Be $false
    $result.Error | Should -Match 'Keine URL'
  }
}

Describe 'Install-DatFromCatalog' {
  It 'restores PackMatch by Id for nointro-pack entries when selection omits it' {
    Mock -CommandName Invoke-DatDownload -MockWith {
      param([string]$Id,[string]$Url,[string]$Format,[string]$TargetDir,[string]$System,[string]$PackMatch,[scriptblock]$Log)
      return [pscustomobject]@{ Success = $true; LocalPath = 'C:\tmp\nointro-ok.dat'; FileSize = 1; FileHash = 'ABC'; Error = '' }
    }
    Mock -CommandName Save-DatInventory -MockWith { param([hashtable]$Inventory) }

    $entry = [pscustomobject]@{
      Id = 'nointro-nes'
      Group = 'No-Intro'
      System = 'Nintendo - NES'
      Url = ''
      Format = 'nointro-pack'
      ConsoleKey = 'NES'
    }

    $datRoot = Join-Path $env:TEMP ("test-dat-root-" + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $datRoot -Force)
    $inv = @{}
    $ok = Install-DatFromCatalog -CatalogEntry $entry -DatRoot $datRoot -Inventory $inv

    $ok | Should -Be $true
    Assert-MockCalled Invoke-DatDownload -Times 1 -Exactly -ParameterFilter { $Id -eq 'nointro-nes' -and $Format -eq 'nointro-pack' -and -not [string]::IsNullOrWhiteSpace($PackMatch) }

    Remove-Item -LiteralPath $datRoot -Recurse -Force -ErrorAction SilentlyContinue
  }

  It 'does not require PackMatch for non-nointro entries' {
    Mock -CommandName Invoke-DatDownload -MockWith {
      param([string]$Id,[string]$Url,[string]$Format,[string]$TargetDir,[string]$System,[string]$PackMatch,[scriptblock]$Log)
      return [pscustomobject]@{ Success = $true; LocalPath = 'C:\tmp\ok.dat'; FileSize = 1; FileHash = 'ABC'; Error = '' }
    }
    Mock -CommandName Save-DatInventory -MockWith { param([hashtable]$Inventory) }

    $entry = [pscustomobject]@{
      Id = 'redump-ps1'
      Group = 'Redump'
      System = 'Sony - PlayStation'
      Url = 'http://redump.org/datfile/psx/'
      Format = 'zip-dat'
      ConsoleKey = 'PSX'
    }

    $datRoot = Join-Path $env:TEMP ("test-dat-root-" + [guid]::NewGuid().ToString('N'))
    [void](New-Item -ItemType Directory -Path $datRoot -Force)
    $inv = @{}
    $ok = $false
    { $ok = Install-DatFromCatalog -CatalogEntry $entry -DatRoot $datRoot -Inventory $inv } | Should -Not -Throw
    $ok | Should -BeOfType [bool]
    Assert-MockCalled Invoke-DatDownload -Times 1 -Exactly -ParameterFilter { $PackMatch -eq '' }

    Remove-Item -LiteralPath $datRoot -Recurse -Force -ErrorAction SilentlyContinue
  }
}

Describe 'Add-CustomDatSource' {
  BeforeAll {
    $script:origInvPath2 = $script:DAT_INVENTORY_PATH
    $script:DAT_INVENTORY_PATH = Join-Path $env:TEMP "test-dat-inventory-custom-$(Get-Random).json"
  }
  AfterAll {
    if (Test-Path -LiteralPath $script:DAT_INVENTORY_PATH) {
      Remove-Item -LiteralPath $script:DAT_INVENTORY_PATH -Force -ErrorAction SilentlyContinue
    }
    $script:DAT_INVENTORY_PATH = $script:origInvPath2
  }

  It 'creates an ID and stores in inventory' {
    $inv = @{}
    $id = Add-CustomDatSource -Group 'Lokal' -System 'Test Console' -ConsoleKey 'TEST' -Inventory $inv
    $id | Should -Match '^custom-'
    $inv[$id] | Should -Not -BeNull
    $inv[$id].group | Should -Be 'Lokal'
    $inv[$id].system | Should -Be 'Test Console'
    $inv[$id].consoleKey | Should -Be 'TEST'
  }
}

Describe 'Remove-DatFromInventory' {
  BeforeAll {
    $script:origInvPath3 = $script:DAT_INVENTORY_PATH
    $script:DAT_INVENTORY_PATH = Join-Path $env:TEMP "test-dat-inventory-remove-$(Get-Random).json"
  }
  AfterAll {
    if (Test-Path -LiteralPath $script:DAT_INVENTORY_PATH) {
      Remove-Item -LiteralPath $script:DAT_INVENTORY_PATH -Force -ErrorAction SilentlyContinue
    }
    $script:DAT_INVENTORY_PATH = $script:origInvPath3
  }

  It 'removes entry from inventory' {
    $inv = @{
      'test-1' = @{ localPath = ''; system = 'X' }
      'test-2' = @{ localPath = ''; system = 'Y' }
    }
    Remove-DatFromInventory -Id 'test-1' -Inventory $inv
    $inv.ContainsKey('test-1') | Should -Be $false
    $inv.ContainsKey('test-2') | Should -Be $true
  }
}
