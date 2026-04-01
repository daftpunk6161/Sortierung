using Microsoft.Extensions.DependencyInjection;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Index;
using RomCleanup.Infrastructure.Orchestration;
using RomCleanup.Infrastructure.Review;

namespace RomCleanup.Infrastructure;

public static class SharedServiceRegistration
{
    public static IServiceCollection AddRomCleanupCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new CollectionIndexPathOptions());
        services.AddSingleton<IFileSystem, FileSystemAdapter>();
        services.AddSingleton<IAuditStore>(sp =>
            new AuditCsvStore(
                sp.GetRequiredService<IFileSystem>(),
                _ => { },
                AuditSecurityPaths.GetDefaultSigningKeyPath()));
        services.AddSingleton<IHeaderRepairService, HeaderRepairService>();
        services.AddSingleton<ICollectionIndex>(sp =>
            new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDatabasePath(
                sp.GetRequiredService<CollectionIndexPathOptions>().DatabasePath)));
        services.AddSingleton<IReviewDecisionStore>(sp =>
            new LiteDbReviewDecisionStore(CollectionIndexPaths.ResolveDatabasePath(
                sp.GetRequiredService<CollectionIndexPathOptions>().DatabasePath)));
        services.AddSingleton<PersistedReviewDecisionService>();

        services.AddSingleton<IRunOptionsFactory, RunOptionsFactory>();
        services.AddSingleton<IRunEnvironmentFactory, RunEnvironmentFactory>();
        services.AddSingleton<IPhasePlanBuilder, PhasePlanBuilder>();

        return services;
    }
}
