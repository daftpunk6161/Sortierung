using Microsoft.Extensions.DependencyInjection;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Audit;
using RomCleanup.Infrastructure.FileSystem;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Infrastructure.Index;
using RomCleanup.Infrastructure.Orchestration;

namespace RomCleanup.Infrastructure;

public static class SharedServiceRegistration
{
    public static IServiceCollection AddRomCleanupCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton<IFileSystem, FileSystemAdapter>();
        services.AddSingleton<IAuditStore>(sp =>
            new AuditCsvStore(
                sp.GetRequiredService<IFileSystem>(),
                _ => { },
                AuditSecurityPaths.GetDefaultSigningKeyPath()));
        services.AddSingleton<IHeaderRepairService, HeaderRepairService>();
        services.AddSingleton<ICollectionIndex>(_ =>
            new LiteDbCollectionIndex(CollectionIndexPaths.ResolveDefaultDatabasePath()));

        services.AddSingleton<IRunOptionsFactory, RunOptionsFactory>();
        services.AddSingleton<IRunEnvironmentFactory, RunEnvironmentFactory>();
        services.AddSingleton<IPhasePlanBuilder, PhasePlanBuilder>();

        return services;
    }
}
