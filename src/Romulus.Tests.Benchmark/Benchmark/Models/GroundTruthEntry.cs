using System.Text.Json.Serialization;

namespace Romulus.Tests.Benchmark.Models;

/// <summary>
/// Single ground-truth JSONL entry. Immutable record matching ground-truth.schema.json.
/// </summary>
public sealed record GroundTruthEntry
{
    [JsonPropertyName("id")]
    public required string Id { get; init; }

    [JsonPropertyName("source")]
    public required SourceInfo Source { get; init; }

    [JsonPropertyName("tags")]
    public required string[] Tags { get; init; }

    [JsonPropertyName("difficulty")]
    public required string Difficulty { get; init; }

    [JsonPropertyName("expected")]
    public required ExpectedResult Expected { get; init; }

    [JsonPropertyName("detectionExpectations")]
    public DetectionExpectations? DetectionExpectations { get; init; }

    [JsonPropertyName("fileModel")]
    public FileModelInfo? FileModel { get; init; }

    [JsonPropertyName("relationships")]
    public RelationshipInfo? Relationships { get; init; }

    [JsonPropertyName("notes")]
    public string? Notes { get; init; }

    [JsonPropertyName("schemaVersion")]
    public string? SchemaVersion { get; init; }

    [JsonPropertyName("addedInVersion")]
    public string? AddedInVersion { get; init; }

    [JsonPropertyName("lastVerified")]
    public string? LastVerified { get; init; }
}

public sealed record SourceInfo
{
    [JsonPropertyName("fileName")]
    public required string FileName { get; init; }

    [JsonPropertyName("extension")]
    public required string Extension { get; init; }

    [JsonPropertyName("sizeBytes")]
    public required long SizeBytes { get; init; }

    [JsonPropertyName("directory")]
    public string? Directory { get; init; }

    [JsonPropertyName("stub")]
    public StubInfo? Stub { get; init; }

    [JsonPropertyName("innerFiles")]
    public InnerFileInfo[]? InnerFiles { get; init; }
}

public sealed record StubInfo
{
    [JsonPropertyName("generator")]
    public required string Generator { get; init; }

    [JsonPropertyName("variant")]
    public string? Variant { get; init; }

    [JsonPropertyName("params")]
    public Dictionary<string, object>? Params { get; init; }
}

public sealed record InnerFileInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }

    [JsonPropertyName("sizeBytes")]
    public long? SizeBytes { get; init; }

    [JsonPropertyName("crc32")]
    public string? Crc32 { get; init; }
}

public sealed record ExpectedResult
{
    [JsonPropertyName("consoleKey")]
    public string? ConsoleKey { get; init; }

    [JsonPropertyName("category")]
    public required string Category { get; init; }

    [JsonPropertyName("confidence")]
    public int? Confidence { get; init; }

    [JsonPropertyName("hasConflict")]
    public bool HasConflict { get; init; }

    [JsonPropertyName("datMatchLevel")]
    public string? DatMatchLevel { get; init; }

    [JsonPropertyName("datEcosystem")]
    public string? DatEcosystem { get; init; }

    [JsonPropertyName("sortDecision")]
    public string? SortDecision { get; init; }

    [JsonPropertyName("gameIdentity")]
    public string? GameIdentity { get; init; }

    [JsonPropertyName("discNumber")]
    public int? DiscNumber { get; init; }

    [JsonPropertyName("repairSafe")]
    public bool? RepairSafe { get; init; }
}

public sealed record DetectionExpectations
{
    [JsonPropertyName("primaryMethod")]
    public string? PrimaryMethod { get; init; }

    [JsonPropertyName("acceptableAlternatives")]
    public string[]? AcceptableAlternatives { get; init; }

    [JsonPropertyName("acceptableConsoleKeys")]
    public string[]? AcceptableConsoleKeys { get; init; }
}

public sealed record FileModelInfo
{
    [JsonPropertyName("type")]
    public string? Type { get; init; }

    [JsonPropertyName("setFiles")]
    public string[]? SetFiles { get; init; }

    [JsonPropertyName("discCount")]
    public int? DiscCount { get; init; }
}

public sealed record RelationshipInfo
{
    [JsonPropertyName("cloneOf")]
    public string? CloneOf { get; init; }

    [JsonPropertyName("biosSystemKeys")]
    public string[]? BiosSystemKeys { get; init; }

    [JsonPropertyName("parentSet")]
    public string? ParentSet { get; init; }
}
