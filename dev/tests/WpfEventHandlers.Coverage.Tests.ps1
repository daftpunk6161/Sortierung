#requires -Modules Pester

Describe 'WpfEventHandlers coverage harness' {
    BeforeAll {
        $script:root = $PSScriptRoot
        while ($script:root -and -not (Test-Path (Join-Path $script:root 'simple_sort.ps1'))) {
            $script:root = Split-Path -Parent $script:root
        }

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
            $ctx['listRoots'] = [pscustomobject]@{ Items = @('C:\ROMS1', 'C:\ROMS2') }
            $ctx['gridDatMap'] = [pscustomobject]@{
                Items = @(
                    [pscustomobject]@{ Console = 'PS1'; DatFile = 'sony_psx.dat' },
                    [pscustomobject]@{ Console = 'PS2'; DatFile = 'sony_ps2.dat' }
                )
            }
            $ctx['txtTrash'] = New-MockTextBox 'C:\Trash'
            $ctx['txtPrefer'] = New-MockTextBox 'EU,US,WORLD'
            $ctx['txtExt'] = New-MockTextBox '.zip,.7z,.chd'
            $ctx['txtConsoleFilter'] = New-MockTextBox 'PS1,PS2'
            $ctx['chkSortConsole'] = New-MockCheck $true
            $ctx['chkJunkAggressive'] = New-MockCheck $false
            $ctx['chkAliasKeying'] = New-MockCheck $true
            $ctx['chkDatUse'] = New-MockCheck $true
            $ctx['chkDatFallback'] = New-MockCheck $true
            $ctx['chkCrcVerifyScan'] = New-MockCheck $true
            $ctx['chkSafetyMode'] = New-MockCheck $true
            $ctx['chkReportDryRun'] = New-MockCheck $false
            $ctx['chkConfirmMove'] = New-MockCheck $true
            $ctx['txtDatRoot'] = New-MockTextBox 'C:\DAT'
            $ctx['txtSafetyScope'] = New-MockTextBox 'C:\Windows;C:\Sandbox'
            $ctx['txtAuditRoot'] = New-MockTextBox 'C:\Audit'
            $ctx['txtPs3Dupes'] = New-MockTextBox 'C:\PS3Dupes'
            $ctx['txtChdman'] = New-MockTextBox 'C:\Tools\chdman.exe'
            $ctx['txtDolphin'] = New-MockTextBox 'C:\Tools\DolphinTool.exe'
            $ctx['txt7z'] = New-MockTextBox 'C:\Tools\7z.exe'
            $ctx['txtPsxtract'] = New-MockTextBox 'C:\Tools\psxtract.exe'
            $ctx['txtCiso'] = New-MockTextBox 'C:\Tools\ciso.exe'
            $ctx['txtJpKeepConsoles'] = New-MockTextBox 'PS1,PS2'
            $ctx['txtGameKeyPreviewInput'] = New-MockTextBox 'Mega Game (Europe)'
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

    It 'Get-WpfRunParameters builds engine parameter hashtable from context' {
        $ctx = New-MockCtx
        $params = Get-WpfRunParameters -Ctx $ctx

        $params | Should -Not -BeNullOrEmpty
        @($params.Roots).Count | Should -Be 2
        $params.TrashRoot | Should -Be 'C:\Trash'
        $params.Mode | Should -Be 'Move'
        $params.DatHashType | Should -Be 'sha1'
        $params.UseDat | Should -BeTrue
        $params.SortConsole | Should -BeTrue
        $params.AliasKeying | Should -BeTrue
        $params.DatMap['PS1'] | Should -Be 'sony_psx.dat'
    }

    It 'Get-WpfRunParameters falls back to DryRun when confirm-move is disabled' {
        $ctx = New-MockCtx
        $ctx['chkConfirmMove'].IsChecked = $false

        $params = Get-WpfRunParameters -Ctx $ctx
        $params.Mode | Should -Be 'DryRun'
    }

    It 'Get-WpfRunParameters übernimmt Roots aus UI-Sammlung auch ohne ViewModel-Helfer' {
        $ctx = New-MockCtx
        $ctx['listRoots'] = [pscustomobject]@{ Items = @() }
        $ctx['__rootsCollection'] = @('W:\roms')

        $params = Get-WpfRunParameters -Ctx $ctx

        @($params.Roots).Count | Should -Be 1
        @($params.Roots)[0] | Should -Be 'W:\roms'
    }

    It 'Initialize-WpfFromSettings applies persisted values to controls' {
        $ctx = New-MockCtx

        Mock -CommandName Get-UserSettings -MockWith {
            @{
                toolPaths = @{
                    chdman = 'D:\tools\chdman.exe'
                    dolphintool = 'D:\tools\DolphinTool.exe'
                    '7z' = 'D:\tools\7z.exe'
                    psxtract = 'D:\tools\psxtract.exe'
                    ciso = 'D:\tools\ciso.exe'
                }
                dat = @{
                    root = 'D:\dat'
                    hashType = 'md5'
                    enabled = $true
                    fallback = $false
                }
                general = @{
                    logLevel = 'Warn'
                    auditRoot = 'D:\audit'
                    ps3DupesRoot = 'D:\ps3'
                }
            }
        }

        Initialize-WpfFromSettings -Ctx $ctx

        $ctx['txtChdman'].Text | Should -Be 'D:\tools\chdman.exe'
        $ctx['txtDatRoot'].Text | Should -Be 'D:\dat'
        $ctx['chkDatUse'].IsChecked | Should -BeTrue
        $ctx['chkDatFallback'].IsChecked | Should -BeFalse
        $ctx['txtAuditRoot'].Text | Should -Be 'D:\audit'
    }

    It 'Save-WpfToSettings writes mapped structure via Set-UserSettings' {
        $ctx = New-MockCtx
        $script:capturedSettings = $null

        Mock -CommandName Get-UserSettings -MockWith { [pscustomobject]@{} }
        Mock -CommandName Set-UserSettings -MockWith {
            param($Settings)
            $script:capturedSettings = $Settings
        }

        Save-WpfToSettings -Ctx $ctx

        $script:capturedSettings | Should -Not -BeNullOrEmpty
        $script:capturedSettings.toolPaths.chdman | Should -Be 'C:\Tools\chdman.exe'
        $script:capturedSettings.dat.root | Should -Be 'C:\DAT'
        $script:capturedSettings.general.logLevel | Should -Be 'Info'
    }

    It 'Invoke-WpfGameKeyPreview normalisiert Titel in nativen GameKey' {
        $ctx = New-MockCtx
        $ctx['chkAliasKeying'].IsChecked = $true

        $key = Invoke-WpfGameKeyPreview -Ctx $ctx

        [string]::IsNullOrWhiteSpace([string]$key) | Should -BeFalse
        $ctx['lblGameKeyPreviewOutput'].Text | Should -Be $key
    }

    It 'Invoke-WpfGameKeyPreview setzt Platzhalter bei leerem Input' {
        $ctx = New-MockCtx
        $ctx['txtGameKeyPreviewInput'].Text = ' '

        $key = Invoke-WpfGameKeyPreview -Ctx $ctx

        $key | Should -BeNullOrEmpty
        $ctx['lblGameKeyPreviewOutput'].Text | Should -Be '–'
    }

    It 'Update-WpfToolSetupVisibility hält Tool-Setup im Expert-Mode sichtbar' {
        $ctx = New-MockCtx
        $ctx['chkExpertMode'].IsChecked = $true

        Update-WpfToolSetupVisibility -Ctx $ctx

        $ctx['expToolsSetup'].IsExpanded | Should -BeTrue
        $ctx['lblToolsSetupHint'].Text | Should -Match 'Expert-Mode'
    }

    It 'Set-WpfLocale aktualisiert mehrere UI-Labels dynamisch' {
        $ctx = New-MockCtx

        Mock -CommandName Set-AppStateValue {}
        Mock -CommandName Initialize-Localization {}
        Mock -CommandName Get-UIString -MockWith {
            param([string]$Key)
            switch ($Key) {
                'App.Title' { 'ROM Cleanup && Region Dedupe' }
                'Tab.Start' { 'Start' }
                'Tab.Settings' { 'Einstellungen' }
                'Start.AddRoot' { 'Ordner hinzufügen' }
                'Start.RemoveRoot' { 'Entfernen' }
                'Start.ClearRoots' { 'Alle leeren' }
                'Start.PasteRoots' { 'Einfügen' }
                'Dat.UseDat' { 'DAT verwenden' }
                'Start.RunButton' { 'Dedupe starten' }
                default { $Key }
            }
        }

        Set-WpfLocale -Ctx $ctx

        $ctx['tabSort'].Header | Should -Be 'Start'
        $ctx['tabConfig'].Header | Should -Be 'Einstellungen'
        $ctx['btnAddRoot'].Content | Should -Be 'Ordner hinzufügen'
        $ctx['btnRemoveRoot'].Content | Should -Be 'Entfernen'
        $ctx['chkDatUse'].Content | Should -Be 'DAT verwenden'
    }

    It 'Get-WpfDatConsoleOptions sammelt bekannte Konsolen und bestehende Zeilen' {
        $ctx = New-MockCtx
        $script:CONSOLE_FOLDER_MAP = @{ 'sony playstation' = 'PS1' }
        $script:CONSOLE_EXT_MAP = @{ '.z64' = 'N64' }
        $ctx['gridDatMap'].Items = @(
            [pscustomobject]@{ Console = 'Arcade'; DatFile = 'arcade.dat' }
        )

        $options = Get-WpfDatConsoleOptions -Ctx $ctx

        @($options) | Should -Contain 'PS1'
        @($options) | Should -Contain 'N64'
        @($options) | Should -Contain 'Arcade'
    }
}
