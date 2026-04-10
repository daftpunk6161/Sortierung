using System.IO.Compression;
using Romulus.Contracts.Ports;

namespace Romulus.Core.Classification;

/// <summary>
/// Abstracts I/O for classification detectors so Core stays testable.
/// Runtime defaults are provided by Infrastructure via <see cref="Use"/>.
/// Tests can override per execution context via <see cref="Configure"/>.
/// Uses AsyncLocal so overrides are scoped to the calling execution context
/// and never leak across parallel test threads.
/// </summary>
public static class ClassificationIo
{
    private static IClassificationIo _default = CreateDefaultAdapter();

    // ── Per-context overrides (test isolation) ───────────────────────
    private static readonly AsyncLocal<Func<string, bool>?> FileExistsOverride = new();
    private static readonly AsyncLocal<Func<string, Stream>?> OpenReadOverride = new();
    private static readonly AsyncLocal<Func<string, long>?> FileLengthOverride = new();
    private static readonly AsyncLocal<Func<string, FileAttributes>?> GetAttributesOverride = new();
    private static readonly AsyncLocal<Func<string, ZipArchive>?> OpenZipReadOverride = new();

    public static void Use(IClassificationIo io)
    {
        ArgumentNullException.ThrowIfNull(io);
        _default = io;
    }

    public static bool FileExists(string path) => (FileExistsOverride.Value ?? _default.FileExists)(path);
    public static Stream OpenRead(string path) => (OpenReadOverride.Value ?? _default.OpenRead)(path);
    public static long FileLength(string path) => (FileLengthOverride.Value ?? _default.FileLength)(path);
    public static FileAttributes GetAttributes(string path) => (GetAttributesOverride.Value ?? _default.GetAttributes)(path);
    public static ZipArchive OpenZipRead(string path) => (OpenZipReadOverride.Value ?? _default.OpenZipRead)(path);

    /// <summary>
    /// Replace I/O delegates for the current execution context only.
    /// Pass null to keep current delegate.
    /// </summary>
    public static void Configure(
        Func<string, bool>? fileExists = null,
        Func<string, Stream>? openRead = null,
        Func<string, long>? fileLength = null,
        Func<string, FileAttributes>? getAttributes = null,
        Func<string, ZipArchive>? openZipRead = null)
    {
        if (fileExists is not null) FileExistsOverride.Value = fileExists;
        if (openRead is not null) OpenReadOverride.Value = openRead;
        if (fileLength is not null) FileLengthOverride.Value = fileLength;
        if (getAttributes is not null) GetAttributesOverride.Value = getAttributes;
        if (openZipRead is not null) OpenZipReadOverride.Value = openZipRead;
    }

    /// <summary>
    /// Reset delegate overrides for the current execution context.
    /// </summary>
    public static void ResetDefaults()
    {
        FileExistsOverride.Value = null;
        OpenReadOverride.Value = null;
        FileLengthOverride.Value = null;
        GetAttributesOverride.Value = null;
        OpenZipReadOverride.Value = null;
    }

    private sealed class UnconfiguredClassificationIo : IClassificationIo
    {
        private const string Message = "Classification I/O is not configured. Register IClassificationIo from Infrastructure before invoking detector logic.";

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

    private static IClassificationIo CreateDefaultAdapter()
    {
        try
        {
            var adapterType = Type.GetType("Romulus.Infrastructure.IO.ClassificationIo, Romulus.Infrastructure", throwOnError: false);
            if (adapterType is not null && Activator.CreateInstance(adapterType) is IClassificationIo adapter)
                return adapter;
        }
        catch
        {
            // Fall back to explicit configuration path.
        }

        return new UnconfiguredClassificationIo();
    }
}
