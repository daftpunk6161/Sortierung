using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts.Errors;
using Xunit;

namespace Romulus.Tests;

public sealed class ApiReachIntegrationTests : IDisposable
{
    private const string ApiKey = "reach-test-api-key";
    private readonly List<string> _tempDirs = new();

    public void Dispose()
    {
        foreach (var dir in _tempDirs)
        {
            try
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, recursive: true);
            }
            catch
            {
                // best effort
            }
        }
    }

    [Fact]
    public async Task DashboardBootstrap_IsAnonymous_AndReturnsMetadata()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/dashboard/bootstrap");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.GetProperty("requiresApiKey").GetBoolean());
        Assert.Equal("/dashboard/", doc.RootElement.GetProperty("dashboardPath").GetString());
    }

    [Fact]
    public async Task DashboardStaticShell_IsAnonymous()
    {
        using var factory = CreateFactory();
        using var client = factory.CreateClient();

        var response = await client.GetAsync("/dashboard/");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("Headless Control Surface", body);
    }

    [Fact]
    public async Task DashboardSummary_RequiresAuth_AndReturnsTypedPayload()
    {
        using var factory = CreateFactory();
        using var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/dashboard/summary");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("recentRuns", out _));
        Assert.True(doc.RootElement.TryGetProperty("datStatus", out _));
        Assert.True(doc.RootElement.TryGetProperty("trends", out _));
    }

    [Fact]
    public async Task RunCreation_OutsideAllowedRoots_IsRejected_WhenHeadlessAllowlistEnabled()
    {
        var allowedRoot = CreateTempDir("allowed");
        var blockedRoot = CreateTempDir("blocked");
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["AllowRemoteClients"] = "true",
            ["PublicBaseUrl"] = "https://romulus.example",
            ["AllowedRoots:0"] = allowedRoot
        });
        using var client = CreateAuthClient(factory);

        using var content = new StringContent(
            JsonSerializer.Serialize(new { roots = new[] { blockedRoot }, mode = "DryRun" }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/runs", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SecurityErrorCodes.OutsideAllowedRoots, body);
    }

    [Fact]
    public async Task Convert_OutsideAllowedRoots_IsRejected_WhenHeadlessAllowlistEnabled()
    {
        var allowedRoot = CreateTempDir("allowed");
        var blockedRoot = CreateTempDir("blocked");
        var inputPath = Path.Combine(blockedRoot, "game.iso");
        File.WriteAllText(inputPath, "demo");

        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["AllowRemoteClients"] = "true",
            ["PublicBaseUrl"] = "https://romulus.example",
            ["AllowedRoots:0"] = allowedRoot
        });
        using var client = CreateAuthClient(factory);

        using var content = new StringContent(
            JsonSerializer.Serialize(new { input = inputPath }),
            Encoding.UTF8,
            "application/json");

        var response = await client.PostAsync("/convert", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains(SecurityErrorCodes.OutsideAllowedRoots, body);
    }

    [Fact]
    public void RemoteMode_WithoutHttpsPublicBaseUrl_IsRejectedAtStartup()
    {
        using var factory = CreateFactory(new Dictionary<string, string?>
        {
            ["AllowRemoteClients"] = "true",
            ["PublicBaseUrl"] = "http://romulus.example",
            ["AllowedRoots:0"] = CreateTempDir("allowed")
        });

        Assert.Throws<InvalidOperationException>(() => factory.CreateClient());
    }

    private WebApplicationFactory<Program> CreateFactory(IDictionary<string, string?>? settings = null)
    {
        var merged = new Dictionary<string, string?>
        {
            ["ApiKey"] = ApiKey
        };

        if (settings is not null)
        {
            foreach (var pair in settings)
                merged[pair.Key] = pair.Value;
        }

        return ApiTestFactory.Create(merged);
    }

    private HttpClient CreateAuthClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        return client;
    }

    private string CreateTempDir(string name)
    {
        var path = Path.Combine(Path.GetTempPath(), $"Romulus_ApiReach_{name}_{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        _tempDirs.Add(path);
        return path;
    }
}
