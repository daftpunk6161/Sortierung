using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests.Smokes;

/// <summary>
/// T-W3-RUN-SMOKE-SYNTHETIC (Option-G-Re-Skopierung 2026-04-29).
///
/// Drei reproduzierbare End-to-End-Smoke-Tests, die die in
/// docs/plan/strategic-reduction-2026/beta-smoke-protocol.md definierten
/// 8 Workflow-Schritte (Add Library, Scan, Verify, Plan, Confirm, Execute,
/// Report, Rollback) ohne Real-User-Cohort decken.
///
/// Pass/Fail ist deterministisch, weil:
///   - Eingaben werden pro Test in einem isolierten Temp-Verzeichnis erzeugt
///   - Region-Praeferenz erzwingt einen eindeutigen Winner
///   - Audit/Report werden in temp geschrieben und assertet
///   - Konverter und DAT-Index sind Test-Doubles
///
/// Trait("Category", "Smoke") erlaubt selektive Ausfuehrung via
/// <c>--filter "Category=Smoke"</c> in CI/Release-Pipelines.
/// </summary>
[Trait("Category", "Smoke")]
public sealed class SyntheticSmokeTests : IDisposable
{
    private readonly string _root;
    private readonly string _trash;
    private readonly string _audit;
    private readonly string _report;
    private readonly string _signingKey;

    public SyntheticSmokeTests()
    {
        var baseDir = Path.Combine(Path.GetTempPath(), "RomulusSmoke_" + Guid.NewGuid().ToString("N")[..8]);
        _root = Path.Combine(baseDir, "lib");
        _trash = Path.Combine(baseDir, "trash");
        Directory.CreateDirectory(_root);
        Directory.CreateDirectory(_trash);
        _audit = Path.Combine(baseDir, "audit", "audit.csv");
        _report = Path.Combine(baseDir, "report", "run.html");
        _signingKey = Path.Combine(baseDir, "audit", "signing.key");
        Directory.CreateDirectory(Path.GetDirectoryName(_audit)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_report)!);
    }

    public void Dispose()
    {
        try
        {
            var baseDir = Directory.GetParent(_root)?.FullName;
            if (baseDir is not null && Directory.Exists(baseDir))
                Directory.Delete(baseDir, recursive: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Best-effort cleanup; don't fail the smoke on transient file locks.
        }
    }

    // ─────────────────────────────────────────────────────────────────
    // S-1: Top-Workflow (Scan -> Plan -> Move -> Report -> Rollback)
    // Deckt: Add Library, Scan, Plan, Confirm (Mode=Move), Execute,
    //        Report, Rollback. (Verify ist via Mode=DryRun separat.)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Smoke_S1_TopWorkflow_DryRunPlansMoveExecutesAndRollbackRestores()
    {
        // Arrange: 3 ROMs, gleicher GameKey, USA gewinnt (PreferRegions),
        //          Europe + Japan sind Loser.
        var winner = CreateRom("Mario (USA).zip", 100);
        var loserEurope = CreateRom("Mario (Europe).zip", 100);
        var loserJapan = CreateRom("Mario (Japan).zip", 100);

        var fs = new FileSystemAdapter();
        var auditStore = new AuditCsvStore(fs, keyFilePath: _signingKey);

        var dryRunOptions = BuildOptions(mode: RunConstants.ModeDryRun);
        var dryRunOrch = new RunOrchestrator(fs, auditStore);

        // Act 1: Plan (DryRun) — keine Datei-Mutation
        var plan = dryRunOrch.Execute(dryRunOptions);

        // Assert 1: Plan klassifiziert korrekt, Files unverändert.
        Assert.Equal("ok", plan.Status);
        Assert.Equal(3, plan.TotalFilesScanned);
        Assert.Equal(1, plan.GroupCount);
        Assert.Equal(1, plan.WinnerCount);
        Assert.Equal(2, plan.LoserCount);
        Assert.Null(plan.MoveResult);
        Assert.True(File.Exists(winner));
        Assert.True(File.Exists(loserEurope));
        Assert.True(File.Exists(loserJapan));
        Assert.True(File.Exists(plan.ReportPath!),
            "DryRun must produce a report file so GUI/CLI/API can preview the plan.");

        // Act 2: Confirm + Execute (Mode=Move)
        var moveOptions = BuildOptions(mode: RunConstants.ModeMove);
        var moveOrch = new RunOrchestrator(fs, auditStore);
        var run = moveOrch.Execute(moveOptions);

        // Assert 2: Winner bleibt, beide Loser sind weg, Audit + Report da.
        Assert.Equal("ok", run.Status);
        Assert.Equal(0, run.ExitCode);
        Assert.NotNull(run.MoveResult);
        Assert.Equal(2, run.MoveResult!.MoveCount);
        Assert.Equal(0, run.MoveResult.FailCount);
        Assert.True(File.Exists(winner), "Winner must remain after Move.");
        Assert.False(File.Exists(loserEurope), "Loser (Europe) must be moved out of the library.");
        Assert.False(File.Exists(loserJapan), "Loser (Japan) must be moved out of the library.");
        Assert.True(File.Exists(_audit), "Audit CSV must exist after Move.");
        Assert.True(File.Exists(_audit + ".meta.json"),
            "Audit metadata sidecar must exist after Move (rollback prerequisite).");
        Assert.True(File.Exists(run.ReportPath!), "Move must produce a report file.");

        var auditLines = File.ReadAllLines(_audit);
        Assert.True(auditLines.Length >= 3,
            $"Audit CSV must contain header + 2 move rows; actual={auditLines.Length}");

        // Act 3: Rollback via AuditSigningService gegen das Audit-CSV.
        var signing = new AuditSigningService(fs, keyFilePath: _signingKey);
        var rollback = signing.Rollback(
            _audit,
            allowedRestoreRoots: new[] { _root },
            allowedCurrentRoots: new[] { _root, _trash },
            dryRun: false);

        // Assert 3: Beide Loser sind wieder am Originalpfad.
        // TotalRows zaehlt alle Audit-Eintraege (Sort + Move); RolledBack bezieht sich auf
        // wiederhergestellte Move-Loser.
        Assert.True(rollback.TotalRows >= 2,
            $"Audit must contain at least the 2 loser-move rows; actual TotalRows={rollback.TotalRows}");
        Assert.True(rollback.RolledBack >= 2,
            $"Rollback must restore both losers; actual RolledBack={rollback.RolledBack}, Failed={rollback.Failed}");
        Assert.True(File.Exists(loserEurope), "Rollback must restore Europe loser to original path.");
        Assert.True(File.Exists(loserJapan), "Rollback must restore Japan loser to original path.");
    }

    // ─────────────────────────────────────────────────────────────────
    // S-2: ConvertOnly (Scan -> ConvertOnly -> Report)
    // Token-Aequivalent: ApproveConversionReview = true (entspricht dem
    // typed AcceptDataLoss-Token in der GUI-Schicht).
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Smoke_S2_ConvertOnly_RunsConverterAndReportsConversionSummary()
    {
        // Arrange: Zwei ROMs in unterschiedlichen Sets (kein Dedupe noetig).
        var rom1 = CreateRom("Sonic (USA).zip", 100);
        var rom2 = CreateRom("Tetris (Europe).zip", 100);

        var converter = new RecordingFakeConverter();
        var fs = new FileSystemAdapter();
        var auditStore = new AuditCsvStore(fs);

        var options = new RunOptions
        {
            Roots = new[] { _root },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd",
            ApproveConversionReview = true, // typed token analog
            PreferRegions = new[] { "USA" },
            TrashRoot = _trash,
            AuditPath = _audit,
            ReportPath = _report
        };

        var orch = new RunOrchestrator(fs, auditStore, converter: converter);

        // Act
        var result = orch.Execute(options);

        // Assert: Konverter wurde fuer beide Files aufgerufen, Report enthaelt
        //         eine ConversionReport-Sektion.
        Assert.Equal("ok", result.Status);
        Assert.Equal(2, converter.ConvertedPaths.Count);
        Assert.Contains(converter.ConvertedPaths, p => p.EndsWith("Sonic (USA).zip", StringComparison.Ordinal));
        Assert.Contains(converter.ConvertedPaths, p => p.EndsWith("Tetris (Europe).zip", StringComparison.Ordinal));
        Assert.True(result.ConvertedCount >= 2,
            $"ConvertOnly must mark both files as converted; actual={result.ConvertedCount}");
        Assert.NotNull(result.ConversionReport);
        Assert.True(result.ConversionReport!.Converted >= 2,
            "ConversionReport must reflect successful conversions for parity with GUI/CLI/API report flows.");
        Assert.True(File.Exists(result.ReportPath!), "ConvertOnly must produce a report file.");

        var reportContent = File.ReadAllText(result.ReportPath!);
        Assert.Contains("Sonic", reportContent);
        Assert.Contains("Tetris", reportContent);
    }

    // ─────────────────────────────────────────────────────────────────
    // S-3: DAT-Audit (Scan -> Verify gegen DatIndex -> Report)
    // ─────────────────────────────────────────────────────────────────
    [Fact]
    public void Smoke_S3_DatAudit_ProducesEntriesAndCountsHaveAndUnknown()
    {
        // Arrange: Zwei Files, beide werden gescannt; DatIndex enthaelt mind.
        // einen Eintrag, sodass DatAuditResult >= 1 Entry liefert.
        CreateRom("Mario (USA).zip", 100);
        CreateRom("Unknown Game.zip", 100);

        var datIndex = new DatIndex();
        datIndex.Add("UNKNOWN", "deadbeef00000000000000000000000000000000", "Mario");

        var fs = new FileSystemAdapter();
        var auditStore = new AuditCsvStore(fs);

        var options = new RunOptions
        {
            Roots = new[] { _root },
            Extensions = new[] { ".zip" },
            Mode = RunConstants.ModeDryRun,
            EnableDat = true,
            EnableDatAudit = true,
            HashType = "SHA1",
            PreferRegions = new[] { "USA" },
            TrashRoot = _trash,
            AuditPath = _audit,
            ReportPath = _report
        };

        var orch = new RunOrchestrator(fs, auditStore, datIndex: datIndex);

        // Act
        var result = orch.Execute(options);

        // Assert: DatAuditResult ist verdrahtet, enthaelt Eintraege fuer alle
        //         gescannten Files und der Report wurde geschrieben.
        Assert.Equal("ok", result.Status);
        Assert.NotNull(result.DatAuditResult);
        Assert.True(result.DatAuditResult!.Entries.Count >= 2,
            $"DAT-Audit must produce one entry per scanned file; actual={result.DatAuditResult.Entries.Count}");
        var totalAudited =
            result.DatAuditResult.HaveCount +
            result.DatAuditResult.HaveWrongNameCount +
            result.DatAuditResult.MissCount +
            result.DatAuditResult.UnknownCount +
            result.DatAuditResult.AmbiguousCount;
        Assert.Equal(result.DatAuditResult.Entries.Count, totalAudited);
        Assert.True(File.Exists(result.ReportPath!), "DAT-Audit run must produce a report file.");
    }

    // ─────────────────────────────────────────────────────────────────
    // Helpers
    // ─────────────────────────────────────────────────────────────────
    private string CreateRom(string fileName, int sizeBytes)
    {
        var path = Path.Combine(_root, fileName);
        File.WriteAllBytes(path, new byte[sizeBytes]);
        return path;
    }

    private RunOptions BuildOptions(string mode) => new()
    {
        Roots = new[] { _root },
        Extensions = new[] { ".zip" },
        Mode = mode,
        PreferRegions = new[] { "US" }, // canonical region code; "USA" is normalized to "US" by RegionDetector
        TrashRoot = _trash,
        AuditPath = _audit,
        ReportPath = _report
    };

    /// <summary>
    /// Test-only fake converter that records invocations and writes a small
    /// target file so post-conversion code paths (e.g. report rendering)
    /// observe a real on-disk artifact.
    /// </summary>
    private sealed class RecordingFakeConverter : IFormatConverter
    {
        public List<string> ConvertedPaths { get; } = new();

        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => sourceExtension == ".zip" ? new ConversionTarget(".chd", "chdman", "createcd") : null;

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
        {
            ConvertedPaths.Add(sourcePath);
            var targetPath = sourcePath + target.Extension;
            File.WriteAllText(targetPath, "smoke-converted");
            return new ConversionResult(sourcePath, targetPath, ConversionOutcome.Success)
            {
                SourceBytes = new FileInfo(sourcePath).Length,
                TargetBytes = new FileInfo(targetPath).Length,
                VerificationResult = VerificationStatus.Verified,
                SourceIntegrity = SourceIntegrity.Lossless,
                Safety = ConversionSafety.Safe
            };
        }

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }
}
