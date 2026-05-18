using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

public sealed class DecisionExplainerEndpointTests : IDisposable
{
    private const string ApiKey = "decision-explainer-test-key";
    private readonly string _root;

    public DecisionExplainerEndpointTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus_DecisionExplainer_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    [Fact]
    public async Task Decisions_ListAndSingleLookup_ReturnSharedExplanationProjectionWithoutFullPathLeak()
    {
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            executor: (run, _, _, _) =>
            {
                run.CoreRunResult = BuildRunResult();
                return new RunExecutionOutcome(RunConstants.StatusCompleted, new ApiRunResult
                {
                    Status = RunConstants.StatusOk,
                    OrchestratorStatus = RunConstants.StatusOk,
                    ExitCode = 0,
                    TotalFiles = 2,
                    Groups = 1,
                    Winners = 1,
                    Losers = 1
                });
            });
        using var client = CreateClientWithApiKey(factory, clientId: "client-a");

        var runId = await StartRunAsync(client);

        var listResponse = await client.GetAsync($"/runs/{runId}/decisions");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        Assert.Equal(1, listDoc.RootElement.GetProperty("count").GetInt32());
        var explanation = Assert.Single(listDoc.RootElement.GetProperty("explanations").EnumerateArray());
        AssertDecisionExplanation(explanation);

        var lookupResponse = await client.GetAsync($"/runs/{runId}/decisions/NES!super-mario-bros");
        Assert.Equal(HttpStatusCode.OK, lookupResponse.StatusCode);

        using var lookupDoc = JsonDocument.Parse(await lookupResponse.Content.ReadAsStringAsync());
        AssertDecisionExplanation(lookupDoc.RootElement);
    }

    [Fact]
    public async Task Decisions_InvalidCompositeKey_ReturnsBadRequestBeforeProjection()
    {
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            executor: (run, _, _, _) =>
            {
                run.CoreRunResult = BuildRunResult();
                return new RunExecutionOutcome(RunConstants.StatusCompleted, new ApiRunResult
                {
                    Status = RunConstants.StatusOk,
                    OrchestratorStatus = RunConstants.StatusOk
                });
            });
        using var client = CreateClientWithApiKey(factory);

        var runId = await StartRunAsync(client);

        var response = await client.GetAsync($"/runs/{runId}/decisions/missing-separator");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiErrorCodes.RunInvalidId, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Decisions_DifferentBoundClientCannotReadRunDecisionData()
    {
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            executor: (run, _, _, _) =>
            {
                run.CoreRunResult = BuildRunResult();
                return new RunExecutionOutcome(RunConstants.StatusCompleted, new ApiRunResult
                {
                    Status = RunConstants.StatusOk,
                    OrchestratorStatus = RunConstants.StatusOk
                });
            });
        using var ownerClient = CreateClientWithApiKey(factory, clientId: "client-owner");
        using var otherClient = CreateClientWithApiKey(factory, clientId: "client-other");

        var runId = await StartRunAsync(ownerClient);

        var response = await otherClient.GetAsync($"/runs/{runId}/decisions");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiErrorCodes.AuthForbidden, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task Decisions_RunWithoutCoreRunResult_ReturnsNotFoundInsteadOfInventingExplanation()
    {
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            executor: (_, _, _, _) => new RunExecutionOutcome(RunConstants.StatusCompleted, new ApiRunResult
            {
                Status = RunConstants.StatusOk,
                OrchestratorStatus = RunConstants.StatusOk
            }));
        using var client = CreateClientWithApiKey(factory);

        var runId = await StartRunAsync(client);

        var response = await client.GetAsync($"/runs/{runId}/decisions");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiErrorCodes.RunNotFound, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static HttpClient CreateClientWithApiKey(WebApplicationFactory<Program> factory, string? clientId = null)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ignored");
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        if (!string.IsNullOrWhiteSpace(clientId))
            client.DefaultRequestHeaders.Add("X-Client-Id", clientId);

        return client;
    }

    private async Task<string> StartRunAsync(HttpClient client)
    {
        var payload = JsonSerializer.Serialize(new
        {
            roots = new[] { _root },
            mode = RunConstants.ModeDryRun,
            extensions = new[] { ".nes" }
        });

        using var content = new StringContent(payload, Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/runs?wait=true", content);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var runId = doc.RootElement.GetProperty("run").GetProperty("runId").GetString();
        Assert.False(string.IsNullOrWhiteSpace(runId));
        return runId!;
    }

    private static RunResult BuildRunResult()
    {
        var trace = new WinnerReasonTrace(
            ConsoleKey: "NES",
            GameKey: "super-mario-bros",
            WinnerFileName: "Super Mario Bros. (Europe).nes",
            WinnerExtension: ".nes",
            WinnerRegion: "EU",
            RegionScore: 1000,
            FormatScore: 700,
            VersionScore: 0,
            HeaderScore: 100,
            CompletenessScore: 100,
            DatMatch: true,
            MultiDatResolution: null,
            SizeTieBreakScore: 4096,
            WinnerCategory: nameof(FileCategory.Game),
            LoserCount: 1,
            TiebreakerSummary: "Region score selected the European winner.");

        return new RunResult
        {
            Status = RunConstants.StatusOk,
            ExitCode = 0,
            WinnerReasons = [trace]
        };
    }

    private static void AssertDecisionExplanation(JsonElement explanation)
    {
        Assert.Equal("NES", explanation.GetProperty("consoleKey").GetString());
        Assert.Equal("super-mario-bros", explanation.GetProperty("gameKey").GetString());
        Assert.Equal("Super Mario Bros. (Europe).nes", explanation.GetProperty("winnerFileName").GetString());
        Assert.Equal(".nes", explanation.GetProperty("winnerExtension").GetString());
        Assert.Equal(nameof(FileCategory.Game), explanation.GetProperty("winnerCategory").GetString());
        Assert.Equal("EU", explanation.GetProperty("winnerRegion").GetString());
        Assert.True(explanation.GetProperty("datMatch").GetBoolean());
        Assert.Equal(1, explanation.GetProperty("loserCount").GetInt32());

        var serialized = explanation.GetRawText();
        Assert.DoesNotContain(Path.GetTempPath(), serialized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Region score selected the European winner.", serialized, StringComparison.Ordinal);
        Assert.Contains(
            explanation.GetProperty("scores").EnumerateArray(),
            score => score.GetProperty("axis").GetString() == "Region"
                && score.GetProperty("value").GetInt64() == 1000);
        Assert.Contains(
            explanation.GetProperty("tiebreakerOrder").EnumerateArray(),
            item => item.GetString() == "Path(Ordinal)");
    }
}
