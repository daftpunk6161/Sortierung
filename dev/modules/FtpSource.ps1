#  FTP/SFTP SOURCE (LF-16)
#  ROM-Roots koennen FTP/SFTP-Pfade sein (Download → Process → Upload-Back).

function New-FtpSourceConfig {
  <#
  .SYNOPSIS
    Erstellt eine FTP/SFTP-Quell-Konfiguration.
  #>
  param(
    [Parameter(Mandatory)][string]$HostName,
    [int]$Port = 21,
    [Parameter(Mandatory)][string]$Username,
    [ValidateSet('FTP','SFTP')]
    [string]$Protocol = 'FTP',
    [string]$RemotePath = '/',
    [string]$LocalCachePath = ''
  )

  return @{
    Host           = $HostName
    Port           = $Port
    Username       = $Username
    Protocol       = $Protocol
    RemotePath     = $RemotePath
    LocalCachePath = $LocalCachePath
    Connected      = $false
    LastSync       = $null
  }
}

function Test-FtpUri {
  <#
  .SYNOPSIS
    Prueft ob ein String eine gueltige FTP/SFTP-URI ist.
  #>
  param(
    [Parameter(Mandatory)][string]$Uri
  )

  $isFtp  = $Uri -match '^ftp://[^/]+(/|$)'
  $isSftp = $Uri -match '^sftp://[^/]+(/|$)'

  return @{
    Valid    = ($isFtp -or $isSftp)
    Protocol = if ($isSftp) { 'SFTP' } elseif ($isFtp) { 'FTP' } else { 'Unknown' }
    Host     = if ($Uri -match '://([\w.\-]+)') { $Matches[1] } else { '' }
    Path     = if ($Uri -match '://[^/]+(/.*)$') { $Matches[1] } else { '/' }
  }
}

function New-FtpSyncPlan {
  <#
  .SYNOPSIS
    Erstellt einen Synchronisationsplan fuer FTP-Quellen.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][array]$RemoteFiles,
    [array]$LocalFiles = @()
  )

  $toDownload = [System.Collections.Generic.List[hashtable]]::new()
  $toUpload   = [System.Collections.Generic.List[hashtable]]::new()
  $unchanged  = [System.Collections.Generic.List[string]]::new()

  $localIndex = @{}
  foreach ($lf in $LocalFiles) {
    $localIndex[$lf.Name] = $lf
  }

  foreach ($rf in $RemoteFiles) {
    if ($localIndex.ContainsKey($rf.Name)) {
      $local = $localIndex[$rf.Name]
      if ($rf.ContainsKey('Size') -and $local.ContainsKey('Size') -and $rf.Size -ne $local.Size) {
        $toDownload.Add(@{ Name = $rf.Name; Size = $rf.Size; Reason = 'SizeMismatch' })
      } else {
        $unchanged.Add($rf.Name)
      }
    } else {
      $toDownload.Add(@{ Name = $rf.Name; Size = if ($rf.ContainsKey('Size')) { $rf.Size } else { 0 }; Reason = 'New' })
    }
  }

  return @{
    Download   = ,$toDownload.ToArray()
    Upload     = ,$toUpload.ToArray()
    Unchanged  = ,$unchanged.ToArray()
    TotalRemote = $RemoteFiles.Count
    TotalLocal  = $LocalFiles.Count
  }
}

function Get-FtpTransferProgress {
  <#
  .SYNOPSIS
    Berechnet Fortschritt eines FTP-Transfers.
  #>
  param(
    [long]$BytesTransferred,
    [long]$TotalBytes,
    [int]$FilesCompleted = 0,
    [int]$TotalFiles = 0
  )

  $percent = if ($TotalBytes -gt 0) { [math]::Round(($BytesTransferred / $TotalBytes) * 100, 1) } else { 0 }
  $filePercent = if ($TotalFiles -gt 0) { [math]::Round(($FilesCompleted / $TotalFiles) * 100, 1) } else { 0 }

  return @{
    BytesTransferred = $BytesTransferred
    TotalBytes       = $TotalBytes
    BytePercent      = $percent
    FilesCompleted   = $FilesCompleted
    TotalFiles       = $TotalFiles
    FilePercent      = $filePercent
  }
}

# ── Echte FTP-Verbindung und Dateitransfer ──────────────────────────────

function Connect-FtpSource {
  <#
  .SYNOPSIS
    Testet die FTP-Verbindung und gibt Verzeichnis-Listing zurueck.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][System.Security.SecureString]$Password
  )

  if ($Config.Protocol -eq 'SFTP') {
    return @{ Success = $false; Error = 'SFTP erfordert SSH.NET oder WinSCP — aktuell nur FTP unterstuetzt' }
  }

  $uri = "ftp://$($Config.Host):$($Config.Port)$($Config.RemotePath)"
  $cred = New-Object System.Net.NetworkCredential($Config.Username, $Password)

  try {
    $request = [System.Net.FtpWebRequest]::Create($uri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::ListDirectoryDetails
    $request.Credentials = $cred
    $request.UseBinary = $true
    $request.UsePassive = $true
    $request.Timeout = 15000

    $response = $request.GetResponse()
    $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
    $listing = $reader.ReadToEnd()
    $reader.Close()
    $response.Close()

    $Config.Connected = $true
    $Config.LastSync = [datetime]::UtcNow

    return @{ Success = $true; Listing = $listing; Config = $Config }
  } catch {
    return @{ Success = $false; Error = $_.Exception.Message }
  }
}

function Get-FtpFileList {
  <#
  .SYNOPSIS
    Holt die Dateiliste von einem FTP-Verzeichnis.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][System.Security.SecureString]$Password
  )

  if ($Config.Protocol -eq 'SFTP') {
    return @{ Success = $false; Files = @(); Error = 'SFTP nicht unterstuetzt' }
  }

  $uri = "ftp://$($Config.Host):$($Config.Port)$($Config.RemotePath)"
  if (-not $uri.EndsWith('/')) { $uri += '/' }
  $cred = New-Object System.Net.NetworkCredential($Config.Username, $Password)

  try {
    $request = [System.Net.FtpWebRequest]::Create($uri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::ListDirectory
    $request.Credentials = $cred
    $request.UseBinary = $true
    $request.UsePassive = $true
    $request.Timeout = 15000

    $response = $request.GetResponse()
    $reader = New-Object System.IO.StreamReader($response.GetResponseStream())
    $names = @($reader.ReadToEnd() -split "`r?`n" | Where-Object { $_ -ne '' })
    $reader.Close()
    $response.Close()

    $files = [System.Collections.Generic.List[hashtable]]::new()
    foreach ($name in $names) {
      $fileUri = $uri + $name
      try {
        $sizeReq = [System.Net.FtpWebRequest]::Create($fileUri)
        $sizeReq.Method = [System.Net.WebRequestMethods+Ftp]::GetFileSize
        $sizeReq.Credentials = $cred
        $sizeReq.UseBinary = $true
        $sizeReq.UsePassive = $true
        $sizeReq.Timeout = 10000
        $sizeResp = $sizeReq.GetResponse()
        $size = $sizeResp.ContentLength
        $sizeResp.Close()
      } catch {
        $size = -1
      }
      $files.Add(@{ Name = $name; Size = $size })
    }

    return @{ Success = $true; Files = ,$files.ToArray() }
  } catch {
    return @{ Success = $false; Files = @(); Error = $_.Exception.Message }
  }
}

function Invoke-FtpDownload {
  <#
  .SYNOPSIS
    Laed eine Datei per FTP herunter.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][System.Security.SecureString]$Password,
    [Parameter(Mandatory)][string]$RemoteFileName,
    [Parameter(Mandatory)][string]$LocalPath
  )

  if ($Config.Protocol -eq 'SFTP') {
    return @{ Success = $false; Error = 'SFTP nicht unterstuetzt' }
  }

  $remotePath = $Config.RemotePath
  if (-not $remotePath.EndsWith('/')) { $remotePath += '/' }
  $uri = "ftp://$($Config.Host):$($Config.Port)$remotePath$RemoteFileName"
  $cred = New-Object System.Net.NetworkCredential($Config.Username, $Password)

  $localDir = [System.IO.Path]::GetDirectoryName($LocalPath)
  if (-not (Test-Path -LiteralPath $localDir)) { [void](New-Item -Path $localDir -ItemType Directory -Force) }

  try {
    $request = [System.Net.FtpWebRequest]::Create($uri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::DownloadFile
    $request.Credentials = $cred
    $request.UseBinary = $true
    $request.UsePassive = $true
    $request.Timeout = 120000

    $response = $request.GetResponse()
    $stream = $response.GetResponseStream()
    $fileStream = [System.IO.File]::Create($LocalPath)

    $buffer = New-Object byte[] 65536
    $totalRead = 0
    while (($read = $stream.Read($buffer, 0, $buffer.Length)) -gt 0) {
      $fileStream.Write($buffer, 0, $read)
      $totalRead += $read
    }

    $fileStream.Close()
    $stream.Close()
    $response.Close()

    return @{ Success = $true; BytesDownloaded = $totalRead; LocalPath = $LocalPath }
  } catch {
    if (Test-Path -LiteralPath $LocalPath) { Remove-Item -LiteralPath $LocalPath -Force -ErrorAction SilentlyContinue }
    return @{ Success = $false; Error = $_.Exception.Message }
  }
}

function Invoke-FtpUpload {
  <#
  .SYNOPSIS
    Laed eine lokale Datei per FTP hoch.
  #>
  param(
    [Parameter(Mandatory)][hashtable]$Config,
    [Parameter(Mandatory)][System.Security.SecureString]$Password,
    [Parameter(Mandatory)][string]$LocalPath,
    [string]$RemoteFileName = ''
  )

  if ($Config.Protocol -eq 'SFTP') {
    return @{ Success = $false; Error = 'SFTP nicht unterstuetzt' }
  }

  if (-not (Test-Path -LiteralPath $LocalPath)) {
    return @{ Success = $false; Error = "Lokale Datei nicht gefunden: $LocalPath" }
  }

  if (-not $RemoteFileName) { $RemoteFileName = [System.IO.Path]::GetFileName($LocalPath) }
  $remotePath = $Config.RemotePath
  if (-not $remotePath.EndsWith('/')) { $remotePath += '/' }
  $uri = "ftp://$($Config.Host):$($Config.Port)$remotePath$RemoteFileName"
  $cred = New-Object System.Net.NetworkCredential($Config.Username, $Password)

  try {
    $request = [System.Net.FtpWebRequest]::Create($uri)
    $request.Method = [System.Net.WebRequestMethods+Ftp]::UploadFile
    $request.Credentials = $cred
    $request.UseBinary = $true
    $request.UsePassive = $true
    $request.Timeout = 120000

    $fileContent = [System.IO.File]::ReadAllBytes($LocalPath)
    $request.ContentLength = $fileContent.Length

    $stream = $request.GetRequestStream()
    $stream.Write($fileContent, 0, $fileContent.Length)
    $stream.Close()

    $response = $request.GetResponse()
    $status = $response.StatusDescription
    $response.Close()

    return @{ Success = $true; BytesUploaded = $fileContent.Length; Status = $status }
  } catch {
    return @{ Success = $false; Error = $_.Exception.Message }
  }
}
