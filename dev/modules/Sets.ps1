function Get-PathSizeCached {
  param(
    [string]$Path,
    [hashtable]$FileInfoByPath
  )

  if ([string]::IsNullOrWhiteSpace($Path)) { return [long]0 }

  $fi = $null
  if ($FileInfoByPath) { $fi = $FileInfoByPath[$Path] }
  if (-not $fi) {
    try {
      $fi = Get-Item -LiteralPath $Path -ErrorAction SilentlyContinue
      if ($fi -and $FileInfoByPath) { $FileInfoByPath[$Path] = $fi }
    } catch {
      $fi = $null
    }
  }

  if ($fi) { return [long]$fi.Length }
  return [long]0
}

function New-SetItem {
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Root,
    [string]$Type,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Category,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$MainPath,
    [string[]]$Paths,
    [string]$Region,
    [string[]]$PreferOrder,
    [long]$VersionScore,
    [string]$GameKey,
    [bool]$DatMatch = $false,
    [string]$BaseName = '',
    [hashtable]$FileInfoByPath,
    [bool]$IsCorrupt = $false,
    [bool]$IsComplete = $true,
    [int]$MissingCount = 0
  )

  $size = [long]0
  foreach ($p in $Paths) {
    $size += Get-PathSizeCached -Path $p -FileInfoByPath $FileInfoByPath
  }

  $mainExt = [IO.Path]::GetExtension($MainPath)
  $completenessScore = if ($IsComplete) { 100 } else { 0 }
  $headerScore = Get-HeaderVariantScore -Root $Root -MainPath $MainPath
  $sizeTieBreakScore = Get-SizeTieBreakScore -Type $Type -Extension $mainExt -SizeBytes $size

  return [pscustomobject]@{
    Root         = $Root
    Type         = $Type
    Category     = $Category
    MainPath     = $MainPath
    Paths        = $Paths
    Region       = $Region
    RegionScore  = Get-RegionScore -Region $Region -Prefer $PreferOrder
    VersionScore = $VersionScore
    FormatScore  = Get-FormatScore -Extension ([IO.Path]::GetExtension($MainPath)) -Type $Type
    GameKey      = $GameKey
    DatMatch     = $DatMatch
    IsCorrupt    = $IsCorrupt
    SizeBytes    = $size
    BaseName     = $BaseName
    IsComplete   = $IsComplete
    MissingCount = $MissingCount
    CompletenessScore = $completenessScore
    HeaderScore  = $headerScore
    SizeTieBreakScore = $sizeTieBreakScore
  }
}

function New-FileItem {
  <# Build FILE item with same contract fields as set items. Delegates to New-SetItem. #>
  param(
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$Root,
    [Parameter(Mandatory=$true)][ValidateNotNullOrEmpty()][string]$MainPath,
    [string]$Category,
    [string]$Region,
    [string[]]$PreferOrder,
    [long]$VersionScore,
    [string]$GameKey,
    [bool]$DatMatch = $false,
    [bool]$IsCorrupt = $false,
    [string]$BaseName = '',
    [hashtable]$FileInfoByPath
  )

  return (New-SetItem -Root $Root -Type 'FILE' -Category $Category -MainPath $MainPath `
    -Paths @($MainPath) -Region $Region -PreferOrder $PreferOrder `
    -VersionScore $VersionScore -GameKey $GameKey -DatMatch $DatMatch `
    -BaseName $BaseName -FileInfoByPath $FileInfoByPath -IsCorrupt $IsCorrupt `
    -IsComplete $true -MissingCount 0)
}
