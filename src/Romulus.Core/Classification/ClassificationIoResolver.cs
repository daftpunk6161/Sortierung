using System.IO.Compression;
using Romulus.Contracts.Ports;

namespace Romulus.Core.Classification;

internal static class ClassificationIoResolver
{
    private static readonly Lazy<IClassificationIo> DefaultIo = new(CreateDefaultIo, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static IClassificationIo Resolve(IClassificationIo? io)
        => io ?? DefaultIo.Value;

    private static IClassificationIo CreateDefaultIo()
    {
        try
        {
            var adapterType = Type.GetType("Romulus.Infrastructure.IO.ClassificationIo, Romulus.Infrastructure", throwOnError: false);
            if (adapterType is not null && Activator.CreateInstance(adapterType) is IClassificationIo adapter)
                return adapter;
        }
        catch
        {
            // Fall back to explicit injection path.
        }

        return new UnconfiguredClassificationIo();
    }

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
