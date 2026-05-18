using Romulus.Contracts;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Paths;
using Xunit;

namespace Romulus.Tests;

public sealed class InfrastructureSafetyPolicyRegressionTests : IDisposable
{
    private readonly string _tempDir;

    public InfrastructureSafetyPolicyRegressionTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_InfraSafety_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void ToolPathValidator_EmptyToolPathMeansNotConfigured(string? path)
    {
        var (normalized, reason) = ToolPathValidator.Validate(path);

        Assert.Null(normalized);
        Assert.Null(reason);
    }

    [Fact]
    public void ToolPathValidator_InvalidPathSyntaxIsRejectedBeforeExistenceCheck()
    {
        var (normalized, reason) = ToolPathValidator.Validate("bad\0tool.exe");

        Assert.Null(normalized);
        Assert.Contains("Dateipfad", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolPathValidator_DisallowedExistingFileTypeIsRejected()
    {
        var toolPath = Path.Combine(_tempDir, "tool.ps1");
        File.WriteAllText(toolPath, "Write-Host unsafe");

        var (normalized, reason) = ToolPathValidator.Validate(toolPath);

        Assert.Null(normalized);
        Assert.Contains("nicht erlaubt", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ToolPathValidator_AllowedExistingToolIsNormalized()
    {
        var toolPath = Path.Combine(_tempDir, "tool.cmd");
        File.WriteAllText(toolPath, "@echo off");

        var (normalized, reason) = ToolPathValidator.Validate(toolPath);

        Assert.Equal(Path.GetFullPath(toolPath), normalized);
        Assert.Null(reason);
    }

    [Fact]
    public void ToolPathValidator_SystemDirectoryToolIsRejected()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var systemDir = Environment.GetFolderPath(Environment.SpecialFolder.System);
        Assert.False(string.IsNullOrWhiteSpace(systemDir));
        var systemTool = Path.Combine(systemDir, "cmd.exe");
        Assert.True(File.Exists(systemTool), $"Expected Windows command processor at {systemTool}.");

        var (normalized, reason) = ToolPathValidator.Validate(systemTool);

        Assert.Null(normalized);
        Assert.Contains("Windows-Verzeichnis", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AuditRecoveryStateResolver_ReturnsTrueOnlyForExistingAuditWithVerifiedSidecar()
    {
        var auditPath = Path.Combine(_tempDir, "run.audit.csv");
        File.WriteAllText(auditPath, "action,old,new");
        var store = new StubAuditStore(testSidecar: _ => true);

        Assert.True(AuditRecoveryStateResolver.HasVerifiedRollback(store, auditPath));
        Assert.False(AuditRecoveryStateResolver.HasVerifiedRollback(store, Path.Combine(_tempDir, "missing.audit.csv")));
        Assert.False(AuditRecoveryStateResolver.HasVerifiedRollback(store, "   "));
    }

    [Theory]
    [InlineData("io")]
    [InlineData("unauthorized")]
    [InlineData("invalid")]
    public void AuditRecoveryStateResolver_TreatsUnreadableSidecarAsNotRollbackable(string failureMode)
    {
        var auditPath = Path.Combine(_tempDir, $"{failureMode}.audit.csv");
        File.WriteAllText(auditPath, "action,old,new");
        var store = new StubAuditStore(testSidecar: _ => failureMode switch
        {
            "io" => throw new IOException("read failed"),
            "unauthorized" => throw new UnauthorizedAccessException("denied"),
            "invalid" => throw new InvalidOperationException("bad sidecar"),
            _ => true
        });

        Assert.False(AuditRecoveryStateResolver.HasVerifiedRollback(store, auditPath));
    }

    [Theory]
    [InlineData(" running ", false, "in-progress")]
    [InlineData("COMPLETED", true, "rollback-available")]
    [InlineData("completed", false, "not-required")]
    [InlineData("completed_with_errors", true, "partial-rollback-available")]
    [InlineData("completed_with_errors", false, "manual-cleanup-may-be-required")]
    [InlineData("cancelled", true, "partial-rollback-available")]
    [InlineData("failed", true, "partial-rollback-available")]
    [InlineData("cancelled", false, "manual-cleanup-may-be-required")]
    [InlineData("failed", false, "manual-cleanup-may-be-required")]
    [InlineData("blocked", true, "unknown")]
    public void AuditRecoveryStateResolver_MapsLifecycleStatusToSharedRecoveryState(
        string status,
        bool canRollback,
        string expected)
    {
        var state = AuditRecoveryStateResolver.ResolveRecoveryState(status, canRollback);

        Assert.Equal(expected, state);
    }

    [Fact]
    public void PhasePlanExecutor_FailedStepResultTagsFailedPhaseAndSkipsRemainingSteps()
    {
        var state = new PipelineState();
        var progress = new List<string>();
        var executedAfterFailure = false;
        var executor = new PhasePlanExecutor(progress.Add);

        executor.Execute(
        [
            new ActionPhaseStep("Scan", (_, _) => PhaseStepResult.Ok()),
            new ActionPhaseStep("Move", (_, _) => new PhaseStepResult
            {
                Status = RunConstants.StatusFailed,
                ItemCount = 1,
                Warnings = ["source verification failed"]
            }),
            new ActionPhaseStep("Report", (_, _) =>
            {
                executedAfterFailure = true;
                return PhaseStepResult.Ok();
            })
        ], state, CancellationToken.None);

        Assert.Equal("Move", state.FailedPhaseName);
        Assert.Equal(RunConstants.StatusFailed, state.FailedPhaseStatus);
        Assert.False(executedAfterFailure);
        Assert.Contains(progress, message => message == "[WARN] Move: source verification failed");
        Assert.Contains(progress, message => message.Contains("aborting remaining phases", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhasePlanExecutor_ExceptionTagsFailedPhaseBeforeRethrow()
    {
        var state = new PipelineState();
        var progress = new List<string>();
        var executor = new PhasePlanExecutor(progress.Add);

        var thrown = Assert.Throws<InvalidOperationException>(() => executor.Execute(
        [
            new ActionPhaseStep("Move", (_, _) => throw new InvalidOperationException("audit write failed"))
        ], state, CancellationToken.None));

        Assert.Equal("audit write failed", thrown.Message);
        Assert.Equal("Move", state.FailedPhaseName);
        Assert.Equal(RunConstants.StatusFailed, state.FailedPhaseStatus);
        Assert.Contains(progress, message => message.Contains("failed with exception", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PhasePlanExecutor_CancellationIsNotConvertedToFailedPhase()
    {
        var state = new PipelineState();
        var executor = new PhasePlanExecutor(onProgress: null);

        Assert.Throws<OperationCanceledException>(() => executor.Execute(
        [
            new ActionPhaseStep("Move", (_, _) => throw new OperationCanceledException())
        ], state, CancellationToken.None));

        Assert.Null(state.FailedPhaseName);
        Assert.Null(state.FailedPhaseStatus);
    }

    private sealed class StubAuditStore(Func<string, bool> testSidecar) : IAuditStore
    {
        public void WriteMetadataSidecar(string auditCsvPath, IDictionary<string, object> metadata)
            => throw new NotSupportedException();

        public bool TestMetadataSidecar(string auditCsvPath)
            => testSidecar(auditCsvPath);

        public void Flush(string auditCsvPath)
        {
        }

        public IReadOnlyList<string> Rollback(
            string auditCsvPath,
            string[] allowedRestoreRoots,
            string[] allowedCurrentRoots,
            bool dryRun = false)
            => throw new NotSupportedException();

        public void AppendAuditRow(
            string auditCsvPath,
            string rootPath,
            string oldPath,
            string newPath,
            string action,
            string category = "",
            string hash = "",
            string reason = "")
            => throw new NotSupportedException();
    }
}
