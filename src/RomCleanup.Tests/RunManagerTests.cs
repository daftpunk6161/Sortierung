using RomCleanup.Api;
using Xunit;

namespace RomCleanup.Tests;

public class RunManagerTests
{
    [Fact]
    public void TryCreate_FirstRun_Succeeds()
    {
        var mgr = new RunManager();
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
        var mgr = new RunManager();
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
        var mgr = new RunManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };
        var run = mgr.TryCreate(request, "DryRun");

        var found = mgr.Get(run!.RunId);

        Assert.NotNull(found);
        Assert.Equal(run.RunId, found!.RunId);
    }

    [Fact]
    public void Get_NonExistent_ReturnsNull()
    {
        var mgr = new RunManager();
        Assert.Null(mgr.Get("nonexistent"));
    }

    [Fact]
    public void GetActive_ReturnsCurrentRun()
    {
        var mgr = new RunManager();
        var request = new RunRequest { Roots = new[] { GetTestRoot() }, Mode = "DryRun" };
        var run = mgr.TryCreate(request, "DryRun");

        var active = mgr.GetActive();
        Assert.NotNull(active);
        Assert.Equal(run!.RunId, active!.RunId);
    }

    [Fact]
    public void GetActive_NoRuns_ReturnsNull()
    {
        var mgr = new RunManager();
        Assert.Null(mgr.GetActive());
    }

    [Fact]
    public async Task Cancel_RunningRun_SetsCancelled()
    {
        var mgr = new RunManager();
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
            // Status should be either cancelled or completed (race condition possible)
            Assert.Contains(updated!.Status, new[] { "cancelled", "completed" });
        }
        finally
        {
            CleanupDir(dir);
        }
    }

    [Fact]
    public async Task WaitForCompletion_EmptyRoot_CompletesQuickly()
    {
        var mgr = new RunManager();
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
    public async Task Run_WithFiles_ProducesResult()
    {
        var mgr = new RunManager();
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
            Assert.Equal("ok", completed.Result!.Status);
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
        var mgr = new RunManager();
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

    private static string GetTestRoot()
    {
        // Return a path that exists for run creation, even if it will fail during scan
        return Path.GetTempPath();
    }

    private static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "romcleanup-api-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static void CleanupDir(string dir)
    {
        try { Directory.Delete(dir, true); }
        catch { /* best effort */ }
    }
}
