using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Conversion;
using Romulus.Infrastructure.Orchestration;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Block 2 – Security hardening tests
/// R6-05: ChdmanToolConverter must pass ToolRequirement into tool execution
/// R6-06: tool-hashes.json must include all referenced conversion tools
/// R6-03: RunService must not construct unsigned AuditCsvStore fallback
/// R7-08: dashboard static shell must not bypass API-key middleware
/// </summary>
public sealed class Block2_SecurityHardeningTests : IDisposable
{
    private const string ApiKey = "block2-test-key";
    private readonly string _tempDir;

    public Block2_SecurityHardeningTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_Block2_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    [Fact]
    public void R6_05_ChdmanToolConverter_MustPassToolRequirement_ToToolRunner()
    {
        var source = Path.Combine(_tempDir, "game.iso");
        var target = Path.Combine(_tempDir, "game.chd");
        File.WriteAllBytes(source, new byte[1024 * 1024]);
        WriteValidChd(target);

        var runner = new RequirementCapturingToolRunner();
        var sut = new ChdmanToolConverter(runner);

        var result = sut.Convert(source, target, Path.Combine(_tempDir, "chdman.exe"), "createcd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.NotNull(runner.LastRequirement);
        Assert.Equal("chdman", runner.LastRequirement!.ToolName);
    }

    [Fact]
    public void R6_05_ChdmanToolConverter_AllInvokeProcessCalls_MustUseRequirementOverload()
    {
        // Ensure no InvokeProcess call in ChdmanToolConverter uses the 3-arg overload (no ToolRequirement).
        // Every call must go through the 6-arg overload with ChdmanRequirement or SevenZipRequirement.
        var sourcePath = FindRepoFile("src", "Romulus.Infrastructure", "Conversion", "ChdmanToolConverter.cs");
        var source = File.ReadAllText(sourcePath);
        var lines = File.ReadAllLines(sourcePath);

        var violations = new List<string>();
        var searchIndex = 0;
        while (true)
        {
            var invokeIndex = source.IndexOf("InvokeProcess(", searchIndex, StringComparison.Ordinal);
            if (invokeIndex < 0)
                break;

            var closeIndex = source.IndexOf(");", invokeIndex, StringComparison.Ordinal);
            if (closeIndex < 0)
                closeIndex = Math.Min(source.Length - 1, invokeIndex + 400);

            var callBlock = source.Substring(invokeIndex, closeIndex - invokeIndex + 1);

            // Every InvokeProcess call in this converter must reference a concrete ToolRequirement marker.
            if (!callBlock.Contains("Requirement", StringComparison.Ordinal))
            {
                var lineNo = 1 + source[..invokeIndex].Count(ch => ch == '\n');
                var lineText = lines[Math.Max(0, lineNo - 1)].Trim();
                violations.Add($"Line {lineNo}: {lineText}");
            }

            searchIndex = invokeIndex + "InvokeProcess(".Length;
        }

        Assert.True(violations.Count == 0,
            $"ChdmanToolConverter has InvokeProcess call(s) without ToolRequirement:\n{string.Join("\n", violations)}");
    }

    [Fact]
    public void R6_06_ToolHashes_MustContain_AllReferencedConversionTools()
    {
        var hashesPath = FindRepoFile("data", "tool-hashes.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(hashesPath));
        var tools = doc.RootElement.GetProperty("Tools");

        Assert.True(tools.TryGetProperty("7z.exe", out _), "tool-hashes.json must pin 7z.exe");
        Assert.True(tools.TryGetProperty("psxtract.exe", out _), "tool-hashes.json must pin psxtract.exe");
        Assert.True(tools.TryGetProperty("chdman.exe", out _), "tool-hashes.json must pin chdman.exe");
        Assert.True(tools.TryGetProperty("dolphintool.exe", out _), "tool-hashes.json must pin dolphintool.exe");
        Assert.True(tools.TryGetProperty("ciso.exe", out _), "tool-hashes.json must pin ciso.exe");
        Assert.True(tools.TryGetProperty("nkitprocessingapp.exe", out _), "tool-hashes.json must pin nkitprocessingapp.exe");

        Assert.False(tools.TryGetProperty("unecm.exe", out _), "unecm.exe must stay disabled until a verified production hash is available.");
        Assert.False(tools.TryGetProperty("flips.exe", out _), "flips.exe must stay disabled until a verified production hash is available.");
        Assert.False(tools.TryGetProperty("xdelta3.exe", out _), "xdelta3.exe must stay disabled until a verified production hash is available.");
    }

    private static void WriteValidChd(string path)
    {
        var bytes = new byte[1024];
        "MComprHD"u8.CopyTo(bytes);
        File.WriteAllBytes(path, bytes);
    }

    [Fact]
    public void R6_03_RunService_MustNotConstruct_UnsignedAuditStoreFallback()
    {
        var sourcePath = FindRepoFile("src", "Romulus.UI.Wpf", "Services", "RunService.cs");
        var source = File.ReadAllText(sourcePath);

        Assert.DoesNotContain("new AuditCsvStore()", source);
    }

    [Fact]
    public async Task R7_08_DashboardStaticShell_RequiresApiKey()
    {
        await using var factory = CreateFactory();
        using var anonymousClient = factory.CreateClient();

        var response = await anonymousClient.GetAsync("/dashboard/");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static WebApplicationFactory<Program> CreateFactory()
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = ApiKey,
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60"
        };

        return ApiTestFactory.Create(settings);
    }

    private static string FindRepoFile(params string[] parts)
        => Romulus.Tests.TestFixtures.RepoPaths.RepoFile(parts);

    private sealed class RequirementCapturingToolRunner : IToolRunner
    {
        public ToolRequirement? LastRequirement { get; private set; }

        public string? FindTool(string toolName) => null;

        public ToolResult InvokeProcess(string filePath, string[] arguments, string? errorLabel = null)
            => new(0, string.Empty, true);

        public ToolResult InvokeProcess(
            string filePath,
            string[] arguments,
            ToolRequirement? requirement,
            string? errorLabel,
            TimeSpan? timeout,
            CancellationToken cancellationToken)
        {
            LastRequirement = requirement;
            return new ToolResult(0, string.Empty, true);
        }

        public ToolResult Invoke7z(string sevenZipPath, string[] arguments) => new(0, string.Empty, true);
    }
}
