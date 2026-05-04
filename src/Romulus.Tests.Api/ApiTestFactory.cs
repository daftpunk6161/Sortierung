using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Romulus.Api;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Review;

namespace Romulus.Tests;

internal static class ApiTestFactory
{
    public static WebApplicationFactory<Program> Create(
        IDictionary<string, string?> settings,
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null,
        ICollectionIndex? collectionIndex = null,
        string? auditSigningKeyPath = null)
        => new IsolatedApiFactory(settings, executor, collectionIndex, auditSigningKeyPath);

    private sealed class IsolatedApiFactory : WebApplicationFactory<Program>
    {
        private readonly IReadOnlyDictionary<string, string?> _settings;
        private readonly Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? _executor;
        private readonly ICollectionIndex? _collectionIndex;
        private readonly string? _auditSigningKeyPath;
        private readonly string _tempDir;
        private readonly string _databasePath;
        private readonly string _datCatalogStatePath;
        private readonly string _profileDirectory;

        public IsolatedApiFactory(
            IDictionary<string, string?> settings,
            Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor,
            ICollectionIndex? collectionIndex,
            string? auditSigningKeyPath)
        {
            _settings = new Dictionary<string, string?>(settings, StringComparer.OrdinalIgnoreCase);
            _executor = executor;
            _collectionIndex = collectionIndex;
            _auditSigningKeyPath = auditSigningKeyPath;
            _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_ApiFactory_" + Guid.NewGuid().ToString("N"));
            _databasePath = Path.Combine(_tempDir, "collection.db");
            _datCatalogStatePath = Path.Combine(_tempDir, "dat-catalog-state.json");
            _profileDirectory = Path.Combine(_tempDir, "profiles");
            Directory.CreateDirectory(_tempDir);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            foreach (var pair in _settings)
            {
                if (pair.Value is not null)
                    builder.UseSetting(pair.Key, pair.Value);
            }
            builder.ConfigureAppConfiguration((_, config) =>
            {
                config.AddInMemoryCollection(_settings);
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<CollectionIndexPathOptions>();
                services.AddSingleton(new CollectionIndexPathOptions
                {
                    DatabasePath = _databasePath
                });

                services.RemoveAll<DatCatalogStatePathOptions>();
                services.AddSingleton(new DatCatalogStatePathOptions
                {
                    StatePath = _datCatalogStatePath
                });

                services.RemoveAll<RunProfilePathOptions>();
                services.AddSingleton(new RunProfilePathOptions
                {
                    DirectoryPath = _profileDirectory
                });

                if (_collectionIndex is not null)
                {
                    services.RemoveAll<ICollectionIndex>();
                    services.AddSingleton<ICollectionIndex>(_ => _collectionIndex);
                }

                if (!string.IsNullOrWhiteSpace(_auditSigningKeyPath))
                {
                    services.RemoveAll<AuditSigningService>();
                    services.AddSingleton(sp =>
                        new AuditSigningService(
                            sp.GetRequiredService<IFileSystem>(),
                            _ => { },
                            _auditSigningKeyPath));

                    services.RemoveAll<IAuditStore>();
                    services.AddSingleton<IAuditStore>(sp =>
                        new AuditCsvStore(
                            sp.GetRequiredService<IFileSystem>(),
                            _ => { },
                            _auditSigningKeyPath));

                    services.RemoveAll<IAuditViewerBackingService>();
                    services.AddSingleton<IAuditViewerBackingService>(sp =>
                        new AuditViewerBackingService(
                            sp.GetRequiredService<IFileSystem>(),
                            _auditSigningKeyPath));
                }

                services.RemoveAll<IReviewDecisionStore>();
                services.AddSingleton<IReviewDecisionStore>(_ =>
                    new LiteDbReviewDecisionStore(_databasePath));

                services.RemoveAll<PersistedReviewDecisionService>();
                services.AddSingleton<PersistedReviewDecisionService>();

                if (_executor is not null)
                {
                    services.RemoveAll<RunManager>();
                    services.RemoveAll<RunLifecycleManager>();
                    services.RemoveAll<ApiAutomationService>();
                    services.AddSingleton<RunManager>(sp => new RunManager(
                        sp.GetRequiredService<IFileSystem>(),
                        sp.GetRequiredService<IAuditStore>(),
                        _executor,
                        sp.GetRequiredService<IRunOptionsFactory>(),
                        sp.GetRequiredService<IRunEnvironmentFactory>(),
                        sp.GetRequiredService<PersistedReviewDecisionService>(),
                        sp.GetRequiredService<CollectionIndexPathOptions>()));
                    services.AddSingleton<RunLifecycleManager>(sp =>
                        sp.GetRequiredService<RunManager>().Lifecycle);
                    services.AddSingleton<ApiAutomationService>();
                }
            });
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (!disposing)
                return;

            try
            {
                if (Directory.Exists(_tempDir))
                    Directory.Delete(_tempDir, recursive: true);
            }
            catch
            {
                // Best effort cleanup for isolated API factory state.
            }

            // The test project runs ~199 isolated API factory instances sequentially
            // (xunit.runner.json maxParallelThreads = 1). WebApplicationFactory keeps
            // a graph of finalizable references (Kestrel TestServer, IServiceProvider,
            // LiteDB streams, in-memory configuration roots). Without explicit GC the
            // testhost working set drifts past 8 GB during the full suite and starts
            // to thrash the page file. A targeted Gen2 collection after each factory
            // disposal keeps memory bounded without changing test semantics.
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true);
        }
    }
}
