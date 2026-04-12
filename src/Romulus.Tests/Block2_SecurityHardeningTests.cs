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
        File.WriteAllBytes(target, new byte[1024]);

        var runner = new RequirementCapturingToolRunner();
        var sut = new ChdmanToolConverter(runner);

        var result = sut.Convert(source, target, Path.Combine(_tempDir, "chdman.exe"), "createcd");

        Assert.Equal(ConversionOutcome.Success, result.Outcome);
        Assert.NotNull(runner.LastRequirement);
        Assert.Equal("chdman", runner.LastRequirement!.ToolName);
    }

    [Fact]
    public void R6_06_ToolHashes_MustContain_AllReferencedConversionTools()
    {
        var hashesPath = FindRepoFile("data", "tool-hashes.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(hashesPath));
        var tools = doc.RootElement.GetProperty("Tools");

        Assert.True(tools.TryGetProperty("unecm.exe", out _), "tool-hashes.json must pin unecm.exe");
        Assert.True(tools.TryGetProperty("flips.exe", out _), "tool-hashes.json must pin flips.exe");
        Assert.True(tools.TryGetProperty("xdelta3.exe", out _), "tool-hashes.json must pin xdelta3.exe");
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
    {
        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var repoRoot = Directory.GetParent(dataDir)?.FullName
            ?? throw new InvalidOperationException("Repository root could not be resolved from data directory.");
        return Path.Combine(new[] { repoRoot }.Concat(parts).ToArray());
    }

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
