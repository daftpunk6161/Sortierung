using Romulus.Core.Classification;
using Romulus.Tests.Benchmark.Generators;
using Romulus.Tests.Benchmark.Infrastructure;
using Xunit;

namespace Romulus.Tests.Benchmark;

public sealed class HoldoutExpanderTests
{
    [Theory]
    [InlineData("GB", "gb-header", "dmg")]
    [InlineData("GBC", "gb-header", "cgb-dual")]
    [InlineData("MD", "md-header", "megadrive")]
    [InlineData("32X", "md-header", "32x")]
    [InlineData("SAT", "sega-ipbin", "saturn")]
    [InlineData("DC", "sega-ipbin", "dreamcast")]
    [InlineData("SCD", "sega-ipbin", "segacd")]
    [InlineData("A78", "a7800-header", "standard")]
    public void GetStubForSystem_UsesCanonicalVariant(string system, string generator, string variant)
    {
        var stub = HoldoutExpander.GetStubForSystem(system);

        Assert.NotNull(stub);
        Assert.Equal(generator, stub!.Generator);
        Assert.Equal(variant, stub.Variant);
    }

    [Fact]
    public void StubGeneratorDispatch_A78CartridgeHeader_GeneratesDetectableStub()
    {
        var entry = new Models.GroundTruthEntry
        {
            Id = "test-a78",
            Source = new Models.SourceInfo
            {
                FileName = "Test Game.a78",
                Extension = ".a78",
                SizeBytes = 1024,
                Directory = "a78"
            },
            Tags = [],
            Difficulty = "easy",
            Expected = new Models.ExpectedResult
            {
                ConsoleKey = "A78",
                Category = "Game"
            },
            DetectionExpectations = new Models.DetectionExpectations
            {
                PrimaryMethod = "CartridgeHeader"
            }
        };

        var dispatch = new StubGeneratorDispatch();
        var bytes = dispatch.GenerateStub(entry);

        var io = new TestClassificationIo
        {
            FileExistsFunc = _ => true,
            FileLengthFunc = _ => bytes.Length,
            OpenReadFunc = _ => new MemoryStream(bytes)
        };

        var detector = new CartridgeHeaderDetector(classificationIo: io);
        Assert.Equal("A78", detector.Detect("test.a78"));
    }
}
