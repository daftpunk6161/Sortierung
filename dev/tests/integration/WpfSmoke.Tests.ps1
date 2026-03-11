#requires -Modules Pester
# ================================================================
#  WpfSmoke.Tests.ps1  –  Integration Smoke Test
#  Verifies that the WPF Window can be constructed from XAML
#  without throwing on an STA thread.
#  Must NOT call ShowDialog() (would block CI).
# ================================================================

Describe 'WPF Smoke – Fenster konstruierbar ohne Ausnahme' {

    BeforeAll {
        $env:ROM_CLEANUP_SKIP_ONBOARDING = '1'
        $env:ROMCLEANUP_TESTMODE = '1'

        $root = $PSScriptRoot
        while ($root -and -not (Test-Path (Join-Path $root 'simple_sort.ps1'))) {
            $root = Split-Path -Parent $root
        }

        $script:wpfModulePaths = @(
            (Join-Path $root 'dev\modules\FileOps.ps1'),
            (Join-Path $root 'dev\modules\Settings.ps1'),
            (Join-Path $root 'dev\modules\ConfigProfiles.ps1'),
            (Join-Path $root 'dev\modules\ConfigMerge.ps1'),
            (Join-Path $root 'dev\modules\AppState.ps1'),
            (Join-Path $root 'dev\modules\Localization.ps1'),
            (Join-Path $root 'dev\modules\WpfXaml.ps1'),
            (Join-Path $root 'dev\modules\WpfHost.ps1'),
            (Join-Path $root 'dev\modules\WpfSelectionConfig.ps1'),
            (Join-Path $root 'dev\modules\WpfMainViewModel.ps1'),
            (Join-Path $root 'dev\modules\WpfSlice.Roots.ps1'),
            (Join-Path $root 'dev\modules\WpfSlice.RunControl.ps1'),
            (Join-Path $root 'dev\modules\WpfSlice.Settings.ps1'),
            (Join-Path $root 'dev\modules\WpfSlice.DatMapping.ps1'),
            (Join-Path $root 'dev\modules\WpfSlice.ReportPreview.ps1'),
            (Join-Path $root 'dev\modules\WpfSlice.AdvancedFeatures.ps1'),
            (Join-Path $root 'dev\modules\WpfEventHandlers.ps1'),
            (Join-Path $root 'dev\modules\SimpleSort.WpfMain.ps1')
        )

        function Invoke-InStaThread {
            param([scriptblock]$Script)

            $iss = [System.Management.Automation.Runspaces.InitialSessionState]::CreateDefault()
            $runspace = [System.Management.Automation.Runspaces.RunspaceFactory]::CreateRunspace($iss)
            $runspace.ApartmentState = [System.Threading.ApartmentState]::STA
            $runspace.ThreadOptions = [System.Management.Automation.Runspaces.PSThreadOptions]::ReuseThread
            $runspace.Open()

            try {
                foreach ($modulePath in $script:wpfModulePaths) {
                    $psLoad = [System.Management.Automation.PowerShell]::Create()
                    $psLoad.Runspace = $runspace
                    [void]$psLoad.AddScript('. ''{0}''' -f $modulePath)
                    [void]$psLoad.Invoke()
                    if ($psLoad.HadErrors) {
                        $firstError = @($psLoad.Streams.Error)[0]
                        throw [System.Exception]::new([string]$firstError)
                    }
                    $psLoad.Dispose()
                }

                $psExec = [System.Management.Automation.PowerShell]::Create()
                $psExec.Runspace = $runspace
                [void]$psExec.AddScript($Script.ToString())
                $result = $psExec.Invoke()
                if ($psExec.Streams.Error.Count -gt 0) {
                    $firstError = @($psExec.Streams.Error)[0]
                    throw [System.Exception]::new([string]$firstError)
                }
                $psExec.Dispose()
                return $result
            } finally {
                try { $runspace.Close() } catch { }
                try { $runspace.Dispose() } catch { }
            }
        }

        $script:invokeSta = ${function:Invoke-InStaThread}
    }

    AfterAll {
        Remove-Item Env:\ROM_CLEANUP_SKIP_ONBOARDING -ErrorAction SilentlyContinue
        Remove-Item Env:\ROMCLEANUP_TESTMODE -ErrorAction SilentlyContinue
    }

    It 'WPF-Assemblies laden ohne Fehler' {
        { & $script:invokeSta { Initialize-WpfAssemblies } } | Should -Not -Throw
    }

    It 'XAML-String ist nicht leer' {
        $mainWindowPath = Join-Path $root 'dev\modules\wpf\MainWindow.xaml'
        Test-Path -LiteralPath $mainWindowPath -PathType Leaf | Should -BeTrue
        $xamlRaw = Get-Content -LiteralPath $mainWindowPath -Raw
        $xamlRaw | Should -Match '<Window'
    }

    It 'Fenster instanziierbar aus XAML' {
        { & $script:invokeSta {
            Initialize-WpfAssemblies
            $null = New-WpfWindowFromXaml -Xaml $script:RC_XAML_MAIN
        } } | Should -Not -Throw
    }

    It 'Alle erwarteten named Elements sind vorhanden' {
        $ctx = & $script:invokeSta {
            Initialize-WpfAssemblies
            $window = New-WpfWindowFromXaml -Xaml $script:RC_XAML_MAIN
            Get-WpfNamedElements -Window $window
        }

        $required = @(
            'listRoots', 'txtTrash',
            'txtChdman', 'txtDolphin', 'txt7z', 'txtPsxtract', 'txtCiso',
            'txtDatRoot', 'cmbDatHash',
            'btnRunGlobal', 'btnCancelGlobal', 'btnRollback',
            'progGlobal', 'listLog',
            'tabMain'
        )

        foreach ($name in $required) {
            $ctx.ContainsKey($name) | Should -BeTrue -Because "$name muss als named element vorhanden sein"
        }
    }

    It 'Start-WpfGui -Headless gibt Window-Objekt zurück' {
        $typeName = & $script:invokeSta {
            $window = Start-WpfGui -Headless
            if ($null -eq $window) { return $null }
            return $window.GetType().Name
        }
        $typeName | Should -Be 'Window'
    }

    It 'Loaded-Handler läuft ohne Ausnahme' {
        { & $script:invokeSta {
            Initialize-WpfAssemblies
            $window = New-WpfWindowFromXaml -Xaml $script:RC_XAML_MAIN
            $ctx = Get-WpfNamedElements -Window $window
            Register-WpfEventHandlers -Window $window -Ctx $ctx
            $loadedArgs = [System.Windows.RoutedEventArgs]::new([System.Windows.FrameworkElement]::LoadedEvent, $window)
            $window.RaiseEvent($loadedArgs)
        } } | Should -Not -Throw
    }
}
