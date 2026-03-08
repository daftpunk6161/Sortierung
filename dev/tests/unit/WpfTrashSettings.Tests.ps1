#requires -Modules Pester

Describe 'Papierkorb in Einstellungen (Allgemein) - XAML & Settings' {
    BeforeAll {
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root 'simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }

        $script:xamlPath = Join-Path $script:root 'dev\modules\wpf\MainWindow.xaml'
        $script:xamlContent = Get-Content -LiteralPath $script:xamlPath -Raw -ErrorAction Stop

        . (Join-Path $script:root 'dev\modules\FileOps.ps1')
        . (Join-Path $script:root 'dev\modules\Settings.ps1')
        . (Join-Path $script:root 'dev\modules\AppState.ps1')
        . (Join-Path $script:root 'dev\modules\Core.ps1')
        . (Join-Path $script:root 'dev\modules\Localization.ps1')
        . (Join-Path $script:root 'dev\modules\WpfSelectionConfig.ps1')
        . (Join-Path $script:root 'dev\modules\WpfMainViewModel.ps1')
        . (Join-Path $script:root 'dev\modules\WpfSlice.DatMapping.ps1')
        . (Join-Path $script:root 'dev\modules\WpfSlice.ReportPreview.ps1')
        . (Join-Path $script:root 'dev\modules\WpfSlice.AdvancedFeatures.ps1')
        . (Join-Path $script:root 'dev\modules\WpfEventHandlers.ps1')

        function New-MockTextBox([string]$text = '') {
            [pscustomobject]@{ Text = $text }
        }

        function New-MockCheck([bool]$value = $false) {
            [pscustomobject]@{ IsChecked = $value; Content = '' }
        }

        function New-MockCombo([string[]]$values, [string]$selected) {
            $items = @($values | ForEach-Object { [pscustomobject]@{ Content = $_ } })
            $selectedItem = $items | Where-Object { $_.Content -eq $selected } | Select-Object -First 1
            [pscustomobject]@{
                Items = $items
                SelectedItem = $selectedItem
                Text = $selected
            }
        }

        function New-MockCtx {
            $ctx = [hashtable]::new([StringComparer]::OrdinalIgnoreCase)
            $ctx['listRoots'] = [pscustomobject]@{ Items = @('C:\ROMS1') }
            $ctx['gridDatMap'] = [pscustomobject]@{ Items = @() }
            $ctx['txtTrash'] = New-MockTextBox ''
            $ctx['txtPrefer'] = New-MockTextBox 'EU,US'
            $ctx['txtExt'] = New-MockTextBox '.zip'
            $ctx['txtConsoleFilter'] = New-MockTextBox ''
            $ctx['chkSortConsole'] = New-MockCheck $true
            $ctx['chkJunkAggressive'] = New-MockCheck $false
            $ctx['chkAliasKeying'] = New-MockCheck $false
            $ctx['chkDatUse'] = New-MockCheck $false
            $ctx['chkDatFallback'] = New-MockCheck $false
            $ctx['chkCrcVerifyScan'] = New-MockCheck $false
            $ctx['chkSafetyMode'] = New-MockCheck $false
            $ctx['chkReportDryRun'] = New-MockCheck $false
            $ctx['chkConfirmMove'] = New-MockCheck $true
            $ctx['txtDatRoot'] = New-MockTextBox ''
            $ctx['txtSafetyScope'] = New-MockTextBox ''
            $ctx['txtAuditRoot'] = New-MockTextBox ''
            $ctx['txtPs3Dupes'] = New-MockTextBox ''
            $ctx['txtChdman'] = New-MockTextBox ''
            $ctx['txtDolphin'] = New-MockTextBox ''
            $ctx['txt7z'] = New-MockTextBox ''
            $ctx['txtPsxtract'] = New-MockTextBox ''
            $ctx['txtCiso'] = New-MockTextBox ''
            $ctx['txtJpKeepConsoles'] = New-MockTextBox ''
            $ctx['txtGameKeyPreviewInput'] = New-MockTextBox ''
            $ctx['lblGameKeyPreviewOutput'] = New-MockTextBox ''
            $ctx['cmbDatHash'] = New-MockCombo -values @('sha1','md5','crc') -selected 'sha1'
            $ctx['cmbLogLevel'] = New-MockCombo -values @('Debug','Info','Warn','Error') -selected 'Info'
            $ctx['chkExpertMode'] = New-MockCheck $false
            $ctx['expToolsSetup'] = [pscustomobject]@{ IsExpanded = $false }
            $ctx['lblToolsSetupHint'] = New-MockTextBox ''
            $ctx['tabSort'] = [pscustomobject]@{ Header = '' }
            $ctx['tabConfig'] = [pscustomobject]@{ Header = '' }
            $ctx['btnAddRoot'] = [pscustomobject]@{ Content = '' }
            $ctx['btnRemoveRoot'] = [pscustomobject]@{ Content = '' }
            $ctx['btnClearRoots'] = [pscustomobject]@{ Content = '' }
            $ctx['btnPasteRoots'] = [pscustomobject]@{ Content = '' }
            $ctx['btnRunGlobal'] = [pscustomobject]@{ Content = '' }
            $ctx['cmbLocale'] = [pscustomobject]@{ SelectedItem = [pscustomobject]@{ Content = 'de' } }
            return $ctx
        }
    }

    Context 'XAML-Platzierung' {
        It 'txtTrash befindet sich im Allgemein-Bereich (tabConfigProfiles)' {
            # txtTrash must appear AFTER the Allgemein expander opens and BEFORE it closes,
            # i.e. within the Profile tab's general section, not in tabConfigAdvanced.
            $allgemeinStart = $script:xamlContent.IndexOf('expCfgGeneral')
            $txtTrashPos = $script:xamlContent.IndexOf('x:Name="txtTrash"')
            $advancedStart = $script:xamlContent.IndexOf('tabConfigAdvanced')

            $allgemeinStart | Should -BeGreaterThan -1 -Because 'expCfgGeneral muss existieren'
            $txtTrashPos | Should -BeGreaterThan -1 -Because 'txtTrash muss existieren'
            $advancedStart | Should -BeGreaterThan -1 -Because 'tabConfigAdvanced muss existieren'

            $txtTrashPos | Should -BeGreaterThan $allgemeinStart -Because 'txtTrash soll nach expCfgGeneral kommen'
            $txtTrashPos | Should -BeLessThan $advancedStart -Because 'txtTrash soll vor tabConfigAdvanced stehen'
        }

        It 'btnBrowseTrash befindet sich im Allgemein-Bereich (tabConfigProfiles)' {
            $allgemeinStart = $script:xamlContent.IndexOf('expCfgGeneral')
            $btnPos = $script:xamlContent.IndexOf('x:Name="btnBrowseTrash"')
            $advancedStart = $script:xamlContent.IndexOf('tabConfigAdvanced')

            $btnPos | Should -BeGreaterThan $allgemeinStart -Because 'btnBrowseTrash soll im Allgemein-Bereich liegen'
            $btnPos | Should -BeLessThan $advancedStart -Because 'btnBrowseTrash soll nicht im Erweitert-Tab sein'
        }

        It 'txtTrash ist nicht mehr im Audit-Bereich (expCfgAudit)' {
            $auditStart = $script:xamlContent.IndexOf('expCfgAudit')
            $auditSectionEnd = $script:xamlContent.IndexOf('</Expander>', $auditStart)
            $txtTrashPos = $script:xamlContent.IndexOf('x:Name="txtTrash"')

            # txtTrash must be positioned before the audit section starts
            $txtTrashPos | Should -BeLessThan $auditStart -Because 'txtTrash darf nicht mehr im Audit-Bereich sein'
        }

        It 'txtTrash und btnBrowseTrash existieren jeweils genau einmal in der XAML' {
            $trashMatches = [regex]::Matches($script:xamlContent, 'x:Name="txtTrash"')
            $btnMatches = [regex]::Matches($script:xamlContent, 'x:Name="btnBrowseTrash"')

            $trashMatches.Count | Should -Be 1
            $btnMatches.Count | Should -Be 1
        }

        It 'Audit-Bereich enthält weiterhin txtAuditRoot und txtPs3Dupes' {
            $auditStart = $script:xamlContent.IndexOf('expCfgAudit')
            $auditAuditRoot = $script:xamlContent.IndexOf('x:Name="txtAuditRoot"')
            $auditPs3 = $script:xamlContent.IndexOf('x:Name="txtPs3Dupes"')

            $auditAuditRoot | Should -BeGreaterThan $auditStart
            $auditPs3 | Should -BeGreaterThan $auditStart
        }
    }

    Context 'Initialize-WpfFromSettings laedt TrashRoot' {
        It 'setzt txtTrash aus gespeichertem trashRoot' {
            $ctx = New-MockCtx

            Mock -CommandName Get-UserSettings -MockWith {
                [pscustomobject]@{
                    toolPaths = [pscustomobject]@{}
                    dat = [pscustomobject]@{}
                    general = [pscustomobject]@{
                        logLevel = 'Info'
                        trashRoot = 'D:\Roms\_TRASH'
                    }
                }
            }

            Initialize-WpfFromSettings -Ctx $ctx

            $ctx['txtTrash'].Text | Should -Be 'D:\Roms\_TRASH'
        }

        It 'laesst txtTrash leer wenn trashRoot nicht in Settings' {
            $ctx = New-MockCtx

            Mock -CommandName Get-UserSettings -MockWith {
                [pscustomobject]@{
                    toolPaths = [pscustomobject]@{}
                    dat = [pscustomobject]@{}
                    general = [pscustomobject]@{
                        logLevel = 'Info'
                    }
                }
            }

            Initialize-WpfFromSettings -Ctx $ctx

            $ctx['txtTrash'].Text | Should -BeNullOrEmpty
        }
    }

    Context 'Save-WpfToSettings persistiert TrashRoot' {
        It 'speichert TrashRoot im general-Objekt' {
            $ctx = New-MockCtx
            $ctx['txtTrash'].Text = 'E:\Collection\_TRASH'
            $script:capturedSettings = $null

            Mock -CommandName Get-UserSettings -MockWith { [pscustomobject]@{} }
            Mock -CommandName Set-UserSettings -MockWith {
                param($Settings)
                $script:capturedSettings = $Settings
            }

            Save-WpfToSettings -Ctx $ctx

            $script:capturedSettings | Should -Not -BeNullOrEmpty
            $script:capturedSettings.general.trashRoot | Should -Be 'E:\Collection\_TRASH'
        }

        It 'speichert leeren TrashRoot wenn kein Pfad gesetzt' {
            $ctx = New-MockCtx
            $ctx['txtTrash'].Text = ''
            $script:capturedSettings = $null

            Mock -CommandName Get-UserSettings -MockWith { [pscustomobject]@{} }
            Mock -CommandName Set-UserSettings -MockWith {
                param($Settings)
                $script:capturedSettings = $Settings
            }

            Save-WpfToSettings -Ctx $ctx

            $script:capturedSettings.general.trashRoot | Should -Be ''
        }
    }

    Context 'Get-WpfRunParameters uebernimmt TrashRoot' {
        It 'liefert den gesetzten Papierkorb-Pfad in den Run-Parametern' {
            $ctx = New-MockCtx
            $ctx['txtTrash'].Text = 'F:\Trash'

            $params = Get-WpfRunParameters -Ctx $ctx

            $params.TrashRoot | Should -Be 'F:\Trash'
        }
    }
}
