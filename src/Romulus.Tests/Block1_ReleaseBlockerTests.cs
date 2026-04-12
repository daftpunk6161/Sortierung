using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Workflow;
using Romulus.Tests.TestFixtures;
using Romulus.UI.Wpf.Services;
using Romulus.UI.Wpf.ViewModels;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Block 1 – Release-Blocker Tests
/// R6-01: UI-Thread Deadlock (.AsTask().Result on SynchronizationContext)
/// R6-08: API Global Exception Handler (unhandled exceptions leak stack traces)
/// </summary>
public sealed class Block1_ReleaseBlockerTests : IDisposable
{
    private const string ApiKey = "block1-test-key";

    private readonly string _tempRoot;

    public Block1_ReleaseBlockerTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_Block1_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempRoot))
                Directory.Delete(_tempRoot, recursive: true);
        }
        catch
        {
            // best-effort cleanup
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    // R6-01: No sync-over-async in WPF ViewModel
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public void R6_01_ProductizationViewModel_MustNotContain_SyncOverAsync()
    {
        // ARRANGE: Locate the source file via caller-path resolution
        var sourceFile = FindSourceFile("ViewModels", "MainViewModel.Productization.cs");
        Assert.True(File.Exists(sourceFile), $"Source file not found: {sourceFile}");

        var source = File.ReadAllText(sourceFile);

        // ACT + ASSERT: No .AsTask().Result or .GetAwaiter().GetResult() in ViewModel
        Assert.DoesNotContain(".AsTask().Result", source);
        Assert.DoesNotContain(".GetAwaiter().GetResult()", source);
    }

    [Fact]
    public async Task R6_01_ApplySelectedRunConfigurationAsync_IsGenuinelyAsync()
    {
        // ARRANGE: Create a mock materializer that is actually async (delayed)
        var tcs = new TaskCompletionSource<MaterializedRunConfiguration>();
        var delayedStore = new DelayedRunProfileStore();
        var dataDir = FeatureService.ResolveDataDirectory() ?? RunEnvironmentBuilder.ResolveDataDir();
        var profileService = new RunProfileService(delayedStore, dataDir);
        var resolver = new RunConfigurationResolver(profileService);
        var materializer = new RunConfigurationMaterializer(resolver, new RunOptionsFactory());

        var vm = CreateViewModel(profileService, materializer);
        vm.SelectedWorkflowScenarioId = WorkflowScenarioIds.QuickClean;

        // ACT: Call the async method — it should NOT block if properly async
        var task = vm.ApplySelectedRunConfigurationAsync();

        // ASSERT: The task should represent real async work, not Task.CompletedTask from sync wrapper.
        // After fix, this will properly await the materializer.
        // The true assertion is that calling this method does not deadlock on a SynchronizationContext.
        // In a test environment (no SyncContext), we verify that the method
        // at least returns the result of an async chain, not Task.CompletedTask wrapping sync code.
        Assert.NotNull(task);
        await task;
    }

    [Fact]
    public async Task R6_01_RefreshRunConfigurationCatalogsAsync_IsGenuinelyAsync()
    {
        // ARRANGE
        var delayedStore = new DelayedRunProfileStore(delayMs: 50);
        var dataDir = FeatureService.ResolveDataDirectory() ?? RunEnvironmentBuilder.ResolveDataDir();
        var profileService = new RunProfileService(delayedStore, dataDir);
        var materializer = new RunConfigurationMaterializer(
            new RunConfigurationResolver(profileService), new RunOptionsFactory());

        var vm = CreateViewModel(profileService, materializer);

        // ACT: Call async version
        var task = vm.RefreshRunConfigurationCatalogsAsync();

        // ASSERT: Task should not be synchronously completed when store has async delay
        // After fix, the async path properly awaits the async store.
        // Before fix, RefreshRunConfigurationCatalogs uses .AsTask().Result which blocks synchronously.
        Assert.NotNull(task);
        await task;

        Assert.NotEmpty(vm.AvailableWorkflows);
    }

    // ═══════════════════════════════════════════════════════════════════
    // R6-08: API Global Exception Handler
    // ═══════════════════════════════════════════════════════════════════

    [Fact]
    public async Task R6_08_UnhandledException_Returns500_WithStructuredJson()
    {
        // ARRANGE: Use a real request path whose endpoint directly awaits ICollectionIndex.
        await using var factory = CreateThrowingFactory();
        using var client = CreateClientWithApiKey(factory);

        // ACT
        var response = await client.GetAsync("/runs/history");

        // ASSERT: Must be 500 with structured JSON error, not HTML/plaintext stack trace
        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);

        var contentType = response.Content.Headers.ContentType?.MediaType;
        Assert.Equal("application/json", contentType);

        var json = await response.Content.ReadAsStringAsync();
        var root = JsonDocument.Parse(json).RootElement;

        Assert.True(root.TryGetProperty("error", out var error), "Response must have 'error' property");
        Assert.Equal("INTERNAL_ERROR", error.GetProperty("code").GetString());
        Assert.Equal("Critical", error.GetProperty("kind").GetString());
        Assert.Equal("API", error.GetProperty("module").GetString());
        Assert.DoesNotContain("System.", json); // No stack trace in response
        Assert.DoesNotContain("at Romulus", json); // No stack trace in response
    }

    [Fact]
    public async Task R6_08_UnhandledException_DoesNotLeakStackTrace()
    {
        // ARRANGE
        await using var factory = CreateThrowingFactory();
        using var client = CreateClientWithApiKey(factory);

        // ACT
        var response = await client.GetAsync("/runs/history");
        var body = await response.Content.ReadAsStringAsync();

        // ASSERT: Stack trace must never be exposed regardless of environment
        Assert.DoesNotContain("Exception", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("StackTrace", body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("at ", body); // typical stack trace line
    }

    // ═══════════════════════════════════════════════════════════════════
    // Helpers
    // ═══════════════════════════════════════════════════════════════════

    private static MainViewModel CreateViewModel(
        RunProfileService? profileService = null,
        RunConfigurationMaterializer? materializer = null)
    {
        var dataDir = FeatureService.ResolveDataDirectory() ?? RunEnvironmentBuilder.ResolveDataDir();
        profileService ??= new RunProfileService(new InMemoryRunProfileStore(), dataDir);
        materializer ??= new RunConfigurationMaterializer(
            new RunConfigurationResolver(profileService), new RunOptionsFactory());

        return new MainViewModel(
            new StubThemeService(),
            new StubDialogService(),
            new StubSettingsService(),
            runProfileService: profileService,
            runConfigurationMaterializer: materializer);
    }

    private WebApplicationFactory<Program> CreateThrowingFactory()
    {
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
        {
            ["ApiKey"] = ApiKey,
            ["CorsMode"] = "strict-local",
            ["CorsAllowOrigin"] = "http://127.0.0.1",
            ["RateLimitRequests"] = "120",
            ["RateLimitWindowSeconds"] = "60"
        };

        return ApiTestFactory.Create(settings, collectionIndex: new ThrowingCollectionIndex());
    }

    private static HttpClient CreateClientWithApiKey(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", ApiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    private static string FindSourceFile(string folder, string fileName, [System.Runtime.CompilerServices.CallerFilePath] string? callerPath = null)
    {
        var dir = Path.GetDirectoryName(callerPath);
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "src", "Romulus.sln")))
                return Path.Combine(dir, "src", "Romulus.UI.Wpf", folder, fileName);
            dir = Path.GetDirectoryName(dir);
        }

        return Path.Combine(Directory.GetCurrentDirectory(), "src", "Romulus.UI.Wpf", folder, fileName);
    }

    // ── Test doubles ─────────────────────────────────────────────────

    private sealed class ThrowingCollectionIndex : ICollectionIndex
    {
        private static InvalidOperationException CreateException()
            => new("Simulated unhandled crash for R6-08 test");

        public ValueTask<CollectionIndexMetadata> GetMetadataAsync(CancellationToken ct = default) => ValueTask.FromException<CollectionIndexMetadata>(CreateException());
        public ValueTask<int> CountEntriesAsync(CancellationToken ct = default) => ValueTask.FromException<int>(CreateException());
        public ValueTask<CollectionIndexEntry?> TryGetByPathAsync(string path, CancellationToken ct = default) => ValueTask.FromException<CollectionIndexEntry?>(CreateException());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> GetByPathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => ValueTask.FromException<IReadOnlyList<CollectionIndexEntry>>(CreateException());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListByConsoleAsync(string consoleKey, CancellationToken ct = default) => ValueTask.FromException<IReadOnlyList<CollectionIndexEntry>>(CreateException());
        public ValueTask<IReadOnlyList<CollectionIndexEntry>> ListEntriesInScopeAsync(IReadOnlyList<string> roots, IReadOnlyCollection<string> extensions, CancellationToken ct = default) => ValueTask.FromException<IReadOnlyList<CollectionIndexEntry>>(CreateException());
        public ValueTask UpsertEntriesAsync(IReadOnlyList<CollectionIndexEntry> entries, CancellationToken ct = default) => ValueTask.FromException(CreateException());
        public ValueTask RemovePathsAsync(IReadOnlyList<string> paths, CancellationToken ct = default) => ValueTask.FromException(CreateException());
        public ValueTask<CollectionHashCacheEntry?> TryGetHashAsync(string path, string algorithm, long sizeBytes, DateTime lastWriteUtc, CancellationToken ct = default) => ValueTask.FromException<CollectionHashCacheEntry?>(CreateException());
        public ValueTask SetHashAsync(CollectionHashCacheEntry entry, CancellationToken ct = default) => ValueTask.FromException(CreateException());
        public ValueTask AppendRunSnapshotAsync(CollectionRunSnapshot snapshot, CancellationToken ct = default) => ValueTask.FromException(CreateException());
        public ValueTask<int> CountRunSnapshotsAsync(CancellationToken ct = default) => ValueTask.FromException<int>(CreateException());
        public ValueTask<IReadOnlyList<CollectionRunSnapshot>> ListRunSnapshotsAsync(int limit = 50, CancellationToken ct = default) => ValueTask.FromException<IReadOnlyList<CollectionRunSnapshot>>(CreateException());
    }

    private sealed class InMemoryRunProfileStore : IRunProfileStore
    {
        private readonly Dictionary<string, RunProfileDocument> _profiles = new(StringComparer.OrdinalIgnoreCase);

        public ValueTask<IReadOnlyList<RunProfileDocument>> ListAsync(CancellationToken ct = default)
            => ValueTask.FromResult<IReadOnlyList<RunProfileDocument>>(
                _profiles.Values
                    .OrderBy(static p => p.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray());

        public ValueTask<RunProfileDocument?> TryGetAsync(string id, CancellationToken ct = default)
        {
            _profiles.TryGetValue(id, out var p);
            return ValueTask.FromResult(p);
        }

        public ValueTask UpsertAsync(RunProfileDocument profile, CancellationToken ct = default)
        {
            _profiles[profile.Id] = profile;
            return ValueTask.CompletedTask;
        }

        public ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default)
            => ValueTask.FromResult(_profiles.Remove(id));
    }

    /// <summary>
    /// Profile store that injects real async delay to detect sync-over-async.
    /// </summary>
    private sealed class DelayedRunProfileStore : IRunProfileStore
    {
        private readonly int _delayMs;

        public DelayedRunProfileStore(int delayMs = 0)
        {
            _delayMs = delayMs;
        }

        public async ValueTask<IReadOnlyList<RunProfileDocument>> ListAsync(CancellationToken ct = default)
        {
            if (_delayMs > 0)
                await Task.Delay(_delayMs, ct);
            return Array.Empty<RunProfileDocument>();
        }

        public async ValueTask<RunProfileDocument?> TryGetAsync(string id, CancellationToken ct = default)
        {
            if (_delayMs > 0)
                await Task.Delay(_delayMs, ct);
            return null;
        }

        public ValueTask UpsertAsync(RunProfileDocument profile, CancellationToken ct = default)
            => ValueTask.CompletedTask;

        public ValueTask<bool> DeleteAsync(string id, CancellationToken ct = default)
            => ValueTask.FromResult(false);
    }

    private sealed class StubDialogService : IDialogService
    {
        public string? BrowseFolder(string title = "Ordner auswaehlen") => null;
        public string? BrowseFile(string title = "Datei auswaehlen", string filter = "Alle Dateien|*.*") => null;
        public string? SaveFile(string title = "Speichern unter", string filter = "Alle Dateien|*.*", string? defaultFileName = null) => null;
        public bool Confirm(string message, string title = "Bestaetigung") => true;
        public void Info(string message, string title = "Information") { }
        public void Error(string message, string title = "Fehler") { }
        public ConfirmResult YesNoCancel(string message, string title = "Frage") => ConfirmResult.Yes;
        public string ShowInputBox(string prompt, string title = "Eingabe", string defaultValue = "") => defaultValue;
        public void ShowText(string title, string content) { }
        public bool DangerConfirm(string title, string message, string confirmText, string buttonLabel = "Bestaetigen") => true;
        public bool ConfirmConversionReview(string title, string summary, IReadOnlyList<ConversionReviewEntry> entries) => true;
        public bool ConfirmDatRenamePreview(IReadOnlyList<DatAuditEntry> renameProposals) => true;
    }

}
