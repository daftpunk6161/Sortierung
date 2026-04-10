using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Paths;
using Xunit;

namespace Romulus.Tests;

public class RunManagerTests
{
    private static RunManager CreateManager() => new(new FileSystemAdapter(), new AuditCsvStore());

    [Fact]
    public void TryCreate_FirstRun_Succeeds()
    {
        var mgr = CreateManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };

        var run = mgr.TryCreate(request, "DryRun");

        Assert.NotNull(run);
        Assert.Equal("running", run!.Status);
        Assert.Equal("DryRun", run.Mode);
        Assert.NotEmpty(run.RunId);
    }

    [Fact]
    public void TryCreate_SecondRun_ReturnsNull_WhenFirstActive()
    {
        var mgr = CreateManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };

        var first = mgr.TryCreate(request, "DryRun");
        Assert.NotNull(first);

        // Second run while first is active should fail
        var second = mgr.TryCreate(request, "DryRun");
        Assert.Null(second);
    }

    [Fact]
    public void Get_ExistingRun_ReturnsRecord()
    {
        var mgr = CreateManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };
        var run = mgr.TryCreate(request, "DryRun");

        var found = mgr.Get(run!.RunId);

        Assert.NotNull(found);
        Assert.Equal(run.RunId, found!.RunId);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        var mgr = CreateManager();
        Assert.Null(mgr.Get("nonexistent"));
    }

    [Fact]
    public void GetActive_ReturnsCurrentRun()
    {
        var mgr = CreateManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };
        var run = mgr.TryCreate(request, "DryRun");

        var active = mgr.GetActive();
        Assert.NotNull(active);
        Assert.Equal(run!.RunId, active!.RunId);
    }

    [Fact]
    public void GetActive_NoRuns_ReturnsNull()
    {
        var mgr = CreateManager();
        Assert.Null(mgr.GetActive());
    }

    [Fact]
    public async Task List_ReturnsNewestRunsFirst()
    {
        var mgr = CreateManager();
        var firstRoot = CreateTempDir();
        var secondRoot = CreateTempDir();

        try
        {
            var first = mgr.TryCreate(new RunRequest { Roots = new[] { firstRoot }, Mode = "DryRun" }, "DryRun");
            Assert.NotNull(first);
            await mgr.WaitForCompletion(first!.RunId, 50);

            await Task.Delay(25);

            var second = mgr.TryCreate(new RunRequest { Roots = new[] { secondRoot }, Mode = "DryRun" }, "DryRun");
            Assert.NotNull(second);
            await mgr.WaitForCompletion(second!.RunId, 50);

            var listed = mgr.List();
            Assert.True(listed.Count >= 2);
            Assert.Equal(second.RunId, listed[0].RunId);
            Assert.Equal(first.RunId, listed[1].RunId);
        }
        finally
        {
            CleanupDir(firstRoot);
            CleanupDir(secondRoot);
        }
    }

    [Fact]
    public async Task Cancel_RunningRun_SetsCancelled()
    {
        var mgr = CreateManager();
        var dir = CreateTempDir();
        try
        {
            var request = new RunRequest { Roots = new[] { dir }, Mode = "DryRun" };
            var run = mgr.TryCreate(request, "DryRun");
            Assert.NotNull(run);

            mgr.Cancel(run!.RunId);

            // Wait for the run to complete
            await mgr.WaitForCompletion(run.RunId, 50);

            var updated = mgr.Get(run.RunId);
            // Status depends on race: cancelled if CTS fired before Execute, completed/failed if Execute finished first
            Assert.Contains(updated!.Status, new[] { "cancelled", "completed", "failed" });
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public async Task Cancel_WhenCancellationSourceAlreadyDisposed_ReturnsNoOpWithoutThrow()
    {
        using var runStarted = new ManualResetEventSlim(false);
        using var allowCompletion = new ManualResetEventSlim(false);

        var mgr = new RunManager(new FileSystemAdapter(), new AuditCsvStore(), (_, _, _, _) =>
        {
            runStarted.Set();
            allowCompletion.Wait(TimeSpan.FromSeconds(5));
            return new RunExecutionOutcome(ApiRunStatus.Completed, new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            });
        });

        var run = mgr.TryCreate(new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" }, "DryRun");
        Assert.NotNull(run);
        Assert.True(runStarted.Wait(TimeSpan.FromSeconds(2)));

        run!.CancellationSource.Dispose();

        var cancelResult = mgr.Cancel(run.RunId);
        Assert.Equal(RunCancelDisposition.NoOp, cancelResult.Disposition);

        allowCompletion.Set();

        var completion = await mgr.WaitForCompletion(run.RunId, 25, TimeSpan.FromSeconds(5));
        Assert.Equal(RunWaitDisposition.Completed, completion.Disposition);
    }

    [Fact]
    public void RunRecord_ReviewApprovalMethods_AreConsistent()
    {
        var run = new RunRecord
        {
            RunId = Guid.NewGuid().ToString(),
            RequestFingerprint = "fp",
            StartedUtc = DateTime.UtcNow
        };

        Assert.True(run.TryApproveReviewPath("C:\\roms\\a.iso"));
        Assert.False(run.TryApproveReviewPath("C:\\roms\\a.iso"));
        Assert.True(run.IsReviewPathApproved("C:\\roms\\a.iso"));
        Assert.False(run.IsReviewPathApproved("C:\\roms\\b.iso"));
        Assert.False(run.TryApproveReviewPath(""));
        Assert.Equal(1, run.ApprovedReviewCount);
    }

    [Fact]
    public async Task WaitForCompletion_EmptyRoot_CompletesQuickly()
    {
        var mgr = CreateManager();
        var dir = CreateTempDir();
        try
        {
            var request = new RunRequest { Roots = new[] { dir }, Mode = "DryRun" };
            var run = mgr.TryCreate(request, "DryRun");

            await mgr.WaitForCompletion(run!.RunId, 50);

            var completed = mgr.Get(run.RunId);
            Assert.NotNull(completed);
            Assert.NotEqual("running", completed!.Status);
            Assert.NotNull(completed.CompletedUtc);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public async Task Run_PreflightBlocked_UsesBlockedApiStatus()
    {
        var mgr = CreateManager();
        var missingRoot = Path.Combine(Path.GetTempPath(), "romulus-missing-" + Guid.NewGuid().ToString("N"));

        var run = mgr.TryCreate(new RunRequest { Roots = new[] { missingRoot }, Mode = "DryRun" }, "DryRun");
        Assert.NotNull(run);

        await mgr.WaitForCompletion(run!.RunId, 50);

        var completed = mgr.Get(run.RunId);
        Assert.NotNull(completed);
        Assert.Equal(ApiRunStatus.Blocked, completed!.Status);
        Assert.Equal("blocked", completed.Result!.OrchestratorStatus);
        Assert.Equal(3, completed.Result.ExitCode);
    }

    [Fact]
    public async Task Run_WithFiles_ProducesResult()
    {
        var mgr = CreateManager();
        var dir = CreateTempDir();
        try
        {
            // Create some test files
            File.WriteAllText(Path.Combine(dir, "Game (USA).zip"), "");
            File.WriteAllText(Path.Combine(dir, "Game (Europe).zip"), "");
            File.WriteAllText(Path.Combine(dir, "Other (Japan).zip"), "");

            var request = new RunRequest
            {
                Roots = new[] { dir },
                Mode = "DryRun",
                PreferRegions = new[] { "US", "EU", "JP" }
            };

            var run = mgr.TryCreate(request, "DryRun");
            await mgr.WaitForCompletion(run!.RunId, 50);

            var completed = mgr.Get(run.RunId);
            Assert.Equal("completed", completed!.Status);
            Assert.NotNull(completed.Result);
            Assert.Equal("ok", completed.Result!.OrchestratorStatus);
            Assert.Equal(0, completed.Result.ExitCode);
            Assert.True(completed.Result.TotalFiles >= 3);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public async Task Run_CompletionUnlocksNewRun()
    {
        var mgr = CreateManager();
        var dir = CreateTempDir();
        try
        {
            var request = new RunRequest { Roots = new[] { dir }, Mode = "DryRun" };
            var first = mgr.TryCreate(request, "DryRun");
            await mgr.WaitForCompletion(first!.RunId, 50);

            // Now we should be able to create a new run
            var second = mgr.TryCreate(request, "DryRun");
            Assert.NotNull(second);
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public void RunRequest_DefaultRegions()
    {
        var request = new RunRequest { Roots = new[] { "C:\\test" } };
        Assert.Null(request.PreferRegions);
    }

    [Fact]
    public void TryCreateOrReuse_WithoutPreferRegions_UsesRunConstantsDefaults()
    {
        var mgr = CreateManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };

        var create = mgr.TryCreateOrReuse(request, "DryRun", "prefer-defaults");

        Assert.Equal(RunCreateDisposition.Created, create.Disposition);
        Assert.Equal(RunConstants.DefaultPreferRegions, create.Run!.PreferRegions);
    }

    [Fact]
    public void TryCreateOrReuse_PropagatesDatAuditAndRenameFlags()
    {
        var mgr = CreateManager();
        var request = new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            Mode = "DryRun",
            EnableDat = true,
            EnableDatAudit = true,
            EnableDatRename = true
        };

        var create = mgr.TryCreateOrReuse(request, "DryRun", "dat-flags");

        Assert.Equal(RunCreateDisposition.Created, create.Disposition);
        Assert.True(create.Run!.EnableDat);
        Assert.True(create.Run.EnableDatAudit);
        Assert.True(create.Run.EnableDatRename);
    }

    [Fact]
    public void TGAP46_RunStatusDto_IncludesAllRunRecordBooleanFlags()
    {
        var run = new RunRecord
        {
            RunId = Guid.NewGuid().ToString("N"),
            RequestFingerprint = "fp",
            StartedUtc = DateTime.UtcNow,
            Status = "running",
            Mode = "DryRun",
            EnableDat = true,
            EnableDatAudit = true,
            EnableDatRename = true
        };

        var dto = run.ToDto();

        Assert.True(dto.EnableDat);
        Assert.True(dto.EnableDatAudit);
        Assert.True(dto.EnableDatRename);
    }

    [Fact]
    public void TryCreateOrReuse_SameIdempotencyKey_ReusesExistingRun()
    {
        var mgr = CreateManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };

        var first = mgr.TryCreateOrReuse(request, "DryRun", "idem-001");
        var second = mgr.TryCreateOrReuse(request, "DryRun", "idem-001");

        Assert.Equal(RunCreateDisposition.Created, first.Disposition);
        Assert.Equal(RunCreateDisposition.Reused, second.Disposition);
        Assert.Equal(first.Run!.RunId, second.Run!.RunId);
    }

    [Fact]
    public void TryCreateOrReuse_SameIdempotencyKey_SameWindowsPathDifferentCase_ReusesExistingRun()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var mgr = CreateManager();
        var root = GetTestRoot();

        var first = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { root }, Mode = "DryRun" }, "DryRun", "idem-001-case");
        var second = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { ToMixedCasePath(root) }, Mode = "DryRun" }, "DryRun", "idem-001-case");

        Assert.Equal(RunCreateDisposition.Created, first.Disposition);
        Assert.Equal(RunCreateDisposition.Reused, second.Disposition);
        Assert.Equal(first.Run!.RunId, second.Run!.RunId);
    }

    [Fact]
    public void TryCreateOrReuse_SameIdempotencyKey_DifferentRequest_ReturnsConflict()
    {
        var mgr = CreateManager();
        var first = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "DryRun", "idem-002");
        var second = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar) } }, "Move", "idem-002");

        Assert.Equal(RunCreateDisposition.IdempotencyConflict, second.Disposition);
        Assert.Equal(first.Run!.RunId, second.Run!.RunId);
    }

    [Fact]
    public async Task WaitForCompletion_CancellationToken_DoesNotCancelUnderlyingRun()
    {
        // Use a gate to ensure the run stays in-progress until we've verified cancellation behavior.
        // This eliminates the race condition where the run completes before WaitForCompletion checks the token.
        using var runGate = new ManualResetEventSlim(false);

        var mgr = new RunManager(new FileSystemAdapter(), new AuditCsvStore(), (_, _, _, ct) =>
        {
            runGate.Wait(ct);
            return new RunExecutionOutcome("completed", new ApiRunResult { OrchestratorStatus = "ok", ExitCode = 0 });
        });

        var run = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "DryRun", "idem-003").Run!;

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var waitResult = await mgr.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(5), cancellationToken: cts.Token);

        Assert.Equal(RunWaitDisposition.ClientDisconnected, waitResult.Disposition);
        Assert.Equal("running", mgr.Get(run.RunId)!.Status);

        // Release the run so it can complete
        runGate.Set();

        var finalWait = await mgr.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(5));
        Assert.Equal(RunWaitDisposition.Completed, finalWait.Disposition);
        Assert.Equal("completed", mgr.Get(run.RunId)!.Status);
    }

    [Fact]
    public async Task TGAP48_Api_StatusStrings_UseCentralConstants()
    {
        var mgr = new RunManager(new FileSystemAdapter(), new AuditCsvStore(), (_, _, _, _) =>
            new RunExecutionOutcome(ApiRunStatus.CompletedWithErrors, new ApiRunResult
            {
                OrchestratorStatus = "ok",
                ExitCode = 0
            }));

        var run = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "DryRun", "tgap-48").Run!;

        Assert.Contains(run.Status, new[] { ApiRunStatus.Running, ApiRunStatus.CompletedWithErrors });

        await mgr.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(5));

        var completed = mgr.Get(run.RunId)!;
        Assert.Equal(ApiRunStatus.CompletedWithErrors, completed.Status);
    }

    [Fact]
    public void TGAP50_Api_ArtifactPaths_UsesRootAdjacentDirectoriesWhenRootsProvided()
    {
        var root = CreateTempDir();
        try
        {
            var expectedAuditDir = ArtifactPathResolver.GetArtifactDirectory(new[] { root }, AppIdentity.ArtifactDirectories.AuditLogs);
            var expectedReportDir = ArtifactPathResolver.GetArtifactDirectory(new[] { root }, AppIdentity.ArtifactDirectories.Reports);

            var (auditPath, reportPath) = RunLifecycleManager.GetArtifactPaths("tgap50", new[] { root });

            Assert.StartsWith(Path.GetFullPath(expectedAuditDir), Path.GetFullPath(auditPath), StringComparison.OrdinalIgnoreCase);
            Assert.StartsWith(Path.GetFullPath(expectedReportDir), Path.GetFullPath(reportPath), StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            CleanupDir(root);
        }
    }

    [Fact]
    public async Task CancelledRun_WithCsvOnlyAudit_RequiresManualCleanupRecoveryState()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"api-recovery-{Guid.NewGuid():N}.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        try
        {
            var mgr = new RunManager(new FileSystemAdapter(), new AuditCsvStore(), (run, _, _, _) =>
            {
                run.AuditPath = auditPath;
                return new RunExecutionOutcome("cancelled", new ApiRunResult
                {
                    OrchestratorStatus = "cancelled",
                    ExitCode = 2
                });
            });

            var run = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "Move", "idem-004").Run!;
            var waitResult = await mgr.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
            var completed = mgr.Get(run.RunId)!;
            Assert.Equal("manual-cleanup-may-be-required", completed.RecoveryState);
            Assert.False(completed.CanRollback);
            Assert.False(completed.ResumeSupported);
            Assert.Equal("audit-rollback-only", completed.RecoveryModel);
        }
        finally
        {
            try { File.Delete(auditPath); } catch { }
        }
    }

    [Fact]
    public async Task FailedRun_WithCsvOnlyAudit_RequiresManualCleanupRecoveryState()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"api-failure-{Guid.NewGuid():N}.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        try
        {
            var mgr = new RunManager(new FileSystemAdapter(), new AuditCsvStore(), (run, _, _, _) =>
            {
                run.AuditPath = auditPath;
                return new RunExecutionOutcome("failed", new ApiRunResult
                {
                    OrchestratorStatus = "failed",
                    ExitCode = 1
                });
            });

            var run = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "Move", "idem-005").Run!;
            var waitResult = await mgr.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
            var completed = mgr.Get(run.RunId)!;
            Assert.Equal("manual-cleanup-may-be-required", completed.RecoveryState);
            Assert.False(completed.CanRollback);
            Assert.True(completed.CanRetry);
            Assert.False(completed.ResumeSupported);
            Assert.Equal("audit-rollback-only", completed.RecoveryModel);
            Assert.Equal("not-persisted", completed.RestartRecovery);
        }
        finally
        {
            try { File.Delete(auditPath); } catch { }
        }
    }

    [Fact]
    public async Task CompletedWithErrorsRun_WithCsvOnlyAudit_RequiresManualCleanupRecoveryState()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"api-completed-errors-{Guid.NewGuid():N}.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");

        try
        {
            var mgr = new RunManager(new FileSystemAdapter(), new AuditCsvStore(), (run, _, _, _) =>
            {
                run.AuditPath = auditPath;
                return new RunExecutionOutcome(ApiRunStatus.CompletedWithErrors, new ApiRunResult
                {
                    OrchestratorStatus = "completed_with_errors",
                    ExitCode = 0,
                    FailCount = 1
                });
            });

            var run = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "Move", "idem-005b").Run!;
            var waitResult = await mgr.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
            var completed = mgr.Get(run.RunId)!;
            Assert.Equal("manual-cleanup-may-be-required", completed.RecoveryState);
            Assert.False(completed.CanRollback);
        }
        finally
        {
            try { File.Delete(auditPath); } catch { }
        }
    }

    [Fact]
    public async Task CompletedWithErrorsRun_WithVerifiedSidecar_ExposesRollbackRecoveryState()
    {
        var auditPath = Path.Combine(Path.GetTempPath(), $"api-completed-errors-verified-{Guid.NewGuid():N}.csv");
        File.WriteAllText(auditPath, "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp\n");
        var auditStore = new AuditCsvStore();
        auditStore.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Status"] = "completed_with_errors" });

        try
        {
            var mgr = new RunManager(new FileSystemAdapter(), auditStore, (run, _, _, _) =>
            {
                run.AuditPath = auditPath;
                return new RunExecutionOutcome(ApiRunStatus.CompletedWithErrors, new ApiRunResult
                {
                    OrchestratorStatus = "completed_with_errors",
                    ExitCode = 1,
                    FailCount = 1
                });
            });

            var run = mgr.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "Move", "idem-005c").Run!;
            var waitResult = await mgr.WaitForCompletion(run.RunId, timeout: TimeSpan.FromSeconds(30));

            Assert.Equal(RunWaitDisposition.Completed, waitResult.Disposition);
            var completed = mgr.Get(run.RunId)!;
            Assert.Equal("partial-rollback-available", completed.RecoveryState);
            Assert.True(completed.CanRollback);
        }
        finally
        {
            try { File.Delete(auditPath); } catch { }
            try { File.Delete(auditPath + ".meta.json"); } catch { }
        }
    }

    [Fact]
    public async Task RestartAfterFailure_DoesNotResumeOrPersistPreviousRunState()
    {
        var firstManager = new RunManager(new FileSystemAdapter(), new AuditCsvStore(), (_, _, _, _) =>
            new RunExecutionOutcome("failed", new ApiRunResult
            {
                OrchestratorStatus = "failed",
                ExitCode = 1,
                Error = new OperationError("RUN-SIMULATED-CRASH", "Simulated crash", ErrorKind.Critical, "TEST")
            }));

        var firstRun = firstManager.TryCreateOrReuse(new RunRequest { Roots = new[] { GetTestRoot() } }, "Move", "idem-006").Run!;
        var firstWaitResult = await firstManager.WaitForCompletion(firstRun.RunId, timeout: TimeSpan.FromSeconds(30));
        Assert.Equal(RunWaitDisposition.Completed, firstWaitResult.Disposition);

        var restartedManager = CreateManager();

        Assert.NotNull(firstManager.Get(firstRun.RunId));
        Assert.Null(restartedManager.Get(firstRun.RunId));
        Assert.Null(restartedManager.GetActive());
    }

    [Fact]
    public async Task TryCreateOrReuse_MapsExtendedRequestOptions_IntoRunRecord()
    {
        var mgr = CreateManager();
        var request = new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            Mode = "Move",
            PreferRegions = new[] { "US", "EU" },
            RemoveJunk = false,
            AggressiveJunk = true,
            SortConsole = true,
            EnableDat = true,
            HashType = "sha256",
            ConvertFormat = "auto",
            TrashRoot = GetTestRoot(),
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = false,
            Extensions = new[] { "chd", ".rvz" }
        };

        var create = mgr.TryCreateOrReuse(request, "Move", "idem-options-map");
        Assert.Equal(RunCreateDisposition.Created, create.Disposition);

        var run = create.Run!;
        Assert.False(run.RemoveJunk);
        Assert.True(run.AggressiveJunk);
        Assert.True(run.SortConsole);
        Assert.True(run.EnableDat);
        Assert.Equal("SHA256", run.HashType);
        Assert.Equal("auto", run.ConvertFormat);
        Assert.Equal(GetTestRoot(), run.TrashRoot);
        Assert.True(run.OnlyGames);
        Assert.False(run.KeepUnknownWhenOnlyGames);
        Assert.Contains(".chd", run.Extensions);
        Assert.Contains(".rvz", run.Extensions);

        await mgr.WaitForCompletion(run.RunId, 50);
    }

    [Fact]
    public void TryCreateOrReuse_SameIdempotencyKey_DifferentExtendedOptions_ReturnsConflict()
    {
        var mgr = CreateManager();
        var key = "idem-extended-conflict";

        var first = mgr.TryCreateOrReuse(new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            EnableDat = false,
            HashType = "SHA1",
            RemoveJunk = true
        }, "DryRun", key);

        var second = mgr.TryCreateOrReuse(new RunRequest
        {
            Roots = new[] { GetTestRoot() },
            EnableDat = true,
            HashType = "SHA256",
            RemoveJunk = false,
            OnlyGames = true,
            KeepUnknownWhenOnlyGames = false
        }, "DryRun", key);

        Assert.Equal(RunCreateDisposition.Created, first.Disposition);
        Assert.Equal(RunCreateDisposition.IdempotencyConflict, second.Disposition);
        Assert.Equal(first.Run!.RunId, second.Run!.RunId);
    }

    private static string GetTestRoot()
    {
        // Return a path that exists for run creation, even if it will fail during scan
        return Path.GetTempPath();
    }

    private static string ToMixedCasePath(string path)
    {
        var chars = path.ToCharArray();
        for (var index = 0; index < chars.Length; index++)
        {
            if (!char.IsLetter(chars[index]))
                continue;

            chars[index] = index % 2 == 0
                ? char.ToUpperInvariant(chars[index])
                : char.ToLowerInvariant(chars[index]);
        }

        return new string(chars);
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "romulus-api-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string dir)
    {
        try { Directory.Delete(dir, true); }
        catch { /* best effort */ }
    }
}
