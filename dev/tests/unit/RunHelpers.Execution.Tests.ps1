BeforeAll {
  $loaderPath = Join-Path $PSScriptRoot '..\..\modules\RomCleanupLoader.ps1'
  if (Test-Path $loaderPath) { . $loaderPath }
}

Describe 'RunHelpers.Execution – Orchestrierung' {

  Context 'New-OperationResult' {

    It 'erzeugt ok-Status mit Outcome=OK' {
      if (-not (Get-Command New-OperationResult -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'New-OperationResult nicht verfügbar'
        return
      }
      $r = New-OperationResult -Status 'ok'
      $r.Status | Should -Be 'ok'
      $r.Outcome | Should -Be 'OK'
      $r.ShouldReturn | Should -Be $false
    }

    It 'setzt ShouldReturn=true bei completed' {
      if (-not (Get-Command New-OperationResult -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'New-OperationResult nicht verfügbar'
        return
      }
      $r = New-OperationResult -Status 'completed'
      $r.ShouldReturn | Should -Be $true
      $r.Outcome | Should -Be 'OK'
    }

    It 'setzt Outcome=ERROR bei blocked/error' {
      if (-not (Get-Command New-OperationResult -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'New-OperationResult nicht verfügbar'
        return
      }
      foreach ($status in @('blocked', 'error')) {
        $r = New-OperationResult -Status $status
        $r.Outcome | Should -Be 'ERROR' -Because "Status '$status' sollte ERROR sein"
      }
    }

    It 'setzt Outcome=SKIP bei skipped' {
      if (-not (Get-Command New-OperationResult -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'New-OperationResult nicht verfügbar'
        return
      }
      $r = New-OperationResult -Status 'skipped'
      $r.Outcome | Should -Be 'SKIP'
    }

    It 'akzeptiert alle ValidateSet-Werte ohne Exception' {
      if (-not (Get-Command New-OperationResult -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'New-OperationResult nicht verfügbar'
        return
      }
      foreach ($status in @('ok', 'completed', 'skipped', 'blocked', 'error', 'continue')) {
        { New-OperationResult -Status $status } | Should -Not -Throw
      }
    }
  }

  Context 'Get-StandaloneDiscExtensionSet' {

    It 'gibt HashSet mit Disc-Extensions zurück' {
      if (-not (Get-Command Get-StandaloneDiscExtensionSet -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Get-StandaloneDiscExtensionSet nicht verfügbar'
        return
      }
      $set = Get-StandaloneDiscExtensionSet
      $set | Should -Not -BeNullOrEmpty
      # mindestens die Basis-Formate
      $set.Contains('.zip') -or $set.Contains('.chd') -or $set.Contains('.iso') | Should -Be $true
    }
  }

  Context 'Invoke-OptionalConsoleSort' {

    It 'gibt skipped zurück wenn nicht aktiviert' {
      if (-not (Get-Command Invoke-OptionalConsoleSort -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-OptionalConsoleSort nicht verfügbar'
        return
      }
      $r = Invoke-OptionalConsoleSort -Enabled $false -Mode 'Move' -Roots @('C:\fake') -Extensions @('.zip')
      $r.Status | Should -Be 'skipped'
    }

    It 'gibt skipped zurück im DryRun-Modus' {
      if (-not (Get-Command Invoke-OptionalConsoleSort -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-OptionalConsoleSort nicht verfügbar'
        return
      }
      $r = Invoke-OptionalConsoleSort -Enabled $true -Mode 'DryRun' -Roots @('C:\fake') -Extensions @('.zip')
      $r.Status | Should -Be 'skipped'
    }
  }

  Context 'Invoke-ConvertPreviewDryRun' {

    It 'gibt skipped zurück wenn nicht aktiviert' {
      if (-not (Get-Command Invoke-ConvertPreviewDryRun -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Invoke-ConvertPreviewDryRun nicht verfügbar'
        return
      }
      $r = Invoke-ConvertPreviewDryRun -Enabled $false -Mode 'DryRun'
      $r.Status | Should -Be 'skipped'
    }
  }
}
