#requires -Modules Pester

Describe 'Test-only API boundary' {
  BeforeAll {
    $root = $PSScriptRoot
    while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
      $root = Split-Path -Parent $root
    }
    $script:root = $root
  }

  It 'Set-SafetyPolicyProfile Alias wird außerhalb von Tests nicht verwendet' {
    $runtimeHits = @(Get-ChildItem -Path (Join-Path $script:root 'dev\modules') -Recurse -Filter '*.ps1' |
      Select-String -Pattern '\bSet-SafetyPolicyProfile\b' -AllMatches)
    $runtimeHits.Count | Should -Be 1 -Because 'Einzige Runtime-Stelle ist die Alias-Definition selbst.'
  }

  It 'Remove-DatFromInventory Alias wird außerhalb von Tests nicht verwendet' {
    $runtimeHits = @(Get-ChildItem -Path (Join-Path $script:root 'dev\modules') -Recurse -Filter '*.ps1' |
      Select-String -Pattern '\bRemove-DatFromInventory\b' -AllMatches)
    $runtimeHits.Count | Should -Be 1 -Because 'Einzige Runtime-Stelle ist die Alias-Definition selbst.'
  }

  It 'Update-DatBatch Alias wird außerhalb von Tests nicht verwendet' {
    $runtimeHits = @(Get-ChildItem -Path (Join-Path $script:root 'dev\modules') -Recurse -Filter '*.ps1' |
      Select-String -Pattern '\bUpdate-DatBatch\b' -AllMatches)
    $runtimeHits.Count | Should -Be 1 -Because 'Einzige Runtime-Stelle ist die Alias-Definition selbst.'
  }
}
