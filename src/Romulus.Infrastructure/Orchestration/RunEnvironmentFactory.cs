using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Hashing;
using Romulus.Infrastructure.Index;
using Romulus.Core.Classification;
using DatIndex = Romulus.Contracts.Models.DatIndex;

namespace Romulus.Infrastructure.Orchestration;

public interface IRunEnvironment : IDisposable
{
    IFileSystem FileSystem { get; }
    IAuditStore AuditStore { get; }
    ConsoleDetector? ConsoleDetector { get; }
    FileHashService? HashService { get; }
    ArchiveHashService? ArchiveHashService { get; }
    IFormatConverter? Converter { get; }
    DatIndex? DatIndex { get; }
    IReadOnlySet<string>? KnownBiosHashes { get; }
    ICollectionIndex? CollectionIndex { get; }
    string? EnrichmentFingerprint { get; }
}

public interface IRunEnvironmentFactory
{
    IRunEnvironment Create(RunOptions options, Action<string>? onWarning = null);
}

public sealed class RunEnvironmentFactory : IRunEnvironmentFactory
{
    private readonly string? _collectionDatabasePath;

    public RunEnvironmentFactory(CollectionIndexPathOptions? collectionIndexPathOptions = null)
    {
        _collectionDatabasePath = collectionIndexPathOptions?.DatabasePath;
    }

    public IRunEnvironment Create(RunOptions options, Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        settings.Dat.UseDat = options.EnableDat;
        settings.Dat.HashType = options.HashType;
        if (!string.IsNullOrWhiteSpace(options.DatRoot))
            settings.Dat.DatRoot = options.DatRoot;

        return RunEnvironmentBuilder.Build(
            options,
            settings,
            dataDir,
            onWarning,
            _collectionDatabasePath);
    }
}
