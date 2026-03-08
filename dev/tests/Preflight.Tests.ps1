#requires -Modules Pester
<##
  ROM Cleanup Preflight - Pester 5.x Tests
  Run: Invoke-Pester -Path .\tests\Preflight.Tests.ps1 -Output Detailed
##>

BeforeAll {
    $script:PreflightPath = Join-Path $PSScriptRoot '..' 'tools' 'preflight' 'Invoke-RomCleanupPreflight.ps1'
    . $script:PreflightPath
}

AfterAll {
}

Describe 'Preflight - Roots' {
    BeforeEach {
        Reset-PreflightState
        $Roots = @()
        $TrashRoot = $null
        $AuditRoot = $null
        $ToolPaths = @{}
        $DatRoot = $null
        $DatMap = @{}
    }
    
    It 'Should error when no roots specified' {
        Test-RootsFolders
        (Get-PreflightState).Errors.Count | Should -BeGreaterThan 0
    }

    It 'Should accept existing root directory' {
        $tempRoot = Join-Path $env:TEMP "pester_root_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        try {
            $Roots = @($tempRoot)
            Test-RootsFolders
            (Get-PreflightState).Errors.Count | Should -Be 0
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Should error on overlapping roots' {
        $tempRoot = Join-Path $env:TEMP "pester_root_$(Get-Random)"
        $tempSub = Join-Path $tempRoot 'sub'
        New-Item -ItemType Directory -Path $tempSub -Force | Out-Null
        try {
            $Roots = @($tempRoot, $tempSub)
            Test-RootsFolders
            (Get-PreflightState).Errors -join "`n" | Should -Match 'Overlapping roots'
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Preflight - TrashRoot' {
    It 'Should error when TrashRoot parent is missing' {
        $TrashRoot = Join-Path $env:TEMP "pester_missing_$(Get-Random)\trash"
        Test-TrashRoot
        (Get-PreflightState).Errors -join "`n" | Should -Match 'TrashRoot parent does not exist'
    }

    It 'Should error when TrashRoot is inside a root' {
        $tempRoot = Join-Path $env:TEMP "pester_root_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        try {
            $Roots = @($tempRoot)
            $TrashRoot = Join-Path $tempRoot 'trash'
            Test-TrashRoot
            (Get-PreflightState).Errors -join "`n" | Should -Match 'TrashRoot'
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Preflight - AuditRoot' {
    It 'Should warn when AuditRoot parent is missing' {
        $AuditRoot = Join-Path $env:TEMP "pester_missing_$(Get-Random)\audit"
        Test-AuditRoot
        (Get-PreflightState).Warnings.Count | Should -BeGreaterThan 0
    }
}

Describe 'Preflight - DAT' {
    It 'Should error when DatRoot is missing' {
        $DatRoot = Join-Path $env:TEMP "pester_missing_$(Get-Random)\dat"
        Test-DatConfiguration
        (Get-PreflightState).Errors -join "`n" | Should -Match 'DatRoot does not exist'
    }

    It 'Should warn for unrecognized DatMap key' {
        $tempRoot = Join-Path $env:TEMP "pester_dat_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        try {
            $DatMap = @{ 'BADCONSOLE' = $tempRoot }
            Test-DatConfiguration
            (Get-PreflightState).Warnings -join "`n" | Should -Match 'Unrecognized console key'
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Preflight - Tools' {
    It 'Should error on invalid tool path' {
        $ToolPaths = @{ chdman = 'C:\missing\chdman.exe' }
        Test-ConversionTools
        (Get-PreflightState).Errors -join "`n" | Should -Match 'path invalid'
    }
}

Describe 'Preflight - Reparse Points' {
    BeforeEach {
        Reset-PreflightState
        $Roots = @()
    }
    
    It 'Should error when a root contains a junction' {
        $tempRoot = Join-Path $env:TEMP "pester_root_$(Get-Random)"
        $targetDir = Join-Path $env:TEMP "pester_target_$(Get-Random)"
        $junction = Join-Path $tempRoot 'link'
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
        try {
            New-Item -ItemType Junction -Path $junction -Target $targetDir | Out-Null
            $Roots = @($tempRoot)
            Test-ReparsePointsInRoots
            @((Get-PreflightState).Errors).Count | Should -BeGreaterThan 0
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item -LiteralPath $targetDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'Preflight - Path Edge Cases' {
    BeforeEach {
        Reset-PreflightState
        $Roots = @()
    }
    
    It 'Should not error on substring tests for valid roots' {
        $tempRoot = Join-Path $env:TEMP "pester_root_$(Get-Random)"
        New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
        try {
            $Roots = @($tempRoot)
            Test-PathSubstringEdgeCases
            @((Get-PreflightState).Errors).Count | Should -Be 0
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
