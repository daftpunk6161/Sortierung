using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

public sealed class ProgramHelpersSecurityMappingTests : IDisposable
{
    private readonly string _tempDir;

    public ProgramHelpersSecurityMappingTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ApiHelpers_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    [Fact]
    public void ParseApiKeys_BlankConfigReturnsEmptyAndConfiguredKeysAreTrimmedAndDeduplicated()
    {
        Assert.Empty(Program.ParseApiKeys("   "));

        var keys = Program.ParseApiKeys(" alpha ; beta\nalpha\r gamma ");

        Assert.Equal(["alpha", "beta", "gamma"], keys);
    }

    [Theory]
    [InlineData("local-dev", "", "http://localhost:3000")]
    [InlineData("strict-local", "", "http://127.0.0.1")]
    [InlineData("custom", "https://romulus.local", "https://romulus.local")]
    [InlineData("custom", "not-a-uri", "http://127.0.0.1")]
    [InlineData("unknown", "https://ignored.example", "http://127.0.0.1")]
    public void ResolveCorsOrigin_UsesSafeDeterministicOrigins(string mode, string customOrigin, string expected)
    {
        Assert.Equal(expected, Program.ResolveCorsOrigin(mode, customOrigin));
    }

    [Theory]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("localhost", true)]
    [InlineData("127.0.0.1", true)]
    [InlineData("::1", true)]
    [InlineData("0.0.0.0", false)]
    public void IsLoopbackBindAddress_ClassifiesOnlyLoopbackValues(string? bindAddress, bool expected)
    {
        Assert.Equal(expected, Program.IsLoopbackBindAddress(bindAddress));
    }

    [Theory]
    [InlineData("", false)]
    [InlineData("relative/path", false)]
    [InlineData("ftp://example.test", false)]
    [InlineData("https://example.test", true)]
    public void IsValidCorsOrigin_RejectsBlankRelativeAndNonHttpOrigins(string origin, bool expected)
    {
        Assert.Equal(expected, Program.IsValidCorsOrigin(origin));
    }

    [Fact]
    public void GetClientBindingId_UsesSanitizedClientHeaderAndCachesIt()
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = IPAddress.Parse("203.0.113.9");
        context.Request.Headers["X-Client-Id"] = "client_1.api";

        var first = Program.GetClientBindingId(context, trustForwardedFor: false);
        context.Request.Headers["X-Client-Id"] = "other-client";
        var second = Program.GetClientBindingId(context, trustForwardedFor: false);

        Assert.Equal("client_1.api", first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task CreateArtifactDownloadResult_MissingArtifactReturnsStructuredConflict()
    {
        var result = Program.CreateArtifactDownloadResult(
            artifactPath: Path.Combine(_tempDir, "missing.json"),
            contentType: "application/json",
            fallbackFileName: "fallback.json",
            unavailableCode: "ARTIFACT-MISSING",
            unavailableMessage: "Artifact unavailable.",
            runId: "run-1");

        using var doc = await ExecuteResultAsJsonAsync(result, expectedStatus: StatusCodes.Status409Conflict);

        Assert.Equal("ARTIFACT-MISSING", doc.RootElement.GetProperty("error").GetProperty("code").GetString());
        Assert.Equal("run-1", doc.RootElement.GetProperty("runId").GetString());
    }

    [Fact]
    public async Task ValidateCollectionMergeRequest_ReturnsStructuredErrorsBeforePlanning()
    {
        var allowedRootPolicy = new AllowedRootPathPolicy([_tempDir]);
        var leftRoot = Path.Combine(_tempDir, "left");
        var rightRoot = Path.Combine(_tempDir, "right");
        var targetRoot = Path.Combine(_tempDir, "target");
        Directory.CreateDirectory(leftRoot);
        Directory.CreateDirectory(rightRoot);

        await AssertErrorCodeAsync(
            Program.ValidateCollectionMergeRequest(
                NewMergeRequest(leftRoots: [], rightRoots: [rightRoot], targetRoot: targetRoot),
                allowedRootPolicy)!,
            "COLLECTION-LEFT-ROOTS-REQUIRED");

        await AssertErrorCodeAsync(
            Program.ValidateCollectionMergeRequest(
                NewMergeRequest(leftRoots: [leftRoot], rightRoots: [], targetRoot: targetRoot),
                allowedRootPolicy)!,
            "COLLECTION-RIGHT-ROOTS-REQUIRED");

        await AssertErrorCodeAsync(
            Program.ValidateCollectionMergeRequest(
                NewMergeRequest(leftRoots: [leftRoot], rightRoots: [rightRoot], targetRoot: targetRoot, limit: 5001),
                allowedRootPolicy)!,
            ApiErrorCodes.CollectionMergeInvalidLimit);

        await AssertErrorCodeAsync(
            Program.ValidateCollectionMergeRequest(
                NewMergeRequest(leftRoots: [leftRoot], rightRoots: [rightRoot], targetRoot: " "),
                allowedRootPolicy)!,
            ApiErrorCodes.CollectionMergeTargetRequired);

        var outsideTarget = Path.Combine(Path.GetTempPath(), "romulus-outside-" + Guid.NewGuid().ToString("N"));
        await AssertErrorCodeAsync(
            Program.ValidateCollectionMergeRequest(
                NewMergeRequest(leftRoots: [leftRoot], rightRoots: [rightRoot], targetRoot: outsideTarget),
                allowedRootPolicy)!,
            SecurityErrorCodes.OutsideAllowedRoots);
    }

    [Theory]
    [InlineData(ConfigurationErrorCode.UncPath, "", SecurityErrorCodes.InvalidPath, "UNC paths are not allowed.")]
    [InlineData(ConfigurationErrorCode.PathTraversal, "RUN", SecurityErrorCodes.InvalidPath, "Path traversal is not allowed.")]
    [InlineData(ConfigurationErrorCode.ReparsePoint, "RUN", SecurityErrorCodes.InvalidPath, "The path contains a reparse point.")]
    [InlineData(ConfigurationErrorCode.AccessDenied, "RUN", SecurityErrorCodes.InvalidPath, "Access to the specified path is denied.")]
    [InlineData(ConfigurationErrorCode.InvalidConsole, "watch", "WATCH-INVALID-CONSOLE", "The specified console is invalid.")]
    [InlineData(ConfigurationErrorCode.MissingDatRoot, "run", "RUN-MISSING-DAT-ROOT", "The DAT root path is required.")]
    [InlineData(ConfigurationErrorCode.MissingTrashRoot, "run", "RUN-MISSING-TRASH-ROOT", "The trash root path is required.")]
    [InlineData(ConfigurationErrorCode.WorkflowNotFound, "run", "RUN-WORKFLOW-NOT-FOUND", "The specified workflow was not found.")]
    [InlineData(ConfigurationErrorCode.ProfileNotFound, "run", "RUN-PROFILE-NOT-FOUND", "The specified profile was not found.")]
    public void MapConfigurationError_MapsTypedValidationFailuresWithoutLeakingRawMessages(
        ConfigurationErrorCode code,
        string prefix,
        string expectedCode,
        string expectedMessage)
    {
        var ex = new ConfigurationValidationException(code, "raw path C:\\secret\\roms");

        var mapped = Program.MapConfigurationError(ex, prefix);

        Assert.Equal(expectedCode, mapped.Code);
        Assert.Equal(expectedMessage, mapped.Message);
        Assert.DoesNotContain("secret", mapped.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static CollectionMergeRequest NewMergeRequest(
        IReadOnlyList<string> leftRoots,
        IReadOnlyList<string> rightRoots,
        string targetRoot,
        int limit = 500)
        => new()
        {
            CompareRequest = new CollectionCompareRequest
            {
                Left = new CollectionSourceScope { SourceId = "left", Label = "Left", Roots = leftRoots },
                Right = new CollectionSourceScope { SourceId = "right", Label = "Right", Roots = rightRoots },
                Limit = limit
            },
            TargetRoot = targetRoot
        };

    private static async Task AssertErrorCodeAsync(IResult result, string expectedCode)
    {
        using var doc = await ExecuteResultAsJsonAsync(result, expectedStatus: StatusCodes.Status400BadRequest);
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static async Task<JsonDocument> ExecuteResultAsJsonAsync(IResult result, int expectedStatus)
    {
        var context = new DefaultHttpContext();
        await using var body = new MemoryStream();
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddOptions()
            .BuildServiceProvider();
        context.RequestServices = services;
        context.Response.Body = body;

        await result.ExecuteAsync(context);

        Assert.Equal(expectedStatus, context.Response.StatusCode);
        body.Position = 0;
        return await JsonDocument.ParseAsync(body);
    }
}
