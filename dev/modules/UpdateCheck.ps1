function Get-GitHubLatestReleaseInfo {
  <#
  .SYNOPSIS
    Liest die neueste GitHub-Release-Information eines Repositories.
  .PARAMETER Repo
    Repository im Format owner/repo.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$Repo,
    [int]$TimeoutSec = 8
  )

  if ($Repo -notmatch '^[^/\s]+/[^/\s]+$') {
    throw 'Repo muss im Format owner/repo angegeben werden.'
  }

  $url = "https://api.github.com/repos/$Repo/releases/latest"
  $headers = @{ 'User-Agent' = 'RomCleanupUpdateCheck/1.0' }
  $resp = Invoke-RestMethod -Method Get -Uri $url -Headers $headers -TimeoutSec $TimeoutSec -ErrorAction Stop
  return [pscustomobject]@{
    Repo = $Repo
    TagName = [string]$resp.tag_name
    Name = [string]$resp.name
    Url = [string]$resp.html_url
    PublishedAt = [string]$resp.published_at
  }
}

function Test-RomCleanupUpdateAvailable {
  <#
  .SYNOPSIS
    Vergleicht aktuelle Version gegen neuestes GitHub Release.
  .PARAMETER Repo
    Repository im Format owner/repo.
  .PARAMETER CurrentVersion
    Aktuelle lokale Version.
  #>
  param(
    [Parameter(Mandatory=$true)][string]$Repo,
    [Parameter(Mandatory=$true)][string]$CurrentVersion,
    [int]$TimeoutSec = 8
  )

  $latest = Get-GitHubLatestReleaseInfo -Repo $Repo -TimeoutSec $TimeoutSec
  $latestTag = [string]$latest.TagName
  $normalizedLatest = $latestTag.TrimStart('v','V')
  $normalizedCurrent = [string]$CurrentVersion

  $isNewer = $false
  try {
    $latestVersion = [version]$normalizedLatest
    $currentVersion = [version]$normalizedCurrent
    $isNewer = ($latestVersion -gt $currentVersion)
  } catch {
    if ($normalizedLatest -and $normalizedCurrent) {
      $isNewer = ($normalizedLatest -ne $normalizedCurrent)
    }
  }

  return [pscustomobject]@{
    UpdateAvailable = [bool]$isNewer
    CurrentVersion = $normalizedCurrent
    LatestTag = $latestTag
    LatestUrl = [string]$latest.Url
    Repo = $Repo
  }
}

