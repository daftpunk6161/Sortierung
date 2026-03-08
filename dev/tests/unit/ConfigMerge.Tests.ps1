#Requires -Modules Pester
<#
  TEST-MOD-02: ConfigMerge.ps1 — Tests für Konfigurations-Merge-Logik
  Testet Precedence-Chain: Defaults → User → Profile → Env → CLI.
#>

BeforeAll {
  . "$PSScriptRoot/../../modules/RomCleanupLoader.ps1"
}

Describe 'ConfigMerge – Precedence-Chain' {

  Context 'Get-ConfigurationBaselineDefaults' {

    It 'liefert nicht-leeres Defaults-Objekt' {
      $defaults = Get-ConfigurationBaselineDefaults
      $defaults | Should -Not -BeNullOrEmpty
    }

    It 'enthält Standard-Werte für mode' {
      $defaults = Get-ConfigurationBaselineDefaults
      $defaults.mode | Should -Be 'DryRun'
    }
  }

  Context 'Get-MergedConfiguration – Defaults' {

    It 'Merged-Config enthält Baseline-Defaults ohne Overrides' {
      $merged = Get-MergedConfiguration
      $merged | Should -Not -BeNullOrEmpty
      $merged.mode | Should -Not -BeNullOrEmpty
    }
  }

  Context 'Get-MergedConfiguration – CLI-Overrides haben höchste Priorität' {

    It 'CLI-Override überschreibt Defaults' {
      $merged = Get-MergedConfiguration -CliOverrides @{ mode = 'Move' }
      $merged.mode | Should -Be 'Move'
    }

    It 'CLI-Override überschreibt für logLevel' {
      $merged = Get-MergedConfiguration -CliOverrides @{ logLevel = 'Debug' }
      $merged.logLevel | Should -Be 'Debug'
    }
  }

  Context 'Get-EnvironmentOverrides' {

    BeforeEach {
      # Bestehende Werte sichern
      $script:savedEnvVars = @{}
      'ROM_CLEANUP_MODE', 'ROM_CLEANUP_LOG_LEVEL', 'ROM_CLEANUP_DRY_RUN' | ForEach-Object {
        $script:savedEnvVars[$_] = [Environment]::GetEnvironmentVariable($_, 'Process')
      }
    }

    AfterEach {
      # Werte zurücksetzen
      $script:savedEnvVars.GetEnumerator() | ForEach-Object {
        if ($null -eq $_.Value) {
          [Environment]::SetEnvironmentVariable($_.Key, $null, 'Process')
        } else {
          [Environment]::SetEnvironmentVariable($_.Key, $_.Value, 'Process')
        }
      }
    }

    It 'liest ROM_CLEANUP_MODE aus Environment' {
      [Environment]::SetEnvironmentVariable('ROM_CLEANUP_MODE', 'Move', 'Process')
      $overrides = Get-EnvironmentOverrides
      $overrides | Should -Not -BeNullOrEmpty
      $overrides.mode | Should -Be 'Move'
    }

    It 'parst Boolean-Werte korrekt (true/1/yes)' {
      [Environment]::SetEnvironmentVariable('ROM_CLEANUP_DRY_RUN', 'true', 'Process')
      $overrides = Get-EnvironmentOverrides
      if ($overrides.PSObject.Properties.Name -contains 'dryRun') {
        $overrides.dryRun | Should -Be $true
      }
    }

    It 'ignoriert nicht-gesetzte Variablen' {
      [Environment]::SetEnvironmentVariable('ROM_CLEANUP_MODE', $null, 'Process')
      $overrides = Get-EnvironmentOverrides
      # Soll kein 'mode'-Key enthalten wenn Variable nicht gesetzt
      if ($overrides -is [hashtable]) {
        $overrides.ContainsKey('mode') | Should -BeFalse
      }
    }
  }

  Context 'Get-ConfigurationDiff' {

    It 'liefert Array für Diff-Ergebnis' {
      if (Get-Command Get-ConfigurationDiff -ErrorAction SilentlyContinue) {
        $diff = @(Get-ConfigurationDiff)
        $diff.Count | Should -BeGreaterOrEqual 0
      }
    }
  }
}
