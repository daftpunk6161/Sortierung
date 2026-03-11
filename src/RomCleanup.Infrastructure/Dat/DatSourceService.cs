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

    public DatSourceService(string datRoot, IToolRunner? tools = null, HttpClient? httpClient = null)
    {
        _datRoot = datRoot ?? throw new ArgumentNullException(nameof(datRoot));
        _tools = tools;
        _http = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(60) };
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

        Directory.CreateDirectory(_datRoot);
        var localPath = Path.Combine(_datRoot, localFileName);

        try
        {
            using var response = await _http.GetAsync(url, ct);
            response.EnsureSuccessStatusCode();

            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            await File.WriteAllBytesAsync(localPath, bytes, ct);

            // Verify integrity
            if (!VerifyDatSignature(localPath, url, expectedSha256))
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
    public bool VerifyDatSignature(string localPath, string sourceUrl, string? expectedSha256 = null)
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
            var shaText = _http.GetStringAsync(shaUrl).GetAwaiter().GetResult();

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
