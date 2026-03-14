<#
.SYNOPSIS
    Split FeatureCommandService.cs into partial class files (RF-006).
#>
$ErrorActionPreference = 'Stop'
$path = "c:\Code\Sortierung\src\RomCleanup.UI.Wpf\Services\FeatureCommandService.cs"
$lines = [System.IO.File]::ReadAllLines($path)
$dir = Split-Path $path

# Section boundaries (1-indexed line numbers)
$sections = @(
    @{ Name="Analysis";    Start=447; End=681;  File="FeatureCommandService.Analysis.cs" },
    @{ Name="Conversion";  Start=682; End=802;  File="FeatureCommandService.Conversion.cs" },
    @{ Name="Dat";         Start=803; End=1058; File="FeatureCommandService.Dat.cs" },
    @{ Name="Collection";  Start=1059; End=1185; File="FeatureCommandService.Collection.cs" },
    @{ Name="Security";    Start=1186; End=1360; File="FeatureCommandService.Security.cs" },
    @{ Name="Workflow";    Start=1361; End=1521; File="FeatureCommandService.Workflow.cs" },
    @{ Name="Export";      Start=1522; End=1589; File="FeatureCommandService.Export.cs" },
    @{ Name="Infra";       Start=1590; End=1890; File="FeatureCommandService.Infra.cs" }
)

$usings = @"
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Input;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.Infrastructure.Tools;
using RomCleanup.UI.Wpf.ViewModels;

"@

foreach ($sec in $sections) {
    $filePath = Join-Path $dir $sec.File
    $sb = [System.Text.StringBuilder]::new()
    [void]$sb.Append($usings)
    [void]$sb.AppendLine("namespace RomCleanup.UI.Wpf.Services;")
    [void]$sb.AppendLine()
    [void]$sb.AppendLine("public sealed partial class FeatureCommandService")
    [void]$sb.AppendLine("{")
    # Copy lines (1-indexed to 0-indexed)
    for ($i = $sec.Start - 1; $i -lt $sec.End -and $i -lt $lines.Count; $i++) {
        [void]$sb.AppendLine($lines[$i])
    }
    [void]$sb.AppendLine("}")
    [System.IO.File]::WriteAllText($filePath, $sb.ToString())
    $lineCount = $sec.End - $sec.Start + 1
    Write-Host "Created $($sec.File) ($lineCount lines)"
}

# Rewrite main file: keep lines 1-446 (class decl + constructor + register + functional buttons + config)
$sb = [System.Text.StringBuilder]::new()
for ($i = 0; $i -lt 446; $i++) {
    [void]$sb.AppendLine($lines[$i])
}
[void]$sb.AppendLine("}")
# Replace "public sealed class" with "public sealed partial class"
$result = $sb.ToString().Replace("public sealed class FeatureCommandService", "public sealed partial class FeatureCommandService")
[System.IO.File]::WriteAllText($path, $result)
Write-Host "`nRewritten main FeatureCommandService.cs (447 lines)"
Write-Host "Done!"
