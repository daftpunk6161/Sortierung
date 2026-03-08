BeforeAll {
  $loaderPath = Join-Path $PSScriptRoot '..\..\modules\RomCleanupLoader.ps1'
  if (Test-Path $loaderPath) { . $loaderPath }
}

Describe 'SafetyToolsService – Profiles & Sandbox' {

  Context 'Get-SafetyPolicyProfiles' {

    It 'enthält alle drei Profile (Conservative, Balanced, Expert)' {
      if (-not (Get-Command Get-SafetyPolicyProfiles -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-SafetyPolicyProfiles nicht verfügbar'
        return
      }
      $profiles = Get-SafetyPolicyProfiles
      $profiles.Keys | Should -Contain 'Conservative'
      $profiles.Keys | Should -Contain 'Balanced'
      $profiles.Keys | Should -Contain 'Expert'
    }

    It 'Conservative hat Strict=true und mehr ProtectedPaths als Expert' {
      if (-not (Get-Command Get-SafetyPolicyProfiles -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-SafetyPolicyProfiles nicht verfügbar'
        return
      }
      $profiles = Get-SafetyPolicyProfiles
      $profiles['Conservative'].Strict | Should -Be $true
      $profiles['Expert'].Strict | Should -Be $false
      @($profiles['Conservative'].ProtectedPaths).Count |
        Should -BeGreaterThan @($profiles['Expert'].ProtectedPaths).Count
    }
  }

  Context 'Invoke-SafetyPolicyProfileApply' {

    It 'gibt Profil-Objekt mit ProtectedPathsText zurück' {
      if (-not (Get-Command Invoke-SafetyPolicyProfileApply -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-SafetyPolicyProfileApply nicht verfügbar'
        return
      }
      $result = Invoke-SafetyPolicyProfileApply -Profile 'Balanced'
      $result.Name | Should -Be 'Balanced'
      $result.ProtectedPaths | Should -Not -BeNullOrEmpty
    }

    It 'wirft bei ungültigem Profilnamen' {
      if (-not (Get-Command Invoke-SafetyPolicyProfileApply -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-SafetyPolicyProfileApply nicht verfügbar'
        return
      }
      { Invoke-SafetyPolicyProfileApply -Profile 'NichtExistent' } | Should -Throw
    }
  }

  Context 'Invoke-SafetySandboxRun – Preflight' {

    It 'blockt bei leeren Roots' {
      if (-not (Get-Command Invoke-SafetySandboxRun -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-SafetySandboxRun nicht verfügbar'
        return
      }
      $result = Invoke-SafetySandboxRun -Roots @() -Extensions @('.zip') -StrictSafety $true -ProtectedPathsText '' -UseDat $false -ConvertEnabled $false
      $result.Status | Should -Be 'Blocked'
      $result.BlockerCount | Should -BeGreaterThan 0
    }

    It 'blockt bei fehlenden Extensions' {
      if (-not (Get-Command Invoke-SafetySandboxRun -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-SafetySandboxRun nicht verfügbar'
        return
      }
      $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "safety_test_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null
      try {
        $result = Invoke-SafetySandboxRun -Roots @($tempRoot) -Extensions @() -StrictSafety $true -ProtectedPathsText '' -UseDat $false -ConvertEnabled $false
        $result.Status | Should -Be 'Blocked'
      } finally {
        Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
      }
    }

    It 'gibt Ready bei gültiger Konfiguration' {
      if (-not (Get-Command Invoke-SafetySandboxRun -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-SafetySandboxRun nicht verfügbar'
        return
      }
      $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "safety_test_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null
      try {
        $result = Invoke-SafetySandboxRun -Roots @($tempRoot) -Extensions @('.zip') -StrictSafety $false -ProtectedPathsText '' -UseDat $false -ConvertEnabled $false
        $result.Status | Should -BeIn @('Ready', 'Review')
      } finally {
        Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
      }
    }

    It 'enthält CheckedAt Timestamp' {
      if (-not (Get-Command Invoke-SafetySandboxRun -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-SafetySandboxRun nicht verfügbar'
        return
      }
      $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "safety_test_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null
      try {
        $result = Invoke-SafetySandboxRun -Roots @($tempRoot) -Extensions @('.zip') -StrictSafety $false -ProtectedPathsText '' -UseDat $false -ConvertEnabled $false
        $result.CheckedAt | Should -Not -BeNullOrEmpty
      } finally {
        Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'Invoke-ToolSelfTest' {

    It 'gibt Result-Objekt mit Results-Array zurück' {
      if (-not (Get-Command Invoke-ToolSelfTest -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-ToolSelfTest nicht verfügbar'
        return
      }
      $result = Invoke-ToolSelfTest -TimeoutSeconds 3
      $result | Should -Not -BeNullOrEmpty
      $result.Results | Should -Not -BeNullOrEmpty
      $result.Results.Count | Should -BeGreaterOrEqual 1
    }

    It 'zählt HealthyCount und MissingCount korrekt' {
      if (-not (Get-Command Invoke-ToolSelfTest -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-ToolSelfTest nicht verfügbar'
        return
      }
      $result = Invoke-ToolSelfTest -TimeoutSeconds 3
      ($result.HealthyCount + $result.MissingCount + $result.WarningCount) |
        Should -Be $result.Results.Count
    }
  }
}
