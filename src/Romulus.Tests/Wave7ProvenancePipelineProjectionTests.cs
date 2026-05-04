using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Provenance;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Wave 7 — T-W7-PROVENANCE-TRAIL Step 2 (Pipeline-Projection).
///
/// Pin-Tests fuer <see cref="ProvenancePipelineProjection.ProjectEvents"/>.
/// Diese Projektion ist die Single-Source-of-Truth zwischen Run-Engine und
/// Provenance-Trail. RunOrchestrator ruft sie genau einmal nach
/// committetem Audit-Sidecar (ADR-0024 §5 Reihenfolge) auf, dann appendet
/// jeder produzierte Eintrag in den per-ROM Trail.
/// </summary>
public sealed class Wave7ProvenancePipelineProjectionTests : IDisposable
{
    private const string AuditRunId = "audit-20260430-100000";
    private const string Ts = "2026-04-30T10:00:00.0000000Z";

    private readonly List<string> _tempDirs = new();

    private string CreateTempDir(string prefix)
    {
        var path = Path.Combine(Path.GetTempPath(), prefix + "-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }

    private static RomCandidate Cand(string path, string hash, string console = "NES",
        bool dat = false, FileCategory cat = FileCategory.Game)
        => new()
        {
            MainPath = path,
            Hash = hash,
            ConsoleKey = console,
            GameKey = Path.GetFileNameWithoutExtension(path),
            Region = "USA",
            DatMatch = dat,
            DatGameName = dat ? "DAT.dat" : null,
            Category = cat,
            Extension = Path.GetExtension(path),
        };

    [Fact]
    public void ProjectEvents_Imported_OneEntryPerCandidateWithHash()
    {
        var rr = new RunResult
        {
            AllCandidates = new[]
            {
                Cand("C:/r/a.nes", "aa11"),
                Cand("C:/r/b.nes", "bb22"),
                Cand("C:/r/no-hash.nes", null!),
            }
        };
        var events = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        var imported = events.Where(e => e.EventKind == ProvenanceEventKind.Imported).ToList();
        Assert.Equal(2, imported.Count);
        Assert.Contains(imported, e => e.Fingerprint == "aa11");
        Assert.Contains(imported, e => e.Fingerprint == "bb22");
    }

    [Fact]
    public void ProjectEvents_Verified_OnlyForDatMatches()
    {
        var rr = new RunResult
        {
            AllCandidates = new[]
            {
                Cand("C:/r/a.nes", "aa11", dat: true),
                Cand("C:/r/b.nes", "bb22", dat: false),
            }
        };
        var events = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        var verified = events.Where(e => e.EventKind == ProvenanceEventKind.Verified).ToList();
        Assert.Single(verified);
        Assert.Equal("aa11", verified[0].Fingerprint);
        Assert.Equal("DAT.dat", verified[0].DatMatchId);
    }

    [Fact]
    public void ProjectEvents_Moved_OneEntryPerMovedSourcePath()
    {
        var rr = new RunResult
        {
            AllCandidates = new[]
            {
                Cand("C:/r/a.nes", "aa11"),
                Cand("C:/r/b.nes", "bb22"),
            },
            MoveResult = new MovePhaseResult(
                MoveCount: 1, FailCount: 0, SavedBytes: 0, SkipCount: 0,
                MovedSourcePaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:/r/a.nes" })
        };
        var events = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        var moved = events.Where(e => e.EventKind == ProvenanceEventKind.Moved).ToList();
        Assert.Single(moved);
        Assert.Equal("aa11", moved[0].Fingerprint);
    }

    [Fact]
    public void ProjectEvents_Converted_OneEntryPerSuccessfulConversion()
    {
        var rr = new RunResult
        {
            AllCandidates = new[] { Cand("C:/r/a.iso", "aa11") },
            ConversionReport = new ConversionReport
            {
                TotalPlanned = 2, Converted = 1, Skipped = 0, Errors = 1,
                Blocked = 0, RequiresReview = 0, TotalSavedBytes = 0,
                Results = new[]
                {
                    new ConversionResult("C:/r/a.iso", "C:/r/a.chd", ConversionOutcome.Success),
                    new ConversionResult("C:/r/missing.iso", null, ConversionOutcome.Error, "x"),
                }
            }
        };
        var events = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        var converted = events.Where(e => e.EventKind == ProvenanceEventKind.Converted).ToList();
        Assert.Single(converted);
        Assert.Equal("aa11", converted[0].Fingerprint);
        Assert.Contains("a.chd", converted[0].Detail ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectEvents_AllEventsCarryAuditRunId()
    {
        var rr = new RunResult
        {
            AllCandidates = new[] { Cand("C:/r/a.nes", "aa11", dat: true) },
            MoveResult = new MovePhaseResult(1, 0, 0, 0,
                MovedSourcePaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:/r/a.nes" })
        };
        var events = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        Assert.NotEmpty(events);
        Assert.All(events, e => Assert.Equal(AuditRunId, e.AuditRunId));
        Assert.All(events, e => Assert.Equal(Ts, e.TimestampUtc));
    }

    [Fact]
    public void ProjectEvents_DeterministicOrder_SameInputSameOutput()
    {
        var rr = new RunResult
        {
            AllCandidates = new[]
            {
                Cand("C:/r/a.nes", "aa11", dat: true),
                Cand("C:/r/b.nes", "bb22", dat: true),
            },
            MoveResult = new MovePhaseResult(2, 0, 0, 0,
                MovedSourcePaths: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "C:/r/a.nes", "C:/r/b.nes" })
        };
        var first = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        var second = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        Assert.Equal(first.Count, second.Count);
        for (var i = 0; i < first.Count; i++)
        {
            Assert.Equal(first[i].Fingerprint, second[i].Fingerprint);
            Assert.Equal(first[i].EventKind, second[i].EventKind);
        }
    }

    [Fact]
    public void ProjectEvents_NeverLeaksAbsolutePathsInDetail()
    {
        var rr = new RunResult
        {
            AllCandidates = new[] { Cand("C:/Users/me/r/a.iso", "aa11") },
            ConversionReport = new ConversionReport
            {
                TotalPlanned = 1, Converted = 1, Skipped = 0, Errors = 0,
                Blocked = 0, RequiresReview = 0, TotalSavedBytes = 0,
                Results = new[]
                {
                    new ConversionResult("C:/Users/me/r/a.iso", "C:/Users/me/r/a.chd", ConversionOutcome.Success),
                }
            }
        };
        var events = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        Assert.All(events, e =>
        {
            Assert.DoesNotContain("Users", e.Detail ?? "", StringComparison.Ordinal);
            Assert.DoesNotContain(":", e.Detail ?? "", StringComparison.Ordinal);
        });
    }

    [Fact]
    public void ProjectEvents_EmptyResult_NoEvents()
    {
        var rr = new RunResult();
        var events = ProvenancePipelineProjection.ProjectEvents(rr, AuditRunId, Ts);
        Assert.Empty(events);
    }

    [Fact]
    public void RollbackProjectEvents_EmitsRolledBack_ForExecutedRestoredPaths()
    {
        var dir = CreateTempDir("rom-w7-rollback-prov");
        var restoredPath = Path.Combine(dir, "a.nes");
        var trashPath = Path.Combine(dir, "trash", "a.nes");
        var auditPath = Path.Combine(dir, "audit-run.csv");
        File.WriteAllLines(auditPath,
        [
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"C:/r,{restoredPath},{trashPath},Move,Game,aa11,winner,2026-05-01T00:00:00Z"
        ]);

        var rollback = new AuditRollbackResult
        {
            AuditCsvPath = auditPath,
            DryRun = false,
            RolledBack = 1,
            RollbackAuditPath = Path.Combine(dir, "audit-run.rollback-audit.csv"),
            RestoredPaths = [restoredPath]
        };

        var events = ProvenanceRollbackAppender.ProjectEvents(rollback, "audit-run.rollback-audit", Ts);

        var ev = Assert.Single(events);
        Assert.Equal(ProvenanceEventKind.RolledBack, ev.EventKind);
        Assert.Equal("aa11", ev.Fingerprint);
        Assert.Equal("audit-run.rollback-audit", ev.AuditRunId);
        Assert.DoesNotContain(dir, ev.Detail ?? "", StringComparison.OrdinalIgnoreCase);
    }
}
