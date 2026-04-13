using System.Text.Json;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// F5: Tool-Hash-Korrektheits-Tests — validates the structural integrity of data/tool-hashes.json.
/// Ensures hash values are valid SHA256 hex strings, not markers/placeholders,
/// and that the schema version and structure are correct.
/// </summary>
public sealed class ToolHashCorrectnessTests
{
    private static readonly string ToolHashesPath = Path.Combine(
        AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "..", "data", "tool-hashes.json");

    [Fact]
    public void ToolHashesFile_Exists()
    {
        Assert.True(File.Exists(ToolHashesPath), $"tool-hashes.json not found at {Path.GetFullPath(ToolHashesPath)}");
    }

    [Fact]
    public void ToolHashesFile_IsValidJson()
    {
        var json = File.ReadAllText(ToolHashesPath);
        var ex = Record.Exception(() => JsonDocument.Parse(json));
        Assert.Null(ex);
    }

    [Fact]
    public void ToolHashesFile_HasSchemaVersion()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ToolHashesPath));
        Assert.True(doc.RootElement.TryGetProperty("schemaVersion", out var sv));
        Assert.Equal("tool-hashes-v1", sv.GetString());
    }

    [Fact]
    public void ToolHashesFile_HasToolsSection()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ToolHashesPath));
        Assert.True(doc.RootElement.TryGetProperty("Tools", out var tools));
        Assert.Equal(JsonValueKind.Object, tools.ValueKind);
    }

    [Fact]
    public void ToolHashesFile_AllHashesAre64CharHex()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ToolHashesPath));
        var tools = doc.RootElement.GetProperty("Tools");

        foreach (var prop in tools.EnumerateObject())
        {
            var hash = prop.Value.GetString();
            Assert.NotNull(hash);
            Assert.Equal(64, hash!.Length);
            Assert.True(IsHexString(hash), $"Tool '{prop.Name}' has non-hex hash: {hash}");
        }
    }

    [Fact]
    public void ToolHashesFile_NoPlaceholderOrPendingHashes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ToolHashesPath));
        var tools = doc.RootElement.GetProperty("Tools");

        foreach (var prop in tools.EnumerateObject())
        {
            var hash = prop.Value.GetString()!;
            Assert.False(hash.StartsWith("PLACEHOLDER", StringComparison.OrdinalIgnoreCase),
                $"Tool '{prop.Name}' has PLACEHOLDER marker hash");
            Assert.False(hash.StartsWith("PENDING", StringComparison.OrdinalIgnoreCase),
                $"Tool '{prop.Name}' has PENDING marker hash");
        }
    }

    [Fact]
    public void ToolHashesFile_AllToolNamesEndWithExe()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ToolHashesPath));
        var tools = doc.RootElement.GetProperty("Tools");

        foreach (var prop in tools.EnumerateObject())
        {
            Assert.EndsWith(".exe", prop.Name, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void ToolHashesFile_NoDuplicateHashes()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ToolHashesPath));
        var tools = doc.RootElement.GetProperty("Tools");

        var hashes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in tools.EnumerateObject())
        {
            var hash = prop.Value.GetString()!;
            Assert.True(hashes.Add(hash),
                $"Duplicate hash found for tool '{prop.Name}': {hash}");
        }
    }

    [Theory]
    [InlineData("7z.exe")]
    [InlineData("chdman.exe")]
    public void ToolHashesFile_ContainsExpectedCoreTools(string toolName)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ToolHashesPath));
        var tools = doc.RootElement.GetProperty("Tools");
        Assert.True(tools.TryGetProperty(toolName, out _),
            $"Expected core tool '{toolName}' not found in tool-hashes.json");
    }

    private static bool IsHexString(string s)
    {
        foreach (var c in s)
        {
            if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F')))
                return false;
        }
        return true;
    }
}
