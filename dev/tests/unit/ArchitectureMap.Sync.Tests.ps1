#requires -Modules Pester

Describe 'Architecture map sync gate' {
  BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
      $root = Split-Path -Parent $root
    }
    $script:modulesPath = Join-Path $root 'dev\modules'
    $script:architecturePath = Join-Path $root 'docs\ARCHITECTURE_MAP.md'
    if (-not (Test-Path -LiteralPath $script:architecturePath -PathType Leaf)) {
      $fallbackPath = Join-Path $root 'docs\implementation\ARCHITECTURE_MAP.md'
      if (Test-Path -LiteralPath $fallbackPath -PathType Leaf) {
        $script:architecturePath = $fallbackPath
      }
    }

    . (Join-Path $script:modulesPath 'ModuleFileList.ps1')
    $script:allModules = @(Get-RomCleanupModuleFiles -Profile 'all')
    $script:architectureText = Get-Content -LiteralPath $script:architecturePath -Raw -ErrorAction Stop
  }

  It 'ARCHITECTURE_MAP.md enthält jeden ModuleFileList-Eintrag als Code-Token' {
    $missing = New-Object System.Collections.Generic.List[string]
    foreach ($module in $script:allModules) {
      $needle = ('`{0}`' -f $module)
      if ($script:architectureText -notmatch [regex]::Escape($needle)) {
        [void]$missing.Add($module)
      }
    }

    $missing | Should -BeNullOrEmpty -Because ('Diese Module fehlen in der Architektur-Doku: ' + ($missing -join ', '))
  }
}
