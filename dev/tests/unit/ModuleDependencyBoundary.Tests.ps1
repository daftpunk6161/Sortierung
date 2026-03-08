<#
.SYNOPSIS
  Tests for module dependency boundaries as defined in ARCHITECTURE_MAP.md (Section 5).
  Ensures no forbidden cross-layer references exist in the codebase.
#>

BeforeAll {
  $script:modulesPath = Join-Path $PSScriptRoot '../../modules'

  # UI symbol patterns that domain modules must NOT reference
  $script:uiSymbolPatterns = @(
    'WpfSlice\.',
    'WpfEventHandler',
    'WpfHost',
    'WpfApp\b',
    'WpfMainViewModel',
    'SimpleSort\.WpfMain',
    'Start-WpfGui',
    'Register-WpfEventHandlers',
    'WpfSelectionConfig'
  )

  function Test-ModuleHasNoUiReferences {
    param([string]$ModuleName)
    $path = Join-Path $script:modulesPath $ModuleName
    if (-not (Test-Path $path)) { return @() }
    $content = Get-Content -Path $path -Raw -ErrorAction SilentlyContinue
    if (-not $content) { return @() }

    $violations = @()
    foreach ($pattern in $script:uiSymbolPatterns) {
      $lines = ($content -split "`n") | Where-Object {
        $_ -match $pattern -and $_ -notmatch '^\s*#' -and $_ -notmatch '^\s*<#'
      }
      if ($lines) { $violations += $lines }
    }
    return $violations
  }

  function Test-ModuleHasNoApiReferences {
    param([string]$ModuleName)
    $path = Join-Path $script:modulesPath $ModuleName
    if (-not (Test-Path $path)) { return @() }
    $content = Get-Content -Path $path -Raw -ErrorAction SilentlyContinue
    if (-not $content) { return @() }

    $lines = ($content -split "`n") | Where-Object {
      $_ -match 'Start-RomCleanupApiServer' -and
      $_ -notmatch '^\s*#' -and $_ -notmatch '^\s*<#'
    }
    return @($lines)
  }
}

Describe 'Module Dependency Boundaries' {

  Context 'NO-UI-IN-DOMAIN: Domain module <_> has no UI references' -ForEach @(
    'Core.ps1', 'Dedupe.ps1', 'Convert.ps1', 'Dat.ps1', 'DatSources.ps1',
    'Classification.ps1', 'FormatScoring.ps1', 'SetParsing.ps1', 'Sets.ps1',
    'Ps3Dedupe.ps1', 'ZipSort.ps1', 'ConsoleSort.ps1', 'ConsolePlugins.ps1'
  ) {
    It '<_> has no UI references' {
      $violations = Test-ModuleHasNoUiReferences -ModuleName $_
      $violations | Should -BeNullOrEmpty -Because "Domain module '$_' must not reference UI modules"
    }
  }

  Context 'NO-CROSSADAPTER: WPF module <_> does not reference ApiServer' -ForEach @(
    'WpfApp.ps1', 'WpfHost.ps1', 'WpfXaml.ps1', 'WpfMainViewModel.ps1',
    'WpfEventHandlers.ps1', 'WpfSelectionConfig.ps1',
    'SimpleSort.WpfMain.ps1',
    'WpfSlice.Roots.ps1', 'WpfSlice.RunControl.ps1', 'WpfSlice.Settings.ps1',
    'WpfSlice.DatMapping.ps1', 'WpfSlice.ReportPreview.ps1', 'WpfSlice.AdvancedFeatures.ps1'
  ) {
    It '<_> does not reference ApiServer' {
      $violations = Test-ModuleHasNoApiReferences -ModuleName $_
      $violations | Should -BeNullOrEmpty -Because "WPF module '$_' must not reference ApiServer"
    }
  }

  Context 'NO-CROSSADAPTER: API module ApiServer.ps1 does not reference WPF' {
    It 'ApiServer.ps1 has no UI references' {
      $violations = Test-ModuleHasNoUiReferences -ModuleName 'ApiServer.ps1'
      $violations | Should -BeNullOrEmpty -Because "API module must not reference UI modules"
    }
  }

  Context 'Module file list completeness' {
    It 'All module files referenced in ModuleFileList exist on disk' {
      . (Join-Path $script:modulesPath 'ModuleFileList.ps1')
      $allModules = Get-RomCleanupModuleFiles -Profile 'all'
      foreach ($module in $allModules) {
        $path = Join-Path $script:modulesPath $module
        Test-Path $path | Should -BeTrue -Because "Module '$module' listed in ModuleFileList must exist"
      }
    }

    It 'UseCaseContracts.ps1 is in the module list' {
      . (Join-Path $script:modulesPath 'ModuleFileList.ps1')
      $allModules = Get-RomCleanupModuleFiles -Profile 'all'
      $allModules | Should -Contain 'UseCaseContracts.ps1'
    }
  }
}
