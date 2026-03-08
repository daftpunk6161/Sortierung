BeforeAll {
  $loaderPath = Join-Path $PSScriptRoot '..\..\modules\RomCleanupLoader.ps1'
  if (Test-Path $loaderPath) { . $loaderPath }
}

Describe 'MemoryGuard – Speicher-Budget-Überwachung' {

  Context 'Initialize-MemoryBudget' {

    It 'initialisiert mit Default-Werten (800/1500)' {
      if (-not (Get-Command Initialize-MemoryBudget -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Initialize-MemoryBudget nicht verfügbar'
        return
      }
      $config = Initialize-MemoryBudget
      $config | Should -Not -BeNullOrEmpty
      $config.SoftLimitMB | Should -Be 800
      $config.HardLimitMB | Should -Be 1500
    }

    It 'akzeptiert benutzerdefinierte Limits' {
      if (-not (Get-Command Initialize-MemoryBudget -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Initialize-MemoryBudget nicht verfügbar'
        return
      }
      $config = Initialize-MemoryBudget -SoftLimitMB 400 -HardLimitMB 900
      $config.SoftLimitMB | Should -Be 400
      $config.HardLimitMB | Should -Be 900
    }
  }

  Context 'Get-MemoryPressure' {

    It 'gibt Pressure-Objekt mit korrekten Properties zurück' {
      if (-not (Get-Command Get-MemoryPressure -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-MemoryPressure nicht verfügbar'
        return
      }
      $pressure = Get-MemoryPressure
      $pressure | Should -Not -BeNullOrEmpty
      $pressure.Pressure | Should -BeIn @('None', 'Soft', 'Hard')
      $pressure.CurrentMB | Should -BeGreaterThan 0
    }

    It 'hat CurrentMB größer als 0' {
      if (-not (Get-Command Get-MemoryPressure -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-MemoryPressure nicht verfügbar'
        return
      }
      $pressure = Get-MemoryPressure
      $pressure.CurrentMB | Should -BeGreaterThan 0
    }
  }

  Context 'Test-MemoryBudget' {

    It 'gibt Boolean zurück' {
      if (-not (Get-Command Test-MemoryBudget -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Test-MemoryBudget nicht verfügbar'
        return
      }
      $result = Test-MemoryBudget
      $result | Should -BeOfType [bool]
    }

    It 'nutzt Log-Callback ohne Fehler' {
      if (-not (Get-Command Test-MemoryBudget -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Test-MemoryBudget nicht verfügbar'
        return
      }
      $logs = [System.Collections.ArrayList]::new()
      $result = Test-MemoryBudget -Log { param($msg) $logs.Add($msg) | Out-Null }
      $result | Should -BeOfType [bool]
    }
  }

  Context 'Get-MemoryBudgetSummary' {

    It 'gibt Summary-Objekt zurück nach Initialisierung' {
      if (-not (Get-Command Get-MemoryBudgetSummary -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-MemoryBudgetSummary nicht verfügbar'
        return
      }
      if (Get-Command Initialize-MemoryBudget -ErrorAction SilentlyContinue) {
        Initialize-MemoryBudget | Out-Null
      }
      $summary = Get-MemoryBudgetSummary
      $summary | Should -Not -BeNullOrEmpty
    }
  }

  Context 'Invoke-MemoryBackpressure' {

    It 'gibt Pressure-Level als String zurück' {
      if (-not (Get-Command Invoke-MemoryBackpressure -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-MemoryBackpressure nicht verfügbar'
        return
      }
      $result = Invoke-MemoryBackpressure -MaxRetries 1 -PauseMs 50
      $result | Should -BeIn @('None', 'Soft', 'Hard')
    }
  }
}
