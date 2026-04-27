using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Romulus.Contracts;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;

namespace Romulus.Infrastructure.Dat;

/// <summary>
/// DAT file download and verification service.
/// Port of DatSources.ps1 — downloads DAT files from Redump/No-Intro and
/// verifies SHA256 integrity via .sha256 sidecar files.
/// </summary>
public sealed class DatSourceService : IDisposable
{
    public const string HttpClientName = "Romulus.DatSourceService";
    private static readonly Lazy<HttpClient> SharedHttpClient = new(CreateConfiguredHttpClient);

    private readonly HttpClient _http;
    private readonly IToolRunner? _tools;
    private readonly string _datRoot;
    private readonly bool _strictSidecarValidation;
    private readonly ILogger<DatSourceService>? _logger;

    /// <summary>Maximum allowed download size (50 MB).</summary>
    private const long MaxDownloadBytes = 50 * 1024 * 1024;

    /// <summary>Maximum catalog file size to load (100 MB).</summary>
    private const long MaxCatalogFileSizeBytes = 100 * 1024 * 1024;
    private static readonly string[] AllowedDownloadHosts =
    [
        "github.com",
        "raw.githubusercontent.com",
        "datomatic.no-intro.org",
        "redump.org",
        "www.redump.org",
        // Reserved non-routable host used by injected test handlers.
        "example.invalid"
    ];

    // F-DAT-01: Strict sidecar validation is now ON by default (release-grade integrity).
    // Callers that need the legacy permissive behaviour (allow missing/unparseable .sha256
    // sidecars and trust HTTPS transport-level integrity only) must opt-in explicitly.
    public DatSourceService(
        string datRoot,
        HttpClient httpClient,
        IToolRunner? tools = null,
        bool strictSidecarValidation = true,
        ILogger<DatSourceService>? logger = null)
    {
        _datRoot = datRoot ?? throw new ArgumentNullException(nameof(datRoot));
        _http = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _tools = tools;
        _strictSidecarValidation = strictSidecarValidation;
        _logger = logger;
    }

    public DatSourceService(
        string datRoot,
        IToolRunner? tools = null,
        bool strictSidecarValidation = true,
        ILogger<DatSourceService>? logger = null)
        : this(datRoot, SharedHttpClient.Value, tools, strictSidecarValidation, logger)
    {
    }

    public static HttpClient CreateConfiguredHttpClient()
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = false };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Romulus/2.0 (DAT-Updater)");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/zip, application/octet-stream, application/xml, text/xml, */*");
        return client;
    }

    /// <summary>Validates that a URL uses HTTPS and an explicit trusted public DAT host.</summary>
    private static bool IsSecureUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && IsAllowedDatSourceUri(uri);
    }

    private static bool IsAllowedDatSourceUri(Uri uri)
    {
        if (!string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
            return false;

        if (string.IsNullOrWhiteSpace(uri.Host))
            return false;

        if (IPAddress.TryParse(uri.Host, out var ip))
            return IsAllowedPublicAddress(ip);

        return AllowedDownloadHosts.Any(host =>
            string.Equals(uri.Host, host, StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsAllowedPublicAddress(IPAddress ip)
    {
        if (IPAddress.IsLoopback(ip))
            return false;

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
        {
            var b = ip.GetAddressBytes();
            return !(b[0] == 10
                     || (b[0] == 172 && b[1] >= 16 && b[1] <= 31)
                     || (b[0] == 192 && b[1] == 168)
                     || (b[0] == 169 && b[1] == 254)
                     || b[0] == 0
                     || b[0] >= 224);
        }

        if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6)
            return !(ip.IsIPv6LinkLocal || ip.IsIPv6Multicast || ip.IsIPv6SiteLocal);

        return false;
    }

    /// <summary>
    /// Download a DAT file from URL to datRoot directory, handling format-specific extraction.
    /// <list type="bullet">
    /// <item><c>raw-dat</c>: Direct download of .dat content.</item>
    /// <item><c>zip-dat</c>: Downloads a ZIP, extracts the first .dat/.xml file inside.</item>
    /// <item><c>7z-dat</c>: Downloads a 7z archive; extraction requires external 7z tool, logs warning if unavailable.</item>
    /// </list>
    /// Returns the local path on success, null on failure.
    /// </summary>
    public async Task<string?> DownloadDatByFormatAsync(string url, string localFileName,
        string format, string? expectedSha256 = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(localFileName))
            return null;

        // nointro-pack: no URL, local scan only
        if (string.Equals(format, RunConstants.FormatNoIntroPack, StringComparison.OrdinalIgnoreCase))
            return null;

        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!IsSecureUrl(url))
            return null;

        if (string.Equals(format, "zip-dat", StringComparison.OrdinalIgnoreCase))
            return await DownloadZipDatAsync(url, localFileName, expectedSha256, ct);

        // raw-dat and other formats: delegate to existing method
        return await DownloadDatAsync(url, localFileName, expectedSha256, ct);
    }

    /// <summary>
    /// Downloads a ZIP file, extracts the first .dat or .xml file from it,
    /// and stores it locally. Mirrors the PowerShell Invoke-DatDownload logic for zip-dat format.
    /// </summary>
    private async Task<string?> DownloadZipDatAsync(string url, string localFileName,
        string? expectedSha256, CancellationToken ct)
    {
        Directory.CreateDirectory(_datRoot);
        var finalPath = Path.GetFullPath(Path.Combine(_datRoot, localFileName));
        if (!finalPath.StartsWith(Path.GetFullPath(_datRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !finalPath.Equals(Path.GetFullPath(_datRoot), StringComparison.OrdinalIgnoreCase))
            return null;

        var tempZip = Path.Combine(Path.GetTempPath(), $"dat_download_{Guid.NewGuid():N}.zip");
        var tempExtract = Path.Combine(Path.GetTempPath(), $"dat_extract_{Guid.NewGuid():N}");
        try
        {
            // Download ZIP to temp
            using var response = await ExecuteHttpWithRetryAsync(
                token => GetWithValidatedRedirectsAsync(url, "zip-dat download", token),
                operationName: "zip-dat download",
                ct);
            response.EnsureSuccessStatusCode();

            // Reject HTML responses (login/redirect pages, e.g. Redump)
            // Allow text/plain and text/xml — some servers serve DATs as text/*
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Server returned HTML instead of a ZIP file. The source may require manual download: {url}");

            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                return null;

            await using (var responseStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var tempZipStream = new FileStream(
                tempZip,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.SequentialScan))
            {
                var copied = await CopyWithSizeLimitAsync(responseStream, tempZipStream, MaxDownloadBytes, ct);
                if (!copied)
                    return null;
            }

            // Verify ZIP integrity before extraction (if SHA256 available)
            if (!string.IsNullOrWhiteSpace(expectedSha256))
            {
                var actual = ComputeFileSha256(tempZip);
                if (!string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase))
                    return null;
            }

            // Extract ZIP — use safe extraction with Zip-Slip protection
            Directory.CreateDirectory(tempExtract);
            var normalizedExtractRoot = Path.GetFullPath(tempExtract).TrimEnd(Path.DirectorySeparatorChar)
                                      + Path.DirectorySeparatorChar;
            using (var archive = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directories

                    // Preserve archive structure and fail closed on Zip-Slip attempts.
                    var destPath = Path.GetFullPath(Path.Combine(tempExtract, entry.FullName));
                    if (!destPath.StartsWith(normalizedExtractRoot, StringComparison.OrdinalIgnoreCase))
                        return null;

                    var parentDir = Path.GetDirectoryName(destPath);
                    if (!string.IsNullOrEmpty(parentDir))
                        Directory.CreateDirectory(parentDir);

                    // Do not overwrite extracted files to avoid ambiguous archive collisions.
                    // R5-009 FIX: Handle IOException for already-existing files gracefully
                    // instead of crashing the entire extraction loop.
                    // F-DAT-08: a duplicate inner entry is a real audit signal — surface it
                    // through the logger instead of swallowing it silently. We still skip the
                    // collision (do not overwrite) so behaviour stays fail-safe.
                    try
                    {
                        entry.ExtractToFile(destPath, overwrite: false);
                    }
                    catch (IOException ex) when (File.Exists(destPath))
                    {
                        _logger?.LogWarning(
                            "ZIP inner collision: entry '{Entry}' from '{Url}' already extracted; keeping first occurrence (audit). Detail: {Detail}",
                            entry.FullName,
                            url,
                            ex.Message);
                    }
                    catch (IOException) { /* unrelated I/O failure on this entry — skip silently for collision safety. */ }
                }
            }

            // Find first .dat or .xml file in extracted contents
            var datFile = new FileSystemAdapter()
                .GetFilesSafe(tempExtract, [".dat", ".xml"])
                .Order(StringComparer.Ordinal)
                .FirstOrDefault();
            if (datFile is null)
                return null;

            // Ensure the target path ends with .dat
            if (!finalPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                && !finalPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                finalPath = Path.ChangeExtension(finalPath, ".dat");

            ReplaceWithBackup(datFile, finalPath);
            return finalPath;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (InvalidOperationException) { throw; } // Propagate HTML detection errors
        catch (InvalidDataException) { return null; } // Corrupt ZIP
        finally
        {
            // Cleanup temp files — specific exceptions to avoid hiding unexpected errors
            try { if (File.Exists(tempZip)) File.Delete(tempZip); }
            catch (IOException) { /* file locked — will be cleaned on next run or OS temp cleanup */ }
            catch (UnauthorizedAccessException) { /* permission denied — non-fatal for temp files */ }
            try { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true); }
            catch (IOException) { /* dir locked — will be cleaned on next run or OS temp cleanup */ }
            catch (UnauthorizedAccessException) { /* permission denied — non-fatal for temp dirs */ }
        }
    }

    /// <summary>
    /// Download a DAT file from URL to datRoot directory.
    /// Verifies integrity via SHA256 sidecar if available.
    /// Returns the local path on success, null on failure.
    /// </summary>
    public async Task<string?> DownloadDatAsync(string url, string localFileName,
        string? expectedSha256 = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;

        if (!IsSecureUrl(url))
            return null;

        // Path-traversal guard: localFileName must resolve within datRoot
        if (string.IsNullOrWhiteSpace(localFileName))
            return null;
        Directory.CreateDirectory(_datRoot);
        var localPath = Path.GetFullPath(Path.Combine(_datRoot, localFileName));
        if (!localPath.StartsWith(Path.GetFullPath(_datRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !localPath.Equals(Path.GetFullPath(_datRoot), StringComparison.OrdinalIgnoreCase))
            return null;

        var tempDownloadPath = Path.Combine(Path.GetTempPath(), $"dat_download_{Guid.NewGuid():N}.tmp");

        try
        {
            using var response = await ExecuteHttpWithRetryAsync(
                token => GetWithValidatedRedirectsAsync(url, "dat download", token),
                operationName: "dat download",
                ct);
            response.EnsureSuccessStatusCode();

            // Reject HTML responses (login/redirect pages, e.g. Redump)
            // Allow text/plain and text/xml — GitHub raw URLs serve .dat files as text/plain
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Server returned HTML instead of a DAT file. The source may require manual download: {url}");

            // P2-DAT-03: Reject downloads exceeding size limit
            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                return null;

            await using (var responseStream = await response.Content.ReadAsStreamAsync(ct))
            await using (var localFileStream = new FileStream(
                tempDownloadPath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 81920,
                FileOptions.SequentialScan))
            {
                var copied = await CopyWithSizeLimitAsync(responseStream, localFileStream, MaxDownloadBytes, ct);
                if (!copied)
                {
                    localFileStream.Close();
                    if (File.Exists(tempDownloadPath))
                        File.Delete(tempDownloadPath);
                    return null;
                }
            }

            // Verify integrity on temp file before replacing the existing DAT.
            // This keeps the previous file intact on network/signature failures.
            if (!await VerifyDatSignatureAsync(tempDownloadPath, url, expectedSha256, ct))
                return null;

            ReplaceWithBackup(tempDownloadPath, localPath);

            return localPath;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            throw; // Propagate HTML detection errors
        }
        finally
        {
            try
            {
                if (File.Exists(tempDownloadPath))
                    File.Delete(tempDownloadPath);
            }
            catch (IOException)
            {
                // non-fatal temp cleanup failure
            }
            catch (UnauthorizedAccessException)
            {
                // non-fatal temp cleanup failure
            }
        }
    }

    private static async Task<bool> CopyWithSizeLimitAsync(
        Stream source,
        Stream destination,
        long maxBytes,
        CancellationToken ct)
    {
        var buffer = new byte[81920];
        long totalBytes = 0;
        while (true)
        {
            var read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), ct);
            if (read <= 0)
                break;

            totalBytes += read;
            if (totalBytes > maxBytes)
                return false;

            await destination.WriteAsync(buffer.AsMemory(0, read), ct);
        }

        return true;
    }

    /// <summary>
    /// Verify a downloaded DAT file against SHA256 hash.
    /// If expectedSha256 is provided, checks against it (fail-closed).
    /// Otherwise tries to download {url}.sha256 sidecar.
    /// If no sidecar exists, allows the download since HTTPS provides integrity
    /// unless strict sidecar validation is enabled.
    /// </summary>
    public async Task<bool> VerifyDatSignatureAsync(string localPath, string sourceUrl,
        string? expectedSha256 = null, CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
            return false;

        // Direct SHA256 check if hash is provided — fail-closed
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actual = ComputeFileSha256(localPath);
            return string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // Try .sha256 sidecar URL — but allow if sidecar is unavailable
        // HTTPS already provides transport-level integrity
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return !_strictSidecarValidation;

        try
        {
            var shaUrl = sourceUrl + ".sha256";
            using var response = await ExecuteHttpWithRetryAsync(
                async token =>
                {
                    return await GetWithValidatedRedirectsAsync(shaUrl, "dat sidecar download", token).ConfigureAwait(false);
                },
                operationName: "dat sidecar download",
                ct);
            if (!response.IsSuccessStatusCode)
                return !_strictSidecarValidation;

            var shaText = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(shaText))
                return !_strictSidecarValidation;

            // Extract 64-char hex hash from response
            var match = Regex.Match(shaText, @"(?i)\b([a-f0-9]{64})\b", RegexOptions.None, TimeSpan.FromMilliseconds(500));
            if (!match.Success)
                return !_strictSidecarValidation;

            // Sidecar found and parseable — verify against it (fail-closed)
            var expected = match.Groups[1].Value;
            var actual = ComputeFileSha256(localPath);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or RegexMatchTimeoutException)
        {
            return !_strictSidecarValidation;
        }
    }

    private async Task<T> ExecuteHttpWithRetryAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        string operationName,
        CancellationToken ct)
    {
        const int maxAttempts = 3;
        var delay = TimeSpan.FromMilliseconds(250);

        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await operation(ct).ConfigureAwait(false);
            }
            catch (HttpRequestException ex) when (attempt < maxAttempts)
            {
                _logger?.LogWarning(ex, "HTTP operation {Operation} failed (attempt {Attempt}/{MaxAttempts}); retrying.", operationName, attempt, maxAttempts);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested && attempt < maxAttempts)
            {
                _logger?.LogWarning(ex, "HTTP operation {Operation} timed out (attempt {Attempt}/{MaxAttempts}); retrying.", operationName, attempt, maxAttempts);
            }

            await Task.Delay(delay, ct).ConfigureAwait(false);
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 2_000));
        }
    }

    private async Task<HttpResponseMessage> GetWithValidatedRedirectsAsync(
        string url,
        string operationName,
        CancellationToken ct)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var currentUri)
            || !IsAllowedDatSourceUri(currentUri))
        {
            throw new HttpRequestException($"Blocked unsafe DAT source URL for {operationName}.");
        }

        const int maxRedirects = 5;
        for (var redirect = 0; redirect <= maxRedirects; redirect++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, currentUri);
            var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
            if (!IsRedirectStatus(response.StatusCode))
                return response;

            var location = response.Headers.Location;
            response.Dispose();
            if (location is null)
                throw new HttpRequestException($"DAT source redirect without Location for {operationName}.");

            currentUri = location.IsAbsoluteUri ? location : new Uri(currentUri, location);
            if (!IsAllowedDatSourceUri(currentUri))
                throw new HttpRequestException($"Blocked unsafe DAT source redirect for {operationName}.");
        }

        throw new HttpRequestException($"Too many DAT source redirects for {operationName}.");
    }

    private static bool IsRedirectStatus(HttpStatusCode statusCode)
        => statusCode is HttpStatusCode.Moved
            or HttpStatusCode.Found
            or HttpStatusCode.RedirectMethod
            or HttpStatusCode.TemporaryRedirect
            or HttpStatusCode.PermanentRedirect;

    /// <summary>
    /// Scan a local directory for DAT files matching No-Intro pack patterns from the catalog.
    /// Copies matching files into datRoot. Returns number of DATs imported.
    /// Similar to RomVault's local DAT pack scanning.
    /// </summary>
    public int ImportLocalDatPacks(string sourceDir, IReadOnlyList<DatCatalogEntry> catalog)
    {
        if (!Directory.Exists(sourceDir))
            return 0;

        var datRoot = Path.GetFullPath(_datRoot);
        Directory.CreateDirectory(datRoot);

        var packEntries = catalog
            .Where(e => !string.IsNullOrWhiteSpace(e.PackMatch))
            .ToList();

        if (packEntries.Count == 0)
            return 0;

        var sourceFiles = new FileSystemAdapter().GetFilesSafe(sourceDir, [".dat", ".xml"])
            .Order(StringComparer.Ordinal)
            .ToList();

        int imported = 0;
        foreach (var entry in packEntries)
        {
            var pattern = entry.PackMatch!;
            var isWildcard = pattern.EndsWith('*');
            var prefix = isWildcard ? pattern[..^1] : pattern;

            var match = sourceFiles.FirstOrDefault(f =>
            {
                var name = Path.GetFileNameWithoutExtension(f)!;
                return isWildcard
                    ? name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                    : name.Equals(pattern, StringComparison.OrdinalIgnoreCase);
            });

            if (isWildcard)
            {
                match = sourceFiles
                    .Where(f => Path.GetFileNameWithoutExtension(f)!
                        .StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(f => f, StringComparer.OrdinalIgnoreCase)
                    .FirstOrDefault();
            }

            if (match is null) continue;

            var targetName = entry.Id + Path.GetExtension(match);
            var targetPath = Path.GetFullPath(Path.Combine(datRoot, targetName));

            // Path-traversal guard
            if (!targetPath.StartsWith(datRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                && !targetPath.Equals(datRoot, StringComparison.OrdinalIgnoreCase))
                continue;

            ReplaceWithBackup(match, targetPath);
            imported++;

        }

        return imported;
    }

    /// <summary>
    /// Load catalogue entries from a dat-catalog.json file.
    /// </summary>
    public static List<DatCatalogEntry> LoadCatalog(string catalogPath)
    {
        if (!File.Exists(catalogPath))
            return new List<DatCatalogEntry>();

        try
        {
            var fileSize = new FileInfo(catalogPath).Length;
            if (fileSize > MaxCatalogFileSizeBytes)
                return new List<DatCatalogEntry>();

            var json = File.ReadAllText(catalogPath);
            var entries = JsonSerializer.Deserialize<List<DatCatalogEntry>>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return entries ?? new List<DatCatalogEntry>();
        }
        catch (JsonException)
        {
            return new List<DatCatalogEntry>();
        }
    }

    private static string ComputeFileSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hashBytes = sha256.ComputeHash(stream);
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }

    private static void ReplaceWithBackup(string sourcePath, string destinationPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
            throw new ArgumentException("Source path must not be empty.", nameof(sourcePath));
        if (string.IsNullOrWhiteSpace(destinationPath))
            throw new ArgumentException("Destination path must not be empty.", nameof(destinationPath));

        var backupPath = destinationPath + $".{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.bak";
        var hadExistingTarget = File.Exists(destinationPath);

        if (hadExistingTarget)
            AtomicFileWriter.CopyFile(destinationPath, backupPath, overwrite: false);

        try
        {
            AtomicFileWriter.CopyFile(sourcePath, destinationPath, overwrite: true);
            // Touch timestamp so staleness check reflects import time, not source file age
            File.SetLastWriteTimeUtc(destinationPath, DateTime.UtcNow);
        }
        catch (IOException)
        {
            // Restore previous file if replacement fails mid-flight.
            if (hadExistingTarget && File.Exists(backupPath))
            {
                TryDeletePath(destinationPath);
                TryRestoreFromBackup(backupPath, destinationPath);
            }

            throw;
        }
        catch (UnauthorizedAccessException)
        {
            // Restore previous file if replacement fails mid-flight.
            if (hadExistingTarget && File.Exists(backupPath))
            {
                TryDeletePath(destinationPath);
                TryRestoreFromBackup(backupPath, destinationPath);
            }

            throw;
        }
    }

    private static void TryDeletePath(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            // SUPPRESSED: best-effort restore cleanup.
        }
        catch (UnauthorizedAccessException)
        {
            // SUPPRESSED: best-effort restore cleanup.
        }
    }

    private static void TryRestoreFromBackup(string backupPath, string destinationPath)
    {
        try
        {
            AtomicFileWriter.CopyFile(backupPath, destinationPath, overwrite: true);
        }
        catch (IOException)
        {
            // SUPPRESSED: best-effort restore — backup may stay for manual recovery.
        }
        catch (UnauthorizedAccessException)
        {
            // SUPPRESSED: best-effort restore — backup may stay for manual recovery.
        }
    }

    public void Dispose()
    {
        // Intentionally no-op: HttpClient lifetime is managed by caller or shared singleton.
    }
}

/// <summary>
/// Entry in dat-catalog.json describing a DAT source.
/// </summary>
public sealed class DatCatalogEntry
{
    public string Id { get; set; } = "";
    public string Group { get; set; } = "";
    public string System { get; set; } = "";
    public string Url { get; set; } = "";
    public string Format { get; set; } = "";
    public string ConsoleKey { get; set; } = "";
    public string? PackMatch { get; set; }
}
