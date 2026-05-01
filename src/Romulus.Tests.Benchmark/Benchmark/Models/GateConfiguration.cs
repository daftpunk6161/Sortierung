using System.Text.Json.Serialization;

namespace Romulus.Tests.Benchmark.Models;

/// <summary>
/// Gate threshold configuration loaded from benchmark/gates.json.
/// </summary>
public sealed record GateConfiguration
{
    [JsonPropertyName("s1")]
    public required S1Gate S1 { get; init; }
}

public sealed record S1Gate
{
    [JsonPropertyName("totalEntries")]
    public required GateThreshold TotalEntries { get; init; }

    [JsonPropertyName("systemsCovered")]
    public required GateThreshold SystemsCovered { get; init; }

    [JsonPropertyName("fallklassenCovered")]
    public required GateThreshold FallklassenCovered { get; init; }

    [JsonPropertyName("platformFamily")]
    public required Dictionary<string, GateThreshold> PlatformFamily { get; init; }

    [JsonPropertyName("tierDepth")]
    public required Dictionary<string, TierThreshold> TierDepth { get; init; }

    [JsonPropertyName("caseClasses")]
    public required Dictionary<string, CaseClassThreshold> CaseClasses { get; init; }

    [JsonPropertyName("specialAreas")]
    public required Dictionary<string, GateThreshold> SpecialAreas { get; init; }

    [JsonPropertyName("difficultyDistribution")]
    public DifficultyDistributionGate? DifficultyDistribution { get; init; }
}

public sealed record DifficultyDistributionGate
{
    [JsonPropertyName("easyMax")]
    public required RatioThreshold EasyMax { get; init; }

    [JsonPropertyName("mediumMin")]
    public required RatioThreshold MediumMin { get; init; }

    [JsonPropertyName("hardMin")]
    public required RatioThreshold HardMin { get; init; }

    [JsonPropertyName("adversarialMin")]
    public required RatioThreshold AdversarialMin { get; init; }
}

public sealed record RatioThreshold
{
    [JsonPropertyName("target")]
    public required double Target { get; init; }

    [JsonPropertyName("hardFail")]
    public required double HardFail { get; init; }
}

public sealed record GateThreshold
{
    [JsonPropertyName("target")]
    public required int Target { get; init; }

    [JsonPropertyName("hardFail")]
    public required int HardFail { get; init; }
}

public sealed record TierThreshold
{
    [JsonPropertyName("minPerSystem")]
    public required int MinPerSystem { get; init; }

    [JsonPropertyName("hardFail")]
    public required int HardFail { get; init; }

    [JsonPropertyName("systems")]
    public string[]? Systems { get; init; }
}

public sealed record CaseClassThreshold
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("target")]
    public required int Target { get; init; }

    [JsonPropertyName("hardFail")]
    public required int HardFail { get; init; }
}
