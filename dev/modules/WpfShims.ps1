# ================================================================
#  WpfShims.ps1  –  C# helper types for WPF in PowerShell 5.1
#  Provides: RomCleanupDatMapRow (typed DataGrid row model)
# ================================================================

# Skip WPF assembly loading in automated test mode to prevent
# VS Code terminal freezes (PresentationFramework blocks the UI thread).
if ($env:ROMCLEANUP_TESTMODE -eq '1') { return }

foreach ($asm in @(
  'PresentationCore',
  'PresentationFramework',
  'WindowsBase',
  'System.Xaml'
)) {
  try {
    Add-Type -AssemblyName $asm -ErrorAction SilentlyContinue
  } catch { }
}

if (-not ([System.Management.Automation.PSTypeName]'RomCleanupDatMapRow').Type) {
    try {
        Add-Type -TypeDefinition @'
public class RomCleanupDatMapRow
{
        public string Console { get; set; }
        public string DatFile { get; set; }

        public RomCleanupDatMapRow()
        {
                Console = string.Empty;
                DatFile = string.Empty;
        }
}
'@ -ErrorAction Stop
    } catch { }
}
