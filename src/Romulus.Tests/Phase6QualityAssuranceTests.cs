using Romulus.Api;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Classification;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Sorting;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Dedicated tests for Phase 6 audit items:
/// TASK-170 (ProgressEstimator edge cases),
/// TASK-171 (PhasePlanBuilder option combinations),
/// TASK-172 (MoveSetAtomically with TempDir fixtures).
/// </summary>
public class Phase6QualityAssuranceTests
{
    // ═══════════════════════════════════════════════════════════════════
    // TASK-170: ProgressEstimator Tests
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ProgressEstimator_NullMessage_ReturnsZero()
    {
        Assert.Equal(0, ProgressEstimator.EstimateFromMessage(null));
    }

    [Fact]
    public void ProgressEstimator_EmptyString_ReturnsZero()
    {
        Assert.Equal(0, ProgressEstimator.EstimateFromMessage(""));
    }

    [Fact]
    public void ProgressEstimator_WhitespaceOnly_ReturnsZero()
    {
        Assert.Equal(0, ProgressEstimator.EstimateFromMessage("   "));
    }

    [Fact]
    public void ProgressEstimator_UnknownPhase_ReturnsZero()
    {
        Assert.Equal(0, ProgressEstimator.EstimateFromMessage("Processing files..."));
    }

    [Theory]
    [InlineData("[Preflight] Checking roots", 5)]
    [InlineData("[Scan] Found 42 files", 20)]
    [InlineData("[Filter] Excluding junk", 30)]
    [InlineData("[Dedupe] Grouping by key", 45)]
    [InlineData("[Junk] Removing trash", 60)]
    [InlineData("[Move] Moving losers", 75)]
    [InlineData("[Sort] Sorting by console", 85)]
    [InlineData("[Convert] CHD conversion", 92)]
    [InlineData("[Report] Writing report", 97)]
    public void ProgressEstimator_KnownPhases_ReturnsExpectedProgress(string message, int expected)
    {
        Assert.Equal(expected, ProgressEstimator.EstimateFromMessage(message));
    }

    [Theory]
    [InlineData("[PREFLIGHT] uppercase")]
    [InlineData("[preflight] lowercase")]
    [InlineData("[Preflight] mixed")]
    public void ProgressEstimator_CaseInsensitive(string message)
    {
        Assert.Equal(5, ProgressEstimator.EstimateFromMessage(message));
    }

    [Fact]
    public void ProgressEstimator_ProgressMonotonicallyIncreases()
    {
        var phases = new[]
        {
            "[Preflight]", "[Scan]", "[Filter]", "[Dedupe]",
            "[Junk]", "[Move]", "[Sort]", "[Convert]", "[Report]"
        };

        var values = phases.Select(p => ProgressEstimator.EstimateFromMessage(p)).ToArray();

        for (int i = 1; i < values.Length; i++)
        {
            Assert.True(values[i] > values[i - 1],
                $"Phase {phases[i]} progress ({values[i]}) should be > phase {phases[i - 1]} ({values[i - 1]})");
        }
    }

    [Fact]
    public void ProgressEstimator_AllValuesInRange0To100()
    {
        var messages = new[]
        {
            null, "", "   ", "unknown",
            "[Preflight]", "[Scan]", "[Filter]", "[Dedupe]",
            "[Junk]", "[Move]", "[Sort]", "[Convert]", "[Report]"
        };

        foreach (var msg in messages)
        {
            var value = ProgressEstimator.EstimateFromMessage(msg);
            Assert.InRange(value, 0, 100);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // TASK-171: PhasePlanBuilder comprehensive option combinations
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void PhasePlanBuilder_MinimalDryRun_OnlyDedupeAndJunk()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "DryRun",
            EnableDatAudit = false,
            EnableDatRename = false,
            SortConsole = false,
            ConvertFormat = null
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToArray();

        Assert.Equal(["Deduplicate", "JunkRemoval"], names);
    }

    [Fact]
    public void PhasePlanBuilder_MoveMode_NoOptionalFeatures_DedupeJunkMove()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatAudit = false,
            EnableDatRename = false,
            SortConsole = false,
            ConvertFormat = null
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToArray();

        Assert.Equal(["Deduplicate", "JunkRemoval", "Move"], names);
    }

    [Fact]
    public void PhasePlanBuilder_DatAuditOnly_NoDatRename()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatAudit = true,
            EnableDatRename = false,
            SortConsole = false,
            ConvertFormat = null
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToArray();

        Assert.Contains("DatAudit", names);
        Assert.DoesNotContain("DatRename", names);
    }

    [Fact]
    public void PhasePlanBuilder_DatRenameInDryRun_Excluded()
    {
        // DatRename only runs in Move mode
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "DryRun",
            EnableDatAudit = true,
            EnableDatRename = true,
            SortConsole = true,
            ConvertFormat = "chd"
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToArray();

        Assert.DoesNotContain("DatRename", names);
        Assert.DoesNotContain("Move", names);
        Assert.Contains("ConsoleSort", names);
        Assert.DoesNotContain("WinnerConversion", names);
    }

    [Fact]
    public void PhasePlanBuilder_SortConsoleWithoutMove_IncludedForPreviewParity()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "DryRun",
            SortConsole = true
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToArray();

        Assert.Contains("ConsoleSort", names);
    }

    [Fact]
    public void PhasePlanBuilder_ConvertWithoutMove_Excluded()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "DryRun",
            ConvertFormat = "chd"
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToArray();

        Assert.DoesNotContain("WinnerConversion", names);
    }

    [Fact]
    public void PhasePlanBuilder_NullDatAuditAction_OmitsDatAudit()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatAudit = true // enabled but action is null
        };

        var actions = new StandardPhaseStepActions
        {
            DatAudit = null, // not provided
            Deduplicate = (_, _) => PhaseStepResult.Ok(),
            JunkRemoval = (_, _) => PhaseStepResult.Ok(),
            DatRename = null,
            Move = (_, _) => PhaseStepResult.Ok(),
            ConsoleSort = (_, _) => PhaseStepResult.Ok(),
            WinnerConversion = (_, _) => PhaseStepResult.Ok()
        };

        var phases = builder.Build(options, actions);
        var names = phases.Select(p => p.Name).ToArray();

        Assert.DoesNotContain("DatAudit", names);
    }

    [Fact]
    public void PhasePlanBuilder_DeterministicOrder_SameInputsSameOutput()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatAudit = true,
            EnableDatRename = true,
            SortConsole = true,
            ConvertFormat = "chd"
        };

        var actions = CreateNoOpActions();

        var run1 = builder.Build(options, actions).Select(p => p.Name).ToArray();
        var run2 = builder.Build(options, actions).Select(p => p.Name).ToArray();
        var run3 = builder.Build(options, actions).Select(p => p.Name).ToArray();

        Assert.Equal(run1, run2);
        Assert.Equal(run2, run3);
    }

    [Fact]
    public void PhasePlanBuilder_DatAuditAlwaysBeforeDeduplicate()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            EnableDatAudit = true,
            EnableDatRename = true,
            SortConsole = true,
            ConvertFormat = "chd"
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToList();

        var datAuditIdx = names.IndexOf("DatAudit");
        var dedupeIdx = names.IndexOf("Deduplicate");

        Assert.True(datAuditIdx >= 0, "DatAudit should be present");
        Assert.True(datAuditIdx < dedupeIdx, "DatAudit must come before Deduplicate");
    }

    [Fact]
    public void PhasePlanBuilder_MoveBeforeSort_Invariant()
    {
        var builder = new PhasePlanBuilder();
        var options = new RunOptions
        {
            Mode = "Move",
            SortConsole = true,
            ConvertFormat = "chd"
        };

        var phases = builder.Build(options, CreateNoOpActions());
        var names = phases.Select(p => p.Name).ToList();

        var moveIdx = names.IndexOf("Move");
        var sortIdx = names.IndexOf("ConsoleSort");
        var convertIdx = names.IndexOf("WinnerConversion");

        Assert.True(moveIdx < sortIdx, "Move must come before ConsoleSort");
        Assert.True(sortIdx < convertIdx, "ConsoleSort must come before WinnerConversion");
    }

    // ═══════════════════════════════════════════════════════════════════
    // TASK-172: MoveSetAtomically with TempDir Fixtures
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void ConsoleSorter_MoveSetAtomically_MovesAllSetMembers()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            // Create a CUE+BIN set
            var cuePath = Path.Combine(dir, "game.cue");
            var binPath = Path.Combine(dir, "game.bin");
            File.WriteAllText(cuePath, $"FILE \"game.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00");
            File.WriteAllBytes(binPath, new byte[2352]);

            var enrichedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [cuePath] = "PS1",
                [binPath] = "PS1"
            };

            var fs = new Infrastructure.FileSystem.FileSystemAdapter();
            var detector = CreateEmptyDetector();
            var sorter = new ConsoleSorter(fs, detector);

            var result = sorter.Sort(
                roots: [dir],
                extensions: [".cue", ".bin"],
                dryRun: false,
                enrichedConsoleKeys: enrichedKeys);

            Assert.Equal(1, result.Moved);
            Assert.Equal(1, result.SetMembersMoved);

            // Both files should be in PS1 subdirectory
            var expectedDir = Path.Combine(dir, "PS1");
            Assert.True(File.Exists(Path.Combine(expectedDir, "game.cue")), "CUE should be in PS1/");
            Assert.True(File.Exists(Path.Combine(expectedDir, "game.bin")), "BIN should be in PS1/");

            // Original locations should be empty
            Assert.False(File.Exists(cuePath), "Original CUE should be moved");
            Assert.False(File.Exists(binPath), "Original BIN should be moved");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsoleSorter_MoveSetAtomically_DryRun_NoActualMove()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var cuePath = Path.Combine(dir, "game.cue");
            var binPath = Path.Combine(dir, "game.bin");
            File.WriteAllText(cuePath, $"FILE \"game.bin\" BINARY\r\n  TRACK 01 MODE1/2352\r\n    INDEX 01 00:00:00");
            File.WriteAllBytes(binPath, new byte[2352]);

            var enrichedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [cuePath] = "PS1",
                [binPath] = "PS1"
            };

            var fs = new Infrastructure.FileSystem.FileSystemAdapter();
            var detector = CreateEmptyDetector();
            var sorter = new ConsoleSorter(fs, detector);

            var result = sorter.Sort(
                roots: [dir],
                extensions: [".cue", ".bin"],
                dryRun: true,
                enrichedConsoleKeys: enrichedKeys);

            Assert.Equal(1, result.Moved);
            Assert.Equal(1, result.SetMembersMoved);

            // Files should still be in original location
            Assert.True(File.Exists(cuePath), "CUE should remain in place during DryRun");
            Assert.True(File.Exists(binPath), "BIN should remain in place during DryRun");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsoleSorter_StandaloneFile_MovedCorrectly()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var romPath = Path.Combine(dir, "game.chd");
            File.WriteAllBytes(romPath, new byte[64]);

            var enrichedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [romPath] = "SNES"
            };

            var fs = new Infrastructure.FileSystem.FileSystemAdapter();
            var detector = CreateEmptyDetector();
            var sorter = new ConsoleSorter(fs, detector);

            var result = sorter.Sort(
                roots: [dir],
                extensions: [".chd"],
                dryRun: false,
                enrichedConsoleKeys: enrichedKeys);

            Assert.Equal(1, result.Moved);
            Assert.True(File.Exists(Path.Combine(dir, "SNES", "game.chd")));
            Assert.False(File.Exists(romPath));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsoleSorter_AlreadyInCorrectSubfolder_Skipped()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        var subDir = Path.Combine(dir, "SNES");
        Directory.CreateDirectory(subDir);
        try
        {
            var romPath = Path.Combine(subDir, "game.chd");
            File.WriteAllBytes(romPath, new byte[64]);

            var enrichedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [romPath] = "SNES"
            };

            var fs = new Infrastructure.FileSystem.FileSystemAdapter();
            var detector = CreateEmptyDetector();
            var sorter = new ConsoleSorter(fs, detector);

            var result = sorter.Sort(
                roots: [dir],
                extensions: [".chd"],
                dryRun: false,
                enrichedConsoleKeys: enrichedKeys);

            Assert.Equal(0, result.Moved);
            Assert.True(result.Skipped > 0);
            Assert.True(File.Exists(romPath), "File should remain in place");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsoleSorter_UnknownConsole_NotMoved()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var romPath = Path.Combine(dir, "unknown.chd");
            File.WriteAllBytes(romPath, new byte[64]);

            var enrichedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [romPath] = "UNKNOWN"
            };

            var fs = new Infrastructure.FileSystem.FileSystemAdapter();
            var detector = CreateEmptyDetector();
            var sorter = new ConsoleSorter(fs, detector);

            var result = sorter.Sort(
                roots: [dir],
                extensions: [".chd"],
                dryRun: false,
                enrichedConsoleKeys: enrichedKeys);

            Assert.Equal(0, result.Moved);
            Assert.Equal(1, result.Unknown);
            Assert.True(File.Exists(romPath), "Unknown file should not be moved");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsoleSorter_ExcludedFolder_FilesIgnored()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        var trashDir = Path.Combine(dir, "_TRASH_REGION_DEDUPE");
        Directory.CreateDirectory(trashDir);
        try
        {
            var romPath = Path.Combine(trashDir, "game.chd");
            File.WriteAllBytes(romPath, new byte[64]);

            var fs = new Infrastructure.FileSystem.FileSystemAdapter();
            var detector = CreateEmptyDetector();
            var sorter = new ConsoleSorter(fs, detector);

            var result = sorter.Sort(
                roots: [dir],
                extensions: [".chd"],
                dryRun: false);

            Assert.Equal(0, result.Total);
            Assert.True(File.Exists(romPath), "Excluded folder files should not be touched");
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void ConsoleSorter_ReviewDecision_MovesToReviewFolder()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"romulus-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var romPath = Path.Combine(dir, "review-game.chd");
            File.WriteAllBytes(romPath, new byte[64]);

            var enrichedKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [romPath] = "PS1"
            };
            var enrichedDecisions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [romPath] = "Review"
            };

            var fs = new Infrastructure.FileSystem.FileSystemAdapter();
            var detector = CreateEmptyDetector();
            var sorter = new ConsoleSorter(fs, detector);

            var result = sorter.Sort(
                roots: [dir],
                extensions: [".chd"],
                dryRun: false,
                enrichedConsoleKeys: enrichedKeys,
                enrichedSortDecisions: enrichedDecisions);

            Assert.Equal(1, result.Reviewed);
            Assert.True(File.Exists(Path.Combine(dir, "_REVIEW", "PS1", "review-game.chd")));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static StandardPhaseStepActions CreateNoOpActions() => new()
    {
        DatAudit = (_, _) => PhaseStepResult.Ok(),
        Deduplicate = (_, _) => PhaseStepResult.Ok(),
        JunkRemoval = (_, _) => PhaseStepResult.Ok(),
        DatRename = (_, _) => PhaseStepResult.Ok(),
        Move = (_, _) => PhaseStepResult.Ok(),
        ConsoleSort = (_, _) => PhaseStepResult.Ok(),
        WinnerConversion = (_, _) => PhaseStepResult.Ok()
    };

    /// <summary>
    /// Creates a ConsoleDetector with no definitions — always returns UNKNOWN.
    /// Tests use enrichedConsoleKeys to bypass detection.
    /// </summary>
    private static ConsoleDetector CreateEmptyDetector()
    {
        return new ConsoleDetector(Array.Empty<ConsoleInfo>());
    }
}
