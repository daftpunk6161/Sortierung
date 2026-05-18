using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
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

    [Fact]
    public void RollbackProjectEvents_ProjectsOnlyMatchedRestoredPathsInReverseAuditOrder()
    {
        var dir = CreateTempDir("rom-w7-rollback-prov-filter");
        var moveRestored = Path.Combine(dir, "restored-move.nes");
        var moveTrash = Path.Combine(dir, "trash", "restored-move.nes");
        var copyCreated = Path.Combine(dir, "generated-copy.nes");
        var copySource = Path.Combine(dir, "source.nes");
        var ignoredRestored = Path.Combine(dir, "ignored.nes");
        var ignoredTrash = Path.Combine(dir, "trash", "ignored.nes");
        var auditPath = Path.Combine(dir, "audit-run.csv");
        File.WriteAllLines(auditPath,
        [
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"C:/r,{moveRestored},{moveTrash},MOVE,Game,aa11,winner,2026-05-01T00:00:00Z",
            $"C:/r,{copySource},{copyCreated},COPY,Game,bb22,copy,2026-05-01T00:01:00Z",
            $"C:/r,{ignoredRestored},{ignoredTrash},MOVE,Game,cc33,not-restored,2026-05-01T00:02:00Z",
            $"C:/r,{Path.Combine(dir, "bad.nes")},{Path.Combine(dir, "trash", "bad.nes")},MOVE,Game,not-hex,bad-hash,2026-05-01T00:03:00Z",
            "malformed,row"
        ]);

        var rollback = new AuditRollbackResult
        {
            AuditCsvPath = auditPath,
            RolledBack = 2,
            RestoredPaths = [moveRestored, copyCreated]
        };

        var events = ProvenanceRollbackAppender.ProjectEvents(rollback, "rollback-run", Ts);

        Assert.Equal(["bb22", "aa11"], events.Select(e => e.Fingerprint).ToArray());
        Assert.All(events, e =>
        {
            Assert.Equal(ProvenanceEventKind.RolledBack, e.EventKind);
            Assert.Equal("rollback-run", e.AuditRunId);
            Assert.DoesNotContain(dir, e.Detail ?? "", StringComparison.OrdinalIgnoreCase);
        });
        Assert.StartsWith("removed generated-copy.nes", events[0].Detail, StringComparison.Ordinal);
        Assert.Contains("restored-move.nes", events[1].Detail, StringComparison.Ordinal);
    }

    [Fact]
    public void TryAppendRolledBackEvents_AppendsProjectedEventsWithRollbackAuditRunId()
    {
        var dir = CreateTempDir("rom-w7-rollback-prov-append");
        var restoredPath = Path.Combine(dir, "a.nes");
        var trashPath = Path.Combine(dir, "trash", "a.nes");
        var auditPath = Path.Combine(dir, "audit-run.csv");
        var rollbackAuditPath = Path.Combine(dir, "audit-run.rollback-audit.csv");
        File.WriteAllLines(auditPath,
        [
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"C:/r,{restoredPath},{trashPath},MOVE,Game,aa11,winner,2026-05-01T00:00:00Z"
        ]);
        var store = new RecordingProvenanceStore();
        var rollback = new AuditRollbackResult
        {
            AuditCsvPath = auditPath,
            RollbackAuditPath = rollbackAuditPath,
            RolledBack = 1,
            RestoredPaths = [restoredPath]
        };

        var appended = ProvenanceRollbackAppender.TryAppendRolledBackEvents(store, rollback, Ts);

        Assert.Equal(1, appended);
        var entry = Assert.Single(store.Entries);
        Assert.Equal("audit-run.rollback-audit", entry.AuditRunId);
        Assert.Equal("aa11", entry.Fingerprint);
    }

    [Fact]
    public void TryAppendRolledBackEvents_DoesNotLetProvenanceFailureBreakRollback()
    {
        var dir = CreateTempDir("rom-w7-rollback-prov-store-fail");
        var restoredPath = Path.Combine(dir, "a.nes");
        var trashPath = Path.Combine(dir, "trash", "a.nes");
        var auditPath = Path.Combine(dir, "audit-run.csv");
        File.WriteAllLines(auditPath,
        [
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"C:/r,{restoredPath},{trashPath},MOVE,Game,aa11,winner,2026-05-01T00:00:00Z"
        ]);
        var logs = new List<string>();
        var rollback = new AuditRollbackResult
        {
            AuditCsvPath = auditPath,
            RolledBack = 1,
            RestoredPaths = [restoredPath]
        };

        var appended = ProvenanceRollbackAppender.TryAppendRolledBackEvents(
            new RecordingProvenanceStore(throwOnAppend: true),
            rollback,
            Ts,
            logs.Add);

        Assert.Equal(0, appended);
        Assert.Contains(logs, message => message.Contains("Rollback trail append skipped", StringComparison.Ordinal));
    }

    private sealed class RecordingProvenanceStore(bool throwOnAppend = false) : IProvenanceStore
    {
        public List<ProvenanceEntry> Entries { get; } = new();

        public void Append(ProvenanceEntry entry)
        {
            if (throwOnAppend)
                throw new IOException("append failed");

            Entries.Add(entry);
        }

        public IReadOnlyList<ProvenanceEntry> Read(string fingerprint)
            => Entries.Where(e => e.Fingerprint == fingerprint).ToArray();

        public ProvenanceVerifyReport Verify(string fingerprint)
            => ProvenanceVerifyReport.Ok(Read(fingerprint));
    }
}
