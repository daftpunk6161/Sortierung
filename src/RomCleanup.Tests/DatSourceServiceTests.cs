using RomCleanup.Infrastructure.Dat;
using Xunit;
using System.IO.Compression;
using System.Net;
using System.Net.Http;

namespace RomCleanup.Tests;

public class DatSourceServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DatSourceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "RomCleanup_DatSrc_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task VerifyDatSignature_CorrectSha256_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "test.dat");
        File.WriteAllText(path, "test content");

        // Compute actual SHA256
        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();

        using var svc = new DatSourceService(_tempDir);
        Assert.True(await svc.VerifyDatSignatureAsync(path, "", hash));
    }

    [Fact]
    public async Task VerifyDatSignature_WrongSha256_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "test.dat");
        File.WriteAllText(path, "test content");

        using var svc = new DatSourceService(_tempDir);
        Assert.False(await svc.VerifyDatSignatureAsync(path, "", "0000000000000000000000000000000000000000000000000000000000000000"));
    }

    [Fact]
    public async Task VerifyDatSignature_NonExistentFile_ReturnsFalse()
    {
        using var svc = new DatSourceService(_tempDir);
        Assert.False(await svc.VerifyDatSignatureAsync(
            Path.Combine(_tempDir, "nope.dat"), "", "abc123"));
    }

    [Fact]
    public async Task VerifyDatSignature_NoHashNoUrl_ReturnsTrue_HttpsIntegrity()
    {
        var path = Path.Combine(_tempDir, "test.dat");
        File.WriteAllText(path, "data");

        using var svc = new DatSourceService(_tempDir);
        // No expected hash, empty URL → allow (HTTPS provides integrity)
        Assert.True(await svc.VerifyDatSignatureAsync(path, "", null));
    }

    [Fact]
    public async Task VerifyDatSignature_ParallelRequests_CompleteWithoutDeadlock()
    {
        var path = Path.Combine(_tempDir, "parallel-test.dat");
        File.WriteAllText(path, "parallel-content");

        using var sha = System.Security.Cryptography.SHA256.Create();
        using var fs = File.OpenRead(path);
        var hash = Convert.ToHexString(sha.ComputeHash(fs)).ToLowerInvariant();

        using var svc = new DatSourceService(_tempDir);

        var tasks = Enumerable.Range(0, 64)
            .Select(_ => svc.VerifyDatSignatureAsync(path, "", hash))
            .ToArray();

        var all = Task.WhenAll(tasks);
        var completed = await Task.WhenAny(all, Task.Delay(TimeSpan.FromSeconds(10))) == all;

        Assert.True(completed, "Parallel signature verification timed out or deadlocked");
        Assert.All(await all, r => Assert.True(r));
    }

    [Fact]
    public async Task VerifyDatSignature_SidecarRequest_Cancellation_ReturnsTrue()
    {
        var path = Path.Combine(_tempDir, "cancel-test.dat");
        File.WriteAllText(path, "cancel-content");

        var handler = new DelayedOkHandler(TimeSpan.FromSeconds(5));
        using var httpClient = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(30) };
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat", null, cts.Token);
        sw.Stop();

        // Sidecar fetch cancelled → allow (HTTPS provides integrity)
        Assert.True(result);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(2), "Cancellation was not observed promptly");
    }

    [Fact]
    public void LoadCatalog_ValidJson_ParsesEntries()
    {
        var json = @"[
            { ""id"": ""redump-ps1"", ""group"": ""Redump"", ""system"": ""Sony - PS1"",
              ""url"": ""https://example.com/ps1.dat"", ""format"": ""zip-dat"", ""consoleKey"": ""PSX"" },
            { ""id"": ""nointro-nes"", ""group"": ""No-Intro"", ""system"": ""Nintendo NES"",
              ""url"": """", ""format"": ""nointro-pack"", ""consoleKey"": ""NES"", ""packMatch"": ""Nintendo*"" }
        ]";
        var path = Path.Combine(_tempDir, "catalog.json");
        File.WriteAllText(path, json);

        var entries = DatSourceService.LoadCatalog(path);
        Assert.Equal(2, entries.Count);
        Assert.Equal("redump-ps1", entries[0].Id);
        Assert.Equal("PSX", entries[0].ConsoleKey);
        Assert.Equal("Nintendo*", entries[1].PackMatch);
    }

    [Fact]
    public void LoadCatalog_NonExistent_ReturnsEmpty()
    {
        var entries = DatSourceService.LoadCatalog(Path.Combine(_tempDir, "nope.json"));
        Assert.Empty(entries);
    }

    [Fact]
    public void LoadCatalog_MalformedJson_ReturnsEmpty()
    {
        var path = Path.Combine(_tempDir, "bad.json");
        File.WriteAllText(path, "not json");

        var entries = DatSourceService.LoadCatalog(path);
        Assert.Empty(entries);
    }

    private sealed class DelayedOkHandler(TimeSpan delay) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Delay(delay, cancellationToken);

            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("0000000000000000000000000000000000000000000000000000000000000000")
            };
        }
    }

    [Fact]
    public async Task VerifyDatSignature_Sidecar404_ReturnsTrue_HttpsIntegrity()
    {
        var path = Path.Combine(_tempDir, "no-sidecar.dat");
        File.WriteAllText(path, "content without sidecar");

        var handler = new FixedStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        // Missing sidecar → allow (HTTPS already provides integrity)
        Assert.True(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    [Fact]
    public async Task VerifyDatSignature_Sidecar500_ReturnsTrue_HttpsIntegrity()
    {
        var path = Path.Combine(_tempDir, "server-error.dat");
        File.WriteAllText(path, "content with server error");

        var handler = new FixedStatusHandler(HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        // Sidecar endpoint error → allow (HTTPS provides integrity)
        Assert.True(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    // ═══ Path-Traversal Tests ═══════════════════════════════════════════

    [Theory]
    [InlineData("../escape.dat")]
    [InlineData("..\\escape.dat")]
    [InlineData("/etc/passwd")]
    [InlineData("C:\\windows\\evil.dat")]
    public async Task DownloadDatAsync_PathTraversal_ReturnsNull(string maliciousName)
    {
        var handler = new FixedStatusHandler(HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatAsync("https://example.invalid/test.dat", maliciousName);
        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadDatAsync_EmptyFileName_ReturnsNull()
    {
        var handler = new FixedStatusHandler(HttpStatusCode.OK);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        Assert.Null(await svc.DownloadDatAsync("https://example.invalid/test.dat", ""));
        Assert.Null(await svc.DownloadDatAsync("https://example.invalid/test.dat", "  "));
    }

    [Fact]
    public async Task DownloadDatAsync_ValidFileName_Succeeds()
    {
        var content = "<?xml version=\"1.0\"?><datafile/>";
        var handler = new ContentHandler(content);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatAsync("https://example.invalid/test.dat", "valid.dat");
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        Assert.Equal(content, File.ReadAllText(result!));
    }

    private sealed class ContentHandler(string content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? "";
            if (uri.EndsWith(".sha256"))
            {
                // Return a valid SHA256 sidecar so verification passes (fail-closed)
                var sha = System.Security.Cryptography.SHA256.HashData(
                    System.Text.Encoding.UTF8.GetBytes(content));
                var hex = Convert.ToHexString(sha).ToLowerInvariant();
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(hex)
                });
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(content)
            });
        }
    }

    // ═══ DownloadDatByFormatAsync (zip-dat) Tests ═══════════════════════

    [Fact]
    public async Task DownloadDatByFormatAsync_ZipDat_ExtractsDatFromZip()
    {
        var datContent = "<?xml version=\"1.0\"?><datafile><header><name>Test</name></header></datafile>";
        var zipBytes = CreateZipWithDat("Sony - PlayStation.dat", datContent);
        var handler = new ByteContentHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatByFormatAsync(
            "https://redump.org/datfile/ps1/", "redump-ps1.dat", "zip-dat");

        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        Assert.Equal(datContent, File.ReadAllText(result!));
    }

    [Fact]
    public async Task DownloadDatByFormatAsync_ZipDat_EmptyZip_ReturnsNull()
    {
        // ZIP with no .dat/.xml files
        var zipBytes = CreateZipWithDat("readme.txt", "not a dat file", "readme.txt");
        var handler = new ByteContentHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatByFormatAsync(
            "https://redump.org/datfile/ps1/", "redump-ps1.dat", "zip-dat");

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadDatByFormatAsync_RawDat_DelegatesToDirectDownload()
    {
        var content = "<?xml version=\"1.0\"?><datafile/>";
        var handler = new ContentHandler(content);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatByFormatAsync(
            "https://example.invalid/test.dat", "fbneo.dat", "raw-dat");

        Assert.NotNull(result);
        Assert.True(File.Exists(result));
        Assert.Equal(content, File.ReadAllText(result!));
    }

    [Fact]
    public async Task DownloadDatByFormatAsync_ZipDat_PathTraversal_ReturnsNull()
    {
        var zipBytes = CreateZipWithDat("test.dat", "<datafile/>");
        var handler = new ByteContentHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatByFormatAsync(
            "https://example.invalid/", "../escape.dat", "zip-dat");

        Assert.Null(result);
    }

    [Fact]
    public async Task DownloadDatByFormatAsync_CorruptZip_ReturnsNull()
    {
        var handler = new ByteContentHandler([0x50, 0x4B, 0x00, 0x00, 0xFF, 0xFF]);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatByFormatAsync(
            "https://example.invalid/", "test.dat", "zip-dat");

        Assert.Null(result);
    }

    private static byte[] CreateZipWithDat(string entryName, string content, string? overrideName = null)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var entry = archive.CreateEntry(overrideName ?? entryName);
            using var writer = new StreamWriter(entry.Open());
            writer.Write(content);
        }
        return ms.ToArray();
    }

    private sealed class ByteContentHandler(byte[] content) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? "";
            if (uri.EndsWith(".sha256"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(content)
            });
        }
    }
}
