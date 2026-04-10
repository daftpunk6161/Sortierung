using System.IO.Compression;

namespace Romulus.Core.Classification;

/// <summary>
/// Abstracts I/O for classification detectors so Core stays testable.
/// Defaults to System.IO; tests can override via <see cref="Configure"/>.
/// Uses AsyncLocal so overrides are scoped to the calling execution context
/// and never leak across parallel test threads.
/// </summary>
internal static class ClassificationIo
{
    // ── Immutable defaults (shared, thread-safe) ─────────────────────
    private static readonly Func<string, bool> DefaultFileExists = File.Exists;
    private static readonly Func<string, Stream> DefaultOpenRead = path =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
    private static readonly Func<string, long> DefaultFileLength = path => new FileInfo(path).Length;
    private static readonly Func<string, FileAttributes> DefaultGetAttributes = File.GetAttributes;
    private static readonly Func<string, ZipArchive> DefaultOpenZipRead = ZipFile.OpenRead;

    // ── Per-context overrides (test isolation) ───────────────────────
    private static readonly AsyncLocal<Func<string, bool>?> FileExistsOverride = new();
    private static readonly AsyncLocal<Func<string, Stream>?> OpenReadOverride = new();
    private static readonly AsyncLocal<Func<string, long>?> FileLengthOverride = new();
    private static readonly AsyncLocal<Func<string, FileAttributes>?> GetAttributesOverride = new();
    private static readonly AsyncLocal<Func<string, ZipArchive>?> OpenZipReadOverride = new();

    public static bool FileExists(string path) => (FileExistsOverride.Value ?? DefaultFileExists)(path);
    public static Stream OpenRead(string path) => (OpenReadOverride.Value ?? DefaultOpenRead)(path);
    public static long FileLength(string path) => (FileLengthOverride.Value ?? DefaultFileLength)(path);
    public static FileAttributes GetAttributes(string path) => (GetAttributesOverride.Value ?? DefaultGetAttributes)(path);
    public static ZipArchive OpenZipRead(string path) => (OpenZipReadOverride.Value ?? DefaultOpenZipRead)(path);

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
    /// Reset to default System.IO delegates for the current execution context.
    /// </summary>
    public static void ResetDefaults()
    {
        FileExistsOverride.Value = null;
        OpenReadOverride.Value = null;
        FileLengthOverride.Value = null;
        GetAttributesOverride.Value = null;
        OpenZipReadOverride.Value = null;
    }
}
