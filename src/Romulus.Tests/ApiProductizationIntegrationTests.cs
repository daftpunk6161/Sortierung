using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts.Models;
using Xunit;

namespace Romulus.Tests;

public sealed class ApiProductizationIntegrationTests
{
    private const string ApiKey = "integration-test-key";

    [Fact]
    public async Task Profiles_List_ReturnsBuiltInProfiles()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var response = await client.GetAsync("/profiles");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var profiles = doc.RootElement.GetProperty("profiles");
        Assert.Contains(profiles.EnumerateArray(), item => item.GetProperty("id").GetString() == "default");
        Assert.Contains(profiles.EnumerateArray(), item => item.GetProperty("id").GetString() == "quick-scan");
    }

    [Fact]
    public async Task Profiles_PutGetDelete_RoundTripsUserProfile()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var profile = new RunProfileDocument
        {
            Version = 1,
            Id = "ignored-by-route",
            Name = "Custom API Profile",
            Description = "Saved via API integration test.",
            Settings = new RunProfileSettings
            {
                SortConsole = true,
                EnableDat = true,
                EnableDatAudit = true,
                Mode = "DryRun"
            }
        };

        using var putContent = new StringContent(
            JsonSerializer.Serialize(profile),
            Encoding.UTF8,
            "application/json");

        var putResponse = await client.PutAsync("/profiles/custom-api-profile", putContent);
        Assert.Equal(HttpStatusCode.OK, putResponse.StatusCode);

        using var putDoc = JsonDocument.Parse(await putResponse.Content.ReadAsStringAsync());
        Assert.Equal("custom-api-profile", putDoc.RootElement.GetProperty("id").GetString());
        Assert.False(putDoc.RootElement.GetProperty("builtIn").GetBoolean());

        var getResponse = await client.GetAsync("/profiles/custom-api-profile");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var getDoc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("Custom API Profile", getDoc.RootElement.GetProperty("name").GetString());
        Assert.True(getDoc.RootElement.GetProperty("settings").GetProperty("enableDat").GetBoolean());

        var deleteResponse = await client.DeleteAsync("/profiles/custom-api-profile");
        Assert.Equal(HttpStatusCode.OK, deleteResponse.StatusCode);

        using var deleteDoc = JsonDocument.Parse(await deleteResponse.Content.ReadAsStringAsync());
        Assert.True(deleteDoc.RootElement.GetProperty("deleted").GetBoolean());
        Assert.Equal("custom-api-profile", deleteDoc.RootElement.GetProperty("id").GetString());
    }

    [Fact]
    public async Task Workflows_ListAndGet_ReturnSharedDefinitions()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var listResponse = await client.GetAsync("/workflows");
        Assert.Equal(HttpStatusCode.OK, listResponse.StatusCode);

        using var listDoc = JsonDocument.Parse(await listResponse.Content.ReadAsStringAsync());
        var workflows = listDoc.RootElement.GetProperty("workflows");
        Assert.Contains(workflows.EnumerateArray(), item => item.GetProperty("id").GetString() == "full-audit");
        Assert.Contains(workflows.EnumerateArray(), item => item.GetProperty("id").GetString() == "quick-clean");

        var getResponse = await client.GetAsync("/workflows/full-audit");
        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        using var getDoc = JsonDocument.Parse(await getResponse.Content.ReadAsStringAsync());
        Assert.Equal("full-audit", getDoc.RootElement.GetProperty("id").GetString());
        Assert.Equal("default", getDoc.RootElement.GetProperty("recommendedProfileId").GetString());
    }

    [Fact]
    public async Task Runs_WithWorkflowScenarioId_MaterializesSharedWorkflowDefaults()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "Example (USA).zip"), "workflow");
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                workflowScenarioId = "full-audit"
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/runs?wait=true", content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var run = doc.RootElement.GetProperty("run");
            Assert.Equal("full-audit", run.GetProperty("workflowScenarioId").GetString());
            Assert.Equal("default", run.GetProperty("profileId").GetString());
            Assert.True(run.GetProperty("enableDat").GetBoolean());
            Assert.True(run.GetProperty("enableDatAudit").GetBoolean());
            Assert.True(run.GetProperty("sortConsole").GetBoolean());
            Assert.Equal("DryRun", run.GetProperty("mode").GetString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_WithProfileId_MaterializesSharedProfileDefaults()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "Example (USA).zip"), "profile");
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                profileId = "quick-scan"
            });

            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            var response = await client.PostAsync("/runs?wait=true", content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var run = doc.RootElement.GetProperty("run");
            Assert.Equal("quick-scan", run.GetProperty("profileId").GetString());
            Assert.False(run.GetProperty("enableDat").GetBoolean());
            Assert.False(run.GetProperty("sortConsole").GetBoolean());
            Assert.True(run.GetProperty("removeJunk").GetBoolean() is false);
            Assert.Equal("DryRun", run.GetProperty("mode").GetString());
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Watch_Start_WithWorkflowAndProfile_TriggersRunUsingMaterializedConfiguration()
    {
        var runCaptured = new TaskCompletionSource<RunRecord>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            executor: (run, _, _, _) =>
            {
                runCaptured.TrySetResult(run);
                return new RunExecutionOutcome("completed", new ApiRunResult
                {
                    OrchestratorStatus = "ok",
                    ExitCode = 0
                });
            });
        using var client = CreateClientWithApiKey(factory, "watch-productization");

        var root = CreateTempRoot();
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                workflowScenarioId = "full-audit",
                profileId = "quick-scan"
            });

            using var startContent = new StringContent(payload, Encoding.UTF8, "application/json");
            var startResponse = await client.PostAsync("/watch/start?debounceSeconds=1&intervalMinutes=1", startContent);
            Assert.Equal(HttpStatusCode.OK, startResponse.StatusCode);

            await Task.Delay(300);
            File.WriteAllText(Path.Combine(root, "watch-trigger.zip"), "watch");

            var completed = await Task.WhenAny(runCaptured.Task, Task.Delay(TimeSpan.FromSeconds(15)));
            Assert.Same(runCaptured.Task, completed);

            var run = await runCaptured.Task;
            Assert.Equal("full-audit", run.WorkflowScenarioId);
            Assert.Equal("quick-scan", run.ProfileId);
            Assert.True(run.EnableDat);
            Assert.True(run.EnableDatAudit);
            Assert.True(run.SortConsole);
        }
        finally
        {
            await client.PostAsync("/watch/stop", content: null);
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Export_Frontend_WithRunId_ProducesLaunchBoxArtifact()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        var outputDir = Path.Combine(CreateTempRoot(), "launchbox-export");
        try
        {
            File.WriteAllText(Path.Combine(root, "Export Me (USA).zip"), "export");
            var runPayload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun"
            });

            using var runContent = new StringContent(runPayload, Encoding.UTF8, "application/json");
            var runResponse = await client.PostAsync("/runs?wait=true", runContent);
            Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

            using var runDoc = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
            var runId = runDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var exportPayload = JsonSerializer.Serialize(new
            {
                frontend = "launchbox",
                outputPath = outputDir,
                collectionName = "Romulus Export",
                runId
            });

            using var exportContent = new StringContent(exportPayload, Encoding.UTF8, "application/json");
            var exportResponse = await client.PostAsync("/export/frontend", exportContent);

            Assert.Equal(HttpStatusCode.OK, exportResponse.StatusCode);
            using var exportDoc = JsonDocument.Parse(await exportResponse.Content.ReadAsStringAsync());
            var rootJson = exportDoc.RootElement;
            Assert.Equal("launchbox", rootJson.GetProperty("frontend").GetString());
            Assert.True(rootJson.GetProperty("gameCount").GetInt32() >= 1);

            var artifactPath = rootJson.GetProperty("artifacts")[0].GetProperty("path").GetString();
            Assert.False(string.IsNullOrWhiteSpace(artifactPath));
            Assert.True(File.Exists(artifactPath));
            Assert.EndsWith(".xml", artifactPath, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
            SafeDeleteDirectory(Path.GetDirectoryName(outputDir)!);
        }
    }

    [Fact]
    public async Task Runs_FixDat_WithoutOutputPath_ReturnsBadRequest()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        try
        {
            File.WriteAllText(Path.Combine(root, "FixDat Candidate (USA).zip"), "fixdat");
            var runPayload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun"
            });

            using var runContent = new StringContent(runPayload, Encoding.UTF8, "application/json");
            var runResponse = await client.PostAsync("/runs?wait=true", runContent);
            Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

            using var runDoc = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
            var runId = runDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var fixDatResponse = await client.PostAsync($"/runs/{runId}/fixdat", content: null);

            Assert.Equal(HttpStatusCode.BadRequest, fixDatResponse.StatusCode);
        }
        finally
        {
            SafeDeleteDirectory(root);
        }
    }

    [Fact]
    public async Task Runs_FixDat_WithQueryOutputPath_WritesFixDatFile()
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateClientWithApiKey(factory);

        var root = CreateTempRoot();
        var datRoot = CreateTempRoot();
        var outputRoot = CreateTempRoot();

        try
        {
            File.WriteAllText(Path.Combine(root, "Unrelated Candidate (USA).chd"), "rom");

            File.WriteAllText(
                Path.Combine(datRoot, "psx.dat"),
                """
                <?xml version="1.0" encoding="utf-8"?>
                <datafile>
                  <header>
                    <name>PSX DAT</name>
                  </header>
                  <game name="Missing Adventure">
                    <description>Missing Adventure</description>
                    <rom name="Missing Adventure.chd" sha1="aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa" size="123" />
                  </game>
                </datafile>
                """);

            var runPayload = JsonSerializer.Serialize(new
            {
                roots = new[] { root },
                mode = "DryRun",
                enableDat = true,
                datRoot,
                extensions = new[] { ".chd" }
            });

            using var runContent = new StringContent(runPayload, Encoding.UTF8, "application/json");
            var runResponse = await client.PostAsync("/runs?wait=true", runContent);
            Assert.Equal(HttpStatusCode.OK, runResponse.StatusCode);

            using var runDoc = JsonDocument.Parse(await runResponse.Content.ReadAsStringAsync());
            var runId = runDoc.RootElement.GetProperty("run").GetProperty("runId").GetString();
            Assert.False(string.IsNullOrWhiteSpace(runId));

            var outputPath = Path.Combine(outputRoot, "api-fixdat.dat");
            var fixDatUrl =
                $"/runs/{runId}/fixdat?outputPath={Uri.EscapeDataString(outputPath)}&name={Uri.EscapeDataString("Api-FixDAT-Test")}";

            var fixDatResponse = await client.PostAsync(fixDatUrl, content: null);
            Assert.Equal(HttpStatusCode.OK, fixDatResponse.StatusCode);

            using var responseDoc = JsonDocument.Parse(await fixDatResponse.Content.ReadAsStringAsync());
            Assert.Equal("Api-FixDAT-Test", responseDoc.RootElement.GetProperty("datName").GetString());
            Assert.Equal(outputPath, responseDoc.RootElement.GetProperty("outputPath").GetString(), StringComparer.OrdinalIgnoreCase);
            Assert.True(File.Exists(outputPath));

            var xml = await File.ReadAllTextAsync(outputPath);
            Assert.Contains("Missing Adventure", xml, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("<?xml version=\"1.0\" encoding=\"utf-8\"?>", xml, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            SafeDeleteDirectory(root);
            SafeDeleteDirectory(datRoot);
            SafeDeleteDirectory(outputRoot);
        }
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

    private static string CreateTempRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "Romulus_ApiProductization_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void SafeDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch
        {
            // best effort cleanup
        }
    }
}
