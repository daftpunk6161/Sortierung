#requires -Modules Pester

# These tests create real WinForms controls (~2,600 lines of GUI code).
# Running them inside VS Code's integrated terminal exhausts GDI handles
# and crashes the editor.  Set ROM_CLEANUP_GUI_TESTS=1 to enable.
if (-not $env:ROM_CLEANUP_GUI_TESTS) {
    Write-Warning 'UiSmoke tests skipped (set $env:ROM_CLEANUP_GUI_TESTS=1 to enable)'
    return
}

BeforeAll {
    . (Join-Path $PSScriptRoot 'TestScriptLoader.ps1')
    $ctx = New-SimpleSortTestScript -TestsRoot $PSScriptRoot -TempPrefix 'ui_smoke_test' -IncludeGui
    $script:TempScript = $ctx.TempScript
    . $script:TempScript

    $script:GetAllControlsRecursive = {
        param([System.Windows.Forms.Control]$Root)

        $list = New-Object System.Collections.Generic.List[System.Windows.Forms.Control]
        if (-not $Root) { return @($list) }

        $stack = New-Object System.Collections.Generic.Stack[System.Windows.Forms.Control]
        $stack.Push($Root)

        while ($stack.Count -gt 0) {
            $node = $stack.Pop()
            foreach ($child in @($node.Controls)) {
                if (-not $child) { continue }
                [void]$list.Add($child)
                if ($child.Controls -and $child.Controls.Count -gt 0) {
                    $stack.Push($child)
                }
            }
        }

        return @($list)
    }
}

AfterAll {
    # Dispose all WinForms controls to release GDI handles immediately.
    if ($form -and $form -is [System.Windows.Forms.Form]) {
        try { $form.Dispose() } catch {}
    }
    Remove-SimpleSortTestTempScript -TempScript $script:TempScript
}

Describe 'UI Accessibility Smoke' {
    It 'H-02 smoke: form should use DPI autoscaling and expose expected tab count' {
        $form | Should -Not -BeNullOrEmpty
        [string]$form.AutoScaleMode | Should -Be 'Dpi'
        # UX-06: AutoScaleDimensions must be set for correct DPI scaling
        $form.AutoScaleDimensions.Width | Should -BeGreaterThan 0
        $tabsMain | Should -Not -BeNullOrEmpty
        $tabsMain.TabPages.Count | Should -BeGreaterThan 6
    }

    It 'H-02 smoke: focusable controls should have non-negative tab indices' {
        $all = @(& $script:GetAllControlsRecursive -Root $form)
        $all.Count | Should -BeGreaterThan 50

        $focusable = @($all | Where-Object {
            ($_.PSObject.Properties.Name -contains 'TabStop') -and
            ($_.TabStop -eq $true)
        })

        $focusable.Count | Should -BeGreaterThan 30
        @($focusable | Where-Object { [int]$_.TabIndex -lt 0 }).Count | Should -Be 0
    }

    It 'H-02 smoke: each main tab should include focusable controls for keyboard navigation' {
        foreach ($page in @($tabsMain.TabPages)) {
            $descendants = @(& $script:GetAllControlsRecursive -Root $page)
            $focusable = @($descendants | Where-Object {
                ($_.PSObject.Properties.Name -contains 'TabStop') -and
                ($_.TabStop -eq $true)
            })

            $focusable.Count | Should -BeGreaterThan 0 -Because ("Tab '{0}' should be keyboard reachable" -f $page.Text)
        }
    }

    It 'Rules tab should expose import/export controls with valid tab order' {
        $script:UIControls | Should -Not -BeNullOrEmpty
        $script:UIControls.BtnRulesImport | Should -Not -BeNullOrEmpty
        $script:UIControls.BtnRulesExport | Should -Not -BeNullOrEmpty

        [int]$script:UIControls.BtnRulesImport.TabIndex | Should -BeLessThan ([int]$script:UIControls.BtnRulesExport.TabIndex)
        [int]$script:UIControls.BtnRulesExport.TabIndex | Should -BeLessThan ([int]$script:UIControls.BtnRulesImport.TabIndex + 3)
    }

    It 'Move mode should be visually marked as risky in run button label' {
        $script:UIControls.RbMove.Checked = $true
        Update-ModeUI
        [string]$script:UIControls.BtnRun.Text | Should -Match 'Risiko'

        $script:UIControls.RbDry.Checked = $true
        Update-ModeUI
        [string]$script:UIControls.BtnRun.Text | Should -Match 'DryRun'
    }

    It 'Global quick navigation controls should be present for Rules and Safety tabs' {
        $script:UIControls.BtnRulesGlobal | Should -Not -BeNullOrEmpty
        $script:UIControls.BtnSafetyGlobal | Should -Not -BeNullOrEmpty
        $script:UIControls.BtnRollbackGlobal | Should -Not -BeNullOrEmpty
    }

    It 'Expert controls should hide/show when Advanced checkbox is toggled (Quick/Expert mode)' {
        # Controls gated on $chkAdvancedStart: BtnPreviewKeys and BtnExportLog
        $chkAdvancedStart = $script:UIControls.ChkAdvancedStart
        $btnPreviewKeys   = $script:UIControls.BtnPreviewKeys
        # BtnExportLog is not in UIControls — find by Name in the form tree
        $allControls = @(& $script:GetAllControlsRecursive -Root $form)
        $btnExportLog = @($allControls | Where-Object { $_.Name -eq 'btnExportLog' }) | Select-Object -First 1

        $chkAdvancedStart | Should -Not -BeNullOrEmpty -Because 'Advanced checkbox must exist'
        $btnPreviewKeys   | Should -Not -BeNullOrEmpty -Because 'BtnPreviewKeys must exist'
        $btnExportLog     | Should -Not -BeNullOrEmpty -Because 'BtnExportLog must exist in form tree'

        # Quick mode (unchecked) — expert controls should be hidden
        $chkAdvancedStart.Checked = $false
        $btnPreviewKeys.Visible | Should -BeFalse -Because 'In Quick mode, BtnPreviewKeys must be hidden'
        $btnExportLog.Visible   | Should -BeFalse -Because 'In Quick mode, BtnExportLog must be hidden'

        # Expert mode (checked) — expert controls should be visible
        $chkAdvancedStart.Checked = $true
        $btnPreviewKeys.Visible | Should -BeTrue  -Because 'In Expert mode, BtnPreviewKeys must be visible'
        $btnExportLog.Visible   | Should -BeTrue  -Because 'In Expert mode, BtnExportLog must be visible'

        # Restore to unchecked for other tests
        $chkAdvancedStart.Checked = $false
    }
}
