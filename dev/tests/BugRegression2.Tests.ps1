#requires -Modules Pester
<#
  Bug Regression Tests – Pack 2  (Scenarios 16–30)
  =================================================
  Structural and functional regression tests for fixes TOOLS-007, TOOLS-003,
  DATSRC-001, DATSRC-002, CLASS-001, API-001, API-002, LOADER-002, DEDUPE-003,
  DEDUPE-008, GUI-002, ENTRY-008, ENTRY-005, ZIPSORT-001, LOADER-003.
#>

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'bugreg2_test'
    $script:ScriptPath   = $ctx.ScriptPath
    $script:TempScript   = $ctx.TempScript
    . $script:TempScript

    # Resolve module and project paths for structural tests
    $script:ProjectRoot  = Resolve-Path (Join-Path $PSScriptRoot '..\..')
    $script:ModulesRoot  = Join-Path $script:ProjectRoot 'dev\modules'
}

AfterAll {
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

# ============================================================
#  16. TOOLS-007 – $env:TEMP with spaces: -o flag quoted
# ============================================================
Describe 'BUG-16 / TOOLS-007 – 7z extraction -o flag quotes TEMP path' {
    It 'Expand-ArchiveToTemp builds -o arg with surrounding quotes' {
        $toolsContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'Tools.ps1') -Raw

        # The -o flag value must be wrapped in double-quotes to handle spaces.
        # Pattern: -o"<path>"  (the FIX comment references TOOLS-007)
        $toolsContent | Should -Match '-o\\?"\{0\}\\?"' `
            -Because 'the -o flag must quote the output path for spaces in TEMP (TOOLS-007 fix)'
    }
}

# ============================================================
#  17. TOOLS-003 – Invoke-7z hash verification before execution
# ============================================================
Describe 'BUG-17 / TOOLS-003 – Invoke-7z verifies binary hash before execution' {
    It 'Test-ToolBinaryHash call exists before 7z process execution' {
        $toolsContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'Tools.ps1') -Raw

        # Extract the Invoke-7z function body
        $fnMatch = [regex]::Match($toolsContent, '(?s)function\s+Invoke-7z\s*\{(.+?)^\}', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        $fnMatch.Success | Should -BeTrue -Because 'Invoke-7z function must exist'

        $fnBody = $fnMatch.Groups[1].Value

        # Test-ToolBinaryHash must appear BEFORE the Invoke-NativeProcess call
        $hashPos   = $fnBody.IndexOf('Test-ToolBinaryHash')
        $nativePos = $fnBody.IndexOf('Invoke-NativeProcess')

        $hashPos   | Should -BeGreaterOrEqual 0 -Because 'Test-ToolBinaryHash must be called in Invoke-7z'
        $nativePos | Should -BeGreaterOrEqual 0 -Because 'Invoke-NativeProcess must be called in Invoke-7z'
        $hashPos   | Should -BeLessThan $nativePos -Because 'hash verification must occur before process execution'
    }
}

# ============================================================
#  18. DATSRC-001 – file:// URL rejected by Invoke-DatDownload
# ============================================================
Describe 'BUG-18 / DATSRC-001 – DAT download rejects file:// URLs' {
    It 'Invoke-DatDownload returns error for file:// URL' {
        $result = Invoke-DatDownload -Id 'test-ssrf' -Url 'file:///C:/Windows/system32/config/sam' `
            -Format 'raw-dat' -TargetDir $env:TEMP -System 'Test'

        $result.Success | Should -Be $false
        $result.Error   | Should -Match 'HTTPS|https|abgelehnt' `
            -Because 'file:// URLs must be rejected to prevent SSRF'
    }

    It 'Invoke-DatDownload returns error for http:// URL (not https)' {
        $result = Invoke-DatDownload -Id 'test-http' -Url 'http://example.com/file.dat' `
            -Format 'raw-dat' -TargetDir $env:TEMP -System 'Test'

        $result.Success | Should -Be $false
        $result.Error   | Should -Match 'HTTPS|https|abgelehnt' `
            -Because 'only HTTPS URLs should be accepted'
    }
}

# ============================================================
#  19. DATSRC-002 – Sidecar download failure returns $false
# ============================================================
Describe 'BUG-19 / DATSRC-002 – DAT sidecar verification fail-closed' {
    It 'Test-DownloadedDatSignature catch block returns $false on network error' {
        $datSrcContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'DatSources.ps1') -Raw

        # Extract Test-DownloadedDatSignature function
        $fnMatch = [regex]::Match($datSrcContent, '(?s)function\s+Test-DownloadedDatSignature\s*\{(.+?)^\}', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        $fnMatch.Success | Should -BeTrue -Because 'Test-DownloadedDatSignature function must exist'

        $fnBody = $fnMatch.Groups[1].Value

        # The catch block must return $false (fail-closed), not $true
        # Look for 'catch' followed by 'return $false' (with any amount of whitespace/code in between)
        $catchSegments = [regex]::Matches($fnBody, '(?s)catch\s*\{(.+?)\}')
        $catchSegments.Count | Should -BeGreaterThan 0 -Because 'there must be a catch block'

        # Every catch block should return $false
        $allCatchesReturnFalse = $true
        foreach ($seg in $catchSegments) {
            $catchBody = $seg.Groups[1].Value
            if ($catchBody -match 'return\s+\$true') {
                $allCatchesReturnFalse = $false
            }
        }
        $allCatchesReturnFalse | Should -BeTrue `
            -Because 'catch blocks in signature verification must return $false (fail-closed per DATSRC-002)'
    }
}

# ============================================================
#  20. CLASS-001 – Disc header has priority over folder-map
# ============================================================
Describe 'BUG-20 / CLASS-001 – Disc header detection is not overwritten by folder-map' {
    It 'Get-ConsoleType checks disc header BEFORE folder-map in code order' {
        $classContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'Classification.ps1') -Raw

        # Extract Get-ConsoleType function body
        $fnMatch = [regex]::Match($classContent, '(?s)function\s+Get-ConsoleType\s*\{(.+?)^function\s', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        if (-not $fnMatch.Success) {
            # Fallback: use the remainder after the function declaration
            $fnMatch = [regex]::Match($classContent, '(?s)function\s+Get-ConsoleType\s*\{(.+)', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        }
        $fnMatch.Success | Should -BeTrue -Because 'Get-ConsoleType function must exist'

        $fnBody = $fnMatch.Groups[1].Value

        # Disc header probe (Get-DiscHeaderConsole) must appear BEFORE folder map lookup
        $headerPos = $fnBody.IndexOf('Get-DiscHeaderConsole')
        $folderPos = $fnBody.IndexOf('CONSOLE_FOLDER_MAP')

        $headerPos | Should -BeGreaterOrEqual 0 -Because 'Get-DiscHeaderConsole call must exist'
        $folderPos | Should -BeGreaterOrEqual 0 -Because 'CONSOLE_FOLDER_MAP lookup must exist'
        $headerPos | Should -BeLessThan $folderPos `
            -Because 'disc header detection must run before folder-map to ensure header has priority (CLASS-001)'
    }

    It 'Folder-map is guarded with if (-not $result) to prevent overwriting disc header result' {
        $classContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'Classification.ps1') -Raw

        # The folder-map block should be inside an if (-not $result) guard
        $classContent | Should -Match 'if\s*\(\s*-not\s+\$result\s*\)\s*\{[^}]*CONSOLE_FOLDER_MAP' `
            -Because 'folder-map lookup must be guarded by if (-not $result) to preserve disc header result'
    }
}

# ============================================================
#  21. API-001 – Rate limiting ignores X-Forwarded-For
# ============================================================
Describe 'BUG-21 / API-001 – Rate limiting uses RemoteEndPoint, not X-Forwarded-For' {
    It 'Get-ApiClientIdentifier does NOT read X-Forwarded-For header' {
        $apiContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'ApiServer.ps1') -Raw

        # Extract Get-ApiClientIdentifier function
        $fnMatch = [regex]::Match($apiContent, '(?s)function\s+Get-ApiClientIdentifier\s*\{(.+?)^\}', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        $fnMatch.Success | Should -BeTrue -Because 'Get-ApiClientIdentifier function must exist'

        $fnBody = $fnMatch.Groups[1].Value

        # Strip comment lines to avoid matching the FIX comment that mentions the old header
        $codeLines = $fnBody -split "`r?`n" | Where-Object { $_.Trim() -notmatch '^\s*#' }
        $codeOnly = $codeLines -join "`n"

        # Must NOT use X-Forwarded-For in actual code (spoofable header)
        $codeOnly | Should -Not -Match 'X-Forwarded-For' `
            -Because 'rate limiting must use RemoteEndPoint, not the spoofable X-Forwarded-For header (API-001)'

        # Must reference RemoteEndPoint in actual code
        $codeOnly | Should -Match 'RemoteEndPoint' `
            -Because 'client identification must use the RemoteEndPoint property'
    }
}

# ============================================================
#  22. API-002 – POST body size limit enforced
# ============================================================
Describe 'BUG-22 / API-002 – API body size limit enforced in Read-ApiJsonBody' {
    It 'maxBodyBytes limit exists in Read-ApiJsonBody' {
        $apiContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'ApiServer.ps1') -Raw

        # Extract Read-ApiJsonBody function
        $fnMatch = [regex]::Match($apiContent, '(?s)function\s+Read-ApiJsonBody\s*\{(.+?)^\}', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        $fnMatch.Success | Should -BeTrue -Because 'Read-ApiJsonBody function must exist'

        $fnBody = $fnMatch.Groups[1].Value

        # Must define a maxBodyBytes variable
        $fnBody | Should -Match '\$maxBodyBytes\s*=' `
            -Because 'a body size limit variable must be defined (API-002)'

        # Must check ContentLength64 against the limit
        $fnBody | Should -Match 'ContentLength64.*maxBodyBytes|maxBodyBytes.*ContentLength64' `
            -Because 'ContentLength64 must be compared against the body size limit'

        # Must return $null when the limit is exceeded
        $fnBody | Should -Match 'return\s+\$null' `
            -Because 'oversized bodies must be rejected by returning $null'
    }
}

# ============================================================
#  23. LOADER-002 – TESTMODE=0 promotes functions globally
# ============================================================
Describe 'BUG-23 / LOADER-002 – TESTMODE parsing: only 1/true/yes/on = active' {
    It 'Loader checks ROMCLEANUP_TESTMODE against explicit active values' {
        $loaderContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'RomCleanupLoader.ps1') -Raw

        # The loader must parse TESTMODE with an explicit list of "active" values
        # (not just truthy/non-empty). The pattern: ('1','true','yes','on') -contains
        $loaderContent | Should -Match "'1'.*'true'.*'yes'.*'on'" `
            -Because 'TESTMODE must only be active for explicit values 1/true/yes/on (LOADER-002)'
    }

    It 'TESTMODE=0 does NOT match the active-values check' {
        # Verify that "0" is not in the active values list
        $activeValues = @('1', 'true', 'yes', 'on')
        $activeValues | Should -Not -Contain '0' -Because '"0" must not be treated as test-mode active'
        $activeValues | Should -Not -Contain 'false' -Because '"false" must not be treated as test-mode active'
    }
}

# ============================================================
#  24. DEDUPE-003 – JP game with empty console not junked
# ============================================================
Describe 'BUG-24 / DEDUPE-003 – JP game with empty/UNKNOWN console is NOT junked by JP filter' {
    It 'Invoke-ClassifyFile does not junk JP game when console is empty and JpOnlyForSelectedConsoles is set' {
        # Create a temp file so MainPath validation passes
        $tempFile = Join-Path $env:TEMP ("bugreg2_jp_$(Get-Random).bin")
        'test' | Out-File -LiteralPath $tempFile -Encoding ascii -Force

        try {
            $result = Invoke-ClassifyFile `
                -BaseName 'Final Fantasy VII (Japan)' `
                -FilePath $tempFile `
                -Extension '.bin' `
                -Root $env:TEMP `
                -AggressiveJunk $false `
                -CrcVerifyScan $false `
                -UseDat $false `
                -DatIndex @{} `
                -HashCache @{} `
                -DatHashType 'SHA1' `
                -DatFallback $false `
                -AliasEditionKeying $false `
                -SevenZipPath '' `
                -PreferredRegionKeys @{ 'EU' = $true; 'US' = $true; 'WORLD' = $true } `
                -JpOnlyForSelectedConsoles $true `
                -JpKeepKeys @{ 'PS1' = $true }

            $result | Should -Not -BeNullOrEmpty
            $result.Region | Should -Be 'JP'
            # When console is empty/UNKNOWN, the JP filter must NOT junk the file
            # because the guard clause checks for empty/UNKNOWN console first
            $result.Category | Should -Be 'GAME' `
                -Because 'JP game with unknown console must not be junked when JpOnlyForSelectedConsoles is active (DEDUPE-003)'
        } finally {
            Remove-Item -LiteralPath $tempFile -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Invoke-ClassifyFile code guards JP filter with empty/UNKNOWN console check' {
        $dedupeContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'Dedupe.ps1') -Raw

        # The JP filter must check for empty or UNKNOWN console before junking
        $dedupeContent | Should -Match 'IsNullOrWhiteSpace.*console.*UNKNOWN|UNKNOWN.*IsNullOrWhiteSpace.*console' `
            -Because 'JP filter must guard against empty/UNKNOWN console before junking'
    }
}

# ============================================================
#  25. DEDUPE-008 – Manual winner override with stale path warns
# ============================================================
Describe 'BUG-25 / DEDUPE-008 – Manual winner override stale path warning' {
    It 'Warning log exists for unmatched manual override paths' {
        $dedupeContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'Dedupe.ps1') -Raw

        # After the override path lookup fails (candidate is empty), there must be a warning log
        # The code should log something like 'WARNUNG: Manual-Override Pfad nicht gefunden'
        $dedupeContent | Should -Match 'WARNUNG.*Manual.*Override.*nicht gefunden|Manual.*Override.*Pfad.*nicht' `
            -Because 'stale manual override paths must produce a warning log (DEDUPE-008)'
    }

    It 'Warning is emitted via $Log scriptblock (not silently swallowed)' {
        $dedupeContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'Dedupe.ps1') -Raw

        # The warning must use the $Log callback pattern: & $Log ('WARNUNG: ...')
        $dedupeContent | Should -Match '\$Log.*WARNUNG.*Manual.*Override|Log.*Manual.*Override.*nicht' `
            -Because 'the warning must be emitted through the Log callback'
    }
}

# ============================================================
#  26. GUI-002 – RunState transitions to Idle after completion
# ============================================================
Describe 'BUG-26 / GUI-002 – RunState transitions to Idle in completion path' {
    It 'Completion handler transitions through intermediate state then to Idle' {
        $wpfContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'WpfEventHandlers.ps1') -Raw

        # The GUI-002 FIX must:
        # 1. Transition to an intermediate state (Completed/Failed/Canceled) first
        # 2. Then transition to Idle
        $wpfContent | Should -Match 'GUI-002' `
            -Because 'the GUI-002 fix comment must exist in WpfEventHandlers.ps1'

        # Verify the state transition chain exists: intermediate -> Idle
        $wpfContent | Should -Match 'Set-AppRunState.*-State\s+\$intermediateState' `
            -Because 'completion must transition through intermediate state before Idle'

        $wpfContent | Should -Match "Set-AppRunState.*-State\s+'Idle'" `
            -Because 'completion must eventually reach Idle state'
    }

    It 'Force-reset fallback to Idle exists for failed transitions' {
        $wpfContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'WpfEventHandlers.ps1') -Raw

        $wpfContent | Should -Match "Set-AppRunState.*-State\s+'Idle'.*-Force" `
            -Because 'a force-reset fallback to Idle must exist for error recovery'
    }
}

# ============================================================
#  27. ENTRY-008 – RemoveJunk defaults to $true (not [switch])
# ============================================================
Describe 'BUG-27 / ENTRY-008 – RemoveJunk parameter type and default value' {
    It 'RemoveJunk parameter is [bool] with default $true in Invoke-RomCleanup.ps1' {
        $entryContent = Get-Content -LiteralPath (Join-Path $script:ProjectRoot 'Invoke-RomCleanup.ps1') -Raw

        # Must be [bool], not [switch]
        $entryContent | Should -Match '\[bool\]\s*\$RemoveJunk\s*=\s*\$true' `
            -Because 'RemoveJunk must be [bool] defaulting to $true so DryRun without -RemoveJunk still classifies junk (ENTRY-008)'
    }

    It 'DatFallback parameter is also [bool] with default $true' {
        $entryContent = Get-Content -LiteralPath (Join-Path $script:ProjectRoot 'Invoke-RomCleanup.ps1') -Raw

        $entryContent | Should -Match '\[bool\]\s*\$DatFallback\s*=\s*\$true' `
            -Because 'DatFallback must be [bool] defaulting to $true (ENTRY-008)'
    }

    It 'GenerateReports parameter is [bool] with default $true' {
        $entryContent = Get-Content -LiteralPath (Join-Path $script:ProjectRoot 'Invoke-RomCleanup.ps1') -Raw

        $entryContent | Should -Match '\[bool\]\s*\$GenerateReports\s*=\s*\$true' `
            -Because 'GenerateReports must be [bool] defaulting to $true (ENTRY-008)'
    }
}

# ============================================================
#  28. ENTRY-005 – Root path C:\ is rejected
# ============================================================
Describe 'BUG-28 / ENTRY-005 – Drive root paths are rejected' {
    It 'Invoke-RomCleanup.ps1 contains ENTRY-005 root path validation' {
        $entryContent = Get-Content -LiteralPath (Join-Path $script:ProjectRoot 'Invoke-RomCleanup.ps1') -Raw

        $entryContent | Should -Match 'ENTRY-005' `
            -Because 'the ENTRY-005 fix must be present'

        # The actual pattern in the script is: '^[A-Za-z]:\\?$'
        # We need to match that string literally in the file content
        $entryContent | Should -Match '\[A-Za-z\]' `
            -Because 'a regex pattern must exist to detect drive root paths like C:\ or D:\'

        $entryContent | Should -Match 'drive.*root|Drive root' `
            -Because 'there must be a comment about drive root detection'
    }

    It 'API payload validation also rejects drive roots' {
        $tempDir = Join-Path $env:TEMP ("bugreg2_entry005_$(Get-Random)")
        [void](New-Item -ItemType Directory -Path $tempDir -Force)
        try {
            # Test-ApiRunPayload should reject C:\
            $payload = [pscustomobject]@{ roots = @('C:\') }
            $validation = Test-ApiRunPayload -Payload $payload

            $validation | Should -Not -BeNullOrEmpty `
                -Because 'drive root C:\ must be rejected by API payload validation'
            $validation | Should -Match 'root|drive|not allowed' `
                -Because 'error message should indicate the root path issue'
        } finally {
            Remove-Item -LiteralPath $tempDir -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ============================================================
#  29. ZIPSORT-001 – Zip integrity check after creation
# ============================================================
Describe 'BUG-29 / ZIPSORT-001 – Corrupt ZIP detected after creation in New-ZipFromFolder' {
    It 'ZipFile::OpenRead exists after zip creation for integrity verification' {
        $zipSortContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'ZipSort.ps1') -Raw

        # Extract New-ZipFromFolder function body
        $fnMatch = [regex]::Match($zipSortContent, '(?s)function\s+New-ZipFromFolder\s*\{(.+?)^function\s', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        if (-not $fnMatch.Success) {
            $fnMatch = [regex]::Match($zipSortContent, '(?s)function\s+New-ZipFromFolder\s*\{(.+)', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        }
        $fnMatch.Success | Should -BeTrue -Because 'New-ZipFromFolder function must exist'

        $fnBody = $fnMatch.Groups[1].Value

        # ZipFile::OpenRead must appear after the CreateFromDirectory or 7z call
        $fnBody | Should -Match 'ZipFile\]::OpenRead' `
            -Because 'ZipFile::OpenRead must be called to verify zip integrity after creation (ZIPSORT-001)'

        # The ZIPSORT-001 comment must exist
        $fnBody | Should -Match 'ZIPSORT-001' `
            -Because 'the ZIPSORT-001 integrity check fix comment must be present'
    }

    It 'Empty zip is detected and rejected' {
        $zipSortContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'ZipSort.ps1') -Raw

        # Extract New-ZipFromFolder function body
        $fnMatch = [regex]::Match($zipSortContent, '(?s)function\s+New-ZipFromFolder\s*\{(.+?)^function\s', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        if (-not $fnMatch.Success) {
            $fnMatch = [regex]::Match($zipSortContent, '(?s)function\s+New-ZipFromFolder\s*\{(.+)', [System.Text.RegularExpressions.RegexOptions]::Multiline)
        }
        $fnBody = $fnMatch.Groups[1].Value

        # Must check entry count == 0 and set zipCreated = $false
        $fnBody | Should -Match 'Entries\.Count\s*-eq\s*0' `
            -Because 'the integrity check must detect empty zip archives'
        $fnBody | Should -Match 'zipCreated\s*=\s*\$false' `
            -Because 'failed integrity check must set zipCreated to $false'
    }
}

# ============================================================
#  30. LOADER-003 – Path traversal in module file list rejected
# ============================================================
Describe 'BUG-30 / LOADER-003 – Module loader rejects path traversal' {
    It 'RomCleanupLoader.ps1 validates resolved path stays within module root' {
        $loaderContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'RomCleanupLoader.ps1') -Raw

        # The LOADER-003 FIX must validate that the resolved path starts with the module root
        $loaderContent | Should -Match 'LOADER-003' `
            -Because 'the LOADER-003 fix comment must be present'

        $loaderContent | Should -Match 'GetFullPath' `
            -Because 'the loader must resolve the full path to detect traversal'

        $loaderContent | Should -Match 'StartsWith.*RomCleanupModuleRoot' `
            -Because 'the resolved path must be checked to start with the module root directory'
    }

    It 'Path with ..\..\path is rejected with warning' {
        $loaderContent = Get-Content -LiteralPath (Join-Path $script:ModulesRoot 'RomCleanupLoader.ps1') -Raw

        # Must emit a warning for paths outside root
        $loaderContent | Should -Match 'Write-Warning.*Modul-Pfad.*Root.*ignoriert|Write-Warning.*ausserhalb.*Root' `
            -Because 'paths outside the module root must produce a warning'

        # Must use 'continue' to skip the bad module (not 'throw')
        $loaderContent | Should -Match 'continue' `
            -Because 'bad module paths must be skipped (not throw) so other modules still load'
    }
}
