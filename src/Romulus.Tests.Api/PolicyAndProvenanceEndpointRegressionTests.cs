using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Xunit;

namespace Romulus.Tests;

public sealed class PolicyAndProvenanceEndpointRegressionTests : IDisposable
{
    private const string ApiKey = "policy-provenance-regression-key";
    private readonly string _tempRoot;

    public PolicyAndProvenanceEndpointRegressionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_PolicyProvenance_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public async Task PolicySign_ThenValidate_ReportsValidSignatureAndRejectsTamperedPolicyText()
    {
        var signingKeyPath = Path.Combine(_tempRoot, "policy-signing.key");
        var root = Path.Combine(_tempRoot, "roms");
        Directory.CreateDirectory(root);

        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            auditSigningKeyPath: signingKeyPath);
        using var client = CreateAuthClient(factory);

        const string policyText = "id: signed\nname: Signed Policy\nallowedExtensions: [.zip]\n";
        var signResponse = await client.PostAsync("/policies/sign", Json(new
        {
            policyText,
            policyFileName = @"C:\Users\demo\policy.yaml",
            signer = "release-bot"
        }));

        Assert.Equal(HttpStatusCode.OK, signResponse.StatusCode);
        var signatureText = await signResponse.Content.ReadAsStringAsync();
        using (var signDoc = JsonDocument.Parse(signatureText))
        {
            Assert.Equal("policy.yaml", signDoc.RootElement.GetProperty("policyFileName").GetString());
            Assert.Equal("release-bot", signDoc.RootElement.GetProperty("signer").GetString());
            Assert.False(string.IsNullOrWhiteSpace(signDoc.RootElement.GetProperty("hmacSha256").GetString()));
        }

        var validateResponse = await client.PostAsync("/policies/validate", Json(new
        {
            policyText,
            policySignatureText = signatureText,
            roots = new[] { root }
        }));

        Assert.Equal(HttpStatusCode.OK, validateResponse.StatusCode);
        using (var validateDoc = JsonDocument.Parse(await validateResponse.Content.ReadAsStringAsync()))
        {
            var signature = validateDoc.RootElement.GetProperty("signature");
            Assert.True(signature.GetProperty("isPresent").GetBoolean());
            Assert.True(signature.GetProperty("isValid").GetBoolean());
            Assert.Equal("release-bot", signature.GetProperty("signer").GetString());
            Assert.True(validateDoc.RootElement.GetProperty("isCompliant").GetBoolean());
        }

        const string tamperedPolicyText = "id: signed\nname: Signed Policy\nallowedExtensions: [.7z]\n";
        var tamperedResponse = await client.PostAsync("/policies/validate", Json(new
        {
            policyText = tamperedPolicyText,
            policySignatureText = signatureText,
            roots = new[] { root }
        }));

        Assert.Equal(HttpStatusCode.OK, tamperedResponse.StatusCode);
        using var tamperedDoc = JsonDocument.Parse(await tamperedResponse.Content.ReadAsStringAsync());
        var tamperedSignature = tamperedDoc.RootElement.GetProperty("signature");
        Assert.True(tamperedSignature.GetProperty("isPresent").GetBoolean());
        Assert.False(tamperedSignature.GetProperty("isValid").GetBoolean());
        Assert.Contains("fingerprint", tamperedSignature.GetProperty("error").GetString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PolicyValidate_OutsideAllowedRoots_IsRejectedBeforePolicyEvaluation()
    {
        var allowedRoot = Path.Combine(_tempRoot, "allowed");
        var outsideRoot = Path.Combine(_tempRoot, "outside");
        Directory.CreateDirectory(allowedRoot);
        Directory.CreateDirectory(outsideRoot);

        using var factory = ApiTestFactory.Create(new Dictionary<string, string?>
        {
            ["ApiKey"] = ApiKey,
            ["AllowedRoots:0"] = allowedRoot
        });
        using var client = CreateAuthClient(factory);

        var response = await client.PostAsync("/policies/validate", Json(new
        {
            policyText = "id: all-zip\nname: All ZIP\nallowedExtensions: [.zip]\n",
            roots = new[] { outsideRoot }
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(SecurityErrorCodes.OutsideAllowedRoots, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("missing-policy", ApiErrorCodes.PolicyTextRequired)]
    [InlineData("missing-roots", ApiErrorCodes.PolicyRootsRequired)]
    [InlineData("empty-root", ApiErrorCodes.PolicyRootEmpty)]
    [InlineData("missing-root", SecurityErrorCodes.RootReparsePoint)]
    [InlineData("invalid-policy", ApiErrorCodes.PolicyInvalid)]
    public async Task PolicyValidate_RequestValidation_ReturnsStructuredErrors(string scenario, string expectedCode)
    {
        var root = Path.Combine(_tempRoot, "policy-root-" + scenario);
        if (scenario is not "missing-root")
            Directory.CreateDirectory(root);

        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateAuthClient(factory);

        var payload = scenario switch
        {
            "missing-policy" => new
            {
                policyText = "",
                roots = new[] { root }
            },
            "missing-roots" => new
            {
                policyText = "id: all-zip\nname: All ZIP\nallowedExtensions: [.zip]\n",
                roots = Array.Empty<string>()
            },
            "empty-root" => new
            {
                policyText = "id: all-zip\nname: All ZIP\nallowedExtensions: [.zip]\n",
                roots = new[] { " " }
            },
            "invalid-policy" => new
            {
                policyText = "id: missing-name\nallowedExtensions: [.zip]\n",
                roots = new[] { root }
            },
            _ => new
            {
                policyText = "id: all-zip\nname: All ZIP\nallowedExtensions: [.zip]\n",
                roots = new[] { root }
            }
        };

        var response = await client.PostAsync("/policies/validate", Json(payload));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Theory]
    [InlineData("", ApiErrorCodes.PolicyTextRequired)]
    [InlineData("{not-json", ApiErrorCodes.PolicyInvalid)]
    public async Task PolicySign_RequestValidation_ReturnsStructuredErrors(string policyText, string expectedCode)
    {
        using var factory = ApiTestFactory.Create(new Dictionary<string, string?> { ["ApiKey"] = ApiKey });
        using var client = CreateAuthClient(factory);

        var response = await client.PostAsync("/policies/sign", Json(new
        {
            policyText,
            policyFileName = "policy.yaml"
        }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(expectedCode, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    [Fact]
    public async Task PolicyValidate_NormalizesRequestedExtensionsBeforeIndexLookup()
    {
        var root = Path.Combine(_tempRoot, "extension-root");
        Directory.CreateDirectory(root);
        var index = new RecordingCollectionIndex();
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            collectionIndex: index);
        using var client = CreateAuthClient(factory);

        var response = await client.PostAsync("/policies/validate", Json(new
        {
            policyText = "id: mixed-ext\nname: Mixed Extensions\nallowedExtensions: [.zip, .nes]\n",
            roots = new[] { root },
            extensions = new[] { "zip", " .NES ", "", "ZIP" }
        }));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal([Path.GetFullPath(root)], index.LastRoots);
        Assert.Equal([".nes", ".zip"], index.LastExtensions);
    }

    [Fact]
    public async Task ProvenanceEndpoint_ProjectsSharedTrailAndTrustScore()
    {
        var entries = new[]
        {
            NewProvenanceEntry("abcdef0123456789", "run-1", ProvenanceEventKind.Verified),
            NewProvenanceEntry("abcdef0123456789", "run-2", ProvenanceEventKind.Moved)
        };
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            provenanceStore: new FakeProvenanceStore(entries));
        using var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/roms/ABCDEF0123456789/provenance");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal("abcdef0123456789", doc.RootElement.GetProperty("fingerprint").GetString());
        Assert.True(doc.RootElement.GetProperty("isValid").GetBoolean());
        Assert.Equal(90, doc.RootElement.GetProperty("trustScore").GetInt32());
        Assert.Equal(2, doc.RootElement.GetProperty("entries").GetArrayLength());
    }

    [Fact]
    public async Task ProvenanceEndpoint_InvalidFingerprint_ReturnsStructuredBadRequest()
    {
        using var factory = ApiTestFactory.Create(
            new Dictionary<string, string?> { ["ApiKey"] = ApiKey },
            provenanceStore: new FakeProvenanceStore([]));
        using var client = CreateAuthClient(factory);

        var response = await client.GetAsync("/roms/not-hex/provenance");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(ApiErrorCodes.ProvenanceInvalidFingerprint, doc.RootElement.GetProperty("error").GetProperty("code").GetString());
    }

    private static HttpClient CreateAuthClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "ignored");
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Add("X-Client-Id", "policy-provenance");
        return client;
    }

    private static StringContent Json(object payload)
        => new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    private static ProvenanceEntry NewProvenanceEntry(
        string fingerprint,
        string auditRunId,
        ProvenanceEventKind eventKind)
        => new(
            Fingerprint: fingerprint,
            AuditRunId: auditRunId,
            EventKind: eventKind,
            TimestampUtc: "2026-05-18T10:00:00.0000000Z",
            Sha256: fingerprint,
            ConsoleKey: "NES",
            DatMatchId: eventKind == ProvenanceEventKind.Verified ? "Nintendo - NES.dat" : null,
            Detail: eventKind.ToString());

    private sealed class RecordingCollectionIndex : ICollectionIndex
    {
        public IReadOnlyList<string> LastRoots { get; private set; } = [];
        public IReadOnlyCollection<string> LastExtensions { get; private set; } = [];

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default)
            => new(new CollectionIndexMetadata());

        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default)
            => new(0);

        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default)
            => new((CollectionIndexEntry?)null);

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => new((IReadOnlyList<CollectionIndexEntry>)[]);

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default)
            => new((IReadOnlyList<CollectionIndexEntry>)[]);

        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(
            IReadOnlyList<string> roots,
            IReadOnlyCollection<string> extensions,
            CancellationToken ct = default)
        {
            LastRoots = roots.Select(Path.GetFullPath).ToArray();
            LastExtensions = extensions.ToArray();
            return new ValueTask<IReadOnlyList<CollectionIndexEntry>>([]);
        }

        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(
            string path,
            string algorithm,
            long sizeBytes,
            DateTime lastWriteUtc,
            CancellationToken ct = default)
            => new((CollectionHashCacheEntry?)null);

        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default)
            => new(0);

        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default)
            => new((IReadOnlyList<CollectionRunSnapshot>)[]);
    }

    private sealed class FakeProvenanceStore(IReadOnlyList<ProvenanceEntry> entries) : IProvenanceStore
    {
        public void Append(ProvenanceEntry entry)
            => throw new NotSupportedException("Endpoint tests use read-only provenance projection.");

        public IReadOnlyList<ProvenanceEntry> Read(string fingerprint)
        {
            ValidateFingerprint(fingerprint);
            return entries;
        }

        public ProvenanceVerifyReport Verify(string fingerprint)
        {
            ValidateFingerprint(fingerprint);
            return ProvenanceVerifyReport.Ok(entries);
        }

        private static void ValidateFingerprint(string fingerprint)
        {
            if (string.IsNullOrWhiteSpace(fingerprint) || fingerprint.Length < 4)
                throw new ArgumentException("Fingerprint must contain at least four hex characters.", nameof(fingerprint));

            if (fingerprint.Any(static ch => !Uri.IsHexDigit(ch)))
                throw new ArgumentException("Fingerprint must be hexadecimal.", nameof(fingerprint));
        }
    }
}
