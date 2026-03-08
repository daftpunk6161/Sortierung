function New-FileWithContent {
    param(
        [Parameter(Mandatory = $true)][string]$Path,
        [Parameter(Mandatory = $true)][string]$Content
    )

    $dir = Split-Path -Parent $Path
    if ($dir -and -not (Test-Path -LiteralPath $dir)) {
        [void](New-Item -ItemType Directory -Path $dir -Force)
    }

    $Content | Out-File -LiteralPath $Path -Encoding ascii -Force
    return $Path
}

function New-E2ESmokeFixtureSet {
    param(
        [Parameter(Mandatory = $true)][string]$Root
    )

    [void](New-Item -ItemType Directory -Path $Root -Force)

    $paths = [ordered]@{}

    $paths.EuChd = New-FileWithContent -Path (Join-Path $Root 'Mega Fixture Game (Europe).chd') -Content 'eu-fixture'
    $paths.UsChd = New-FileWithContent -Path (Join-Path $Root 'Mega Fixture Game (USA).chd') -Content 'us-fixture'

    $paths.CueTrack1 = New-FileWithContent -Path (Join-Path $Root 'Cue Fixture Game (Europe) (Track 01).bin') -Content 'cue-track-01'
    $paths.CueTrack2 = New-FileWithContent -Path (Join-Path $Root 'Cue Fixture Game (Europe) (Track 02).bin') -Content 'cue-track-02'
    $paths.Cue = New-FileWithContent -Path (Join-Path $Root 'Cue Fixture Game (Europe).cue') -Content @"
FILE "Cue Fixture Game (Europe) (Track 01).bin" BINARY
  TRACK 01 MODE1/2352
    INDEX 01 00:00:00
FILE "Cue Fixture Game (Europe) (Track 02).bin" BINARY
  TRACK 02 AUDIO
    INDEX 01 00:02:00
"@

    $paths.GdiTrack1 = New-FileWithContent -Path (Join-Path $Root 'gdi_fixture_track01.raw') -Content 'gdi-track-01'
    $paths.GdiTrack2 = New-FileWithContent -Path (Join-Path $Root 'gdi_fixture_track02.bin') -Content 'gdi-track-02'
    $paths.Gdi = New-FileWithContent -Path (Join-Path $Root 'Gdi Fixture Game (Europe).gdi') -Content @"
2
1 0 4 2352 gdi_fixture_track01.raw 0
2 45000 4 2352 gdi_fixture_track02.bin 0
"@

    $paths.Ccd = New-FileWithContent -Path (Join-Path $Root 'Ccd Fixture Game (Europe).ccd') -Content '[CloneCD]'
    $paths.CcdImg = New-FileWithContent -Path (Join-Path $Root 'Ccd Fixture Game (Europe).img') -Content 'ccd-img'
    $paths.CcdSub = New-FileWithContent -Path (Join-Path $Root 'Ccd Fixture Game (Europe).sub') -Content 'ccd-sub'

    $paths.M3u = New-FileWithContent -Path (Join-Path $Root 'Playlist Fixture Game (Europe).m3u') -Content @"
# fixture playlist
Cue Fixture Game (Europe).cue
Gdi Fixture Game (Europe).gdi
Ccd Fixture Game (Europe).ccd
Mega Fixture Game (Europe).chd
"@

    $paths.Junk = New-FileWithContent -Path (Join-Path $Root 'Junk Fixture Game (Beta).chd') -Content 'junk-fixture'
    $paths.Bios = New-FileWithContent -Path (Join-Path $Root '[BIOS] Fixture Console (Europe).bin') -Content 'bios-fixture'

    $archiveSrc = Join-Path $Root 'archive-src'
    [void](New-Item -ItemType Directory -Path $archiveSrc -Force)
    [void](New-FileWithContent -Path (Join-Path $archiveSrc 'archive-disc.iso') -Content 'archive-disc')
    $paths.ArchiveZip = Join-Path $Root 'Archive Fixture Game (Europe).zip'
    if (Test-Path -LiteralPath $paths.ArchiveZip) {
        Remove-Item -LiteralPath $paths.ArchiveZip -Force -ErrorAction SilentlyContinue
    }
    Compress-Archive -Path (Join-Path $archiveSrc '*') -DestinationPath $paths.ArchiveZip -Force
    Remove-Item -LiteralPath $archiveSrc -Recurse -Force -ErrorAction SilentlyContinue

    return [pscustomobject]@{
        Root = $Root
        Paths = [pscustomobject]$paths
    }
}
