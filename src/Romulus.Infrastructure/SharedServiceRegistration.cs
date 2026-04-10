using Microsoft.Extensions.DependencyInjection;
using Romulus.Contracts.Ports;
using IO = Romulus.Infrastructure.IO;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.FileSystem;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Review;

namespace Romulus.Infrastructure;

public static class SharedServiceRegistration
{
    public static IServiceCollection AddRomulusCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddSingleton(new CollectionIndexPathOptions());
        services.AddSingleton(new RunProfilePathOptions());
        services.AddSingleton<ISetParserIo>(sp =>
        {
            var io = new IO.SetParserIo();
            Romulus.Core.SetParsing.SetParserIo.Use(io);
            return io;
        });
        services.AddSingleton<IClassificationIo>(sp =>
        {
            var io = new IO.ClassificationIo();
            Romulus.Core.Classification.ClassificationIo.Use(io);
            return io;
        });
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
        services.AddSingleton<IRunProfileStore, JsonRunProfileStore>();
        services.AddSingleton<RunProfileService>();
        services.AddSingleton<RunConfigurationResolver>();
        services.AddSingleton<RunConfigurationMaterializer>();

        services.AddSingleton<IRunOptionsFactory, RunOptionsFactory>();
        services.AddSingleton<IRunEnvironmentFactory, RunEnvironmentFactory>();
        services.AddSingleton<IPhasePlanBuilder, PhasePlanBuilder>();
        services.AddSingleton<IFamilyDatStrategyResolver, FamilyDatStrategyResolver>();
        services.AddSingleton<IFamilyPipelineSelector, FamilyPipelineSelector>();

        return services;
    }
}
