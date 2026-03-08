#requires -Modules Pester
<#
  ROM Cleanup - Startup/Parse Tests
  ==================================
  Diese Tests prüfen ob das Skript überhaupt gestartet werden kann,
  OHNE es tatsächlich auszuführen (keine GUI, kein dot-sourcing).
  
  Bug it would catch: Missing -Name in Add-Type -MemberDefinition
  (causes PowerShell to prompt for input and block startup)
  
  Run with: Invoke-Pester -Path .\tests\Startup.Tests.ps1 -Output Detailed
#>

Describe 'Script Startup' {
    BeforeAll {
        $script:ScriptPath = Join-Path $PSScriptRoot '..\..\simple_sort.ps1'
    }
    
    It 'Script file exists' {
        Test-Path -LiteralPath $script:ScriptPath | Should -BeTrue
    }
    
    It 'Script has no syntax errors' {
        $tokens = $null
        $errors = $null
        $null = [System.Management.Automation.Language.Parser]::ParseFile($script:ScriptPath, [ref]$tokens, [ref]$errors)
        
        if ($errors.Count -gt 0) {
            $errorMessages = $errors | ForEach-Object { "Line $($_.Extent.StartLineNumber): $($_.Message)" }
            $errorMessages -join "`n" | Should -Be '' -Because "Script must parse without syntax errors"
        }
        $errors.Count | Should -Be 0
    }
    
    It 'All Add-Type -MemberDefinition calls have -Name parameter' {
        # Bug: Add-Type -MemberDefinition without -Name prompts for input
        $content = Get-Content -LiteralPath $script:ScriptPath -Raw
        
        # Pattern: Add-Type followed by -MemberDefinition but NOT -Name before the here-string
        $lines = $content -split "`n"
        $inMemberDef = $false
        $memberDefStart = 0
        $hasName = $false
        
        for ($i = 0; $i -lt $lines.Count; $i++) {
            $line = $lines[$i]
            
            if ($line -match 'Add-Type\s.*-MemberDefinition') {
                $inMemberDef = $true
                $memberDefStart = $i + 1
                $hasName = $line -match '-Name'
            }
            
            if ($inMemberDef -and $line -match "^'@") {
                # End of MemberDefinition
                if (-not $hasName) {
                    "Add-Type -MemberDefinition at line $memberDefStart is missing -Name parameter" | 
                        Should -Be '' -Because "Add-Type -MemberDefinition requires -Name to avoid prompting"
                }
                $inMemberDef = $false
            }
        }
    }
    
    It 'Script can be executed without blocking on parameter prompts' {
        # Run the script in a separate process with a timeout
        # If it prompts for parameters, it will timeout
        $psi = [System.Diagnostics.ProcessStartInfo]::new()
        $psi.FileName = 'powershell.exe'
        $psi.Arguments = "-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command `"& { . '$($script:ScriptPath)' } 2>&1`""
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.UseShellExecute = $false
        $psi.CreateNoWindow = $true
        
        $proc = [System.Diagnostics.Process]::Start($psi)
        
        # Give it 10 seconds max - should either start GUI or error quickly
        $exited = $proc.WaitForExit(10000)
        
        if (-not $exited) {
            # Process is still running - could be the GUI or stuck on prompt
            $proc.Kill()
            
            # If it's waiting for input, this is a failure
            # We check stderr for "Supply values for"
            $stdout = $proc.StandardOutput.ReadToEnd()
            $stderr = $proc.StandardError.ReadToEnd()
            if ($stderr -match 'Supply values|Geben Sie Werte') {
                "Script is prompting for parameters: $stderr" | 
                    Should -Be '' -Because "Script must not prompt for mandatory parameters"
            }
            $stderr | Should -Not -Match "Cannot bind argument to parameter 'Roots' because it is an empty array" -Because 'GUI startup must tolerate empty roots and show readiness blocker instead of throwing'
            $stderr | Should -Not -Match 'pipeline has been stopped' -Because 'GUI startup should not surface PipelineStopped host shutdown as user-facing error'
            if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                $numericOnlyLines = @(
                    $stdout -split "`r?`n" |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    Where-Object { $_ -match '^\d+$' }
                )
                @($numericOnlyLines).Count | Should -Be 0 -Because 'Startup must not emit bare numeric lines to stdout'
            }
            # Otherwise it was probably the GUI - that's OK
        } else {
            # Process exited - check for prompt errors
            $stdout = $proc.StandardOutput.ReadToEnd()
            $stderr = $proc.StandardError.ReadToEnd()
            if ($stderr -match 'Supply values|Geben Sie Werte') {
                "Script is prompting for parameters: $stderr" | 
                    Should -Be '' -Because "Script must not prompt for mandatory parameters"
            }
            $stderr | Should -Not -Match "Cannot bind argument to parameter 'Roots' because it is an empty array" -Because 'GUI startup must tolerate empty roots and show readiness blocker instead of throwing'
            $stderr | Should -Not -Match 'pipeline has been stopped' -Because 'GUI startup should not surface PipelineStopped host shutdown as user-facing error'
            if (-not [string]::IsNullOrWhiteSpace($stdout)) {
                $numericOnlyLines = @(
                    $stdout -split "`r?`n" |
                    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
                    Where-Object { $_ -match '^\d+$' }
                )
                @($numericOnlyLines).Count | Should -Be 0 -Because 'Startup must not emit bare numeric lines to stdout'
            }
        }
    }

    It 'Script should tolerate repeated startup initialization in same process' {
        $scriptContent = Get-Content -LiteralPath $script:ScriptPath -Raw
        $scriptContent = $scriptContent -replace '\[void\]\$form\.ShowDialog\(\)', '# GUI disabled'
        $scriptContent = $scriptContent -replace '\$form\.Add_Shown\(\{[^}]+\}\)', '# Shown disabled'
        $scriptContent = $scriptContent -replace 'if \(\[System\.Threading\.Thread\]::CurrentThread\.ApartmentState -ne \[System\.Threading\.ApartmentState\]::STA\) \{', 'if ($false -and [System.Threading.Thread]::CurrentThread.ApartmentState -ne [System.Threading.ApartmentState]::STA) {'
        $scriptContent = $scriptContent -replace '(?s)# GUI-BLOCK-BEGIN[^\r\n]*\r?\n.*?# GUI-BLOCK-END[^\r\n]*', '# GUI block stripped for tests'

        $tempScript = Join-Path $env:TEMP ("startup_repeat_{0}.ps1" -f (Get-Random))
        try {
            $scriptContent | Out-File -LiteralPath $tempScript -Encoding utf8 -Force
            { . $tempScript; . $tempScript } | Should -Not -Throw -Because 'SetCompatibleTextRenderingDefault errors must be suppressed in host processes'
        } finally {
            Remove-Item -LiteralPath $tempScript -Force -ErrorAction SilentlyContinue
        }
    }

    It 'Script should contain and invoke required module function self-test' {
        $content = Get-Content -LiteralPath $script:ScriptPath -Raw
        $content | Should -Match 'function\s+Test-RequiredModuleFunctions'
        $content | Should -Match 'if\s*\(-not\s*\(Test-RequiredModuleFunctions\)\)\s*\{[^}]*return'
    }

    It 'Script should resolve module directory robustly (incl. CWD fallback)' {
        $content = Get-Content -LiteralPath $script:ScriptPath -Raw
        $content | Should -Match 'function\s+Resolve-ModuleDirectory'
        $content | Should -Match 'ROM_CLEANUP_MODULE_DIR'
        $content | Should -Match 'Get-Location'
    }
}
