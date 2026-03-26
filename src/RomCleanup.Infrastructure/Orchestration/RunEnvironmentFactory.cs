using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;
using RomCleanup.Infrastructure.Hashing;
using RomCleanup.Core.Classification;
using DatIndex = RomCleanup.Contracts.Models.DatIndex;

namespace RomCleanup.Infrastructure.Orchestration;

public interface IRunEnvironment
{
    IFileSystem FileSystem { get; }
    IAuditStore AuditStore { get; }
    ConsoleDetector? ConsoleDetector { get; }
    FileHashService? HashService { get; }
    ArchiveHashService? ArchiveHashService { get; }
    IFormatConverter? Converter { get; }
    DatIndex? DatIndex { get; }
}

public interface IRunEnvironmentFactory
{
    IRunEnvironment Create(RunOptions options, Action<string>? onWarning = null);
}

public sealed class RunEnvironmentFactory : IRunEnvironmentFactory
{
    public IRunEnvironment Create(RunOptions options, Action<string>? onWarning = null)
    {
        ArgumentNullException.ThrowIfNull(options);

        var dataDir = RunEnvironmentBuilder.ResolveDataDir();
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);
        settings.Dat.UseDat = options.EnableDat;
        settings.Dat.HashType = options.HashType;
        if (!string.IsNullOrWhiteSpace(options.DatRoot))
            settings.Dat.DatRoot = options.DatRoot;

        return RunEnvironmentBuilder.Build(options, settings, dataDir, onWarning);
    }
}
