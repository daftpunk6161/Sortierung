#requires -Modules Pester
# ================================================================
#  FolderDedupe.Tests.ps1
#  Unit tests for folder-level deduplication by base-name key.
# ================================================================

Describe 'FolderDedupe – Get-FolderBaseKey' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')
    }

    Context 'Parenthetical suffix stripping' {

        It 'strips trailing parenthetical version "(v1.1)"' {
            Get-FolderBaseKey -FolderName 'Doom (v1.1)' | Should -Be 'doom'
        }

        It 'strips trailing parenthetical region "(USA)"' {
            Get-FolderBaseKey -FolderName 'Doom (USA)' | Should -Be 'doom'
        }

        It 'strips trailing parenthetical with spaces " ( v2 )"' {
            Get-FolderBaseKey -FolderName 'Doom ( v2 )' | Should -Be 'doom'
        }

        It 'keeps mid-name parenthetical intact' {
            # Only trailing groups are stripped
            Get-FolderBaseKey -FolderName 'Command & Conquer (Red Alert) Gold' |
              Should -Be 'command & conquer (red alert) gold'
        }
    }

    Context 'Bracket suffix stripping' {

        It 'strips trailing bracket tag "[USA]"' {
            Get-FolderBaseKey -FolderName 'Wolf3D [USA]' | Should -Be 'wolf3d'
        }

        It 'strips trailing bracket with whitespace " [ DE ] "' {
            Get-FolderBaseKey -FolderName 'Wolf3D [ DE ] ' | Should -Be 'wolf3d'
        }
    }

    Context 'Version suffix stripping' {

        It 'strips trailing version number "v1.2"' {
            Get-FolderBaseKey -FolderName 'Doom v1.2' | Should -Be 'doom'
        }

        It 'strips trailing bare version "1.0"' {
            Get-FolderBaseKey -FolderName 'Doom 1.0' | Should -Be 'doom'
        }

        It 'strips trailing version "v2.1.3"' {
            Get-FolderBaseKey -FolderName 'Game v2.1.3' | Should -Be 'game'
        }
    }

    Context 'Whitespace normalisation' {

        It 'collapses multiple spaces' {
            Get-FolderBaseKey -FolderName 'Duke  Nukem   3D' | Should -Be 'duke nukem 3d'
        }

        It 'trims leading/trailing whitespace' {
            Get-FolderBaseKey -FolderName '  Doom  ' | Should -Be 'doom'
        }
    }

    Context 'Edge cases' {

        It 'returns empty string for whitespace input' {
            Get-FolderBaseKey -FolderName '   ' | Should -Be ''
        }

        It 'returns lowered name for already-clean input' {
            Get-FolderBaseKey -FolderName 'Doom' | Should -Be 'doom'
        }

        It 'handles complex name with multiple markers' {
            # Trailing parens stripped first, then version suffix stripped
            Get-FolderBaseKey -FolderName 'Prince of Persia v1.0 (USA)' | Should -Be 'prince of persia'
        }
    }

    Context 'Grouping correctness' {

        It 'groups "Doom" and "Doom (v1.1)" together' {
            $a = Get-FolderBaseKey -FolderName 'Doom'
            $b = Get-FolderBaseKey -FolderName 'Doom (v1.1)'
            $a | Should -Be $b
        }

        It 'groups "Wolf3D" and "Wolf3D [USA]" together' {
            $a = Get-FolderBaseKey -FolderName 'Wolf3D'
            $b = Get-FolderBaseKey -FolderName 'Wolf3D [USA]'
            $a | Should -Be $b
        }

        It 'does NOT group "Doom" and "Doom 2" together' {
            $a = Get-FolderBaseKey -FolderName 'Doom'
            $b = Get-FolderBaseKey -FolderName 'Doom 2'
            $a | Should -Not -Be $b
        }
    }
}

Describe 'FolderDedupe – Get-NewestFileTimestamp' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')
    }

    It 'returns folder timestamp for empty directory' {
        $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        try {
            $dirInfo = [System.IO.DirectoryInfo]::new($tempDir)
            $ts = Get-NewestFileTimestamp -Directory $dirInfo
            $ts | Should -BeOfType [datetime]
            # Should be approximately the directory creation time
            ($ts - $dirInfo.LastWriteTimeUtc).TotalSeconds | Should -BeLessOrEqual 2
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'returns newest file timestamp when files exist' {
        $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        try {
            $oldFile = Join-Path $tempDir 'old.txt'
            $newFile = Join-Path $tempDir 'new.txt'
            Set-Content -Path $oldFile -Value 'old'
            Start-Sleep -Milliseconds 50
            Set-Content -Path $newFile -Value 'new'

            $dirInfo = [System.IO.DirectoryInfo]::new($tempDir)
            $ts = Get-NewestFileTimestamp -Directory $dirInfo
            $newFileTime = (Get-Item $newFile).LastWriteTimeUtc
            ($ts - $newFileTime).TotalSeconds | Should -BeLessOrEqual 1
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'FolderDedupe – Get-FolderFileCount' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')
    }

    It 'returns 0 for empty directory' {
        $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        try {
            $dirInfo = [System.IO.DirectoryInfo]::new($tempDir)
            Get-FolderFileCount -Directory $dirInfo | Should -Be 0
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'counts files recursively' {
        $tempDir = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
        New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
        $subDir = Join-Path $tempDir 'sub'
        New-Item -ItemType Directory -Path $subDir -Force | Out-Null
        try {
            Set-Content -Path (Join-Path $tempDir 'a.txt') -Value 'a'
            Set-Content -Path (Join-Path $subDir 'b.txt') -Value 'b'
            Set-Content -Path (Join-Path $subDir 'c.txt') -Value 'c'

            $dirInfo = [System.IO.DirectoryInfo]::new($tempDir)
            Get-FolderFileCount -Directory $dirInfo | Should -Be 3
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

Describe 'FolderDedupe – Invoke-FolderDedupeByBaseName' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')
    }

    Context 'DryRun mode' {

        It 'detects duplicate groups without moving anything' {
            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            try {
                $dir1 = Join-Path $tempRoot 'Doom'
                $dir2 = Join-Path $tempRoot 'Doom (v1.1)'
                New-Item -ItemType Directory -Path $dir1 -Force | Out-Null
                New-Item -ItemType Directory -Path $dir2 -Force | Out-Null
                Set-Content -Path (Join-Path $dir1 'game.exe') -Value 'old'
                Start-Sleep -Milliseconds 50
                Set-Content -Path (Join-Path $dir2 'game.exe') -Value 'new'

                $result = Invoke-FolderDedupeByBaseName -Roots @($tempRoot) -Mode DryRun
                $result.DupeGroups | Should -Be 1
                $result.Moved | Should -Be 0
                $result.Mode | Should -Be 'DryRun'
                @($result.Actions).Count | Should -Be 1
                $result.Actions[0].Action | Should -Be 'DRYRUN-MOVE'

                # Both directories should still exist
                (Test-Path -LiteralPath $dir1) | Should -BeTrue
                (Test-Path -LiteralPath $dir2) | Should -BeTrue
            } finally {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Move mode' {

        It 'moves loser folders to dupe directory' {
            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            try {
                $dir1 = Join-Path $tempRoot 'Doom'
                $dir2 = Join-Path $tempRoot 'Doom (v1.1)'
                New-Item -ItemType Directory -Path $dir1 -Force | Out-Null
                New-Item -ItemType Directory -Path $dir2 -Force | Out-Null
                # Make dir2 the winner (newer file)
                Set-Content -Path (Join-Path $dir1 'game.exe') -Value 'old'
                (Get-Item (Join-Path $dir1 'game.exe')).LastWriteTimeUtc = [datetime]::UtcNow.AddDays(-10)
                Start-Sleep -Milliseconds 50
                Set-Content -Path (Join-Path $dir2 'game.exe') -Value 'new'

                $result = Invoke-FolderDedupeByBaseName -Roots @($tempRoot) -Mode Move
                $result.DupeGroups | Should -Be 1
                $result.Moved | Should -Be 1
                $result.Mode | Should -Be 'Move'

                # dir2 should still be at original location (winner)
                (Test-Path -LiteralPath $dir2) | Should -BeTrue
                # dir1 (loser) should have been moved
                (Test-Path -LiteralPath $dir1) | Should -BeFalse
            } finally {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'handles multiple dupe groups correctly' {
            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            try {
                # Group 1: Doom
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Doom') -Force | Out-Null
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Doom (v1.1)') -Force | Out-Null
                Set-Content -Path (Join-Path $tempRoot 'Doom' 'g.exe') -Value 'x'
                Set-Content -Path (Join-Path $tempRoot 'Doom (v1.1)' 'g.exe') -Value 'x'

                # Group 2: Wolf3D
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Wolf3D') -Force | Out-Null
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Wolf3D [USA]') -Force | Out-Null
                Set-Content -LiteralPath (Join-Path $tempRoot 'Wolf3D' 'g.exe') -Value 'x'
                Set-Content -LiteralPath (Join-Path $tempRoot 'Wolf3D [USA]' 'g.exe') -Value 'x'

                # Unique: should not be touched
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Duke3D') -Force | Out-Null
                Set-Content -Path (Join-Path $tempRoot 'Duke3D' 'g.exe') -Value 'x'

                $result = Invoke-FolderDedupeByBaseName -Roots @($tempRoot) -Mode DryRun
                $result.DupeGroups | Should -Be 2
                @($result.Actions).Count | Should -Be 2
            } finally {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }

    Context 'Edge cases' {

        It 'returns zero counts for empty root' {
            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            try {
                $result = Invoke-FolderDedupeByBaseName -Roots @($tempRoot) -Mode DryRun
                $result.DupeGroups | Should -Be 0
                $result.TotalFolders | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'does not treat unique folders as duplicates' {
            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            try {
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Doom') -Force | Out-Null
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Duke3D') -Force | Out-Null
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Wolf3D') -Force | Out-Null

                $result = Invoke-FolderDedupeByBaseName -Roots @($tempRoot) -Mode DryRun
                $result.DupeGroups | Should -Be 0
                @($result.Actions).Count | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'excludes dupe target directory from scan' {
            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            try {
                $dupeDir = Join-Path $tempRoot '_FOLDER_DUPES'
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Doom') -Force | Out-Null
                New-Item -ItemType Directory -Path $dupeDir -Force | Out-Null

                $result = Invoke-FolderDedupeByBaseName -Roots @($tempRoot) -Mode DryRun
                $result.TotalFolders | Should -Be 1
                $result.DupeGroups | Should -Be 0
            } finally {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }

        It 'accepts logging scriptblock without errors' {
            $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('FolderDedupeTest_' + [guid]::NewGuid().ToString('N'))
            New-Item -ItemType Directory -Path $tempRoot -Force | Out-Null
            try {
                $logMessages = [System.Collections.Generic.List[string]]::new()
                $logBlock = [scriptblock]::Create('param($msg) $null = $msg')

                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Doom') -Force | Out-Null
                New-Item -ItemType Directory -Path (Join-Path $tempRoot 'Doom (v1)') -Force | Out-Null
                Set-Content -Path (Join-Path $tempRoot 'Doom' 'g.exe') -Value 'x'
                Set-Content -Path (Join-Path $tempRoot 'Doom (v1)' 'g.exe') -Value 'x'

                { Invoke-FolderDedupeByBaseName -Roots @($tempRoot) -Mode DryRun -Log $logBlock } |
                  Should -Not -Throw
            } finally {
                Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
            }
        }
    }
}

Describe 'FolderDedupe – Get-ConsoleKeyForRoot' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Classification.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')
    }

    It 'detects DOS from "Z:\MS-DOS"' {
        Get-ConsoleKeyForRoot -RootPath 'Z:\MS-DOS' | Should -Be 'DOS'
    }

    It 'detects PS3 from "Z:\roms\PS3"' {
        Get-ConsoleKeyForRoot -RootPath 'Z:\roms\PS3' | Should -Be 'PS3'
    }

    It 'detects AMIGA from "Z:\Amiga"' {
        Get-ConsoleKeyForRoot -RootPath 'Z:\Amiga' | Should -Be 'AMIGA'
    }

    It 'detects DOS from "Z:\roms\msdos"' {
        Get-ConsoleKeyForRoot -RootPath 'Z:\roms\msdos' | Should -Be 'DOS'
    }

    It 'returns null for unrecognised root "Z:\stuff"' {
        Get-ConsoleKeyForRoot -RootPath 'Z:\stuff' | Should -BeNullOrEmpty
    }
}

Describe 'FolderDedupe – Test-RootNeedsFolderDedupe / Test-RootNeedsPs3Dedupe' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Classification.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')
    }

    It 'DOS root needs folder dedupe' {
        Test-RootNeedsFolderDedupe -RootPath 'Z:\MS-DOS' | Should -BeTrue
    }

    It 'PS3 root does NOT need folder dedupe' {
        Test-RootNeedsFolderDedupe -RootPath 'Z:\PS3' | Should -BeFalse
    }

    It 'PS3 root needs PS3 dedupe' {
        Test-RootNeedsPs3Dedupe -RootPath 'Z:\PS3' | Should -BeTrue
    }

    It 'DOS root does NOT need PS3 dedupe' {
        Test-RootNeedsPs3Dedupe -RootPath 'Z:\MS-DOS' | Should -BeFalse
    }

    It 'SNES root needs neither folder nor PS3 dedupe' {
        Test-RootNeedsFolderDedupe -RootPath 'Z:\SNES' | Should -BeFalse
        Test-RootNeedsPs3Dedupe   -RootPath 'Z:\SNES' | Should -BeFalse
    }
}

Describe 'FolderDedupe – Invoke-AutoFolderDedupe' {

    BeforeAll {
        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }
        . (Join-Path $root 'dev\modules\Settings.ps1')
        . (Join-Path $root 'dev\modules\FileOps.ps1')
        . (Join-Path $root 'dev\modules\Classification.ps1')
        . (Join-Path $root 'dev\modules\FolderDedupe.ps1')

        # Stub Test-CancelRequested if not loaded
        if (-not (Get-Command Test-CancelRequested -ErrorAction SilentlyContinue)) {
            function global:Test-CancelRequested { }
        }
    }

    It 'detects and dedupes a DOS root automatically in DryRun' {
        $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('AutoDedupeTest_DOS_' + [guid]::NewGuid().ToString('N'))
        $dosRoot = Join-Path $tempRoot 'MS-DOS'
        New-Item -ItemType Directory -Path $dosRoot -Force | Out-Null
        try {
            New-Item -ItemType Directory -Path (Join-Path $dosRoot 'Doom') -Force | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $dosRoot 'Doom (v1.1)') -Force | Out-Null
            Set-Content -Path (Join-Path $dosRoot 'Doom' 'game.exe') -Value 'old'
            Set-Content -Path (Join-Path $dosRoot 'Doom (v1.1)' 'game.exe') -Value 'new'

            $result = Invoke-AutoFolderDedupe -Roots @($dosRoot) -Mode DryRun
            $result.FolderRoots.Count | Should -Be 1
            $result.Ps3Roots.Count | Should -Be 0
            $result.Results.Count | Should -Be 1
            $result.Results[0].Type | Should -Be 'FolderBaseName'
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'skips roots that do not match folder-based consoles (e.g. SNES)' {
        $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('AutoDedupeTest_SNES_' + [guid]::NewGuid().ToString('N'))
        $snesRoot = Join-Path $tempRoot 'SNES'
        New-Item -ItemType Directory -Path $snesRoot -Force | Out-Null
        try {
            New-Item -ItemType Directory -Path (Join-Path $snesRoot 'GameA') -Force | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $snesRoot 'GameA (v1)') -Force | Out-Null

            $result = Invoke-AutoFolderDedupe -Roots @($snesRoot) -Mode DryRun
            $result.FolderRoots.Count | Should -Be 0
            $result.Ps3Roots.Count | Should -Be 0
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }

    It 'handles mixed roots (DOS + SNES)' {
        $tempRoot = Join-Path ([IO.Path]::GetTempPath()) ('AutoDedupeTest_Mix_' + [guid]::NewGuid().ToString('N'))
        $dosRoot  = Join-Path $tempRoot 'MS-DOS'
        $snesRoot = Join-Path $tempRoot 'SNES'
        New-Item -ItemType Directory -Path $dosRoot -Force | Out-Null
        New-Item -ItemType Directory -Path $snesRoot -Force | Out-Null
        try {
            New-Item -ItemType Directory -Path (Join-Path $dosRoot 'Doom') -Force | Out-Null
            New-Item -ItemType Directory -Path (Join-Path $dosRoot 'Doom (v2)') -Force | Out-Null
            Set-Content -Path (Join-Path $dosRoot 'Doom' 'g.exe') -Value 'x'
            Set-Content -Path (Join-Path $dosRoot 'Doom (v2)' 'g.exe') -Value 'x'
            New-Item -ItemType Directory -Path (Join-Path $snesRoot 'Zelda') -Force | Out-Null

            $result = Invoke-AutoFolderDedupe -Roots @($dosRoot, $snesRoot) -Mode DryRun
            $result.FolderRoots.Count | Should -Be 1
            $result.FolderRoots[0] | Should -Be $dosRoot
        } finally {
            Remove-Item -LiteralPath $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}
