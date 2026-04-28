using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Logging;
using Romulus.Infrastructure.Reporting;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave4RemediationRegressionTests : IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Wave4_Remediation_" + Guid.NewGuid().ToString("N"));

    public Wave4RemediationRegressionTests()
    {
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // Best effort cleanup.
        }
    }

    [Fact]
    public void ReportGenerator_Html_IsDeterministicSchemaVersionedAndHasNoInlineStyleAttributes()
    {
        var summary = new ReportSummary
        {
            Timestamp = new DateTime(2026, 04, 24, 12, 0, 0, DateTimeKind.Utc),
            TotalFiles = 2,
            Candidates = 2,
            KeepCount = 1,
            MoveCount = 1,
            HealthScore = 90
        };
        var entries = new[]
        {
            new ReportEntry { GameKey = "game", Action = "KEEP", Category = "GAME", FileName = "game.zip", FilePath = @"C:\roms\game.zip" }
        };

        var first = ReportGenerator.GenerateHtml(summary, entries);
        var second = ReportGenerator.GenerateHtml(summary, entries);

        Assert.Equal(first, second);
        Assert.Contains("romulus-schema-version", first, StringComparison.Ordinal);
        Assert.Contains(RunConstants.ReportSchemaVersion, first, StringComparison.Ordinal);
        Assert.Contains("base-uri 'none'", first, StringComparison.Ordinal);
        Assert.DoesNotContain("style=\"", first, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("�", first, StringComparison.Ordinal);
    }

    [Fact]
    public void ReportGenerator_JsonAndCsv_ExposeSchemaVersion()
    {
        var summary = new ReportSummary { Timestamp = new DateTime(2026, 04, 24, 12, 0, 0, DateTimeKind.Utc) };
        var entries = new[] { new ReportEntry { GameKey = "game", FileName = "game.zip" } };

        using var json = JsonDocument.Parse(ReportGenerator.GenerateJson(summary, entries));
        Assert.Equal(RunConstants.ReportSchemaVersion, json.RootElement.GetProperty("schemaVersion").GetString());
        Assert.Equal(RunConstants.ReportSchemaVersion, json.RootElement.GetProperty("summary").GetProperty("schemaVersion").GetString());

        var csv = ReportGenerator.GenerateCsv(entries);
        Assert.Contains("SchemaVersion,GameKey", csv, StringComparison.Ordinal);
        Assert.Contains(RunConstants.ReportSchemaVersion, csv, StringComparison.Ordinal);
    }

    [Fact]
    public void RunReportWriter_WriteReport_AccountingMismatchWritesDiagnosticReport()
    {
        var reportPath = Path.Combine(_tempDir, "mismatch.json");
        var result = new RunResult
        {
            Status = RunConstants.StatusOk,
            TotalFilesScanned = 1,
            CompletedUtc = new DateTime(2026, 04, 24, 12, 0, 0, DateTimeKind.Utc),
            AllCandidates =
            [
                new RomCandidate { MainPath = @"C:\roms\a.zip", Category = FileCategory.Game, GameKey = "a" },
                new RomCandidate { MainPath = @"C:\roms\b.zip", Category = FileCategory.Game, GameKey = "b" }
            ]
        };

        var written = RunReportWriter.WriteReport(reportPath, result, RunConstants.ModeDryRun);

        Assert.Equal(Path.GetFullPath(reportPath), written, StringComparer.OrdinalIgnoreCase);
        Assert.True(File.Exists(reportPath));
        Assert.Contains(RunConstants.ReportSchemaVersion, File.ReadAllText(reportPath), StringComparison.Ordinal);
    }

    [Fact]
    public void JsonlLogWriter_BoundsLargeEntriesAndAddsSchemaVersion()
    {
        var logPath = Path.Combine(_tempDir, "bounded.jsonl");
        using (var writer = new JsonlLogWriter(logPath))
        {
            writer.Info("TEST", "large", new string('x', 20_000));
        }

        using var json = JsonDocument.Parse(File.ReadAllText(logPath));
        Assert.Equal("romulus-jsonl-log-v1", json.RootElement.GetProperty("schemaVersion").GetString());
        var message = json.RootElement.GetProperty("message").GetString();
        Assert.NotNull(message);
        Assert.True(message!.Length < 20_000);
        Assert.EndsWith("[truncated]", message, StringComparison.Ordinal);
    }

    [Fact]
    public void JsonlLogRotation_RepeatedRotationsUseDistinctArchiveNames()
    {
        var logPath = Path.Combine(_tempDir, "rotate.jsonl");

        File.WriteAllText(logPath, new string('a', 200));
        JsonlLogRotation.Rotate(logPath, maxBytes: 100);

        File.WriteAllText(logPath, new string('b', 200));
        JsonlLogRotation.Rotate(logPath, maxBytes: 100);

        var archives = Directory.GetFiles(_tempDir, "rotate-*.jsonl");
        Assert.Equal(2, archives.Length);
        Assert.Equal(2, archives.Select(Path.GetFileName).Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void ConsoleSorter_MissingEnrichmentAuditWarning_IsNotMoveLikeConsoleSortRow()
    {
        var romPath = Path.Combine(_tempDir, "Game.nes");
        File.WriteAllText(romPath, "content");
        var audit = new RecordingAuditStore();
        var sorter = new ConsoleSorter(new FileSystemAdapter(), BuildDetector(), audit, Path.Combine(_tempDir, "audit.csv"));

        var result = sorter.Sort([_tempDir], [".nes"], dryRun: true);

        Assert.Equal(1, result.Skipped);
        var row = Assert.Single(audit.Rows);
        Assert.Equal(RunConstants.AuditActions.ConsoleSortWarning, row.Action);
        Assert.Equal("WARNING", row.Category);
        Assert.Equal(string.Empty, row.OldPath);
        Assert.Equal(string.Empty, row.NewPath);
    }

    private static ConsoleDetector BuildDetector()
        => new(
        [
            new ConsoleInfo("NES", "Nintendo", false, [".nes"], [], ["NES"])
        ]);

    private sealed class RecordingAuditStore : IAuditStore
    {
        public List<(string OldPath, string NewPath, string Action, string Category)> Rows { get; } = [];

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
            => Rows.Add((oldPath, newPath, action, category));

        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata) { }
        public bool TestMetadataSidecar(string auditCsvPath) => true;
        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => [];
        public void Flush(string auditCsvPath) { }
    }
}
