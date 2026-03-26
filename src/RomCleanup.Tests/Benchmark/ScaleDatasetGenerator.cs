namespace RomCleanup.Tests.Benchmark;

internal sealed class ScaleDatasetGenerator
{
    private static readonly (string Folder, string Extension)[] SystemProfiles =
    [
        ("nes", ".nes"), ("snes", ".sfc"), ("n64", ".z64"), ("gba", ".gba"), ("gb", ".gb"),
        ("gbc", ".gbc"), ("md", ".md"), ("ps1", ".iso"), ("ps2", ".iso"), ("dc", ".gdi"),
        ("sat", ".iso"), ("arcade", ".zip"), ("c64", ".d64"), ("amiga", ".adf"), ("dos", ".exe"),
        ("psp", ".iso"), ("gc", ".iso"), ("wii", ".iso"), ("jag", ".j64"), ("a26", ".a26")
    ];

    public IReadOnlyList<string> Generate(string rootDir, int count = 5000)
    {
        Directory.CreateDirectory(rootDir);

        var random = new Random(42);
        var paths = new List<string>(capacity: count);

        for (int i = 0; i < count; i++)
        {
            var profile = SystemProfiles[i % SystemProfiles.Length];
            string folder = random.NextDouble() switch
            {
                < 0.5 => profile.Folder,
                < 0.8 => "mixed",
                _ => "wrong-folder"
            };

            string extension = random.NextDouble() switch
            {
                < 0.7 => profile.Extension,
                < 0.9 => ".bin",
                _ => ".rom"
            };

            if (random.NextDouble() < 0.05)
            {
                extension = ".txt";
            }

            string fileName = $"scale-{i:000000}{extension}";
            string fullFolder = Path.Combine(rootDir, folder);
            Directory.CreateDirectory(fullFolder);
            string fullPath = Path.Combine(fullFolder, fileName);

            byte[] content = extension.ToLowerInvariant() switch
            {
                ".nes" => BuildNesStub(),
                ".iso" => BuildIsoPvdStub(),
                ".txt" => System.Text.Encoding.ASCII.GetBytes("not-a-rom"),
                _ => [0x00]
            };

            File.WriteAllBytes(fullPath, content);
            paths.Add(fullPath);
        }

        return paths;
    }

    private static byte[] BuildNesStub()
    {
        var bytes = new byte[16 + 16 * 1024];
        bytes[0] = 0x4E; // N
        bytes[1] = 0x45; // E
        bytes[2] = 0x53; // S
        bytes[3] = 0x1A;
        return bytes;
    }

    private static byte[] BuildIsoPvdStub()
    {
        var bytes = new byte[17 * 2048];
        int pvd = 16 * 2048;
        bytes[pvd] = 0x01;
        bytes[pvd + 1] = (byte)'C';
        bytes[pvd + 2] = (byte)'D';
        bytes[pvd + 3] = (byte)'0';
        bytes[pvd + 4] = (byte)'0';
        bytes[pvd + 5] = (byte)'1';
        return bytes;
    }
}
