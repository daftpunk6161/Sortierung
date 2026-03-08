Describe 'Parallele Konvertierung' {
    BeforeAll {
        . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
        $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'rom_parallel_test'
        $script:TempScript = $ctx.TempScript
        . $script:TempScript
    }

    AfterAll {
        Remove-SimpleSortTestTempScript -TempScript $script:TempScript
    }

    Context 'New-ConversionRunspacePool' {
        It 'erstellt einen geoeffneten Pool' {
            $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
            $logQueue    = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
            try {
                $pool = New-ConversionRunspacePool -MaxParallel 2 -ToolPaths @{} `
                    -CancelEvent $cancelEvent -LogQueue $logQueue
                $pool | Should -Not -BeNullOrEmpty
                $pool.RunspacePoolStateInfo.State | Should -Be 'Opened'
            } finally {
                if ($pool) { $pool.Close(); $pool.Dispose() }
                $cancelEvent.Dispose()
            }
        }

        It 'injiziert ConvertTo-QuotedArg in den Pool' {
            $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
            $logQueue    = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
            try {
                $pool = New-ConversionRunspacePool -MaxParallel 2 -ToolPaths @{} `
                    -CancelEvent $cancelEvent -LogQueue $logQueue

                $ps = [PowerShell]::Create()
                $ps.RunspacePool = $pool
                [void]$ps.AddScript({ ConvertTo-QuotedArg 'hello world' })
                $result = $ps.Invoke()
                $ps.Dispose()

                $result | Should -Not -BeNullOrEmpty
                ($result | Select-Object -Last 1) | Should -Be '"hello world"'
            } finally {
                if ($pool) { $pool.Close(); $pool.Dispose() }
                $cancelEvent.Dispose()
            }
        }

        It 'Cancel-Event stoppt Test-CancelRequested im Pool' {
            $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
            $logQueue    = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
            try {
                $pool = New-ConversionRunspacePool -MaxParallel 2 -ToolPaths @{} `
                    -CancelEvent $cancelEvent -LogQueue $logQueue

                # Ohne Cancel: kein Fehler
                $ps1 = [PowerShell]::Create()
                $ps1.RunspacePool = $pool
                [void]$ps1.AddScript({
                    $script:_CancelEvent = $global:_CancelEvent
                    Test-CancelRequested
                    'ok'
                })
                [void]$ps1.Invoke()
                $ps1.HadErrors | Should -BeFalse
                $ps1.Dispose()

                # Mit Cancel: OperationCanceledException
                $cancelEvent.Set()
                $ps2 = [PowerShell]::Create()
                $ps2.RunspacePool = $pool
                [void]$ps2.AddScript({
                    $script:_CancelEvent = $global:_CancelEvent
                    Test-CancelRequested
                })
                { $ps2.Invoke() } | Should -Throw
                $ps2.Dispose()
            } finally {
                if ($pool) { $pool.Close(); $pool.Dispose() }
                $cancelEvent.Dispose()
            }
        }

        It 'Log-Queue empfaengt Nachrichten aus dem Pool' {
            $cancelEvent = [System.Threading.ManualResetEventSlim]::new($false)
            $logQueue    = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
            try {
                $pool = New-ConversionRunspacePool -MaxParallel 2 -ToolPaths @{} `
                    -CancelEvent $cancelEvent -LogQueue $logQueue

                $ps = [PowerShell]::Create()
                $ps.RunspacePool = $pool
                [void]$ps.AddScript({
                    $script:_LogQueue = $global:_LogQueue
                    $script:_LogQueue.Enqueue('Test-Nachricht')
                })
                $ps.Invoke()
                $ps.Dispose()

                $msg = $null
                $logQueue.TryDequeue([ref]$msg) | Should -BeTrue
                $msg | Should -Be 'Test-Nachricht'
            } finally {
                if ($pool) { $pool.Close(); $pool.Dispose() }
                $cancelEvent.Dispose()
            }
        }
    }

    Context 'Invoke-ConvertBatchParallel' {
        It 'gibt Ergebnis-Hashtable mit korrekten Zaehlerfeldern zurueck' {
            # Mock: alle Items skippen (Quelle fehlt)
            Mock -CommandName Test-Path -MockWith { return $false } -ParameterFilter {
                $LiteralPath -and $LiteralPath -like '*fake*'
            }

            $candidates = [System.Collections.Generic.List[psobject]]::new()
            $item = [pscustomobject]@{
                MainPath  = 'C:\fake\game.cue'
                Root      = 'C:\fake'
                Paths     = @('C:\fake\game.cue')
                Type      = 'FILE'
                SizeBytes = 1000
            }
            $item | Add-Member -NotePropertyName '_TargetFormat' -NotePropertyValue @{
                Ext  = '.chd'
                Tool = 'chdman'
                Cmd  = 'createcd'
            }
            [void]$candidates.Add($item)

            $logLines = [System.Collections.Generic.List[string]]::new()
            $logFn = { param($m) [void]$logLines.Add($m) }

            $result = Invoke-ConvertBatchParallel -Candidates $candidates -Tools @{ chdman = 'C:\fake\chdman.exe' } `
                -MaxParallel 2 -Log $logFn -OnProgress $null `
                -RetryableReasons ([System.Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)) `
                -MaxRetries 0 -RetryDelayMs 0

            $result | Should -Not -BeNullOrEmpty
            $result.Keys | Should -Contain 'ConvertCount'
            $result.Keys | Should -Contain 'SkipCount'
            $result.Keys | Should -Contain 'ErrorCount'
            $result.Keys | Should -Contain 'TotalSaved'
            $result.Keys | Should -Contain 'AuditRows'
            # Item hat SKIP weil Quelle nicht existiert
            ($result.SkipCount -gt 0 -or $result.ErrorCount -gt 0) | Should -BeTrue
        }
    }

    Context 'Invoke-FormatConversion MaxParallel Parameter' {
        It 'akzeptiert MaxParallel-Parameter ohne Fehler' {
            # Mock alle externen Aufrufe
            Mock -CommandName Find-ConversionTool -MockWith { return $null }
            Mock -CommandName Get-ConsoleType -MockWith { return $null }
            Mock -CommandName Get-TargetFormat -MockWith { return $null }

            $winners = [System.Collections.Generic.List[psobject]]::new()
            $logLines = [System.Collections.Generic.List[string]]::new()

            { Invoke-FormatConversion -Winners $winners -Roots @('C:\fake') `
                -Log { param($m) [void]$logLines.Add($m) } -MaxParallel 2 } | Should -Not -Throw
        }

        It 'Fallback auf sequentiell bei MaxParallel=1' {
            Mock -CommandName Find-ConversionTool -MockWith { return $null }

            $winners = [System.Collections.Generic.List[psobject]]::new()
            $logLines = [System.Collections.Generic.List[string]]::new()

            Invoke-FormatConversion -Winners $winners -Roots @('C:\fake') `
                -Log { param($m) [void]$logLines.Add($m) } -MaxParallel 1

            # Kein "Parallele Konvertierung" Log bei MaxParallel=1
            $logLines | Where-Object { $_ -match 'Parallele Konvertierung' } | Should -BeNullOrEmpty
        }
    }
}
