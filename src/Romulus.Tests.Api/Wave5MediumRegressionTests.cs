using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Xunit;

namespace Romulus.Tests;

public sealed class Wave5MediumRegressionTests : IDisposable
{
    private const string ApiKey = "wave5-medium-test-key";
    private readonly List<string> _tempRoots = [];

    public void Dispose()
    {
        foreach (var root in _tempRoots)
        {
            try
            {
                if (Directory.Exists(root))
                    Directory.Delete(root, recursive: true);
            }
            catch
            {
                // Best effort cleanup.
            }
        }
    }

    [Fact]
    public async Task W5_ApiRunPost_DeduplicatesRoots_CaseInsensitive_BeforeRunCreation()
    {
        var root = CreateTempRoot();
        string[]? capturedRoots = null;

        using var factory = CreateFactory(executor: (run, _, _, _) =>
        {
            capturedRoots = run.Roots;
            return new RunExecutionOutcome(RunConstants.StatusCompleted, new ApiRunResult
            {
                OrchestratorStatus = RunConstants.StatusOk,
                ExitCode = 0
            });
        });
        using var client = CreateClientWithApiKey(factory);

        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { root, root.ToUpperInvariant() },
            mode = "DryRun"
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(capturedRoots);
        Assert.Single(capturedRoots!);
    }

    [Fact]
    public void W5_DatIndex_Add_WhenCapacityReached_TracksDroppedCount()
    {
        var index = new DatIndex
        {
            MaxEntriesPerConsole = 1
        };

        index.Add("SNES", "hash-1", "Game A");
        index.Add("SNES", "hash-2", "Game B");

        Assert.Equal(1, index.TotalEntries);
        Assert.Equal(1, index.DroppedByCapacityLimit);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null)
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = ApiKey,
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60"
        };

        return ApiTestFactory.Create(settings, executor: executor);
    }

    private static HttpClient CreateClientWithApiKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Romulus_Wave5Medium_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "sample.rom"), "test");
        _tempRoots.Add(root);
        return root;
    }

    private static string ReadSource(string relativeFromSrc)
    {
        var srcDir = FindSrcDirectory();
        var fullPath = Path.Combine(srcDir, relativeFromSrc.Replace('/', Path.DirectorySeparatorChar));
        return File.ReadAllText(fullPath);
    }

    private static string FindSrcDirectory()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null && !Directory.Exists(Path.Combine(dir, "Romulus.Infrastructure")))
            dir = Directory.GetParent(dir)?.FullName;

        return dir ?? throw new DirectoryNotFoundException("Could not locate src directory.");
    }
}