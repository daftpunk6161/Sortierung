<#
.SYNOPSIS
  Unit tests for UseCase-Contracts (v1).
  Validates contract factories, validation, and version consistency.
#>

BeforeAll {
  $modulesPath = Join-Path $PSScriptRoot '../../modules'
  . (Join-Path $modulesPath 'UseCaseContracts.ps1')
}

Describe 'UseCaseContracts' {

  Context 'Contract Version' {
    It 'Returns current version' {
      Get-UseCaseContractVersion | Should -Be 'v1'
    }
  }

  Context 'RunDedupe Contract' {
    It 'Creates valid RunDedupeInput' {
      $input = New-RunDedupeInput -Roots @('C:\Roms') -Mode 'DryRun'
      $input.ContractType | Should -Be 'UseCaseContract.RunDedupeInput'
      $input.ContractVersion | Should -Be 'v1'
      $input.Roots | Should -HaveCount 1
      $input.Mode | Should -Be 'DryRun'
      $input.Prefer | Should -Contain 'EU'
      $input.UseDat | Should -BeFalse
    }

    It 'Throws on empty Roots' {
      { New-RunDedupeInput -Roots @() -Mode 'DryRun' } | Should -Throw '*Roots*'
    }

    It 'Creates valid RunDedupeOutput' {
      $output = New-RunDedupeOutput -Success $true -TotalFiles 100 -WinnerCount 50 -LoserCount 30
      $output.ContractType | Should -Be 'UseCaseContract.RunDedupeOutput'
      $output.Success | Should -BeTrue
      $output.TotalFiles | Should -Be 100
      $output.WinnerCount | Should -Be 50
      $output.LoserCount | Should -Be 30
    }

    It 'Validates RunDedupeInput contract type' {
      $input = New-RunDedupeInput -Roots @('C:\Roms') -Mode 'Move'
      $result = Test-UseCaseContract -Object $input -ExpectedType 'UseCaseContract.RunDedupeInput'
      $result.Valid | Should -BeTrue
    }

    It 'Fails validation for wrong type' {
      $input = New-RunDedupeInput -Roots @('C:\Roms') -Mode 'DryRun'
      $result = Test-UseCaseContract -Object $input -ExpectedType 'UseCaseContract.PreflightInput'
      $result.Valid | Should -BeFalse
      $result.Reason | Should -Match 'Expected type'
    }
  }

  Context 'Preflight Contract' {
    It 'Creates valid PreflightInput' {
      $input = New-PreflightInput -Roots @('C:\Roms') -Mode 'DryRun'
      $input.ContractType | Should -Be 'UseCaseContract.PreflightInput'
      $input.ContractVersion | Should -Be 'v1'
      $input.Roots.Count | Should -BeGreaterThan 0
    }

    It 'Throws on empty Roots' {
      { New-PreflightInput -Roots @() -Mode 'DryRun' } | Should -Throw '*Roots*'
    }

    It 'Creates valid PreflightOutput' {
      $output = New-PreflightOutput -Valid $true -Warnings @('Minor issue')
      $output.ContractType | Should -Be 'UseCaseContract.PreflightOutput'
      $output.Valid | Should -BeTrue
      $output.Warnings | Should -HaveCount 1
    }

    It 'Creates PreflightCheck' {
      $check = New-PreflightCheck -Name 'RootExists' -Passed $true -Message 'All roots exist'
      $check.ContractType | Should -Be 'UseCaseContract.PreflightCheck'
      $check.Name | Should -Be 'RootExists'
      $check.Passed | Should -BeTrue
      $check.FixSuggestion | Should -BeNullOrEmpty
    }

    It 'Creates PreflightCheck with fix suggestion' {
      $check = New-PreflightCheck -Name 'DatExists' -Passed $false -Message 'DAT not found' -FixSuggestion 'Set DatRoot path'
      $check.Passed | Should -BeFalse
      $check.FixSuggestion | Should -Be 'Set DatRoot path'
    }
  }

  Context 'Conversion Contract' {
    It 'Creates valid ConversionInput' {
      $input = New-ConversionInput -Operation 'WinnerMove' -Mode 'Move'
      $input.ContractType | Should -Be 'UseCaseContract.ConversionInput'
      $input.Operation | Should -Be 'WinnerMove'
      $input.Enabled | Should -BeTrue
    }

    It 'Creates valid ConversionOutput' {
      $output = New-ConversionOutput -Success $true -ConvertedCount 10 -SkippedCount 5
      $output.ContractType | Should -Be 'UseCaseContract.ConversionOutput'
      $output.Success | Should -BeTrue
      $output.ConvertedCount | Should -Be 10
      $output.SkippedCount | Should -Be 5
    }
  }

  Context 'Rollback Contract' {
    It 'Creates valid RollbackInput' {
      $input = New-RollbackInput -AuditCsvPath 'C:\audit.csv' -AllowedRestoreRoots @('C:\Roms') -AllowedCurrentRoots @('C:\Roms')
      $input.ContractType | Should -Be 'UseCaseContract.RollbackInput'
      $input.DryRun | Should -BeTrue
      $input.AuditCsvPath | Should -Be 'C:\audit.csv'
    }

    It 'Throws on empty AllowedRestoreRoots' {
      { New-RollbackInput -AuditCsvPath 'C:\a.csv' -AllowedRestoreRoots @() -AllowedCurrentRoots @('C:\R') } | Should -Throw '*AllowedRestoreRoots*'
    }

    It 'Throws on empty AllowedCurrentRoots' {
      { New-RollbackInput -AuditCsvPath 'C:\a.csv' -AllowedRestoreRoots @('C:\R') -AllowedCurrentRoots @() } | Should -Throw '*AllowedCurrentRoots*'
    }

    It 'Creates valid RollbackOutput' {
      $output = New-RollbackOutput -Success $true -RestoredCount 5
      $output.ContractType | Should -Be 'UseCaseContract.RollbackOutput'
      $output.RestoredCount | Should -Be 5
    }
  }

  Context 'Report Contract' {
    It 'Creates valid ReportInput' {
      $input = New-ReportInput -Type 'Summary' -SourceData @{ Total = 100 }
      $input.ContractType | Should -Be 'UseCaseContract.ReportInput'
      $input.Type | Should -Be 'Summary'
      $input.Format | Should -Be 'JSON'
    }

    It 'Creates valid ReportOutput' {
      $output = New-ReportOutput -Success $true -OutputPath 'C:\report.json'
      $output.ContractType | Should -Be 'UseCaseContract.ReportOutput'
      $output.OutputPath | Should -Be 'C:\report.json'
    }
  }

  Context 'Assert-UseCaseContract' {
    It 'Does not throw for valid contract' {
      $testObj = New-RunDedupeInput -Roots @('C:\Roms') -Mode 'DryRun'
      { Assert-UseCaseContract -Object $testObj -ExpectedType 'UseCaseContract.RunDedupeInput' } | Should -Not -Throw
    }

    It 'Throws for null object' {
      { Assert-UseCaseContract -Object $null -ExpectedType 'UseCaseContract.RunDedupeInput' } | Should -Throw '*Contract*'
    }

    It 'Throws for wrong type' {
      $testObj = New-PreflightInput -Roots @('C:\Roms') -Mode 'DryRun'
      { Assert-UseCaseContract -Object $testObj -ExpectedType 'UseCaseContract.RunDedupeInput' } | Should -Throw '*Expected type*'
    }
  }
}

