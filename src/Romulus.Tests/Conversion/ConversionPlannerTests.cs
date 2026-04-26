using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Core.Conversion;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ConversionPlannerTests
{
    [Fact]
    public void Plan_UnknownConsole_IsBlocked()
    {
        var planner = CreatePlanner(CreateRegistry([], ConversionPolicy.Auto, ".chd"), _ => "C:\\tools\\x.exe");
        var plan = planner.Plan("C:\\roms\\game.iso", "UNKNOWN", ".iso");

        Assert.Equal(ConversionSafety.Blocked, plan.Safety);
        Assert.Equal("unknown-console", plan.SkipReason);
    }

    [Fact]
    public void Plan_NonePolicy_IsBlocked()
    {
        var planner = CreatePlanner(CreateRegistry([], ConversionPolicy.None, ".chd"), _ => "C:\\tools\\x.exe");
        var plan = planner.Plan("C:\\roms\\game.iso", "PS1", ".iso");

        Assert.Equal(ConversionSafety.Blocked, plan.Safety);
        Assert.Contains("policy-none", plan.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_AlreadyTarget_Skips()
    {
        var planner = CreatePlanner(CreateRegistry([], ConversionPolicy.Auto, ".chd"), _ => "C:\\tools\\x.exe");
        var plan = planner.Plan("C:\\roms\\game.chd", "PS1", ".chd");

        Assert.Equal("already-target-format", plan.SkipReason);
        Assert.Empty(plan.Steps);
    }

    [Fact]
    public void Plan_MultiStep_ProducesOrderedSteps()
    {
        var registry = CreateRegistry(
        [
            Cap(".cso", ".iso", "ciso", 3),
            Cap(".iso", ".chd", "chdman", 1)
        ],
        ConversionPolicy.Auto,
        ".chd");

        var planner = CreatePlanner(registry, _ => "C:\\tools\\ok.exe");
        var plan = planner.Plan("C:\\roms\\game.cso", "PSP", ".cso");

        Assert.Null(plan.SkipReason);
        Assert.Equal(2, plan.Steps.Count);
        Assert.True(plan.Steps[0].IsIntermediate);
        Assert.False(plan.Steps[1].IsIntermediate);
        Assert.Equal(".iso", plan.Steps[0].OutputExtension);
        Assert.Equal(".chd", plan.Steps[1].OutputExtension);
    }

    [Fact]
    public void Plan_MissingTool_SkipsWithReason()
    {
        var registry = CreateRegistry([Cap(".iso", ".chd", "chdman", 1)], ConversionPolicy.Auto, ".chd");
        var planner = CreatePlanner(registry, _ => null);

        var plan = planner.Plan("C:\\roms\\game.iso", "PS1", ".iso");

        Assert.Empty(plan.Steps);
        Assert.Equal(ConversionSafety.Blocked, plan.Safety);
        Assert.Contains("tool-not-found", plan.SkipReason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Plan_UsesAlternativeTarget_WhenPreferredUnavailable()
    {
        var registry = CreateRegistry(
            [Cap(".iso", ".zip", "7z", 1)],
            ConversionPolicy.Auto,
            ".chd",
            [".zip"]);
        var planner = CreatePlanner(registry, _ => "C:\\tools\\ok.exe");

        var plan = planner.Plan("C:\\roms\\game.iso", "PS1", ".iso");

        Assert.Single(plan.Steps);
        Assert.Equal(".zip", plan.Steps[0].OutputExtension);
    }

    [Fact]
    public void Plan_Ps2CdDetectorOverridesLargeFileSize_SelectsCreatecd()
    {
        var registry = CreateRegistry(
        [
            Cap(".iso", ".chd", "chdman", 0, "createcd", "PS2", ConversionCondition.FileSizeLessThan700MB),
            Cap(".iso", ".chd", "chdman", 0, "createdvd", "PS2", ConversionCondition.FileSizeGreaterEqual700MB)
        ],
        ConversionPolicy.Auto,
        ".chd");

        var planner = CreatePlanner(
            registry,
            _ => "C:\\tools\\ok.exe",
            _ => ConversionThresholds.CdImageThresholdBytes + 1,
            _ => true);

        var plan = planner.Plan("C:\\roms\\game.iso", "PS2", ".iso");

        Assert.Single(plan.Steps);
        Assert.Equal("createcd", plan.Steps[0].Capability.Command);
    }

    [Fact]
    public void Plan_Ps2DvdDetectorOverridesSmallFileSize_SelectsCreatedvd()
    {
        var registry = CreateRegistry(
        [
            Cap(".iso", ".chd", "chdman", 0, "createcd", "PS2", ConversionCondition.FileSizeLessThan700MB),
            Cap(".iso", ".chd", "chdman", 0, "createdvd", "PS2", ConversionCondition.FileSizeGreaterEqual700MB)
        ],
        ConversionPolicy.Auto,
        ".chd");

        var planner = CreatePlanner(
            registry,
            _ => "C:\\tools\\ok.exe",
            _ => ConversionThresholds.CdImageThresholdBytes - 1,
            _ => false);

        var plan = planner.Plan("C:\\roms\\game.iso", "PS2", ".iso");

        Assert.Single(plan.Steps);
        Assert.Equal("createdvd", plan.Steps[0].Capability.Command);
    }

    [Fact]
    public void Plan_EncryptedPbp_IsBlockedBeforePathSelection()
    {
        var registry = CreateRegistry(
        [
            Cap(".pbp", ".chd", "psxtract", 1, "pbp2chd", "PSP")
        ],
        ConversionPolicy.Auto,
        ".chd");
        var planner = new ConversionPlanner(
            registry,
            _ => "C:\\tools\\psxtract.exe",
            _ => 1024,
            encryptedPbpDetector: _ => true);

        var plan = planner.Plan("C:\\roms\\encrypted.pbp", "PSP", ".pbp");

        Assert.Equal(ConversionSafety.Blocked, plan.Safety);
        Assert.Equal("encrypted-pbp", plan.SkipReason);
        Assert.Empty(plan.Steps);
    }

    private static ConversionPlanner CreatePlanner(IConversionRegistry registry, Func<string, string?> toolFinder)
    {
        return new ConversionPlanner(registry, toolFinder, _ => 1024);
    }

    private static ConversionPlanner CreatePlanner(
        IConversionRegistry registry,
        Func<string, string?> toolFinder,
        Func<string, long> fileSizeProvider,
        Func<string, bool?>? ps2CdDetector = null)
    {
        return new ConversionPlanner(registry, toolFinder, fileSizeProvider, ps2CdDetector: ps2CdDetector);
    }

    private static IConversionRegistry CreateRegistry(
        IReadOnlyList<ConversionCapability> capabilities,
        ConversionPolicy policy,
        string? preferredTarget,
        IReadOnlyList<string>? alternativeTargets = null)
    {
        return new FakeRegistry(capabilities, policy, preferredTarget, alternativeTargets ?? []);
    }

    private static ConversionCapability Cap(
        string source,
        string target,
        string tool,
        int cost,
        string command = "convert",
        string consoleKey = "PS1",
        ConversionCondition condition = ConversionCondition.None)
    {
        return new ConversionCapability
        {
            SourceExtension = source,
            TargetExtension = target,
            Tool = new ToolRequirement { ToolName = tool },
            Command = command,
            ApplicableConsoles = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { consoleKey, "PSP" },
            RequiredSourceIntegrity = null,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = cost,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = condition
        };
    }

    private sealed class FakeRegistry(
        IReadOnlyList<ConversionCapability> capabilities,
        ConversionPolicy policy,
        string? preferredTarget,
        IReadOnlyList<string> alternatives) : IConversionRegistry
    {
        public IReadOnlyList<ConversionCapability> GetCapabilities() => capabilities;
        public ConversionPolicy GetPolicy(string consoleKey) => policy;
        public string? GetPreferredTarget(string consoleKey) => preferredTarget;
        public IReadOnlyList<string> GetAlternativeTargets(string consoleKey) => alternatives;
    }
}
