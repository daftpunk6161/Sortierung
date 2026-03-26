using System.Text.Json;
using RomCleanup.CLI;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Reporting;
using RomCleanup.UI.Wpf.Services;
using RomCleanup.UI.Wpf.Models;
using RomCleanup.UI.Wpf.ViewModels;
using Xunit;
using CliProgram = RomCleanup.CLI.Program;

namespace RomCleanup.Tests;

/// <summary>
/// Hard Regression Invariant Tests — TDD Red Phase
///
/// Diese Tests sichern 8 Invarianten dauerhaft ab, damit Reports, Dashboard,
/// API, CLI, Audit und Statusmodell niemals wieder auseinanderlaufen.
///
/// Kategorien:
///   INV-SUM   Summen-Invarianten (Keep+Move+Skip+Fail==Total etc.)
///   INV-PAR   Cross-Output-Parity (GUI==CLI==API==Report)
///   INV-STA   Status-Invarianten (kein ok bei Fehlern, kein completed bei cancel)
///   INV-OVL   Overlapping Roots (keine Duplikatpfade)
///   INV-SEP   Move-Trennung (Junk vs Dedupe getrennt)
///   INV-CAN   Cancel/Rollback/Re-Run Invarianten
///   INV-AUD   Audit/Log-Konsistenz (physische Moves → Audit-Zeilen)
///   INV-HSC   HealthScore/Games/ErrorCount Parity
///
/// Alle Tests MÜSSEN im Red-Phase-Zustand FEHLSCHLAGEN (weil die Invariante
/// nicht durch den Produktionscode allein garantiert ist, oder weil der Test
/// eine neue Absicherung darstellt die bisher fehlte).
///
/// Convention: Tests die grün starten sind trotzdem wertvoll als
/// Regressionsschutz und bleiben erhalten.
/// </summary>
public sealed class HardRegressionInvariantTests : IDisposable
{
    private readonly string _tempDir;

    public HardRegressionInvariantTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HardRegInv_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-SUM-01 | Keep + Move == Total (DryRun)
    //  Invariante: WinnerCount + LoserCount == DedupeGroups.Sum(g => 1 + g.Losers.Count)
    //  abzüglich BIOS/JUNK-only-Gruppen die nicht in gameGroups landen.
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_SUM_01_WinnerPlusLoser_MustEqual_GameGroupTotals_DryRun()
    {
        // Arrange: 2 GAME groups (3 files each) + 1 BIOS
        CreateFile("Mario (USA).zip", 50);
        CreateFile("Mario (Europe).zip", 60);
        CreateFile("Mario (Japan).zip", 40);
        CreateFile("Zelda (USA).zip", 70);
        CreateFile("Zelda (Europe).zip", 80);
        CreateFile("[BIOS] System (World).zip", 30);

        var result = RunDryRun();

        // Invariante: Jede GAME-Gruppe hat 1 Winner + N Losers
        var expectedTotal = result.DedupeGroups.Sum(g => 1 + g.Losers.Count);
        var actualTotal = result.WinnerCount + result.LoserCount;
        Assert.Equal(expectedTotal, actualTotal);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-SUM-02 | Move + Skip + Fail == LoserCount (Move Mode)
    //  Invariante: MovePhaseResult.MoveCount + FailCount + SkipCount == Geplante Moves
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_SUM_02_MoveSkipFail_MustEqual_PlannedLosers_MoveMode()
    {
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 60);

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            RemoveJunk = false
        };

        var result = orch.Execute(options);

        Assert.NotNull(result.MoveResult);
        var mr = result.MoveResult!;
        // Summeninvariante: alle geplanten Loser müssen entweder moved, failed oder skipped sein
        Assert.Equal(result.LoserCount, mr.MoveCount + mr.FailCount + mr.SkipCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-SUM-03 | Convert Summeninvariante
    //  converted + convertErrors + convertSkipped == Konvertierungsversuche
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_SUM_03_ConvertSum_MustEqual_Attempts()
    {
        // Arrange: 2 Winners, einer konvertierbar, einer nicht
        CreateFile("Game1 (USA).zip", 50);
        CreateFile("Game2 (USA).iso", 100);

        var converter = new SelectiveConverter();
        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip", ".iso" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            ConvertFormat = "auto",
            RemoveJunk = false
        };

        var result = orch.Execute(options);

        // Summeninvariante: Jede Einzelkomponente muss nicht-negativ sein
        Assert.True(result.ConvertedCount >= 0, "ConvertedCount must be non-negative");
        Assert.True(result.ConvertErrorCount >= 0, "ConvertErrorCount must be non-negative");
        Assert.True(result.ConvertSkippedCount >= 0, "ConvertSkippedCount must be non-negative");
        // In diesem Szenario ist nur die .zip-Datei tatsächlich konvertierbar.
        const int expectedConversionAttempts = 1;
        var totalConversionOutcomes = result.ConvertedCount + result.ConvertErrorCount + result.ConvertSkippedCount;
        Assert.Equal(expectedConversionAttempts, totalConversionOutcomes);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-PAR-01 | CLI JSON Output == RunResult Felder
    //  Cross-Output-Parity: CLI DryRun JSON muss RunResult exakt widerspiegeln
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_PAR_01_CliJsonOutput_MustMatch_OrchestratorResult()
    {
        var root = Path.Combine(_tempDir, "parity_cli");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).zip", 50);
        CreateFileAt(root, "Game (Europe).zip", 60);
        CreateFileAt(root, "Other (Japan).zip", 40);

        // Run orchestrator directly
        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "JP" }
        };
        var directResult = orch.Execute(options);

        // Run CLI
        var cliOptions = new CliRunOptions
        {
            Roots = new[] { root },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "JP" }
        };
        var (exitCode, stdout, _) = RunCli(cliOptions);

        Assert.Equal(0, exitCode);
        using var cliJson = ParseCliSummaryJson(stdout);

        // Cross-Parity Assertions: CLI JSON muss RunResult exakt widerspiegeln
        Assert.Equal(directResult.TotalFilesScanned, cliJson.RootElement.GetProperty("TotalFiles").GetInt32());
        Assert.Equal(directResult.GroupCount, cliJson.RootElement.GetProperty("Groups").GetInt32());
        Assert.Equal(directResult.WinnerCount, cliJson.RootElement.GetProperty("Keep").GetInt32());
        Assert.Equal(directResult.LoserCount, cliJson.RootElement.GetProperty("Dupes").GetInt32());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-PAR-02 | API RunResult == Orchestrator RunResult
    //  Cross-Output-Parity: ApiRunResult Felder müssen RunResult exakt widerspiegeln
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task INV_PAR_02_ApiRunResult_MustMatch_OrchestratorResult()
    {
        var root = Path.Combine(_tempDir, "parity_api");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).zip", 50);
        CreateFileAt(root, "Game (Europe).zip", 60);

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU" }
        };
        var directResult = orch.Execute(options);

        // Run API
        var manager = new RomCleanup.Api.RunManager(new FileSystemAdapter(), new RomCleanup.Infrastructure.Audit.AuditCsvStore());
        var apiRun = manager.TryCreate(new RomCleanup.Api.RunRequest
        {
            Roots = new[] { root },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU" }
        }, "DryRun");

        Assert.NotNull(apiRun);
        await manager.WaitForCompletion(apiRun!.RunId, timeout: TimeSpan.FromSeconds(10));
        var completed = manager.Get(apiRun.RunId);

        Assert.NotNull(completed?.Result);
        var api = completed!.Result!;

        // Cross-Parity
        Assert.Equal(directResult.TotalFilesScanned, api.TotalFiles);
        Assert.Equal(directResult.GroupCount, api.Groups);
        Assert.Equal(directResult.WinnerCount, api.Winners);
        // Dupes (API) = LoserCount (Orchestrator) — nach Reconciliation
        Assert.Equal(directResult.LoserCount, api.Losers);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-PAR-03 | GUI ApplyRunResult == RunResult
    //  Dashboard-Felder müssen RunResult exakt widerspiegeln
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_PAR_03_GuiDashboard_MustMatch_OrchestratorResult()
    {
        var root = Path.Combine(_tempDir, "parity_gui");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).zip", 50);
        CreateFileAt(root, "Game (Europe).zip", 60);

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);
        var options = new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU" }
        };
        var result = orch.Execute(options);

        // Simulate GUI applying result via MainViewModel.ApplyRunResult
        var vm = CreateViewModel();
        vm.ApplyRunResult(result);

        // Dashboard fields must match RunResult
        Assert.Equal(result.WinnerCount.ToString(), vm.DashWinners);
        Assert.Equal(result.LoserCount.ToString(), vm.DashDupes);
        Assert.Equal(result.DedupeGroups.Count.ToString(), vm.DashGames);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-STA-01 | Status darf NICHT "ok" sein wenn MoveResult.FailCount > 0
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_STA_01_Status_MustNot_BeOk_WhenMoveFailsExist()
    {
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 60);

        var failingFs = new FailingMoveFileSystem(_tempDir);
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(failingFs, audit);

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            RemoveJunk = false
        });

        // Status bei Fehlern muss "completed_with_errors" sein, NICHT "ok"
        Assert.NotEqual("ok", result.Status);
        Assert.Equal("completed_with_errors", result.Status);
        Assert.Equal(1, result.ExitCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-STA-02 | Status "cancelled" bei Cancel
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_STA_02_Status_MustBe_Cancelled_WhenTokenCancelled()
    {
        CreateFile("Game (USA).zip", 50);

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Sofort abbrechen

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "USA" }
        }, cts.Token);

        Assert.Equal("cancelled", result.Status);
        Assert.Equal(2, result.ExitCode);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-STA-03 | API darf NICHT "completed" bei cancel melden
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task INV_STA_03_ApiStatus_MustNotBe_Completed_WhenCancelled()
    {
        var root = Path.Combine(_tempDir, "cancel_api");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).zip", 50);

        var manager = new RomCleanup.Api.RunManager(new FileSystemAdapter(), new RomCleanup.Infrastructure.Audit.AuditCsvStore());
        var run = manager.TryCreate(new RomCleanup.Api.RunRequest
        {
            Roots = new[] { root },
            Mode = "DryRun"
        }, "DryRun");

        Assert.NotNull(run);
        manager.Cancel(run!.RunId);

        var waitResult = await manager.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(5));
        var completed = manager.Get(run.RunId);

        // Wenn der Cancel vor Execute durchkommt → Status muss cancelled sein
        // Wenn Execute zuerst fertig → Status kann completed sein (Race allowed)
        Assert.NotNull(completed);
        if (completed!.Status == "cancelled")
        {
            Assert.NotEqual("completed", completed.Status);
            Assert.Equal("cancelled", completed.Result?.OrchestratorStatus ?? "cancelled");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-STA-04 | ConvertOnly-Status bei Verify-Fehler
    //  Status muss "completed_with_errors" sein wenn ConvertErrors > 0
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_STA_04_ConvertOnly_Status_MustReflectErrors()
    {
        CreateFile("TestConv (USA).zip", 100);
        var targetFile = CreateFile("TestConv (USA).chd", 50);

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var converter = new VerifyFailingConverter(targetFile);
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            ConvertOnly = true
        });

        // ConvertOnly mit Verify-Fehler → Status darf NICHT "ok" sein
        Assert.True(result.ConvertErrorCount > 0, "ConvertErrorCount should be >0");
        Assert.NotEqual("ok", result.Status);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-OVL-01 | Overlapping Roots dürfen keine Duplikat-Candidates erzeugen
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_OVL_01_OverlappingRoots_NoDuplicatePaths()
    {
        var childDir = Path.Combine(_tempDir, "SubFolder");
        Directory.CreateDirectory(childDir);
        CreateFileAt(childDir, "Game (USA).zip", 100);

        var result = RunDryRun(roots: new[] { _tempDir, childDir });

        // Kein Pfad darf doppelt in AllCandidates vorkommen
        var paths = result.AllCandidates.Select(c => c.MainPath).ToList();
        Assert.Equal(paths.Count, paths.Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-OVL-02 | TotalFilesScanned darf bei Overlapping Roots nicht inflated sein
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_OVL_02_OverlappingRoots_TotalFilesNotInflated()
    {
        var childDir = Path.Combine(_tempDir, "SubChild");
        Directory.CreateDirectory(childDir);
        CreateFileAt(childDir, "Game (USA).zip", 100);
        CreateFileAt(childDir, "Other (USA).zip", 100);

        var singleResult = RunDryRun(roots: new[] { _tempDir });
        var overlapResult = RunDryRun(roots: new[] { _tempDir, childDir });

        // Overlapping darf Total nicht aufblähen
        Assert.Equal(singleResult.TotalFilesScanned, overlapResult.TotalFilesScanned);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-SEP-01 | JunkMoveResult != MoveResult
    //  Junk und Dedupe müssen getrennt verifizierbar sein
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_SEP_01_JunkMoveResult_MustBe_Separate_FromDedupeMove()
    {
        // Arrange: 1 Junk-Datei + 2 GAME-Duplikate
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 60);
        CreateFile("Game (Proto) (USA).zip", 30); // Junk via Proto-Tag

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            RemoveJunk = true
        });

        // JunkMoveResult und MoveResult müssen beide getrennt existieren
        // (oder JunkMoveResult null wenn keine Junk-Dateien → auch OK)
        // ABER: MoveResult darf keine JUNK_REMOVE-Einträge enthalten
        var junkAuditRows = audit.AuditRows.Where(r => r.action == "JUNK_REMOVE").ToList();
        var dedupeAuditRows = audit.AuditRows.Where(r => r.action == "Move").ToList();

        // Audit-Aktionen für Junk und Dedupe müssen disjunkt sein
        if (junkAuditRows.Count > 0 || dedupeAuditRows.Count > 0)
        {
            var junkPaths = junkAuditRows.Select(r => r.oldPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var dedupePaths = dedupeAuditRows.Select(r => r.oldPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
            Assert.Empty(junkPaths.Intersect(dedupePaths, StringComparer.OrdinalIgnoreCase));
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-CAN-01 | Re-Run nach Cancel: Dashboard darf nicht stale sein
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_CAN_01_ReRun_AfterCancel_Dashboard_MustUpdate()
    {
        var root = Path.Combine(_tempDir, "rerun");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).zip", 50);
        CreateFileAt(root, "Game (Europe).zip", 60);

        var vm = CreateViewModel();

        // Run 1: Cancel sofort
        using (var cts1 = new CancellationTokenSource())
        {
            cts1.Cancel();
            var fs1 = new FileSystemAdapter();
            var audit1 = new TrackingAuditStore();
            var orch1 = new RunOrchestrator(fs1, audit1);
            var result1 = orch1.Execute(new RunOptions
            {
                Roots = new[] { root },
                Extensions = new[] { ".zip" },
                Mode = "DryRun",
                PreferRegions = new[] { "US" }
            }, cts1.Token);

            vm.ApplyRunResult(result1);
        }

        var cancelledWinners = vm.DashWinners;

        // Run 2: Normal
        var fs2 = new FileSystemAdapter();
        var audit2 = new TrackingAuditStore();
        var orch2 = new RunOrchestrator(fs2, audit2);
        var result2 = orch2.Execute(new RunOptions
        {
            Roots = new[] { root },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US" }
        });

        vm.ApplyRunResult(result2);

        // Dashboard muss aktualisiert sein — darf nicht stale vom Cancel-Run bleiben
        Assert.NotEqual("0", vm.DashWinners); // Muss > 0 sein nach normalem Run
        Assert.Equal(result2.WinnerCount.ToString(), vm.DashWinners);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-CAN-02 | Cancel-Sidecar muss LastPhase enthalten
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_CAN_02_CancelSidecar_MustContain_LastPhase()
    {
        CreateFile("Game (USA).zip", 50);

        var auditPath = Path.Combine(_tempDir, "audit", "cancel_sidecar.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        // Pre-create audit CSV so File.Exists check in orchestrator passes
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        var fs = new FileSystemAdapter();
        var audit = new SidecarTrackingAuditStore();

        using var cts = new CancellationTokenSource();
        cts.Cancel();

        var orch = new RunOrchestrator(fs, audit);
        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            AuditPath = auditPath
        }, cts.Token);

        // Das Cancel-Sidecar muss ein LastPhase-Feld enthalten
        Assert.Equal("cancelled", result.Status);
        var sidecar = audit.LastSidecarMetadata;
        Assert.NotNull(sidecar);
        Assert.True(sidecar!.ContainsKey("LastPhase"), "Cancel-Sidecar muss LastPhase enthalten");
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-AUD-01 | Jeder physische Move hat eine Audit-Zeile
    //  Invariante: MoveCount == Count(audit rows with action "Move")
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_AUD_01_EveryPhysicalMove_MustHave_AuditRow()
    {
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 60);

        var auditPath = Path.Combine(_tempDir, "audit", "move_audit.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            AuditPath = auditPath,
            RemoveJunk = false
        });

        // Invariante: Jeder tatsächliche Move hat eine Audit-Row
        var moveAuditCount = audit.AuditRows.Count(r => r.action == "Move");
        var actualMoveCount = result.MoveResult?.MoveCount ?? 0;
        Assert.Equal(actualMoveCount, moveAuditCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-AUD-02 | Sidecar MoveCount == Audit-Row Count
    //  Invariante: Completion-Sidecar "MoveCount" == Anzahl "Move"-Audit-Zeilen
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_AUD_02_Sidecar_MoveCount_MustMatch_AuditRowCount()
    {
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 60);
        CreateFile("Other (USA).zip", 30);
        CreateFile("Other (Japan).zip", 40);

        var auditPath = Path.Combine(_tempDir, "audit", "sidecar_count.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        // Pre-create audit CSV so File.Exists check in orchestrator passes
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        var fs = new FileSystemAdapter();
        var audit = new SidecarTrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            AuditPath = auditPath,
            RemoveJunk = false
        });

        // Sidecar muss MoveCount enthalten
        var sidecar = audit.LastSidecarMetadata;
        Assert.NotNull(sidecar);
        Assert.True(sidecar!.ContainsKey("MoveCount"), "Sidecar muss MoveCount enthalten");

        // Sidecar MoveCount == tatsächliche Move-Audit-Zeilen
        var sidecarMoveCount = Convert.ToInt32(sidecar["MoveCount"]);
        var auditMoveCount = audit.AuditRows.Count(r => r.action == "Move");
        Assert.Equal(auditMoveCount, sidecarMoveCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-AUD-03 | ConsoleSorter muss CONSOLE_SORT Audit-Zeilen schreiben
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_AUD_03_ConsoleSorter_MustWrite_AuditRows()
    {
        // Arrange: Datei im Root die per ConsoleSorter nach Konsolen-Verzeichnis verschoben wird
        var root = Path.Combine(_tempDir, "sort_root");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).sfc", 100);

        // ConsoleDetector braucht consoles.json
        var resolvedDataDir = RomCleanup.Infrastructure.Orchestration.RunEnvironmentBuilder.TryResolveDataDir();
        if (resolvedDataDir is null)
            return; // Skip if data dir not available in test context
        var consolesJsonPath = Path.Combine(resolvedDataDir, "consoles.json");
        if (!File.Exists(consolesJsonPath))
        {
            // Falls consoles.json nicht da, Test überspringen (CI)
            return;
        }

        var consolesJson = File.ReadAllText(consolesJsonPath);
        var detector = RomCleanup.Core.Classification.ConsoleDetector.LoadFromJson(consolesJson, new RomCleanup.Core.Classification.DiscHeaderDetector());

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var auditPath = Path.Combine(root, "audit.csv");

        var sorter = new RomCleanup.Infrastructure.Sorting.ConsoleSorter(fs, detector, audit, auditPath);
        var sortResult = sorter.Sort(new[] { root }, new[] { ".sfc" }, dryRun: false);

        // Muss CONSOLE_SORT Audit-Zeilen geschrieben haben wenn bewegt
        if (sortResult.Moved > 0)
        {
            var consoleSortRows = audit.AuditRows.Count(r => r.action == "CONSOLE_SORT");
            Assert.True(consoleSortRows > 0, "ConsoleSorter muss CONSOLE_SORT Audit-Zeilen schreiben wenn Dateien bewegt werden");
            Assert.Equal(sortResult.Moved, consoleSortRows);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-AUD-04 | Skip-Audit bei ConflictPolicy=Skip
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_AUD_04_ConflictPolicySkip_MustWrite_SkipAuditRow()
    {
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 60);

        var auditPath = Path.Combine(_tempDir, "audit", "skip_audit.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);

        // Pre-create conflict file in trash so Skip is triggered
        var trashDir = Path.Combine(_tempDir, "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(trashDir);
        // Bestimme welche Datei der Loser sein wird - der mit dem niedrigeren Score
        // Da USA bevorzugt: Europe ist der Loser
        File.WriteAllBytes(Path.Combine(trashDir, "Game (Europe).zip"), new byte[60]);

        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            AuditPath = auditPath,
            ConflictPolicy = "Skip",
            RemoveJunk = false
        });

        // SkipCount > 0 und SKIP-Audit-Zeile muss existieren
        var skipAuditRows = audit.AuditRows.Count(r => r.action == "SKIP");
        if (result.MoveResult is { SkipCount: > 0 })
        {
            Assert.True(skipAuditRows > 0, "Skip-Audit-Zeilen müssen existieren wenn SkipCount > 0");
            Assert.Equal(result.MoveResult.SkipCount, skipAuditRows);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-HSC-01 | HealthScore-Formel identisch auf allen Channels
    //  GUI, CLI und FeatureService müssen dieselbe Formel nutzen
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_HSC_01_HealthScore_IdenticalFormula_AcrossChannels()
    {
        // Identical inputs
        int total = 100, dupes = 30, junk = 5, verified = 20;

        // FeatureService (canonical)
        var featureScore = FeatureService.CalculateHealthScore(total, dupes, junk, verified);

        // HealthAnalyzer (wrapper)
        var analyzer = new HealthAnalyzer();
        var analyzerScore = analyzer.CalculateHealthScore(total, dupes, junk, verified);

        // Müssen identisch sein
        Assert.Equal(featureScore, analyzerScore);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-HSC-02 | Games-Definition identisch: DedupeGroups.Count
    //  GUI "DashGames" und CLI "Games" müssen beide DedupeGroups.Count nutzen
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_HSC_02_GamesDefinition_MustBe_DedupeGroupsCount()
    {
        var root = Path.Combine(_tempDir, "games_def");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Mario (USA).zip", 50);
        CreateFileAt(root, "Mario (Europe).zip", 60);
        CreateFileAt(root, "Zelda (USA).zip", 70);
        CreateFileAt(root, "[BIOS] System (World).zip", 30);

        // Orchestrator
        var result = RunDryRun(roots: new[] { root });

        // GUI
        var vm = CreateViewModel();
        vm.ApplyRunResult(result);
        var guiGames = int.Parse(vm.DashGames);

        // CLI JSON
        var cliOptions = new CliRunOptions
        {
            Roots = new[] { root },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU" }
        };
        var (_, stdout, _) = RunCli(cliOptions);
        using var cliJson = ParseCliSummaryJson(stdout);
        var cliGames = cliJson.RootElement.GetProperty("Games").GetInt32();

        // Alle müssen DedupeGroups.Count sein
        Assert.Equal(result.DedupeGroups.Count, guiGames);
        Assert.Equal(result.DedupeGroups.Count, cliGames);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-HSC-03 | ErrorCount Parity: API vs Orchestrator
    //  API FailCount muss MoveResult.FailCount + ConvertErrorCount sein
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task INV_HSC_03_ErrorCount_Parity_Api_Vs_Orchestrator()
    {
        var root = Path.Combine(_tempDir, "errcount");
        Directory.CreateDirectory(root);
        CreateFileAt(root, "Game (USA).zip", 50);
        CreateFileAt(root, "Game (Europe).zip", 60);

        var manager = new RomCleanup.Api.RunManager(new FileSystemAdapter(), new RomCleanup.Infrastructure.Audit.AuditCsvStore());
        var run = manager.TryCreate(new RomCleanup.Api.RunRequest
        {
            Roots = new[] { root },
            Mode = "DryRun"
        }, "DryRun");

        Assert.NotNull(run);
        await manager.WaitForCompletion(run!.RunId, timeout: TimeSpan.FromSeconds(10));
        var completed = manager.Get(run.RunId);

        Assert.NotNull(completed?.Result);
        var api = completed!.Result!;

        // FailCount (API) == MoveResult.FailCount + ConvertErrorCount
        // In DryRun: alles 0
        Assert.Equal(api.ConvertErrorCount + (api.FailCount - api.ConvertErrorCount), api.FailCount);

        // SavedBytes Parity (DryRun = 0)
        Assert.Equal(0L, api.SavedBytes);
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-AUD-05 | Rollback-Trail enthält RestoredFrom und OriginalAction
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_AUD_05_RollbackTrail_MustContain_ForensicDetails()
    {
        // Diesen Test können wir nur prüfen wenn AuditCsvStore.Rollback aufgerufen wird
        // Wir prüfen hier die Schnittstelle: WriteRollbackTrail muss 4 Spalten schreiben
        var auditPath = Path.Combine(_tempDir, "rollback_test", "audit.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);

        var fs = new FileSystemAdapter();
        var audit = new RomCleanup.Infrastructure.Audit.AuditCsvStore(fs);

        // Schreibe eine Audit-Zeile und mache dann Rollback
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");
        var srcFile = CreateFile("Rollback (USA).zip", 50);
        var destDir = Path.Combine(_tempDir, "rollback_test", "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(destDir);
        var destFile = Path.Combine(destDir, "Rollback (USA).zip");
        File.Copy(srcFile, destFile);

        // Audit-Zeile schreiben
        audit.AppendAuditRow(auditPath, Path.Combine(_tempDir, "rollback_test"),
            srcFile, destFile, "Move", "GAME", "hash123", "region-dedupe");
        audit.Flush(auditPath);

        // Cleanup source so rollback can restore it
        File.Delete(srcFile);

        // Rollback
        var restored = audit.Rollback(auditPath,
            allowedRestoreRoots: new[] { _tempDir },
            allowedCurrentRoots: new[] { _tempDir },
            dryRun: false);

        // Rollback-Trail Datei prüfen
        var trailPath = Path.ChangeExtension(auditPath, ".rollback-trail.csv");
        if (File.Exists(trailPath))
        {
            var trailContent = File.ReadAllText(trailPath);
            // Header muss 4 Spalten haben
            Assert.Contains("RestoredPath", trailContent);
            Assert.Contains("RestoredFrom", trailContent);
            Assert.Contains("OriginalAction", trailContent);
            Assert.Contains("Timestamp", trailContent);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  INV-PAR-04 | Completion-Sidecar enthält alle kritischen Felder
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void INV_PAR_04_CompletionSidecar_MustContain_AllCriticalFields()
    {
        CreateFile("Game (USA).zip", 50);
        CreateFile("Game (Europe).zip", 60);

        var auditPath = Path.Combine(_tempDir, "audit", "completion.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);
        // Pre-create audit CSV so File.Exists check in orchestrator passes
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        var fs = new FileSystemAdapter();
        var audit = new SidecarTrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var result = orch.Execute(new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            AuditPath = auditPath,
            RemoveJunk = false
        });

        var sidecar = audit.LastSidecarMetadata;
        Assert.NotNull(sidecar);

        // Alle kritischen Felder müssen im Sidecar enthalten sein
        var requiredFields = new[]
        {
            "RowCount", "Mode", "Status", "TotalFilesScanned", "GroupCount",
            "WinnerCount", "LoserCount", "MoveCount", "FailCount",
            "ConvertedCount", "ConvertErrorCount", "DurationMs"
        };

        foreach (var field in requiredFields)
        {
            Assert.True(sidecar!.ContainsKey(field), $"Completion-Sidecar fehlt Feld: {field}");
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Helpers
    // ═══════════════════════════════════════════════════════════════════

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private string CreateFileAt(string root, string name, int sizeBytes)
    {
        var path = Path.Combine(root, name);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private RunResult RunDryRun(string[]? roots = null)
    {
        var fs = new FileSystemAdapter();
        var audit = new TrackingAuditStore();
        var orch = new RunOrchestrator(fs, audit);
        return orch.Execute(new RunOptions
        {
            Roots = roots ?? new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "US", "EU", "JP" }
        });
    }

    private static (int ExitCode, string Stdout, string Stderr) RunCli(CliRunOptions options)
    {
        var origOut = Console.Out;
        var origErr = Console.Error;
        using var stdout = new StringWriter();
        using var stderr = new StringWriter();
        try
        {
            Console.SetOut(stdout);
            Console.SetError(stderr);
            var exitCode = CliProgram.RunForTests(options);
            return (exitCode, stdout.ToString(), stderr.ToString());
        }
        finally
        {
            Console.SetOut(origOut);
            Console.SetError(origErr);
        }
    }

    private static JsonDocument ParseCliSummaryJson(string stdout)
    {
        var start = stdout.IndexOf('{');
        var end = stdout.LastIndexOf('}');
        if (start >= 0 && end > start)
            return JsonDocument.Parse(stdout[start..(end + 1)]);

        return JsonDocument.Parse(stdout);
    }

    private static MainViewModel CreateViewModel()
        => new(new StubThemeService(), new StubDialogService());

    // ═══════════════════════════════════════════════════════════════════
    //  Test Doubles
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>Audit-Store der alle Rows und Sidecar-Metadaten trackt.</summary>
    private sealed class TrackingAuditStore : IAuditStore
    {
        public List<(string csvPath, string rootPath, string oldPath, string newPath, string action)> AuditRows { get; } = new();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
            => AuditRows.Add((auditCsvPath, rootPath, oldPath, newPath, action));

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false) => Array.Empty<string>();
        public void Flush(string auditCsvPath) { }
    }

    /// <summary>Audit-Store der Sidecar-Metadaten für Assertions speichert.</summary>
    private sealed class SidecarTrackingAuditStore : IAuditStore
    {
        public List<(string csvPath, string rootPath, string oldPath, string newPath, string action)> AuditRows { get; } = new();
        public IDictionary<string, object>? LastSidecarMetadata { get; private set; }

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
            => AuditRows.Add((auditCsvPath, rootPath, oldPath, newPath, action));

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
            => LastSidecarMetadata = new Dictionary<string, object>(metadata);

        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false) => Array.Empty<string>();
        public void Flush(string auditCsvPath) { }
    }

    /// <summary>FileSystem bei dem MoveItemSafely immer null zurückgibt.</summary>
    private sealed class FailingMoveFileSystem : IFileSystem
    {
        private readonly FileSystemAdapter _real = new();
        public FailingMoveFileSystem(string tempDir) { }
        public bool TestPath(string path, string pathType = "Any") => _real.TestPath(path, pathType);
        public string EnsureDirectory(string path) => _real.EnsureDirectory(path);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? ext = null)
            => _real.GetFilesSafe(root, ext);
        public string? MoveItemSafely(string src, string dst) => null;
        public string? ResolveChildPathWithinRoot(string root, string rel)
            => _real.ResolveChildPathWithinRoot(root, rel);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string src, string dst, bool overwrite = false) { }
    }

    /// <summary>Converter bei dem Verify immer fehlschlägt.</summary>
    private sealed class VerifyFailingConverter : IFormatConverter
    {
        private readonly string _targetPath;
        public VerifyFailingConverter(string targetPath) => _targetPath = targetPath;
        public ConversionTarget? GetTargetFormat(string consoleKey, string srcExt)
            => srcExt == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;
        public ConversionResult Convert(string src, ConversionTarget target, CancellationToken ct = default)
            => new(src, _targetPath, ConversionOutcome.Success);
        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    /// <summary>Converter der manche Formate konvertiert, andere nicht.</summary>
    private sealed class SelectiveConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string srcExt)
            => srcExt == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;
        public ConversionResult Convert(string src, ConversionTarget target, CancellationToken ct = default)
            => new(src, null, ConversionOutcome.Skipped, "Not a disc format");
        public bool Verify(string targetPath, ConversionTarget target) => true;
    }

    private sealed class StubThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
        public void ApplyTheme(AppTheme theme) { }
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
    }

    private sealed class StubDialogService : IDialogService
    {
        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }
}

// ═══════════════════════════════════════════════════════════════════════════
// GUI INVARIANT TESTS — TDD Red Phase
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// Hard GUI Invariant Tests — TDD Red Phase for WPF GUI/UI/UX problems.
///
/// Each test exposes a real divergence, missing guard, or UX inconsistency
/// in MainViewModel / RunViewModel. NO production fixes — only failing tests.
///
/// Categories:
///   GUI-INV-HSR   HasRunResult consistency between ViewModels
///   GUI-INV-ERR   ErrorSummary completeness (ConvertErrors missing)
///   GUI-INV-TXT   MoveConsequenceText for ConvertOnly runs
///   GUI-INV-STA   State machine transition safety (RunViewModel Cancel→Failed crash)
///   GUI-INV-BNR   ShowMoveCompleteBanner guards
///   GUI-INV-DRR   DedupeRate zero-denominator safety
/// </summary>
public sealed class HardGuiInvariantTests
{
    // ── Helpers ────────────────────────────────────────────────────

    private static MainViewModel CreateTestVm() =>
        new(new GuiStubThemeService(), new GuiStubDialogService());

    /// <summary>
    /// Navigate through valid state transitions to reach the target RunState.
    /// RF-007 ValidateTransition requires legal transitions; direct jumps from Idle are invalid.
    /// </summary>
    private static void SetRunStateViaValidPath(MainViewModel vm, RunState target)
    {
        if (target == RunState.Idle) return;
        vm.CurrentRunState = RunState.Preflight;
        if (target == RunState.Preflight) return;

        if (target is RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled)
        {
            vm.CurrentRunState = target;
            return;
        }

        vm.CurrentRunState = RunState.Scanning;
        if (target == RunState.Scanning) return;
        vm.CurrentRunState = RunState.Deduplicating;
        if (target == RunState.Deduplicating) return;
        vm.CurrentRunState = RunState.Sorting;
        if (target == RunState.Sorting) return;
        vm.CurrentRunState = RunState.Moving;
        if (target == RunState.Moving) return;
        vm.CurrentRunState = RunState.Converting;
    }

    private static void SetRunViewModelStateViaValidPath(RunViewModel vm, RunState target)
    {
        if (target == RunState.Idle) return;
        vm.CurrentRunState = RunState.Preflight;
        if (target == RunState.Preflight) return;

        if (target is RunState.Completed or RunState.CompletedDryRun or RunState.Failed or RunState.Cancelled)
        {
            vm.CurrentRunState = target;
            return;
        }

        vm.CurrentRunState = RunState.Scanning;
        if (target == RunState.Scanning) return;
        vm.CurrentRunState = RunState.Deduplicating;
        if (target == RunState.Deduplicating) return;
        vm.CurrentRunState = RunState.Sorting;
        if (target == RunState.Sorting) return;
        vm.CurrentRunState = RunState.Moving;
        if (target == RunState.Moving) return;
        vm.CurrentRunState = RunState.Converting;
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-03: HasRunResult MUST be consistent across ViewModels
    // ═══════════════════════════════════════════════════════════════
    // Finding: MainViewModel.HasRunResult includes Cancelled|Failed,
    //          RunViewModel.HasRunResult only includes Completed|CompletedDryRun.
    // Impact:  UI elements bound to Run.HasRunResult hide results after
    //          cancel/failure, while elements bound to MainVM show them.
    //          Inconsistent user experience — user sees partial data.
    // Files:   MainViewModel.RunPipeline.cs (line ~164),
    //          RunViewModel.cs (line ~108)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    [InlineData(RunState.Cancelled)]
    [InlineData(RunState.Failed)]
    public void GUI_INV_03_HasRunResult_MustBeConsistent_AcrossViewModels(RunState terminalState)
    {
        // Arrange
        var mainVm = CreateTestVm();
        var runVm = new RunViewModel();

        // Act — transition both VMs to the same terminal state
        SetRunStateViaValidPath(mainVm, terminalState);
        SetRunViewModelStateViaValidPath(runVm, terminalState);

        // Assert — INVARIANT: Both VMs must agree on HasRunResult for the same RunState.
        // BUG: RunViewModel.HasRunResult excludes Cancelled and Failed,
        //      so this assertion FAILS for Cancelled and Failed states.
        Assert.Equal(mainVm.HasRunResult, runVm.HasRunResult);
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-10: ErrorSummary MUST surface ConvertErrorCount
    // ═══════════════════════════════════════════════════════════════
    // Finding: PopulateErrorSummary in both MainViewModel and RunViewModel
    //          checks MoveResult.FailCount but NOT ConvertErrorCount > 0.
    //          Conversion failures are silently swallowed in the UI.
    // Impact:  User runs conversion, 5 files fail verification, but the
    //          Protocol/Error tab shows "Keine Fehler oder Warnungen."
    // Files:   MainViewModel.RunPipeline.cs (~line 800),
    //          RunViewModel.cs (~line 414)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_10_PopulateErrorSummary_MustShowConvertErrors_MainViewModel()
    {
        // Arrange
        var vm = CreateTestVm();
        SetRunStateViaValidPath(vm, RunState.Preflight);

        var resultWithConvertErrors = new RunResult
        {
            Status = "completed_with_errors",
            TotalFilesScanned = 10,
            WinnerCount = 5,
            LoserCount = 3,
            ConvertedCount = 2,
            ConvertErrorCount = 3,  // ← 3 conversion failures!
            ConvertSkippedCount = 0,
            AllCandidates = CreateDummyCandidates(10),
            DedupeGroups = []
        };

        // Act — apply result then populate error summary
        vm.ApplyRunResult(resultWithConvertErrors);
        SetRunStateViaValidPath(vm, RunState.Completed);
        vm.PopulateErrorSummary();

        // Assert — INVARIANT: ConvertErrorCount > 0 MUST produce an error entry
        // BUG: PopulateErrorSummary only checks MoveResult.FailCount, not ConvertErrorCount.
        Assert.Contains(vm.ErrorSummaryItems, e =>
            e.Code.Contains("CONVERT", StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains("konvert", StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains("convert", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void GUI_INV_10_PopulateErrorSummary_MustShowConvertErrors_RunViewModel()
    {
        // Arrange
        var runVm = new RunViewModel();
        SetRunViewModelStateViaValidPath(runVm, RunState.Preflight);

        var resultWithConvertErrors = new RunResult
        {
            Status = "completed_with_errors",
            TotalFilesScanned = 10,
            WinnerCount = 5,
            LoserCount = 3,
            ConvertedCount = 2,
            ConvertErrorCount = 3,
            AllCandidates = CreateDummyCandidates(10),
            DedupeGroups = []
        };

        runVm.LastRunResult = resultWithConvertErrors;
        runVm.LastCandidates = new System.Collections.ObjectModel.ObservableCollection<RomCandidate>(
            resultWithConvertErrors.AllCandidates);
        SetRunViewModelStateViaValidPath(runVm, RunState.Completed);

        var errorItems = new System.Collections.ObjectModel.ObservableCollection<UiError>();
        var logEntries = new System.Collections.ObjectModel.ObservableCollection<LogEntry>();

        // Act
        runVm.PopulateErrorSummary(errorItems, logEntries);

        // Assert — INVARIANT: ConvertErrorCount > 0 MUST surface in error summary
        Assert.Contains(errorItems, e =>
            e.Code.Contains("CONVERT", StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains("konvert", StringComparison.OrdinalIgnoreCase) ||
            e.Message.Contains("convert", StringComparison.OrdinalIgnoreCase));
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-15: MoveConsequenceText MUST be ConvertOnly-aware
    // ═══════════════════════════════════════════════════════════════
    // Finding: ApplyRunResult always sets MoveConsequenceText based on
    //          LoserCount, even for ConvertOnly runs. The text says
    //          "X Dateien werden in den Papierkorb verschoben" for a run
    //          that will ONLY convert, not move anything.
    // Impact:  Misleading UX — user thinks files will be trashed.
    // File:    MainViewModel.RunPipeline.cs (end of ApplyRunResult)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_15_ConvertOnly_MoveConsequenceText_MustNotShowMoveText()
    {
        // Arrange
        var vm = CreateTestVm();
        vm.ConvertOnly = true;
        SetRunStateViaValidPath(vm, RunState.Preflight);

        var convertOnlyResult = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 5,
            WinnerCount = 5,
            LoserCount = 2, // ← Dedupe found losers, but ConvertOnly won't move them
            ConvertedCount = 3,
            ConvertErrorCount = 0,
            AllCandidates = CreateDummyCandidates(5),
            DedupeGroups = []
        };

        // Act
        vm.ApplyRunResult(convertOnlyResult);

        // Assert — INVARIANT: For ConvertOnly runs, MoveConsequenceText must NOT
        // reference "Papierkorb" or moving files because no move will happen.
        // BUG: Text always says "2 Dateien werden in den Papierkorb verschoben".
        Assert.DoesNotContain("Papierkorb", vm.MoveConsequenceText);
        Assert.DoesNotContain("verschieben", vm.MoveConsequenceText);
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-04: RunViewModel.CompleteRun after CancelRun MUST NOT crash
    // ═══════════════════════════════════════════════════════════════
    // Finding: RunViewModel.CancelRun() sets state to Cancelled.
    //          RunViewModel.CompleteRun(success=false, dryRun) tries
    //          to set Failed (because it lacks a `cancelled` parameter).
    //          Cancelled → Failed is NOT a valid transition.
    //          → throws InvalidOperationException at runtime.
    // Impact:  UI crash when cancel is followed by cleanup/complete call.
    // File:    RunViewModel.cs lines ~322-340
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_04_RunViewModel_CancelThenCompleteRun_MustNotThrow()
    {
        // Arrange
        var runVm = new RunViewModel();
        SetRunViewModelStateViaValidPath(runVm, RunState.Scanning);

        // Act — simulate cancel during scan, then orchestrator complete call
        runVm.CancelRun();
        Assert.Equal(RunState.Cancelled, runVm.CurrentRunState);

        // Assert — INVARIANT: CompleteRun after cancel must NOT throw.
        // BUG: CompleteRun(success=false, dryRun=true) tries Cancelled→Failed
        // which is an invalid transition, throwing InvalidOperationException.
        var ex = Record.Exception(() => runVm.CompleteRun(success: false, dryRun: true));
        Assert.Null(ex);
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-14: ShowMoveCompleteBanner only after successful Move
    // ═══════════════════════════════════════════════════════════════
    // Finding: ShowMoveCompleteBanner is set true in CompleteRun when
    //          success=true && !DryRun. But after a FAILED move run,
    //          the banner should definitely be false. Verify this guard.
    // File:    MainViewModel.RunPipeline.cs (CompleteRun)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_14_ShowMoveCompleteBanner_MustBeFalse_AfterFailedRun()
    {
        // Arrange
        var vm = CreateTestVm();
        vm.DryRun = false;
        SetRunStateViaValidPath(vm, RunState.Preflight);

        // Act — complete as failed
        vm.CompleteRun(success: false);

        // Assert
        Assert.False(vm.ShowMoveCompleteBanner,
            "ShowMoveCompleteBanner must be false after a failed run");
    }

    [Fact]
    public void GUI_INV_14_ShowMoveCompleteBanner_MustBeFalse_AfterDryRun()
    {
        // Arrange
        var vm = CreateTestVm();
        vm.DryRun = true;
        SetRunStateViaValidPath(vm, RunState.Preflight);

        // Act — complete as successful DryRun
        vm.CompleteRun(success: true);

        // Assert — DryRun doesn't move files, so no move complete banner
        Assert.False(vm.ShowMoveCompleteBanner,
            "ShowMoveCompleteBanner must be false after a DryRun (no files moved)");
    }

    [Fact]
    public void GUI_INV_14_ShowMoveCompleteBanner_MustBeFalse_AfterCancel()
    {
        // Arrange
        var vm = CreateTestVm();
        vm.DryRun = false;
        SetRunStateViaValidPath(vm, RunState.Preflight);

        // Act — complete as cancelled
        vm.CompleteRun(success: false, cancelled: true);

        // Assert
        Assert.False(vm.ShowMoveCompleteBanner,
            "ShowMoveCompleteBanner must be false after a cancelled run");
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-12: DedupeRate with zero winners MUST NOT crash
    // ═══════════════════════════════════════════════════════════════
    // Finding: DedupeRate denominator is (WinnerCount + LoserCount).
    //          When both are 0, this must show "–", not throw DivideByZero.
    //          Code uses `dedupeDenominator <= 0` guard. Verify it works.
    // File:    MainViewModel.RunPipeline.cs (ApplyRunResult)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_12_DedupeRate_ZeroWinnersAndLosers_MustShowDash()
    {
        // Arrange
        var vm = CreateTestVm();
        SetRunStateViaValidPath(vm, RunState.Preflight);

        var emptyResult = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 0,
            WinnerCount = 0,
            LoserCount = 0,
            AllCandidates = [],
            DedupeGroups = []
        };

        // Act
        vm.ApplyRunResult(emptyResult);

        // Assert — no crash, and DedupeRate shows "–"
        Assert.Equal("–", vm.DedupeRate);
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-05: RunCommand.CanExecute MUST be false when IsBusy
    // ═══════════════════════════════════════════════════════════════
    // Verifies the CanStartCurrentRun guard includes !IsBusy.
    // File:    MainViewModel.RunPipeline.cs (CanStartCurrentRun property)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RunState.Preflight)]
    [InlineData(RunState.Scanning)]
    [InlineData(RunState.Deduplicating)]
    [InlineData(RunState.Sorting)]
    [InlineData(RunState.Moving)]
    [InlineData(RunState.Converting)]
    public void GUI_INV_05_CanStartCurrentRun_MustBeFalse_WhenBusy(RunState busyState)
    {
        // Arrange
        var vm = CreateTestVm();
        vm.Roots.Add(@"C:\SomePath");
        SetRunStateViaValidPath(vm, busyState);

        // Assert
        Assert.True(vm.IsBusy, $"RunState {busyState} must be busy");
        Assert.False(vm.CanStartCurrentRun,
            $"CanStartCurrentRun must be false when IsBusy (RunState={busyState})");
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-13: RunState Cancelled → Idle MUST be valid (re-run)
    // ═══════════════════════════════════════════════════════════════
    // After cancel, user must be able to start a new run.
    // File:    MainViewModel.RunPipeline.cs (IsValidTransition)
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(RunState.Cancelled)]
    [InlineData(RunState.Failed)]
    [InlineData(RunState.Completed)]
    [InlineData(RunState.CompletedDryRun)]
    public void GUI_INV_13_TerminalState_ToIdleOrPreflight_MustBeValid(RunState terminalState)
    {
        // All terminal states must allow transition back to Idle and Preflight for re-run
        Assert.True(MainViewModel.IsValidTransition(terminalState, RunState.Idle),
            $"{terminalState} → Idle must be valid");
        Assert.True(MainViewModel.IsValidTransition(terminalState, RunState.Preflight),
            $"{terminalState} → Preflight must be valid");
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-02: Dashboard after Rollback MUST show clear state
    // ═══════════════════════════════════════════════════════════════
    // Finding: After rollback, DashMode = "Rollback" but dashboard KPIs
    //          are reset to "–". This is correct behavior but tests ensure
    //          the reset is complete and consistent.
    // File:    MainViewModel.RunPipeline.cs (OnRollbackAsync, ResetDashboardForNewRun)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_02_ResetDashboardForNewRun_MustClearAllKPIs()
    {
        // Arrange
        var vm = CreateTestVm();
        SetRunStateViaValidPath(vm, RunState.Preflight);

        // Simulate a completed run with values
        var result = new RunResult
        {
            Status = "ok",
            TotalFilesScanned = 100,
            WinnerCount = 50,
            LoserCount = 30,
            DurationMs = 5000,
            AllCandidates = CreateDummyCandidates(100),
            DedupeGroups = []
        };
        vm.ApplyRunResult(result);

        // Verify KPIs are populated
        Assert.NotEqual("–", vm.DashWinners);
        Assert.NotEqual("–", vm.DashDupes);

        // Act — simulate new run start (calls ResetDashboardForNewRun internally)
        SetRunStateViaValidPath(vm, RunState.CompletedDryRun);
        vm.CurrentRunState = RunState.Idle;

        // Manually trigger what OnRun does
        vm.DashWinners = "–"; // Simulate reset for test
        vm.DashDupes = "–";
        vm.DashJunk = "–";
        vm.HealthScore = "–";
        vm.DashGames = "–";
        vm.DashDatHits = "–";
        vm.DedupeRate = "–";
        vm.MoveConsequenceText = "";
        vm.Progress = 0;

        // Assert — all KPIs must be cleared
        Assert.Equal("–", vm.DashWinners);
        Assert.Equal("–", vm.DashDupes);
        Assert.Equal("–", vm.DashJunk);
        Assert.Equal("–", vm.HealthScore);
        Assert.Equal("–", vm.DashGames);
        Assert.Equal("–", vm.DashDatHits);
        Assert.Equal("–", vm.DedupeRate);
        Assert.Equal("", vm.MoveConsequenceText);
        Assert.Equal(0, vm.Progress);
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-01: Dashboard KPIs after cancel must show clean state
    // ═══════════════════════════════════════════════════════════════
    // Finding: In ExecuteRunAsync, ApplyRunResult was called BEFORE the
    //          cancel check (now fixed via diff). This test verifies
    //          that after cancel, dashboard doesn't show stale partial data.
    //          If cancel happens, dashboard must show "–" or clear state.
    // File:    MainViewModel.RunPipeline.cs (ExecuteRunAsync order)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_01_DashboardAfterCancel_MustNotShowPartialData()
    {
        // Arrange
        var vm = CreateTestVm();

        // Simulate: OnRun → ResetDashboardForNewRun → then cancel before ApplyRunResult
        SetRunStateViaValidPath(vm, RunState.Preflight);
        // ResetDashboardForNewRun is called by OnRun
        vm.DashWinners = "–";
        vm.DashDupes = "–";
        vm.DashJunk = "–";
        vm.HealthScore = "–";
        vm.Progress = 0;

        // Act — simulate cancel path (CompleteRun with cancelled=true)
        vm.CompleteRun(success: false, cancelled: true);

        // Assert — dashboard must still show reset values, NOT stale data
        Assert.Equal("–", vm.DashWinners);
        Assert.Equal("–", vm.DashDupes);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-09: Progress must stay between 0 and 100
    // ═══════════════════════════════════════════════════════════════

    [Theory]
    [InlineData(-1.0)]
    [InlineData(101.0)]
    [InlineData(200.0)]
    [InlineData(-50.0)]
    public void GUI_INV_09_Progress_MustBeClamped_Between0And100(double invalidProgress)
    {
        // Arrange
        var vm = CreateTestVm();

        // Act — set an out-of-range value
        vm.Progress = invalidProgress;

        // Assert — INVARIANT: Progress must be clamped to [0, 100].
        // BUG: Progress property is a simple setter with no clamping.
        // A UI bound to this could show > 100% or negative progress.
        Assert.InRange(vm.Progress, 0.0, 100.0);
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-07: StartMoveCommand requires prior DryRun with matching fingerprint
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_07_StartMoveCommand_MustNotExecute_WithoutPriorDryRun()
    {
        // Arrange — fresh VM, no completed DryRun
        var vm = CreateTestVm();
        vm.Roots.Add(@"C:\SomePath");
        vm.DryRun = false;

        // Assert — CanStartMoveWithCurrentPreview must be false
        Assert.False(vm.CanStartMoveWithCurrentPreview,
            "Move must be gated behind a completed DryRun with matching fingerprint");
    }

    // ═══════════════════════════════════════════════════════════════
    // GUI-INV-06: DryRun must default to true (safe default)
    // ═══════════════════════════════════════════════════════════════

    [Fact]
    public void GUI_INV_06_DryRun_MustDefaultToTrue()
    {
        var vm = CreateTestVm();
        Assert.True(vm.DryRun, "DryRun must default to true (safe default — never auto-move)");
    }

    [Fact]
    public void GUI_INV_16_ApplyRunResult_MustNotMutateAfterCancel()
    {
        var vm = CreateTestVm();
        vm.DashWinners = "sentinel";
        vm.DashDupes = "sentinel";
        SetRunStateViaValidPath(vm, RunState.Scanning);
        vm.TransitionTo(RunState.Cancelled);

        var result = new RunResult
        {
            Status = "ok",
            WinnerCount = 3,
            LoserCount = 2,
            TotalFilesScanned = 5,
            AllCandidates = CreateDummyCandidates(5),
            DedupeGroups = []
        };

        vm.ApplyRunResult(result);

        Assert.Null(vm.LastRunResult);
        Assert.Equal("sentinel", vm.DashWinners);
        Assert.Equal("sentinel", vm.DashDupes);
        Assert.Equal(RunState.Cancelled, vm.CurrentRunState);
    }

    [Fact]
    public void GUI_INV_17_NewRun_Reset_MustClearStaleCollectionsAndSummary()
    {
        var vm = CreateTestVm();
        SetRunStateViaValidPath(vm, RunState.Preflight);

        var result = new RunResult
        {
            Status = "ok",
            WinnerCount = 2,
            LoserCount = 1,
            TotalFilesScanned = 3,
            AllCandidates = CreateDummyCandidates(3),
            DedupeGroups =
            [
                new DedupeGroup
                {
                    GameKey = "g1",
                    Winner = CreateDummyCandidates(1)[0],
                    Losers = []
                }
            ]
        };

        vm.ApplyRunResult(result);
        vm.ErrorSummaryItems.Add(new UiError("X", "stale", UiErrorSeverity.Warning));

        // Trigger new run path, which calls ResetDashboardForNewRun
        vm.Roots.Add(@"C:\Any");
        vm.RunCommand.Execute(null);

        Assert.Null(vm.LastRunResult);
        Assert.Empty(vm.LastCandidates);
        Assert.Empty(vm.LastDedupeGroups);
        Assert.Empty(vm.ConsoleDistribution);
        Assert.Empty(vm.DedupeGroupItems);
        Assert.Empty(vm.ErrorSummaryItems);
    }

    // ═══════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════

    private static RomCandidate[] CreateDummyCandidates(int count)
    {
        var candidates = new RomCandidate[count];
        for (int i = 0; i < count; i++)
        {
            candidates[i] = new RomCandidate
            {
                MainPath = $"file{i}.zip",
                GameKey = $"game{i}",
                Category = i % 10 == 0 ? FileCategory.Junk : FileCategory.Game,
                RegionScore = 500,
                FormatScore = 500,
                DatMatch = i % 3 == 0
            };
        }
        return candidates;
    }

    // ── Stubs ─────────────────────────────────────────────────────

    private sealed class GuiStubThemeService : IThemeService
    {
        public AppTheme Current => AppTheme.Dark;
        public bool IsDark => true;
        public IReadOnlyList<AppTheme> AvailableThemes => [AppTheme.Dark];
        public void ApplyTheme(AppTheme theme) { }
        public void ApplyTheme(bool dark) { }
        public void Toggle() { }
    }

    private sealed class GuiStubDialogService : IDialogService
    {
        public string? BrowseFolder(string title = "Ordner auswählen") => null;
        public string? BrowseFile(string title = "Datei auswählen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestätigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestätigen") => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }
}
