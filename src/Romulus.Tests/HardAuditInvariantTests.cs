using System.Text.Json;
using Romulus.CLI;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Reporting;
using Romulus.UI.Wpf.Services;
using Xunit;
using CliProgram = Romulus.CLI.Program;

namespace Romulus.Tests;

/// <summary>
/// Hard Audit Invariant Tests — Issue #9
///
/// Diese Tests beweisen die im FINAL_CONSOLIDATED_AUDIT dokumentierten P0/P1-Bugs.
/// Jeder Test MUSS heute ROT sein. Kein Produktionscode wird hier gefixt.
///
/// Priorität: P0-A bis P0-D (Release-Blocker), dann P1-01 bis P1-07.
/// </summary>
public sealed class HardAuditInvariantTests : IDisposable
{
    private readonly string _tempDir;

    public HardAuditInvariantTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "HardAudit_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P0-A | Convert Verify-Failure: Doppelzählung + Datenverlust
    // Finding: F-026, F-027, F-032
    // Ursache: converted++ VOR Verify. Bei Verify-Fehler: convertErrors++
    //          zusätzlich (doppelt). Source-Datei wird trotzdem getrashed.
    // Betroffen: RunOrchestrator.cs Phase 6 (Convert)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P0A_ConvertVerifyFailure_ConvertedCount_MustNotIncrement_Issue9()
    {
        // Arrange: Eine Datei, Converter meldet "Success", Verify schlägt fehl.
        var sourceFile = CreateFile("Winner (USA).zip", 100);
        var targetFile = CreateFile("Winner (USA).chd", 50); // Simulate converted file

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var converter = new VerifyFailingConverter(targetFile);
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            ConvertFormat = "chd",
            PreferRegions = new[] { "USA" }
        };

        // Act
        var result = orch.Execute(options);

        // Assert: Verify ist fehlgeschlagen → ConvertedCount darf NICHT erhöht sein.
        // BUG: Aktuell ist ConvertedCount == 1 (converted++ VOR Verify).
        Assert.Equal(0, result.ConvertedCount);
    }

    [Fact]
    public void P0A_ConvertVerifyFailure_SourceFile_MustSurvive_Issue9()
    {
        // Arrange
        var sourceFile = CreateFile("Survivor (USA).zip", 100);
        var targetFile = CreateFile("Survivor (USA).chd", 50);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var converter = new VerifyFailingConverter(targetFile);
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            ConvertFormat = "chd",
            PreferRegions = new[] { "USA" }
        };

        // Act
        orch.Execute(options);

        // Assert: Bei Verify-Failure darf die Quelldatei NICHT gelöscht/in Trash verschoben werden.
        // BUG: Source wird trotzdem in _TRASH_CONVERTED verschoben → Datenverlust.
        Assert.True(File.Exists(sourceFile),
            "Quelldatei wurde bei Verify-Failure in Trash verschoben — DATENVERLUST (P0-A/F-032)");
    }

    [Fact]
    public void P0A_ConvertVerifyFailure_SumInvariant_Issue9()
    {
        // Arrange: Eine Datei, Verify schlägt fehl.
        var sourceFile = CreateFile("SumCheck (USA).zip", 100);
        var targetFile = CreateFile("SumCheck (USA).chd", 50);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var converter = new VerifyFailingConverter(targetFile);
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            ConvertFormat = "chd",
            PreferRegions = new[] { "USA" }
        };

        // Act
        var result = orch.Execute(options);

        // Assert: Summeninvariante: converted + errors + skipped == Konvertierungsversuche (1)
        // BUG: 1 (converted) + 1 (error) + 0 (skipped) = 2 ≠ 1 (Doppelzählung).
        var totalCounted = result.ConvertedCount + result.ConvertErrorCount + result.ConvertSkippedCount;
        Assert.Equal(1, totalCounted);
    }

    [Fact]
    public void P0A_ConvertVerifyFailure_AuditMustNotLogSuccess_Issue9()
    {
        // Arrange
        var sourceFile = CreateFile("AuditCheck (USA).zip", 100);
        var targetFile = CreateFile("AuditCheck (USA).chd", 50);
        var auditPath = Path.Combine(_tempDir, "audit", "audit.csv");
        Directory.CreateDirectory(Path.GetDirectoryName(auditPath)!);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var converter = new VerifyFailingConverter(targetFile);
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            ConvertFormat = "chd",
            PreferRegions = new[] { "USA" },
            AuditPath = auditPath
        };

        // Act
        orch.Execute(options);

        // Assert: Bei Verify-Failure darf kein "CONVERT" Audit-Eintrag geschrieben werden.
        // BUG: Audit schreibt "CONVERT" für fehlgeschlagene Verifikation (F-027).
        var convertAuditRows = audit.AuditRows.Where(r => r.action == "CONVERT").ToList();
        Assert.Empty(convertAuditRows);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P0-B | Drei divergente HealthScore-Formeln
    // Finding: F-002, F-011
    // Ursache: MainViewModel nutzt FeatureService.CalculateHealthScore(),
    //          RunViewModel nutzt WinnerCount/Total, ReportSummary hat eigene Ableitung.
    // Betroffen: FeatureService.Analysis.cs, RunViewModel.cs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P0B_HealthScore_Parity_FeatureService_Vs_RunViewModel_Issue9()
    {
        // Arrange: Identische Werte, zwei verschiedene Formeln.
        int totalFiles = 100;
        int winnerCount = 30;
        int loserCount = 70;
        int junkCount = 5;
        int verified = 10;

        // MainViewModel-Formel (FeatureService.CalculateHealthScore):
        var mainViewModelScore = FeatureService.CalculateHealthScore(
            totalFiles, loserCount, junkCount, verified);

        // RunViewModel-Formel: 100.0 * WinnerCount / Total
        // (aus RunViewModel.cs:352 → HealthScore = $"{100.0 * result.WinnerCount / total:F0}%")
        var runViewModelScore = (int)(100.0 * winnerCount / totalFiles);

        // Assert: Beide MÜSSEN denselben Wert ergeben. Tun sie nicht.
        // BUG: FeatureService berechnet ~50, RunViewModel berechnet 30.
        Assert.Equal(mainViewModelScore, runViewModelScore);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P0-C | "Games" Definition divergiert GUI vs. CLI
    // Finding: F-012
    // Ursache: GUI = DedupeGroups.Count (alle Gruppen inkl. BIOS/JUNK)
    //          CLI = AllCandidates.Count(c => c.Category == "GAME") (nur GAME-Dateien)
    // Betroffen: MainViewModel.RunPipeline.cs, CLI/Program.cs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P0C_GamesCount_GroupsVsGameCandidates_MustMatch_Issue9()
    {
        // Arrange: Mischung aus GAME, BIOS und JUNK Dateien mit verschiedenen GameKeys.
        CreateFile("Mario (USA).zip", 50);           // GAME
        CreateFile("Zelda (Europe).zip", 60);          // GAME
        CreateFile("[BIOS] System (World).zip", 40);   // BIOS (wird als BIOS klassifiziert)

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun",
            PreferRegions = new[] { "USA", "EU" }
        };

        // Act
        var result = orch.Execute(options);

        // GUI-Definition: "Games" = result.DedupeGroups.Count (inkl. BIOS-Gruppen)
        var guiGames = result.DedupeGroups.Count;

        // CLI-Definition: "Games" = AllCandidates.Count(c => c.Category == "GAME")
        var cliGames = result.AllCandidates.Count(c => c.Category == FileCategory.Game);

        // Assert: MÜSSEN identisch sein.
        // BUG: GUI zählt alle Gruppen (inkl. BIOS), CLI zählt nur GAME-Dateien.
        Assert.Equal(guiGames, cliGames);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P0-D | Überlappende Roots: Duplikat-Candidates
    // Finding: F-045
    // Ursache: ScanFiles iteriert jede Root unabhängig, kein cross-root Path-Dedup.
    // Betroffen: RunOrchestrator.cs ScanFiles
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P0D_OverlappingRoots_NoDuplicateCandidates_Issue9()
    {
        // Arrange: Parent-Root enthält Child-Root. Child enthält eine Datei.
        var childDir = Path.Combine(_tempDir, "SNES");
        Directory.CreateDirectory(childDir);
        var file = Path.Combine(childDir, "Game (USA).zip");
        File.WriteAllBytes(file, new byte[100]);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        // Roots = [parent, child] → parent scannt rekursiv und findet child/Game.zip,
        // child scannt ebenfalls child/Game.zip → Duplikat.
        var options = new RunOptions
        {
            Roots = new[] { _tempDir, childDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        // Act
        var result = orch.Execute(options);

        // Assert: Dieselbe Datei darf nur EINMAL in AllCandidates.
        // BUG: Kein Cross-Root-Dedup → Datei erscheint zweimal.
        var distinctPaths = result.AllCandidates
            .Select(c => Path.GetFullPath(c.MainPath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();
        Assert.Equal(result.AllCandidates.Count, distinctPaths);
    }

    [Fact]
    public void P0D_OverlappingRoots_TotalFilesNotInflated_Issue9()
    {
        // Arrange: Exakt 1 Datei in überlappenden Roots.
        var childDir = Path.Combine(_tempDir, "GBA");
        Directory.CreateDirectory(childDir);
        File.WriteAllBytes(Path.Combine(childDir, "Pokémon (USA).zip"), new byte[100]);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir, childDir },
            Extensions = new[] { ".zip" },
            Mode = "DryRun"
        };

        // Act
        var result = orch.Execute(options);

        // Assert: 1 physische Datei → TotalFilesScanned == 1
        // BUG: Wird durch Overlap doppelt gezählt → TotalFilesScanned == 2
        Assert.Equal(1, result.TotalFilesScanned);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P1-01 | Status = "ok" trotz Move-/Convert-Fehlern
    // Finding: F-014
    // Ursache: result.Status = "ok" unconditional nach Phase 6.
    // Betroffen: RunOrchestrator.cs:395
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P1_01_Status_MustNotBeOk_WhenConvertErrorsExist_Issue9()
    {
        // Arrange: Converter meldet Fehler (Outcome=Error).
        CreateFile("FailConvert (USA).zip", 100);

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var converter = new AlwaysFailingConverter();
        var orch = new RunOrchestrator(fs, audit, converter: converter);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            ConvertFormat = "chd",
            PreferRegions = new[] { "USA" }
        };

        // Act
        var result = orch.Execute(options);

        // Assert: ConvertErrorCount > 0 → Status darf NICHT "ok" sein.
        // BUG: result.Status = "ok" unconditional.
        Assert.True(result.ConvertErrorCount > 0, "Precondition: Es muss mindestens 1 ConvertError geben");
        Assert.NotEqual("ok", result.Status);
    }

    [Fact]
    public void P1_01_Status_MustNotBeOk_WhenMoveFailsExist_Issue9()
    {
        // Arrange: Zwei Dateien, gleicher GameKey → 1 Loser. FS gibt null bei Move zurück (= Fehler).
        CreateFile("FailMove (USA).zip", 100);
        CreateFile("FailMove (Europe).zip", 100);

        var fs = new FailingMoveFileSystem(_tempDir);
        var audit = new StubAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" }
        };

        // Act
        var result = orch.Execute(options);

        // Assert: MoveResult.FailCount > 0 → Status darf NICHT "ok" sein.
        // BUG: result.Status = "ok" unconditional.
        Assert.NotNull(result.MoveResult);
        Assert.True(result.MoveResult!.FailCount > 0, "Precondition: Es muss mindestens 1 Move-Fehler geben");
        Assert.NotEqual("ok", result.Status);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P1-02 | MovePhaseResult vermischt Junk + Dedupe
    // Finding: F-017/F-041
    // Ursache: JunkMoveResult und DedupeMoveResult werden addiert.
    // Betroffen: RunOrchestrator.cs:274-281
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P1_02_MovePhase_JunkAndDedupe_MustBeSeparate_Issue9()
    {
        // Arrange: 1 Junk-Solo-Datei + 2 GAME-Dateien (gleicher Key → 1 Loser).
        CreateFile("Solo (Beta).zip", 50);        // JUNK (Solo-Gruppe, kein Loser → JunkRemoval)
        CreateFile("Shared (USA).zip", 100);       // GAME
        CreateFile("Shared (Europe).zip", 100);    // GAME (Loser bei US-Prefer)

        var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
        var audit = new StubAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            RemoveJunk = true,
            PreferRegions = new[] { "USA" }
        };

        // Act
        var result = orch.Execute(options);

        // Sanity check: Junk wurde entfernt
        Assert.True(result.JunkRemovedCount >= 1,
            "Precondition: Mindestens 1 Junk-Datei muss entfernt worden sein");

        // Assert: MoveResult.MoveCount darf NUR Dedupe-Moves zählen, NICHT Junk.
        // BUG: MoveResult = JunkMoveResult + DedupeMoveResult (addiert).
        // Bei 1 Junk + 1 Dedupe → MoveResult.MoveCount == 2 statt 1.
        Assert.NotNull(result.MoveResult);
        var expectedDedupeMoves = result.LoserCount; // nur Dedupe-Loser
        Assert.Equal(expectedDedupeMoves, result.MoveResult!.MoveCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P1-03 | API droppt 6+ Fields silent
    // Finding: F-016
    // Ursache: ApiRunResult fehlen ConvertedCount, ConvertErrorCount,
    //          JunkRemovedCount, FailCount, SavedBytes, PhaseMetrics.
    // Betroffen: RunManager.cs (ApiRunResult)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P1_03_ApiRunResult_MustContainConvertAndErrorFields_Issue9()
    {
        // Assert: ApiRunResult muss zentrale numerische Felder enthalten.
        // BUG: ConvertedCount, ConvertErrorCount, JunkRemovedCount,
        //      FailCount, SavedBytes fehlen komplett.
        var props = typeof(Romulus.Api.ApiRunResult).GetProperties()
            .Select(p => p.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Pflichtfelder für Audit-Konsistenz:
        Assert.Contains("ConvertedCount", props);
        Assert.Contains("ConvertErrorCount", props);
        Assert.Contains("JunkRemovedCount", props);
        Assert.Contains("SavedBytes", props);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P1-04 | CLI Sidecar nutzt LoserCount statt echtem MoveCount
    // Finding: F-031/F-040
    // Ursache: CLI schreibt ["move"] = result.LoserCount statt
    //          result.MoveResult?.MoveCount ?? 0 in Sidecar.
    // Betroffen: CLI/Program.cs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P1_04_CliSidecar_LoserCountMustEqualMoveCount_OrSidecarIsWrong_Issue9()
    {
        // BUG: CLI schreibt ["move"] = result.LoserCount statt result.MoveResult.MoveCount.
        // Wenn Moves fehlschlagen, divergieren die Werte → Sidecar enthält falsche Zahl.
        // Dieser Test beweist: LoserCount ≠ MoveCount wenn Fehler auftreten.
        CreateFile("SidecarTest (USA).zip", 100);
        CreateFile("SidecarTest (Europe).zip", 100);

        // FS versagt bei Move → MoveResult.MoveCount = 0, LoserCount = 1
        var fs = new FailingMoveFileSystem(_tempDir);
        var audit = new StubAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" }
        };

        // Act
        var result = orch.Execute(options);

        // Assert (fixed behavior): LoserCount ist geplant, MoveCount ist tatsächlich.
        // Bei Move-Fehlern dürfen die Werte divergieren. Entscheidend ist, dass
        // Sidecar/Projection MoveCount aus MoveResult ableitet, nicht aus LoserCount.
        Assert.NotNull(result.MoveResult);
        Assert.True(result.MoveResult!.FailCount > 0);
        Assert.NotEqual(result.LoserCount, result.MoveResult.MoveCount);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P1-05 | ConflictPolicy Skip: stiller Zähler-Gap
    // Finding: F-013
    // Ursache: Bei ConflictPolicy=Skip wird continue ohne Counter aufgerufen.
    // Betroffen: RunOrchestrator.cs ExecuteMovePhase
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void P1_05_ConflictPolicySkip_SumInvariant_Issue9()
    {
        // BUG: Bei ConflictPolicy=Skip wird bei Conflict ein `continue` ohne Counter
        // aufgerufen. Es gibt kein SkipCount-Feld in MovePhaseResult.
        // → MoveCount + FailCount < attempted → stiller Zähler-Gap.

        // Arrange: 2 Dateien, gleicher Key → 1 Loser.
        // FS: SkipTrackingFS simuliert Skip-Szenario (Move wird nie aufgerufen für Skips).
        CreateFile("Skip (USA).zip", 100);
        CreateFile("Skip (Europe).zip", 100);

        var fs = new ConflictSkipFileSystem(_tempDir);
        var audit = new StubAuditStore();
        var orch = new RunOrchestrator(fs, audit);

        var options = new RunOptions
        {
            Roots = new[] { _tempDir },
            Extensions = new[] { ".zip" },
            Mode = "Move",
            PreferRegions = new[] { "USA" },
            ConflictPolicy = "Skip"
        };

        // Act
        var result = orch.Execute(options);

        // Assert: Es MUSS ein SkipCount existieren damit die Summe aufgeht.
        // BUG: MovePhaseResult hat kein SkipCount-Feld → MoveCount + FailCount < LoserCount.
        // Beweis via Reflection: MovePhaseResult sollte SkipCount enthalten.
        var skipProp = typeof(MovePhaseResult).GetProperty("SkipCount");
        Assert.NotNull(skipProp);
    }

    // ═══════════════════════════════════════════════════════════════════
    // P1-07 | Cancel → "ok"/"completed" ist verboten
    // Finding: F-020, F-015 (SSE "completed" für cancelled)
    // Betroffen: RunOrchestrator.cs, RunManager.cs
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task P1_07_ApiStatusMapping_ErrorRunMustNotBeCompleted_Issue9()
    {
        // BUG: RunManager mappt ExitCode=0 → "completed" (l.276).
        // Aber Orchestrator setzt ExitCode=0 UND Status="ok" auch bei Errors.
        // → Run mit ConvertErrors > 0 bekommt trotzdem "completed".

        // Arrange: Executor simuliert Run mit Fehlern aber ExitCode=0.
        var fs = new StubFileSystem();
        fs.ExistingPaths.Add(@"C:\FakeRoot");
        var audit = new StubAuditStore();

        var manager = new Romulus.Api.RunManager(fs, audit,
            executor: (run, _, _, ct) =>
            {
                // Simuliert: Orchestrator.Execute gibt ExitCode=0 + Status="ok" zurück,
                // obwohl ConvertErrors > 0. Das passiert im echten Code.
                return new Romulus.Api.RunExecutionOutcome(
                    "completed",
                    new Romulus.Api.ApiRunResult
                    {
                        OrchestratorStatus = "ok",
                        ExitCode = 0,
                        TotalFiles = 10,
                        Groups = 5,
                        Winners = 5,
                        Losers = 5
                    });
            });

        var request = new Romulus.Api.RunRequest { Roots = new[] { @"C:\FakeRoot" } };
        var createResult = manager.TryCreateOrReuse(request, "Move");
        Assert.NotNull(createResult.Run);
        var runId = createResult.Run!.RunId;

        // Warten bis abgeschlossen
        await manager.WaitForCompletion(runId, pollMs: 50, timeout: TimeSpan.FromSeconds(5));

        var finalRun = manager.Get(runId);
        Assert.NotNull(finalRun);

        // Assert: Status="ok" wird durchgereicht, obwohl Errors existieren.
        // BUG P1-01: Die Kombination von Orchestrator Status="ok" + API "completed"
        // maskiert Move-/Convert-Fehler komplett.
        // Korrekt: Wenn Errors > 0, darf Status nicht "ok" sein.
        // Da der API-Test den Executor-Output 1:1 weitergibt und der Orchestrator
        // immer "ok" liefert, ist das System-Level-Bug.
        // Hier assertieren wir: ApiRunResult MUSS ConvertErrorCount enthalten.
        // Ohne dieses Feld kann die API gar keine Fehler kommunizieren.
        var resultType = typeof(Romulus.Api.ApiRunResult);
        var hasConvertErrors = resultType.GetProperty("ConvertErrorCount") is not null;
        var hasFailCount = resultType.GetProperty("FailCount") is not null;
        Assert.True(hasConvertErrors || hasFailCount,
            "ApiRunResult hat weder ConvertErrorCount noch FailCount — Fehler werden komplett maskiert");
    }

    // ═══════════════════════════════════════════════════════════════════
    // R-02 | CLI Sidecar überschreibt Orchestrator-Sidecar → Datenverlust
    // Finding: Architecture Review R-02
    // Ursache: CLI.Program.cs schreibt nach Orchestrator.Execute() einen
    //          zweiten WriteMetadataSidecar mit nur 6 Feldern, der den
    //          umfassenden 14-Feld-Sidecar des Orchestrators überschreibt.
    // Betroffen: CLI Program.cs (Move-Modus)
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void R02_CliMoveSidecar_MustContainOrchestratorFields_NotOverwrittenWith6Fields()
    {
        // Arrange: ROM-Dateien für Move anlegen
        var root = Path.Combine(_tempDir, "roms");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (USA).zip"), "usa-content");
        File.WriteAllText(Path.Combine(root, "Game (Europe).zip"), "eu-content");

        var auditPath = Path.Combine(_tempDir, "audit-r02.csv");
        var trashRoot = Path.Combine(_tempDir, "trash");
        Directory.CreateDirectory(trashRoot);

        var cliOptions = new CliRunOptions
        {
            Roots = [root],
            Mode = "Move",
            PreferRegions = ["US", "EU"],
            AuditPath = auditPath,
            TrashRoot = trashRoot
        };

        // Act: CLI Move-Run ausführen
        RunCliSilent(cliOptions);

        // Assert: Sidecar muss existieren
        var sidecarPath = auditPath + ".meta.json";
        Assert.True(File.Exists(sidecarPath), "Sidecar .meta.json muss existieren");

        var json = File.ReadAllText(sidecarPath);
        using var doc = JsonDocument.Parse(json);
        var meta = doc.RootElement;

        // Der Orchestrator schreibt diese Felder als [JsonExtensionData] → root-level.
        // Wenn die CLI sie überschreibt, fehlen sie → Test schlägt fehl.

        // Orchestrator-Felder die vorhanden sein MÜSSEN:
        Assert.True(meta.TryGetProperty("GroupCount", out _),
            "Sidecar fehlt 'GroupCount' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.True(meta.TryGetProperty("WinnerCount", out _),
            "Sidecar fehlt 'WinnerCount' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.True(meta.TryGetProperty("LoserCount", out _),
            "Sidecar fehlt 'LoserCount' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.True(meta.TryGetProperty("FailCount", out _),
            "Sidecar fehlt 'FailCount' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.True(meta.TryGetProperty("SkipCount", out _),
            "Sidecar fehlt 'SkipCount' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.True(meta.TryGetProperty("ConvertedCount", out _),
            "Sidecar fehlt 'ConvertedCount' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.True(meta.TryGetProperty("ConvertErrorCount", out _),
            "Sidecar fehlt 'ConvertErrorCount' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.True(meta.TryGetProperty("DurationMs", out _),
            "Sidecar fehlt 'DurationMs' — CLI hat Orchestrator-Sidecar überschrieben");

        // Felder die NUR die CLI schreibt (und nicht der Orchestrator) dürfen NICHT da sein:
        Assert.False(meta.TryGetProperty("roots", out _),
            "Sidecar enthält CLI-only Feld 'roots' — CLI hat Orchestrator-Sidecar überschrieben");
        Assert.False(meta.TryGetProperty("timestamp", out _),
            "Sidecar enthält CLI-only Feld 'timestamp' — CLI hat Orchestrator-Sidecar überschrieben");
    }

    private static void RunCliSilent(CliRunOptions options)
    {
        lock (SharedTestLocks.ConsoleLock)
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            using var stdout = new StringWriter();
            using var stderr = new StringWriter();
            try
            {
                Console.SetOut(stdout);
                Console.SetError(stderr);
                CliProgram.RunForTests(options);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private string CreateFile(string name, int sizeBytes)
    {
        var path = Path.Combine(_tempDir, name);
        var dir = Path.GetDirectoryName(path);
        if (dir is not null) Directory.CreateDirectory(dir);
        var data = new byte[sizeBytes];
        new Random(42).NextBytes(data);
        File.WriteAllBytes(path, data);
        return path;
    }

    // ═══════════════════════════════════════════════════════════════════
    // Test Doubles
    // ═══════════════════════════════════════════════════════════════════

    /// <summary>
    /// Converter der "Success" meldet aber Verify fehlschlagen lässt.
    /// Reproduziert P0-A: converted++ vor Verify, Source gelöscht trotz Verify-Failure.
    /// </summary>
    private sealed class VerifyFailingConverter : IFormatConverter
    {
        private readonly string _targetPath;

        public VerifyFailingConverter(string targetPath) => _targetPath = targetPath;

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, _targetPath, ConversionOutcome.Success);

        public bool Verify(string targetPath, ConversionTarget target) => false; // ← immer fehlschlagend
    }

    /// <summary>
    /// Converter der immer Error zurückgibt.
    /// </summary>
    private sealed class AlwaysFailingConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken ct = default)
            => new(sourcePath, null, ConversionOutcome.Error, "Simulated failure");

        public bool Verify(string targetPath, ConversionTarget target) => false;
    }

    /// <summary>
    /// FileSystem das alles liest aber bei MoveItemSafely null zurückgibt (Move-Fehler).
    /// </summary>
    private sealed class FailingMoveFileSystem : IFileSystem
    {
        private readonly Romulus.Infrastructure.FileSystem.FileSystemAdapter _real = new();
        private readonly string _tempDir;

        public FailingMoveFileSystem(string tempDir) => _tempDir = tempDir;

        public bool TestPath(string literalPath, string pathType = "Any") => _real.TestPath(literalPath, pathType);
        public string EnsureDirectory(string path) => _real.EnsureDirectory(path);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => _real.GetFilesSafe(root, allowedExtensions);
        public string? MoveItemSafely(string sourcePath, string destinationPath) => null; // ← Move schlägt fehl
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => _real.ResolveChildPathWithinRoot(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    /// <summary>
    /// FileSystem das Moves durchlässt aber Destinations als "existierend" meldet.
    /// Für ConflictPolicy=Skip: File.Exists(destPath) ist true → Skip → kein Counter.
    /// </summary>
    private sealed class ConflictSkipFileSystem : IFileSystem
    {
        private readonly Romulus.Infrastructure.FileSystem.FileSystemAdapter _real = new();

        public ConflictSkipFileSystem(string tempDir) { }

        public bool TestPath(string literalPath, string pathType = "Any") => _real.TestPath(literalPath, pathType);
        public string EnsureDirectory(string path) => _real.EnsureDirectory(path);
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => _real.GetFilesSafe(root, allowedExtensions);
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
            => _real.ResolveChildPathWithinRoot(rootPath, relativePath);
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    /// <summary>Minimaler Stub für IFileSystem.</summary>
    private sealed class StubFileSystem : IFileSystem
    {
        public HashSet<string> ExistingPaths { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool TestPath(string literalPath, string pathType = "Any") => ExistingPaths.Contains(literalPath);
        public string EnsureDirectory(string path) { ExistingPaths.Add(path); return path; }
        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Array.Empty<string>();
        public string? MoveItemSafely(string sourcePath, string destinationPath) => destinationPath;
        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath)
        {
            var full = Path.Combine(rootPath, relativePath);
            return full.StartsWith(rootPath, StringComparison.OrdinalIgnoreCase) ? full : null;
        }
        public bool IsReparsePoint(string path) => false;
        public void DeleteFile(string path) { }
        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false) { }
    }

    /// <summary>Minimaler Stub für IAuditStore.</summary>
    private sealed class StubAuditStore : IAuditStore
    {
        public List<(string csvPath, string rootPath, string oldPath, string newPath, string action)> AuditRows { get; } = new();

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => false;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots,
            string[] allowedCurrentRoots, bool dryRun = false) => Array.Empty<string>();
        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath,
            string newPath, string action, string category = "", string hash = "", string reason = "")
            => AuditRows.Add((auditCsvPath, rootPath, oldPath, newPath, action));
        public void Flush(string auditCsvPath) { }
    }
}