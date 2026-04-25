using System.IO.Compression;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Behavioral end-to-end tests retained from the historical Audit A/B set.
/// Source-mirror assertions (A01/B01/B04/B05) were removed in Block A
/// of test-suite-remediation-plan-2026-04-25.md and are reintroduced
/// as proper behavioural coverage in Blocks B and C.
/// </summary>
public sealed class AuditABEndToEndRedTests : IDisposable
{
    private readonly string _tempDir;

    public AuditABEndToEndRedTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "AuditAB_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void A03_ConvertOnlyRun_PopulatesGroupAndWinnerCounts()
    {
        var root = Path.Combine(_tempDir, "convert-only");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "Game (US).iso"), "iso-content");

        using var orchestrator = new RunOrchestrator(
            new FileSystemAdapter(),
            new NullAuditStore(),
            converter: new NoOpFormatConverter());

        var result = orchestrator.Execute(new RunOptions
        {
            Roots = [root],
            Extensions = [".iso"],
            Mode = RunConstants.ModeMove,
            ConvertOnly = true,
            ConvertFormat = "chd"
        });

        Assert.Equal("ok", result.Status);
        Assert.True(result.TotalFilesScanned > 0);
        Assert.True(result.GroupCount > 0, "ConvertOnly should not report 0 groups when candidates exist.");
        Assert.True(result.WinnerCount > 0, "ConvertOnly should not report 0 winners when candidates exist.");
    }

    [Fact]
    public void A05_RunProjection_WinnerFallback_EmitsTraceWarning()
    {
        var run = new RunResult
        {
            AllCandidates = Array.Empty<RomCandidate>(),
            DedupeGroups = Array.Empty<DedupeGroup>(),
            GroupCount = 3,
            WinnerCount = 3
        };

        var trace = CaptureTrace(() =>
        {
            var projection = RunProjectionFactory.Create(run);
            Assert.Equal(3, projection.Keep);
        });

        Assert.Contains("winner-count-fallback", trace, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A06_RunProjection_NegativeVerifyDelta_EmitsTraceWarning()
    {
        var run = new RunResult
        {
            ConvertVerifyFailedCount = 1,
            ConvertErrorCount = 3
        };

        var trace = CaptureTrace(() =>
        {
            var projection = RunProjectionFactory.Create(run);
            Assert.True(projection.FailCount >= 3);
        });

        Assert.Contains("negative-verify-delta", trace, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void B02_OutputValidator_OneByteOutput_IsRejectedAsTooSmall()
    {
        var path = Path.Combine(_tempDir, "tiny-output.chd");
        File.WriteAllBytes(path, [0x01]);

        var ok = ConversionOutputValidator.TryValidateCreatedOutput(path, out var reason);

        Assert.False(ok);
        Assert.Equal("output-too-small", reason);
    }

    [Fact]
    public void B03_ZipSorter_EncodedTraversalEntry_IsBlocked()
    {
        var zipPath = Path.Combine(_tempDir, "encoded-traversal.zip");
        using (var stream = new FileStream(zipPath, FileMode.Create))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            archive.CreateEntry("safe/game.cue");
            archive.CreateEntry("%2e%2e/%2e%2e/evil.bin");
        }

        var exts = ZipSorter.GetZipEntryExtensions(zipPath);

        Assert.Contains(".cue", exts);
        Assert.DoesNotContain(".bin", exts);
    }

    private static string CaptureTrace(Action action)
    {
        using var writer = new StringWriter();
        using var listener = new System.Diagnostics.TextWriterTraceListener(writer);
        var listeners = System.Diagnostics.Trace.Listeners;
        listeners.Add(listener);
        var previousAutoFlush = System.Diagnostics.Trace.AutoFlush;
        System.Diagnostics.Trace.AutoFlush = true;

        try
        {
            action();
            listener.Flush();
            return writer.ToString();
        }
        finally
        {
            System.Diagnostics.Trace.AutoFlush = previousAutoFlush;
            listeners.Remove(listener);
        }
    }

    private sealed class NullAuditStore : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
        {
        }

        public bool TestMetadataSidecar(string auditCsvPath) => true;

        public void Flush(string auditCsvPath)
        {
        }

        public IReadOnlyList<string> Rollback(string auditCsvPath, string[] allowedRestoreRoots, string[] allowedCurrentRoots, bool dryRun = false)
            => Array.Empty<string>();

        public void AppendAuditRow(string auditCsvPath, string rootPath, string oldPath, string newPath, string action, string category = "", string hash = "", string reason = "")
        {
        }
    }

    private sealed class NoOpFormatConverter : IFormatConverter
    {
        public ConversionTarget? GetTargetFormat(string consoleKey, string sourceExtension)
            => new(sourceExtension, ".chd", "noop");

        public ConversionResult Convert(string sourcePath, ConversionTarget target, CancellationToken cancellationToken = default)
            => new(sourcePath, sourcePath, ConversionOutcome.Skipped, "noop");

        public bool Verify(string targetPath, ConversionTarget target) => true;
    }
}
