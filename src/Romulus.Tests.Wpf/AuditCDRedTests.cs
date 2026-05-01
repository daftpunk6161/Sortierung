using Romulus.Contracts.Models;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Sorting;
using Romulus.UI.Wpf.ViewModels;
using Romulus.Tests.TestHelpers;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Behavioral tests retained from the historical Audit C/D/E/F set.
/// All source-mirror assertions were removed in Block A
/// of test-suite-remediation-plan-2026-04-25.md.
/// </summary>
public sealed class AuditCDRedTests
{
    [Fact]
    public void C01_MainViewModel_UsesChildViewModels_ForMajorDomains()
    {
        var vm = new MainViewModel();

        Assert.NotNull(vm.Setup);
        Assert.NotNull(vm.Run);
        Assert.NotNull(vm.Tools);
        Assert.NotNull(vm.CommandPalette);
    }

    [Fact]
    public void C08_ApplyMaterializedRunConfiguration_InvalidConflictPolicy_RollsBackState()
    {
        var vm = new MainViewModel();
        var previousDryRun = vm.DryRun;
        var previousRemoveJunk = vm.RemoveJunk;
        var previousConflictPolicy = vm.ConflictPolicy;

        var materialized = new MaterializedRunConfiguration(
            EffectiveDraft: new RunConfigurationDraft
            {
                Mode = "Move",
                RemoveJunk = !previousRemoveJunk,
                ConflictPolicy = "not-a-valid-policy"
            },
            Workflow: null,
            Profile: null,
            EffectiveProfileId: null,
            Options: new RunOptions());

        Assert.Throws<InvalidOperationException>(() => vm.ApplyMaterializedRunConfiguration(materialized));
        Assert.Equal(previousDryRun, vm.DryRun);
        Assert.Equal(previousRemoveJunk, vm.RemoveJunk);
        Assert.Equal(previousConflictPolicy, vm.ConflictPolicy);
    }

    [Fact]
    public void C15_RollbackService_IntegrityFailure_MustReportAffectedRowCount()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_C15_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var auditPath = Path.Combine(tempDir, "audit.csv");
            var keyPath = Path.Combine(tempDir, "audit-signing.key");
            var store = new AuditCsvStore(keyFilePath: keyPath);

            for (var i = 1; i <= 3; i++)
            {
                store.AppendAuditRow(
                    auditPath,
                    tempDir,
                    Path.Combine(tempDir, $"old{i}.rom"),
                    Path.Combine(tempDir, $"new{i}.rom"),
                    "Move",
                    "GAME",
                    string.Empty,
                    "test");
            }

            store.WriteMetadataSidecar(auditPath, new Dictionary<string, object> { ["Mode"] = "Move" });

            // Corrupt sidecar JSON so integrity verification fails before rollback starts.
            File.WriteAllText(auditPath + ".meta.json", "{ not-valid-json }");

            var result = RollbackService.Execute(auditPath, [tempDir], keyPath);

            Assert.Equal(3, result.Failed);
            Assert.Equal(0, result.RolledBack);
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    [Fact]
    public void C16_M3uRewrite_MustNotCollapseDistinctRelativeEntriesOnNameCollision()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "Romulus_C16_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            var root = Path.Combine(tempDir, "sort-m3u-collision");
            var inputDir = Path.Combine(root, "Input");
            Directory.CreateDirectory(Path.Combine(inputDir, "sub"));

            var m3uPath = Path.Combine(inputDir, "Game.m3u");
            var cuePrimary = Path.Combine(inputDir, "disc1.cue");
            var cueSecondary = Path.Combine(inputDir, "sub", "disc1.cue");

            File.WriteAllText(m3uPath, "disc1.cue\r\nsub\\disc1.cue\r\n");
            File.WriteAllText(cuePrimary, "FILE \"disc1.bin\" BINARY");
            File.WriteAllText(cueSecondary, "FILE \"disc1-alt.bin\" BINARY");

            var sorter = new ConsoleSorter(new FileSystemAdapter(), LoadDetector());
            var result = sorter.SortWithAutoSortDecisions(
                [root],
                [".m3u", ".cue"],
                dryRun: false,
                enrichedConsoleKeys: new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [m3uPath] = "PS1",
                    [cuePrimary] = "PS1",
                    [cueSecondary] = "PS1"
                },
                candidatePaths: [m3uPath, cuePrimary, cueSecondary]);

            var movedPlaylist = Path.Combine(root, "PS1", "Game.m3u");
            Assert.True(File.Exists(movedPlaylist));
            Assert.Equal(0, result.Failed);

            var lines = File.ReadAllLines(movedPlaylist)
                .Select(static line => line.Trim())
                .Where(static line => !string.IsNullOrWhiteSpace(line) && !line.StartsWith('#'))
                .ToArray();

            Assert.Equal(2, lines.Length);
            Assert.Equal(1, lines.Count(static line => line.Equals("disc1.cue", StringComparison.OrdinalIgnoreCase)));
            Assert.Equal(1, lines.Count(static line => line.Equals("disc1__DUP1.cue", StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            try
            {
                if (Directory.Exists(tempDir))
                    Directory.Delete(tempDir, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    [Fact]
    public void F15_CliParser_RepeatedRootsFlag_ReturnsValidationError()
    {
        var rootA = Path.Combine(Path.GetTempPath(), "Romulus_F15_A_" + Guid.NewGuid().ToString("N"));
        var rootB = Path.Combine(Path.GetTempPath(), "Romulus_F15_B_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(rootA);
        Directory.CreateDirectory(rootB);

        try
        {
            var result = Romulus.CLI.CliArgsParser.Parse([
                "--roots", rootA,
                "--roots", rootB
            ]);

            Assert.Equal(3, result.ExitCode);
            Assert.Contains(result.Errors, e => e.Contains("--roots", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            try
            {
                if (Directory.Exists(rootA))
                    Directory.Delete(rootA, recursive: true);
                if (Directory.Exists(rootB))
                    Directory.Delete(rootB, recursive: true);
            }
            catch
            {
                // best effort cleanup
            }
        }
    }

    private static string FindRepositoryRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "AGENTS.md")))
            dir = dir.Parent;

        return dir?.FullName ?? throw new InvalidOperationException("Could not resolve repository root from test context.");
    }

    private static ConsoleDetector LoadDetector()
    {
        var repoRoot = FindRepositoryRoot();
        var consolesPath = Path.Combine(repoRoot, "data", "consoles.json");
        return ConsoleDetector.LoadFromJson(File.ReadAllText(consolesPath));
    }
}
