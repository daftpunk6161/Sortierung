using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using RomCleanup.Api;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Index;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Review;

namespace RomCleanup.Tests;

internal static class ApiTestFactory
{
    public static WebApplicationFactory<Program> Create(
        IDictionary<string, string?> settings,
        Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor = null,
        ICollectionIndex? collectionIndex = null)
        => new IsolatedApiFactory(settings, executor, collectionIndex);

    private sealed class IsolatedApiFactory : WebApplicationFactory<Program>
    {
        private readonly IReadOnlyDictionary<string, string?> _settings;
        private readonly Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? _executor;
        private readonly ICollectionIndex? _collectionIndex;
        private readonly string _tempDir;
        private readonly string _databasePath;

        public IsolatedApiFactory(
            IDictionary<string, string?> settings,
            Func<RunRecord, IFileSystem, IAuditStore, CancellationToken, RunExecutionOutcome>? executor,
            ICollectionIndex? collectionIndex)
        {
            _settings = new Dictionary<string, string?>(settings, StringComparer.OrdinalIgnoreCase);
            _executor = executor;
            _collectionIndex = collectionIndex;
            _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_ApiFactory_" + Guid.NewGuid().ToString("N"));
            _databasePath = Path.Combine(_tempDir, "collection.db");
            Directory.CreateDirectory(_tempDir);
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
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

                if (_collectionIndex is not null)
                {
                    services.RemoveAll<ICollectionIndex>();
                    services.AddSingleton<ICollectionIndex>(_ => _collectionIndex);
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
        }
    }
}
