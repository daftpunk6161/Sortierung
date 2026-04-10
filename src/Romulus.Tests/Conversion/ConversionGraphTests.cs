using Romulus.Contracts.Models;
using Romulus.Core.Conversion;
using Xunit;

namespace Romulus.Tests.Conversion;

public sealed class ConversionGraphTests
{
    [Fact]
    public void FindPath_PicksCheapestPath()
    {
        var graph = new ConversionGraph([
            Edge(".cso", ".iso", "ciso", 3),
            Edge(".iso", ".chd", "chdman", 2),
            Edge(".cso", ".chd", "chdman", 20)
        ]);

        var path = graph.FindPath(".cso", ".chd", "PSP", _ => true);

        Assert.NotNull(path);
        Assert.Equal(2, path!.Count);
        Assert.Equal(".iso", path[0].TargetExtension);
        Assert.Equal(".chd", path[1].TargetExtension);
    }

    [Fact]
    public void FindPath_ReturnsNull_WhenNoPathExists()
    {
        var graph = new ConversionGraph([Edge(".iso", ".chd", "chdman", 1)]);
        var path = graph.FindPath(".wbfs", ".chd", "WII", _ => true);
        Assert.Null(path);
    }

    [Fact]
    public void FindPath_RespectsConsoleFilter()
    {
        var onlyPs1 = Edge(".cue", ".chd", "chdman", 1, applicableConsoles: new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "PS1" });
        var graph = new ConversionGraph([onlyPs1]);

        Assert.NotNull(graph.FindPath(".cue", ".chd", "PS1", _ => true));
        Assert.Null(graph.FindPath(".cue", ".chd", "SAT", _ => true));
    }

    [Fact]
    public void FindPath_RespectsConditionEvaluator()
    {
        var edge = Edge(".iso", ".chd", "chdman", 1, condition: ConversionCondition.FileSizeGreaterEqual700MB);
        var graph = new ConversionGraph([edge]);

        Assert.Null(graph.FindPath(".iso", ".chd", "PS2", c => c != ConversionCondition.FileSizeGreaterEqual700MB));
        Assert.NotNull(graph.FindPath(".iso", ".chd", "PS2", _ => true));
    }

    [Fact]
    public void FindPath_RespectsSourceIntegrityRequirement()
    {
        var edge = Edge(".cso", ".iso", "ciso", 1, requiredIntegrity: SourceIntegrity.Lossy);
        var graph = new ConversionGraph([edge]);

        Assert.NotNull(graph.FindPath(".cso", ".iso", "PSP", _ => true, SourceIntegrity.Lossy));
        Assert.Null(graph.FindPath(".cso", ".iso", "PSP", _ => true, SourceIntegrity.Lossless));
    }

    [Fact]
    public void FindPath_SourceEqualsTarget_ReturnsEmptyPath()
    {
        var graph = new ConversionGraph([Edge(".iso", ".chd", "chdman", 1)]);
        var path = graph.FindPath(".chd", ".chd", "PS1", _ => true);

        Assert.NotNull(path);
        Assert.Empty(path!);
    }

    [Fact]
    public void FindPath_EqualCostAlternativePaths_IsDeterministic()
    {
        var graph = new ConversionGraph([
            Edge(".cso", ".iso", "b-tool", 5),
            Edge(".cso", ".cue", "a-tool", 5),
            Edge(".iso", ".chd", "z-tool", 5),
            Edge(".cue", ".chd", "z-tool", 5)
        ]);

        var firstPath = graph.FindPath(".cso", ".chd", "PSP", _ => true);
        Assert.NotNull(firstPath);

        for (var i = 0; i < 20; i++)
        {
            var nextPath = graph.FindPath(".cso", ".chd", "PSP", _ => true);
            Assert.NotNull(nextPath);
            Assert.Equal(firstPath!.Count, nextPath!.Count);
            Assert.Equal(firstPath[0].TargetExtension, nextPath[0].TargetExtension);
            Assert.Equal(firstPath[0].Tool.ToolName, nextPath[0].Tool.ToolName);
            Assert.Equal(firstPath[1].TargetExtension, nextPath[1].TargetExtension);
        }
    }

    private static ConversionCapability Edge(
        string source,
        string target,
        string tool,
        int cost,
        IReadOnlySet<string>? applicableConsoles = null,
        ConversionCondition condition = ConversionCondition.None,
        SourceIntegrity? requiredIntegrity = null)
    {
        return new ConversionCapability
        {
            SourceExtension = source,
            TargetExtension = target,
            Tool = new ToolRequirement { ToolName = tool },
            Command = "convert",
            ApplicableConsoles = applicableConsoles,
            RequiredSourceIntegrity = requiredIntegrity,
            ResultIntegrity = SourceIntegrity.Lossless,
            Lossless = true,
            Cost = cost,
            Verification = VerificationMethod.FileExistenceCheck,
            Condition = condition
        };
    }
}
