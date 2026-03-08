#requires -Modules Pester

Describe 'Convert coverage harness' {
    BeforeAll {
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root 'simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }

        . (Join-Path $script:root 'dev\modules\RunspaceLifecycle.ps1')
        . (Join-Path $script:root 'dev\modules\Convert.ps1')

        if (-not (Get-Command ConvertTo-QuotedArg -ErrorAction SilentlyContinue)) {
            function ConvertTo-QuotedArg { param([string]$Text) return '"' + $Text + '"' }
        }
        if (-not (Get-Command Invoke-ExternalToolProcess -ErrorAction SilentlyContinue)) {
            function Invoke-ExternalToolProcess { param($ToolPath,$ToolArgs,$TempFiles,$Log,$ErrorLabel,$WorkingDirectory) [pscustomobject]@{ Success=$true; ExitCode=0; ErrorText='' } }
        }
        if (-not (Get-Command Get-ArchiveEntryPaths -ErrorAction SilentlyContinue)) {
            function Get-ArchiveEntryPaths { param($ArchivePath,$SevenZipPath,$TempFiles) @('file.bin') }
        }
        if (-not (Get-Command Test-ArchiveEntryPathsSafe -ErrorAction SilentlyContinue)) {
            function Test-ArchiveEntryPathsSafe { param($EntryPaths) $true }
        }
        if (-not (Get-Command Find-ConversionTool -ErrorAction SilentlyContinue)) {
            function Find-ConversionTool { param([string]$ToolName) 'C:\Tools\chdman.exe' }
        }
        if (-not (Get-Command Expand-ArchiveToTemp -ErrorAction SilentlyContinue)) {
            function Expand-ArchiveToTemp { param($ArchivePath,$SevenZipPath,$Log) return 'SKIP' }
        }
        if (-not (Get-Variable -Name TOOL_CACHE -Scope Script -ErrorAction SilentlyContinue)) {
            $script:TOOL_CACHE = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
        }
    }

    It 'Get-TargetFormat resolves PBP special format' {
        $fmt = Get-TargetFormat -ConsoleType 'PS1' -Extension '.pbp'
        $fmt.Tool | Should -Be 'psxtract'
        $fmt.Cmd | Should -Be 'pbp2chd'
        $fmt.Ext | Should -Be '.chd'
    }

    It 'Get-TargetFormat resolves PS2 to chdman createdvd' {
        $fmt = Get-TargetFormat -ConsoleType 'PS2' -Extension '.iso'
        $fmt.Tool | Should -Be 'chdman'
        $fmt.Cmd | Should -Be 'createdvd'
        $fmt.Ext | Should -Be '.chd'
    }

    It 'Get-ConvertToolStrategy maps known tool names' {
        Get-ConvertToolStrategy -Tool '7z' | Should -Be 'Invoke-ConvertStrategy7z'
        Get-ConvertToolStrategy -Tool 'dolphintool' | Should -Be 'Invoke-ConvertStrategyDolphinTool'
    }

    It 'Get-ConvertToolStrategy returns null for unknown tools' {
        (Get-ConvertToolStrategy -Tool 'unknown') | Should -BeNullOrEmpty
    }

    It 'Test-ConvertedOutputVerified returns missing reason for CHD when verify tool not found' {
        Mock -CommandName Find-ConversionTool -MockWith { $null }
        $script:TOOL_CACHE.Clear()

        $result = Test-ConvertedOutputVerified -TargetPath 'C:\out.chd' -TargetExt '.chd' -FormatInfo @{ Tool='psxtract' } -ToolPath '' -SevenZipPath '' -Log { }
        $result.Success | Should -BeFalse
        $result.Reason | Should -Be 'chd-verify-tool-missing'
    }

    It 'Test-ConvertedOutputVerified returns missing reason for RVZ when tool path is invalid' {
        Mock -CommandName Test-Path -MockWith { $false }

        $result = Test-ConvertedOutputVerified -TargetPath 'C:\out.rvz' -TargetExt '.rvz' -FormatInfo @{ Tool='dolphintool' } -ToolPath 'C:\missing\dolphin.exe' -SevenZipPath '' -Log { }
        $result.Success | Should -BeFalse
        $result.Reason | Should -Be 'rvz-verify-tool-missing'
    }

    It 'Test-ConvertedOutputVerified verifies ZIP output successfully with safe entries' {
        Mock -CommandName Test-Path -MockWith { $true }
        Mock -CommandName Invoke-ExternalToolProcess -MockWith { [pscustomobject]@{ Success=$true; ExitCode=0; ErrorText='' } }
        Mock -CommandName Get-ArchiveEntryPaths -MockWith { @('ok/file.bin') }
        Mock -CommandName Test-ArchiveEntryPathsSafe -MockWith { $true }

        $result = Test-ConvertedOutputVerified -TargetPath 'C:\out.zip' -TargetExt '.zip' -FormatInfo @{ Tool='7z' } -ToolPath 'C:\Tools\7z.exe' -SevenZipPath '' -Log { }
        $result.Success | Should -BeTrue
        $result.Reason | Should -BeNullOrEmpty
    }

    It 'Invoke-CsoToIso retries and succeeds on second strategy' {
        $script:call = 0
        Mock -CommandName Test-Path -MockWith { $true }
        Mock -CommandName Remove-Item -MockWith { }
        Mock -CommandName Invoke-ExternalToolProcess -MockWith {
            $script:call++
            if ($script:call -eq 1) { return [pscustomobject]@{ Success=$false; ExitCode=1; ErrorText='x' } }
            return [pscustomobject]@{ Success=$true; ExitCode=0; ErrorText='' }
        }
        Mock -CommandName Get-Item -MockWith { [pscustomobject]@{ PSIsContainer=$false; Length=100 } }

        $ok = Invoke-CsoToIso -ToolPath 'C:\Tools\ciso.exe' -InputPath 'C:\in.cso' -OutputPath 'C:\out.iso' -TempFiles ([System.Collections.Generic.List[string]]::new()) -Log { }
        $ok | Should -BeTrue
    }

    It 'Invoke-ConvertStrategyPsxtract returns SKIP when source extension is not .pbp' {
        $ctx = @{
            SourceExt = '.iso'
            NewOutcome = { param($Status,$Path,$Reason,$Details) [pscustomobject]@{ Status=$Status; Path=$Path; Reason=$Reason; Details=$Details } }
        }

        $result = Invoke-ConvertStrategyPsxtract -Context $ctx
        $result.Outcome.Status | Should -Be 'SKIP'
        $result.Outcome.Reason | Should -Be 'unknown-tool'
    }

    It 'Invoke-ConvertStrategyPsxtract returns SKIP when chdman is missing' {
        Mock -CommandName Test-Path -MockWith { $false }
        Mock -CommandName Find-ConversionTool -MockWith { $null }
        $script:TOOL_CACHE.Clear()

        $ctx = @{
            SourceExt = '.pbp'
            ToolPath = 'C:\Tools\psxtract.exe'
            SourcePath = 'C:\in.pbp'
            MainPath = 'C:\in.pbp'
            TargetPath = 'C:\out.chd'
            TempDirs = [System.Collections.Generic.List[string]]::new()
            TempFiles = [System.Collections.Generic.List[string]]::new()
            Log = { }
            NewOutcome = { param($Status,$Path,$Reason,$Details) [pscustomobject]@{ Status=$Status; Path=$Path; Reason=$Reason; Details=$Details } }
        }

        $result = Invoke-ConvertStrategyPsxtract -Context $ctx
        $result.Outcome.Status | Should -Be 'SKIP'
        $result.Outcome.Reason | Should -Be 'missing-chdman'
    }

    It 'Invoke-ConvertStrategyDolphinTool skips unsupported source type' {
        $ctx = @{
            SourceExt = '.zip'
            MainPath = 'C:\in.zip'
            SourcePath = 'C:\in.zip'
            ToolPath = 'C:\Tools\DolphinTool.exe'
            TempFiles = [System.Collections.Generic.List[string]]::new()
            Log = { }
            NewOutcome = { param($Status,$Path,$Reason,$Details) [pscustomobject]@{ Status=$Status; Path=$Path; Reason=$Reason; Details=$Details } }
        }

        $result = Invoke-ConvertStrategyDolphinTool -Context $ctx
        $result.Outcome.Status | Should -Be 'SKIP'
        $result.Outcome.Reason | Should -Be 'dolphintool-unsupported-source'
    }

    It 'Invoke-ConvertStrategyDolphinTool skips already-compressed RVZ' {
        $ctx = @{
            SourceExt = '.rvz'
            MainPath = 'C:\in.rvz'
            SourcePath = 'C:\in.rvz'
            ToolPath = 'C:\Tools\DolphinTool.exe'
            TempFiles = [System.Collections.Generic.List[string]]::new()
            Log = { }
            NewOutcome = { param($Status,$Path,$Reason,$Details) [pscustomobject]@{ Status=$Status; Path=$Path; Reason=$Reason; Details=$Details } }
        }

        $result = Invoke-ConvertStrategyDolphinTool -Context $ctx
        $result.Outcome.Status | Should -Be 'SKIP'
        $result.Outcome.Reason | Should -Be 'dolphintool-already-compressed'
    }

    It 'Invoke-ConvertStrategy7z skips when source is already zip' {
        $ctx = @{
            SourceExt = '.zip'
            MainPath = 'C:\in.zip'
            TargetPath = 'C:\out.zip'
            ToolPath = 'C:\Tools\7z.exe'
            SevenZipPath = 'C:\Tools\7z.exe'
            TempFiles = [System.Collections.Generic.List[string]]::new()
            TempDirs = [System.Collections.Generic.List[string]]::new()
            Log = { }
            NewOutcome = { param($Status,$Path,$Reason,$Details) [pscustomobject]@{ Status=$Status; Path=$Path; Reason=$Reason; Details=$Details } }
        }

        $result = Invoke-ConvertStrategy7z -Context $ctx
        $result.Outcome.Status | Should -Be 'SKIP'
        $result.Outcome.Reason | Should -Be 'already-zip'
    }

    It 'Invoke-ConvertStrategy7z returns archive-expand-skip when expansion signals skip' {
        Mock -CommandName Expand-ArchiveToTemp -MockWith { 'SKIP' }

        $ctx = @{
            SourceExt = '.7z'
            MainPath = 'C:\in.7z'
            TargetPath = 'C:\out.zip'
            ToolPath = 'C:\Tools\7z.exe'
            SevenZipPath = 'C:\Tools\7z.exe'
            TempFiles = [System.Collections.Generic.List[string]]::new()
            TempDirs = [System.Collections.Generic.List[string]]::new()
            Log = { }
            NewOutcome = { param($Status,$Path,$Reason,$Details) [pscustomobject]@{ Status=$Status; Path=$Path; Reason=$Reason; Details=$Details } }
        }

        $result = Invoke-ConvertStrategy7z -Context $ctx
        $result.Outcome.Status | Should -Be 'SKIP'
        $result.Outcome.Reason | Should -Be 'archive-expand-skip'
    }

    It 'Invoke-ConvertItem returns SKIP when source is missing' {
        Mock -CommandName Test-Path -MockWith { $false }

        $item = [pscustomobject]@{ MainPath = 'C:\roms\game.iso'; Root = 'C:\roms'; Paths = @('C:\roms\game.iso'); Type = 'GAME' }
        $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

        $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { } -SevenZipPath 'C:\Tools\7z.exe'
        $result.Status | Should -Be 'SKIP'
        $result.Reason | Should -Be 'missing-source'
    }

    It 'Invoke-ConvertItem returns SKIP when target already exists' {
        Mock -CommandName Test-Path -MockWith {
            param($LiteralPath)
            if ($LiteralPath -eq 'C:\roms\game.iso') { return $true }
            if ($LiteralPath -eq 'C:\roms\game.chd') { return $true }
            return $false
        }

        $item = [pscustomobject]@{ MainPath = 'C:\roms\game.iso'; Root = 'C:\roms'; Paths = @('C:\roms\game.iso'); Type = 'GAME' }
        $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

        $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { } -SevenZipPath 'C:\Tools\7z.exe'
        $result.Status | Should -Be 'SKIP'
        $result.Reason | Should -Be 'target-exists'
    }

    It 'Invoke-ConvertItem returns SKIP for unknown conversion tool' {
        Mock -CommandName Test-Path -MockWith {
            param($LiteralPath)
            if ($LiteralPath -eq 'C:\roms\game.iso') { return $true }
            return $false
        }
        Mock -CommandName Get-ConvertToolStrategy -MockWith { $null }

        $item = [pscustomobject]@{ MainPath = 'C:\roms\game.iso'; Root = 'C:\roms'; Paths = @('C:\roms\game.iso'); Type = 'GAME' }
        $fmt = @{ Ext = '.chd'; Tool = 'unknownTool'; Cmd = 'x' }

        $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\x.exe' -Log { } -SevenZipPath 'C:\Tools\7z.exe'
        $result.Status | Should -Be 'SKIP'
        $result.Reason | Should -Be 'unknown-tool'
    }

    It 'Invoke-ConvertItem returns ERROR when strategy does not create output' {
        Mock -CommandName Test-Path -MockWith {
            param($LiteralPath)
            if ($LiteralPath -eq 'C:\roms\game.iso') { return $true }
            return $false
        }
        Mock -CommandName Get-ConvertToolStrategy -MockWith { 'Invoke-FakeConvertStrategy' }
        function Invoke-FakeConvertStrategy { param([hashtable]$Context) [pscustomobject]@{ Outcome = $null } }
        Mock -CommandName Get-Item -MockWith { $null }

        $item = [pscustomobject]@{ MainPath = 'C:\roms\game.iso'; Root = 'C:\roms'; Paths = @('C:\roms\game.iso'); Type = 'GAME' }
        $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

        $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { } -SevenZipPath 'C:\Tools\7z.exe'
        $result.Status | Should -Be 'ERROR'
        $result.Reason | Should -Be 'output-missing-or-empty'
    }

    It 'Invoke-ConvertItem returns OK when strategy produced valid output and verify passes' {
        Mock -CommandName Test-Path -MockWith {
            param($LiteralPath)
            if ($LiteralPath -eq 'C:\roms\game.iso') { return $true }
            if ($LiteralPath -eq 'C:\roms\game.chd') { return $false }
            return $false
        }
        Mock -CommandName Get-ConvertToolStrategy -MockWith { 'Invoke-FakeConvertStrategy' }
        function Invoke-FakeConvertStrategy { param([hashtable]$Context) [pscustomobject]@{ Outcome = $null } }
        Mock -CommandName Get-Item -MockWith { [pscustomobject]@{ PSIsContainer = $false; Length = 12345 } }
        Mock -CommandName Test-ConvertedOutputVerified -MockWith { [pscustomobject]@{ Success = $true; Reason = $null } }
        Mock -CommandName Move-Item -MockWith { }
        Mock -CommandName Remove-Item -MockWith { }

        $item = [pscustomobject]@{ MainPath = 'C:\roms\game.iso'; Root = 'C:\roms'; Paths = @('C:\roms\game.iso'); Type = 'GAME' }
        $fmt = @{ Ext = '.chd'; Tool = 'chdman'; Cmd = 'createcd' }

        $result = Invoke-ConvertItem -Item $item -FormatInfo $fmt -ToolPath 'C:\Tools\chdman.exe' -Log { } -SevenZipPath 'C:\Tools\7z.exe'
        $result.Status | Should -Be 'OK'
    }

    It 'Move-ConvertedSourceToBackup should create backup and remove stale backup files' {
        Push-Location $TestDrive
        try {
            $source = Join-Path $TestDrive 'sample.iso'
            'payload' | Out-File -LiteralPath $source -Encoding ascii -Force

            $stale = Join-Path $TestDrive 'old.iso.converted_backup'
            'old' | Out-File -LiteralPath $stale -Encoding ascii -Force
            (Get-Item -LiteralPath $stale).LastWriteTime = (Get-Date).AddDays(-10)

            $backupPath = Move-ConvertedSourceToBackup -Path $source -RetentionDays 7 -Log { }

            [string]::IsNullOrWhiteSpace([string]$backupPath) | Should -BeFalse
            Test-Path -LiteralPath $source | Should -BeFalse
            Test-Path -LiteralPath $backupPath | Should -BeTrue
            Test-Path -LiteralPath $stale | Should -BeFalse
        } finally {
            Pop-Location
        }
    }
}
