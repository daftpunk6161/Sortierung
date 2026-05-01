namespace Romulus.Tests.Benchmark.Generators;

/// <summary>
/// Registry of all available stub generators, keyed by generator ID.
/// Used by test infrastructure to produce stub files for ground-truth entries.
/// </summary>
internal sealed class StubGeneratorRegistry
{
    private readonly Dictionary<string, IStubGenerator> _generators = new(StringComparer.OrdinalIgnoreCase);

    public StubGeneratorRegistry()
    {
        // Cartridge header generators
        Register(new Cartridge.NesInesGenerator());
        Register(new Cartridge.SnesHeaderGenerator());
        Register(new Cartridge.N64HeaderGenerator());
        Register(new Cartridge.GbaHeaderGenerator());
        Register(new Cartridge.GbHeaderGenerator());
        Register(new Cartridge.MdHeaderGenerator());
        Register(new Cartridge.LynxHeaderGenerator());
        Register(new Cartridge.A7800HeaderGenerator());

        // Disc header generators
        Register(new Disc.Ps1PvdGenerator());
        Register(new Disc.Ps2PvdGenerator());
        Register(new Disc.Ps3PvdGenerator());
        Register(new Disc.SegaIpBinGenerator());
        Register(new Disc.NintendoDiscGenerator());
        Register(new Disc.MultiFileSetGenerator());
        Register(new Disc.OperaFsGenerator());
        Register(new Disc.BootSectorTextGenerator());
        Register(new Disc.XdvdfsGenerator());
        Register(new Disc.FmTownsPvdGenerator());
        Register(new Disc.CdiDiscGenerator());
        Register(new Disc.CcdImgGenerator());
        Register(new Disc.MdsMdfGenerator());
        Register(new Disc.M3uPlaylistGenerator());

        // Utility generators
        Register(new ExtOnlyGenerator());
        Register(new RandomBytesGenerator());
        Register(new NonRomContentGenerator());
    }

    public void Register(IStubGenerator generator)
    {
        _generators[generator.GeneratorId] = generator;
    }

    public IStubGenerator? Get(string generatorId)
    {
        return _generators.GetValueOrDefault(generatorId);
    }

    public IStubGenerator GetRequired(string generatorId)
    {
        return _generators.TryGetValue(generatorId, out var gen)
            ? gen
            : throw new KeyNotFoundException($"No stub generator registered for '{generatorId}'");
    }

    public IReadOnlyCollection<string> RegisteredIds => _generators.Keys;
}
