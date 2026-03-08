@{
    RootModule        = 'RomCleanup.psm1'
    ModuleVersion     = '1.0.0'
    GUID              = 'a3f7e2c1-94d8-4b6f-b3e5-1c8d9a2f4e7b'
    Author            = 'ROM Cleanup Contributors'
    CompanyName       = ''
    Copyright         = '(c) 2024-2026. All rights reserved.'
    Description       = 'ROM Cleanup & Region Dedupe — Removes junk/demo/beta files, deduplicates by region, converts formats and supports console sorting.'
    PowerShellVersion = '5.1'

    FunctionsToExport = '*'
    CmdletsToExport   = @()
    VariablesToExport = @()
    AliasesToExport   = '*'

    PrivateData = @{
        PSData = @{
            Tags       = @('ROM', 'Cleanup', 'Dedupe', 'Retro', 'Gaming', 'NoIntro', 'Redump')
            ProjectUri = ''
        }
    }
}
