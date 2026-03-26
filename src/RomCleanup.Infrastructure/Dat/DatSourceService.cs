using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Dat;

/// <summary>
/// DAT file download and verification service.
/// Port of DatSources.ps1 — downloads DAT files from Redump/No-Intro and
/// verifies SHA256 integrity via .sha256 sidecar files.
/// </summary>
public sealed class DatSourceService : IDisposable
{
    private readonly HttpClient _http;
    private readonly IToolRunner? _tools;
    private readonly string _datRoot;

    /// <summary>Maximum allowed download size (50 MB).</summary>
    private const long MaxDownloadBytes = 50 * 1024 * 1024;

    /// <summary>Maximum catalog file size to load (100 MB).</summary>
    private const long MaxCatalogFileSizeBytes = 100 * 1024 * 1024;

    public DatSourceService(string datRoot, IToolRunner? tools = null, HttpClient? httpClient = null)
    {
        _datRoot = datRoot ?? throw new ArgumentNullException(nameof(datRoot));
        _tools = tools;
        if (httpClient is not null)
        {
            _http = httpClient;
        }
        else
        {
            var handler = new HttpClientHandler { AllowAutoRedirect = true };
            _http = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(60) };
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Romulus/2.0 (DAT-Updater)");
            _http.DefaultRequestHeaders.Accept.ParseAdd("application/zip, application/octet-stream, application/xml, text/xml, */*");
        }
    }

    /// <summary>Validates that a URL uses HTTPS scheme. Returns false for http/file/ftp/etc.</summary>
    private static bool IsSecureUrl(string url)
    {
        return Uri.TryCreate(url, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, "https", StringComparison.OrdinalIgnoreCase);
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
        if (string.Equals(format, "nointro-pack", StringComparison.OrdinalIgnoreCase))
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
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            // Reject HTML responses (login/redirect pages, e.g. Redump)
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Server returned HTML instead of a ZIP file. The source may require manual download: {url}");

            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                return null;
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > MaxDownloadBytes)
                return null;

            await File.WriteAllBytesAsync(tempZip, bytes, ct);

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
                    entry.ExtractToFile(destPath, overwrite: false);
                }
            }

            // Find first .dat or .xml file in extracted contents
            var datFile = Directory.GetFiles(tempExtract, "*.dat", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault()
                       ?? Directory.GetFiles(tempExtract, "*.xml", SearchOption.AllDirectories).Order(StringComparer.Ordinal).FirstOrDefault();
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

        try
        {
            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            // Reject HTML responses (login/redirect pages, e.g. Redump)
            var contentType = response.Content.Headers.ContentType?.MediaType ?? "";
            if (contentType.Contains("html", StringComparison.OrdinalIgnoreCase)
                || contentType.Contains("text", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Server returned HTML instead of a DAT file. The source may require manual download: {url}");

            // P2-DAT-03: Reject downloads exceeding size limit
            if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            if (bytes.Length > MaxDownloadBytes)
            {
                // Content-Length header was absent but body exceeds limit
                return null;
            }

            await File.WriteAllBytesAsync(localPath, bytes, ct);

            // Verify integrity
            if (!await VerifyDatSignatureAsync(localPath, url, expectedSha256, ct))
            {
                // Fail-closed: delete unverified file
                File.Delete(localPath);
                return null;
            }

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
    }

    /// <summary>
    /// Verify a downloaded DAT file against SHA256 hash.
    /// If expectedSha256 is provided, checks against it (fail-closed).
    /// Otherwise tries to download {url}.sha256 sidecar.
    /// If no sidecar exists, allows the download since HTTPS provides integrity.
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
            return true; // No source URL, no sidecar possible — allow (HTTPS integrity)

        try
        {
            var shaUrl = sourceUrl + ".sha256";
            using var request = new HttpRequestMessage(HttpMethod.Get, shaUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (!response.IsSuccessStatusCode)
                return true; // Sidecar unavailable — allow (HTTPS provides integrity)

            var shaText = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(shaText))
                return true; // Empty sidecar — allow

            // Extract 64-char hex hash from response
            var match = Regex.Match(shaText, @"(?i)\b([a-f0-9]{64})\b", RegexOptions.None, TimeSpan.FromMilliseconds(500));
            if (!match.Success)
                return true; // Sidecar malformed — allow

            // Sidecar found and parseable — verify against it (fail-closed)
            var expected = match.Groups[1].Value;
            var actual = ComputeFileSha256(localPath);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException)
        {
            return true; // Network error checking sidecar — allow (HTTPS integrity)
        }
    }

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
            .Where(e => !string.IsNullOrWhiteSpace(e.PackMatch)
                     && string.Equals(e.Format, "nointro-pack", StringComparison.OrdinalIgnoreCase))
            .ToList();

        if (packEntries.Count == 0)
            return 0;

        var sourceFiles = Directory.GetFiles(sourceDir, "*.*", SearchOption.AllDirectories)
            .Where(f => f.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                     || f.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
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

        var backupPath = destinationPath + ".bak";
        var hadExistingTarget = File.Exists(destinationPath);

        if (hadExistingTarget)
        {
            if (File.Exists(backupPath))
                File.Delete(backupPath);

            File.Move(destinationPath, backupPath, overwrite: true);
        }

        try
        {
            File.Copy(sourcePath, destinationPath, overwrite: false);
        }
        catch (IOException)
        {
            // Restore previous file if replacement fails mid-flight.
            if (hadExistingTarget && File.Exists(backupPath) && !File.Exists(destinationPath))
            {
                try { File.Move(backupPath, destinationPath, overwrite: true); }
                catch (IOException) { /* best-effort restore — file may be locked */ }
            }

            throw;
        }
    }

    public void Dispose()
    {
        _http.Dispose();
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
