using System.IO.Compression;
using Romulus.Contracts.Ports;

namespace Romulus.Core.Classification;

public static class ClassificationIoResolver
{
    private static readonly object DefaultGate = new();
    private static Lazy<IClassificationIo> _defaultIo = new(CreateUnconfiguredIo, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void ConfigureDefault(Func<IClassificationIo> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (DefaultGate)
            _defaultIo = new Lazy<IClassificationIo>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal static IClassificationIo Resolve(IClassificationIo? io)
        => io ?? _defaultIo.Value;

    private static IClassificationIo CreateUnconfiguredIo()
        => new UnconfiguredClassificationIo();

    private sealed class UnconfiguredClassificationIo : IClassificationIo
    {
        private const string Message = "Classification I/O is not configured. Inject IClassificationIo from Infrastructure before invoking detector logic.";

        public bool FileExists(string path)
            => throw new InvalidOperationException(Message);

        public Stream OpenRead(string path)
            => throw new InvalidOperationException(Message);

        public long FileLength(string path)
            => throw new InvalidOperationException(Message);

        public FileAttributes GetAttributes(string path)
            => throw new InvalidOperationException(Message);

        public ZipArchive OpenZipRead(string path)
            => throw new InvalidOperationException(Message);
    }
}
