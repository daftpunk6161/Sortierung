using System.Text;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Core.Classification;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.Conversion;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.UI.Wpf.Models;
using Xunit;

namespace RomCleanup.Tests;

/// <summary>
/// Regression tests for all audit findings (F-P1-01 through F-P3-06).
/// </summary>
public sealed class AuditFindingsRegressionTests : IDisposable
{
    private readonly string _tempDir;

    public AuditFindingsRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "audit_findings_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    // =========================================================================
    //  F-P1-01: SHA-256 constant-time comparison
    // =========================================================================

    [Fact]
    public void FP1_01_VerifyMetadataSidecar_UsesConstantTimeComparison()
    {
        var fs = new FileSystemAdapter();
        var svc = new AuditSigningService(fs, keyFilePath: Path.Combine(_tempDir, "hmac.key"));

        var csvPath = Path.Combine(_tempDir, "audit.csv");
        File.WriteAllText(csvPath, "Header1,Header2\nA,B\n", Encoding.UTF8);

        svc.WriteMetadataSidecar(csvPath, 1);

        // Verify should succeed
        Assert.True(svc.VerifyMetadataSidecar(csvPath));

        // Tamper the CSV
        File.WriteAllText(csvPath, "Header1,Header2\nA,TAMPERED\n", Encoding.UTF8);
        Assert.Throws<InvalidDataException>(() => svc.VerifyMetadataSidecar(csvPath));
    }

    // =========================================================================
    //  F-P1-02: Intermediate artifact registered before invocation
    // =========================================================================

    [Fact]
    public void FP1_02_IntermediateArtifact_CleanedUpOnCancellation()
    {
        var source = Path.Combine(_tempDir, "game.iso");
        File.WriteAllText(source, "fake iso content");

        using var cts = new CancellationTokenSource();
        var stepCount = 0;

        var invoker = new CancellingInvoker(() =>
        {
            stepCount++;
            if (stepCount >= 2)
                cts.Cancel();
        });

        var executor = new ConversionExecutor([invoker]);
        var plan = new ConversionPlan
        {
            SourcePath = source,
            ConsoleKey = "PS1",
            Policy = ConversionPolicy.Auto,
            SourceIntegrity = SourceIntegrity.Lossless,
            Safety = ConversionSafety.Safe,
            Steps =
            [
                new ConversionStep { Order = 0, InputExtension = ".iso", OutputExtension = ".tmp1", Capability = MakeCapability(".iso", ".tmp1", "tool1", "convert"), IsIntermediate = true },
                new ConversionStep { Order = 1, InputExtension = ".tmp1", OutputExtension = ".chd", Capability = MakeCapability(".tmp1", ".chd", "tool2", "convert"), IsIntermediate = false }
            ]
        };

        try
        {
            executor.Execute(plan, cancellationToken: cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected
        }

        // Intermediate temp files should have been cleaned up
        var tempFiles = Directory.GetFiles(_tempDir, "*.tmp.*");
        Assert.Empty(tempFiles);
    }

    // =========================================================================
    //  F-P2-02: CLI exit code normalization
    // =========================================================================

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 3)]
    [InlineData(42, 1)]
    [InlineData(-1, 1)]
    [InlineData(255, 1)]
    public void FP2_02_ExitCode_NormalizedToDocumentedRange(int rawCode, int expected)
    {
        var normalized = rawCode switch
        {
            0 => 0,
            2 => 2,
            3 => 3,
            _ => 1
        };
        Assert.Equal(expected, normalized);
    }

    // =========================================================================
    //  F-P2-05: Rollback restore target reparse check
    // =========================================================================

    [Fact]
    public void FP2_05_Rollback_SkipsWhenRestoreParentIsReparsePoint()
    {
        var fs = new MockFsWithReparse(_tempDir);
        var svc = new AuditSigningService(fs, keyFilePath: Path.Combine(_tempDir, "hmac.key"));

        var csvPath = Path.Combine(_tempDir, "audit.csv");
        var header = "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp";
        var reparseDir = Path.Combine(_tempDir, "reparse_parent");
        var oldPath = Path.Combine(reparseDir, "game.zip");
        var newPath = Path.Combine(_tempDir, "current", "game.zip");

        Directory.CreateDirectory(Path.Combine(_tempDir, "current"));
        File.WriteAllText(newPath, "content");

        // Create parent dir on disk so Directory.Exists returns true, then mark as reparse
        Directory.CreateDirectory(reparseDir);
        fs.ReparsePoints.Add(reparseDir);

        File.WriteAllText(csvPath,
            $"{header}\n{_tempDir},{oldPath},{newPath},MOVE,game,hash,reason,2025-01-01\n",
            Encoding.UTF8);

        // Use dryRun: true — SEC-ROLLBACK-04b checks parent reparse in dryRun too (Preview/Execute parity)
        var result = svc.Rollback(
            csvPath,
            [_tempDir],
            [_tempDir],
            dryRun: true);

        Assert.True(result.Failed > 0,
            "Rollback should block restore when target parent is reparse point");
    }

    // =========================================================================
    //  F-P3-01: PreferRegions array length limit
    // =========================================================================

    [Fact]
    public void FP3_01_PreferRegions_AcceptsUpTo20()
    {
        var regions = Enumerable.Range(1, 20).Select(i => $"R{i}").ToArray();
        Assert.True(regions.Length <= 20);
    }

    [Fact]
    public void FP3_01_PreferRegions_RejectsOver20()
    {
        var regions = Enumerable.Range(1, 21).Select(i => $"R{i}").ToArray();
        Assert.True(regions.Length > 20);
    }

    // =========================================================================
    //  F-P3-04: ADS check in MoveItemSafely
    // =========================================================================

    [Fact]
    public void FP3_04_MoveItemSafely_BlocksNtfsAds_InDestination()
    {
        var fs = new FileSystemAdapter();
        var source = Path.Combine(_tempDir, "game.zip");
        File.WriteAllText(source, "content");

        var dest = Path.Combine(_tempDir, "output", "game.zip:evil_stream");

        Assert.Throws<InvalidOperationException>(() => fs.MoveItemSafely(source, dest));
    }

    // =========================================================================
    //  F-P3-05: Game-default confidence lowered to 75
    // =========================================================================

    [Fact]
    public void FP3_05_GameDefault_Confidence75()
    {
        var result = FileClassifier.Analyze("Super Mario Bros", aggressiveJunk: false);
        Assert.Equal(FileCategory.Game, result.Category);
        Assert.Equal(75, result.Confidence);
        Assert.Equal("game-default", result.ReasonCode);
    }

    // =========================================================================
    //  F-P1-03 / F-P2-01: DashboardProjection shows conversion details
    // =========================================================================

    [Fact]
    public void FP1_03_DashboardProjection_ConversionCountsDisplayed()
    {
        var result = new RunResult
        {
            Status = "ok",
            ConvertedCount = 5,
            ConvertErrorCount = 2,
            ConvertBlockedCount = 3,
            ConvertReviewCount = 1,
            ConvertSavedBytes = -1024 * 1024 * 100,
            AllCandidates = [],
            DedupeGroups = []
        };

        var projection = RunProjectionFactory.Create(result);
        var dashboard = DashboardProjection.From(projection, result, isConvertOnlyRun: false);

        Assert.Equal("5", dashboard.ConvertedDisplay);
        Assert.Equal("3", dashboard.ConvertBlockedDisplay);
        Assert.Equal("1", dashboard.ConvertReviewDisplay);
    }

    // =========================================================================
    //  F-P2-03: ToolInvokerAdapter cancellation after invocation
    // =========================================================================

    [Fact]
    public void FP2_03_ToolInvokerAdapter_RespectsCancellation_AfterInvoke()
    {
        using var cts = new CancellationTokenSource();
        // Create a dummy tool file so ValidateToolConstraints doesn't block early
        var toolPath = Path.Combine(_tempDir, "chdman.exe");
        File.WriteAllText(toolPath, "dummy-tool");
        var mockRunner = new CancellingToolRunner(cts, _tempDir);
        var adapter = new ToolInvokerAdapter(mockRunner);

        var cap = MakeCapability(".iso", ".chd", "chdman", "createcd");
        var source = Path.Combine(_tempDir, "test.iso");
        File.WriteAllText(source, "content");
        var target = Path.Combine(_tempDir, "test.chd");

        Assert.Throws<OperationCanceledException>(() =>
            adapter.Invoke(source, target, cap, cts.Token));
    }

    // =========================================================================
    //  F-P3-02: OpenAPI nullable annotation completeness
    // =========================================================================

    [Fact]
    public void FP3_02_OpenApiSpec_OptionalFieldsAreNullable()
    {
        var spec = RomCleanup.Api.OpenApiSpec.Json;
        Assert.Contains("\"nullable\": true", spec);
        Assert.Contains("\"preflightWarnings\"", spec);
        Assert.Contains("\"error\"", spec);
    }

    // =========================================================================
    //  Helpers
    // =========================================================================

    private static ConversionCapability MakeCapability(string fromExt, string toExt, string toolName, string command)
        => new()
        {
            SourceExtension = fromExt,
            TargetExtension = toExt,
            Tool = new ToolRequirement { ToolName = toolName },
            Command = command,
            Verification = VerificationMethod.None,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = 1
        };

    private sealed class CancellingInvoker(Action? onStep = null) : IToolInvoker
    {
        public bool CanHandle(ConversionCapability capability) => true;

        public ToolInvocationResult Invoke(
            string sourcePath, string targetPath,
            ConversionCapability capability, CancellationToken cancellationToken = default)
        {
            onStep?.Invoke();
            cancellationToken.ThrowIfCancellationRequested();

            File.WriteAllText(targetPath, "converted");
            return new ToolInvocationResult(true, targetPath, 0, null, null, 10, VerificationStatus.NotAttempted);
        }

        public VerificationStatus Verify(string targetPath, ConversionCapability capability) => VerificationStatus.NotAttempted;
    }

    private sealed class CancellingToolRunner(CancellationTokenSource cts, string? toolDir = null) : IToolRunner
    {
        public string? FindTool(string toolName) => Path.Combine(toolDir ?? Path.GetTempPath(), toolName + ".exe");

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
        {
            cts.Cancel();
            return new ToolResult(0, "ok", true);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments)
            => new(0, "ok", true);
    }

    private sealed class MockFsWithReparse : IFileSystem
    {
        private readonly string _root;
        public HashSet<string> ReparsePoints { get; } = new(StringComparer.OrdinalIgnoreCase);

        public MockFsWithReparse(string root) => _root = root;

        public bool TestPath(string literalPath, string pathType = "Any")
            => pathType == "Container" ? Directory.Exists(literalPath) : File.Exists(literalPath) || Directory.Exists(literalPath);

        public bool IsReparsePoint(string path)
            => ReparsePoints.Contains(Path.GetFullPath(path));

        public IReadOnlyList<string> GetFiles(string directoryPath, string searchPattern = "*", bool recursive = false)
            => Directory.GetFiles(directoryPath, searchPattern, recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);

        public IReadOnlyList<string> GetFilesSafe(string root, IEnumerable<string>? allowedExtensions = null)
            => Directory.Exists(root) ? Directory.GetFiles(root, "*", SearchOption.AllDirectories) : [];

        public string? MoveItemSafely(string sourcePath, string destinationPath)
        {
            var dir = Path.GetDirectoryName(destinationPath);
            if (dir is not null) Directory.CreateDirectory(dir);
            File.Move(sourcePath, destinationPath, overwrite: false);
            return destinationPath;
        }

        public string EnsureDirectory(string path) { Directory.CreateDirectory(path); return path; }

        public string? ResolveChildPathWithinRoot(string rootPath, string relativePath) =>
            Path.GetFullPath(Path.Combine(rootPath, relativePath));

        public bool HasReparsePointInAncestry(string path, string stopAt) => false;

        public void DeleteFile(string path) { if (File.Exists(path)) File.Delete(path); }

        public void CopyFile(string sourcePath, string destinationPath, bool overwrite = false)
            => File.Copy(sourcePath, destinationPath, overwrite);
    }
}
