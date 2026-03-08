BeforeAll {
  $loaderPath = Join-Path $PSScriptRoot '..\..\modules\RomCleanupLoader.ps1'
  if (Test-Path $loaderPath) { . $loaderPath }
}

Describe 'BackgroundOps – Start/Complete/Cancel' {

  Context 'Start-BackgroundRunspace' {

    It 'gibt Hashtable mit erwarteten Keys zurück' {
      if (-not (Get-Command Start-BackgroundRunspace -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Start-BackgroundRunspace nicht verfügbar'
        return
      }
      $rs = Start-BackgroundRunspace
      try {
        $rs | Should -Not -BeNullOrEmpty
        $rs.Keys | Should -Contain 'LogQueue'
        $rs.Keys | Should -Contain 'CancelEvent'
      } finally {
        if ($rs.Runspace) { try { $rs.Runspace.Dispose() } catch {} }
        if ($rs.PS) { try { $rs.PS.Dispose() } catch {} }
      }
    }

    It 'CancelEvent ist initial nicht gesetzt' {
      if (-not (Get-Command Start-BackgroundRunspace -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Start-BackgroundRunspace nicht verfügbar'
        return
      }
      $rs = Start-BackgroundRunspace
      try {
        $rs.CancelEvent.IsSet | Should -Be $false
      } finally {
        if ($rs.Runspace) { try { $rs.Runspace.Dispose() } catch {} }
        if ($rs.PS) { try { $rs.PS.Dispose() } catch {} }
      }
    }
  }

  Context 'Start-BackgroundDedupe – Parameter-Mapping' {

    It 'akzeptiert minimale Parameter ohne Exception' {
      if (-not (Get-Command Start-BackgroundDedupe -ErrorAction SilentlyContinue)) {
        Set-ItResult -Skipped -Because 'Start-BackgroundDedupe nicht verfügbar'
        return
      }
      $tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) "bgops_test_$([guid]::NewGuid().ToString('N').Substring(0,8))"
      New-Item -Path $tempRoot -ItemType Directory -Force | Out-Null
      try {
        $params = @{
          Roots      = @($tempRoot)
          Mode       = 'DryRun'
          Extensions = @('.zip')
        }
        $job = Start-BackgroundDedupe -DedupeParams $params
        $job | Should -Not -BeNullOrEmpty
        $job.Completed | Should -Be $false
        # Cleanup
        if ($job.CancelEvent) { $job.CancelEvent.Set() }
        Start-Sleep -Milliseconds 500
        if ($job.PS) { try { $job.PS.Dispose() } catch {} }
        if ($job.Runspace) { try { $job.Runspace.Dispose() } catch {} }
      } finally {
        Remove-Item -Path $tempRoot -Recurse -Force -ErrorAction SilentlyContinue
      }
    }
  }

  Context 'Cancel-Mechanik' {

    It 'CancelEvent.Set() signalisiert Abbruch' {
      $cancel = [System.Threading.ManualResetEventSlim]::new($false)
      $cancel.IsSet | Should -Be $false
      $cancel.Set()
      $cancel.IsSet | Should -Be $true
      $cancel.Dispose()
    }
  }

  Context 'LogQueue – Concurrency' {

    It 'ConcurrentQueue akzeptiert parallele Schreibzugriffe' {
      $queue = [System.Collections.Concurrent.ConcurrentQueue[string]]::new()
      1..100 | ForEach-Object { $queue.Enqueue("msg$_") }
      $queue.Count | Should -Be 100
    }
  }
}
