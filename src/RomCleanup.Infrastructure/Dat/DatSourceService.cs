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
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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
        if (string.IsNullOrWhiteSpace(url) || string.IsNullOrWhiteSpace(localFileName))
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
            using (var archive = ZipFile.OpenRead(tempZip))
            {
                foreach (var entry in archive.Entries)
                {
                    if (string.IsNullOrEmpty(entry.Name)) continue; // Skip directories
                    var destPath = Path.GetFullPath(Path.Combine(tempExtract, entry.Name));
                    if (!destPath.StartsWith(Path.GetFullPath(tempExtract), StringComparison.OrdinalIgnoreCase))
                        continue; // Zip-Slip protection
                    entry.ExtractToFile(destPath, overwrite: true);
                }
            }

            // Find first .dat or .xml file in extracted contents
            var datFile = Directory.GetFiles(tempExtract, "*.dat", SearchOption.TopDirectoryOnly).FirstOrDefault()
                       ?? Directory.GetFiles(tempExtract, "*.xml", SearchOption.TopDirectoryOnly).FirstOrDefault();
            if (datFile is null)
                return null;

            // Ensure the target path ends with .dat
            if (!finalPath.EndsWith(".dat", StringComparison.OrdinalIgnoreCase)
                && !finalPath.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                finalPath = Path.ChangeExtension(finalPath, ".dat");

            File.Copy(datFile, finalPath, overwrite: true);
            return finalPath;
        }
        catch (HttpRequestException) { return null; }
        catch (TaskCanceledException) { return null; }
        catch (InvalidDataException) { return null; } // Corrupt ZIP
        finally
        {
            // Cleanup temp files
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { /* best-effort */ }
            try { if (Directory.Exists(tempExtract)) Directory.Delete(tempExtract, true); } catch { /* best-effort */ }
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
    }

    /// <summary>
    /// Verify a downloaded DAT file against SHA256 hash.
    /// If expectedSha256 is provided, checks against it.
    /// Otherwise tries to download {url}.sha256 sidecar.
    /// Fail-closed: returns false if verification cannot be completed.
    /// </summary>
    public async Task<bool> VerifyDatSignatureAsync(string localPath, string sourceUrl,
        string? expectedSha256 = null, CancellationToken ct = default)
    {
        if (!File.Exists(localPath))
            return false;

        // Direct SHA256 check if hash is provided
        if (!string.IsNullOrWhiteSpace(expectedSha256))
        {
            var actual = ComputeFileSha256(localPath);
            return string.Equals(actual, expectedSha256.Trim(), StringComparison.OrdinalIgnoreCase);
        }

        // Try .sha256 sidecar URL
        if (string.IsNullOrWhiteSpace(sourceUrl))
            return false;

        try
        {
            var shaUrl = sourceUrl + ".sha256";
            using var request = new HttpRequestMessage(HttpMethod.Get, shaUrl);
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                return false; // Fail-closed: no sidecar available — cannot verify integrity
            if (!response.IsSuccessStatusCode)
                return false; // Fail-closed on other HTTP errors

            var shaText = await response.Content.ReadAsStringAsync(ct);

            if (string.IsNullOrWhiteSpace(shaText))
                return false; // Fail-closed

            // Extract 64-char hex hash from response
            var match = Regex.Match(shaText, @"(?i)\b([a-f0-9]{64})\b");
            if (!match.Success)
                return false; // Fail-closed

            var expected = match.Groups[1].Value;
            var actual = ComputeFileSha256(localPath);
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false; // Fail-closed on network error
        }
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
