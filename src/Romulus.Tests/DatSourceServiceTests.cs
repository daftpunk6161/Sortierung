using Romulus.Infrastructure.Dat;
using Xunit;
using System.IO.Compression;
using System.Net;
using System.Net.Http;
using System.Reflection;

namespace Romulus.Tests;

public class DatSourceServiceTests : IDisposable
{
    private readonly string _tempDir;

    public DatSourceServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "Romulus_DatSrc_" + Guid.NewGuid().ToString("N")[..8]);
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

        // F-DAT-01: this test asserts the legacy permissive behaviour, so it must
        // explicitly opt out of the strict sidecar validation default.
        using var svc = new DatSourceService(_tempDir, strictSidecarValidation: false);
        // No expected hash, empty URL -> allow (HTTPS provides integrity)
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
        // Permissive mode is opt-in (F-DAT-01) — this test asserts the legacy "trust HTTPS on cancel" branch.
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: false);

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        var sw = System.Diagnostics.Stopwatch.StartNew();
        var result = await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat", null, cts.Token);
        sw.Stop();

        // Sidecar fetch cancelled -> allow (HTTPS provides integrity)
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
    public void ImportLocalDatPacks_Wildcard_PicksNewestStemAcrossFolders()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        var olderDir = Path.Combine(sourceDir, "zzz-older");
        var newerDir = Path.Combine(sourceDir, "aaa-newer");
        Directory.CreateDirectory(olderDir);
        Directory.CreateDirectory(newerDir);

        var older = Path.Combine(olderDir, "Nintendo - Nintendo Entertainment System (Headered) (20260301-000000).dat");
        var newer = Path.Combine(newerDir, "Nintendo - Nintendo Entertainment System (Headered) (20260327-000000).dat");
        File.WriteAllText(older, "old");
        File.WriteAllText(newer, "new");

        var catalog = new[]
        {
            new DatCatalogEntry
            {
                Id = "nointro-nes",
                Group = "No-Intro",
                Format = "nointro-pack",
                PackMatch = "Nintendo - Nintendo Entertainment System (Headered)*"
            }
        };

        using var svc = new DatSourceService(_tempDir);
        var imported = svc.ImportLocalDatPacks(sourceDir, catalog);

        Assert.Equal(1, imported);
        var target = Path.Combine(_tempDir, "nointro-nes.dat");
        Assert.True(File.Exists(target));
        Assert.Equal("new", File.ReadAllText(target));
    }

    [Fact]
    public void ImportLocalDatPacks_RedumpFormat_MatchesByPackMatch()
    {
        var sourceDir = Path.Combine(_tempDir, "source", "Redump");
        Directory.CreateDirectory(sourceDir);

        var sourceDat = Path.Combine(sourceDir, "Panasonic - 3DO Interactive Multiplayer - Datfile (73) (2026-01-15).dat");
        File.WriteAllText(sourceDat, "redump-3do-content");

        var catalog = new[]
        {
            new DatCatalogEntry
            {
                Id = "redump-3do",
                Group = "Redump",
                System = "Panasonic - 3DO Interactive Multiplayer",
                Format = "zip-dat",
                PackMatch = "Panasonic - 3DO Interactive Multiplayer - Datfile*"
            }
        };

        using var svc = new DatSourceService(_tempDir);
        var imported = svc.ImportLocalDatPacks(sourceDir, catalog);

        Assert.Equal(1, imported);
        var target = Path.Combine(_tempDir, "redump-3do.dat");
        Assert.True(File.Exists(target));
        Assert.Equal("redump-3do-content", File.ReadAllText(target));
    }

    [Fact]
    public async Task VerifyDatSignature_Sidecar404_ReturnsTrue_HttpsIntegrity()
    {
        var path = Path.Combine(_tempDir, "no-sidecar.dat");
        File.WriteAllText(path, "content without sidecar");

        var handler = new FixedStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        // Permissive mode is now opt-in (F-DAT-01).
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: false);

        // Missing sidecar -> allow (HTTPS already provides integrity)
        Assert.True(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    [Fact]
    public async Task VerifyDatSignature_Sidecar500_ReturnsTrue_HttpsIntegrity()
    {
        var path = Path.Combine(_tempDir, "server-error.dat");
        File.WriteAllText(path, "content with server error");

        var handler = new FixedStatusHandler(HttpStatusCode.InternalServerError);
        using var httpClient = new HttpClient(handler);
        // Permissive mode is now opt-in (F-DAT-01).
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: false);

        // Sidecar endpoint error -> allow (HTTPS provides integrity)
        Assert.True(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    // F-DAT-01 RED -> GREEN: default constructor must be strict (fail closed when sidecar absent).
    [Fact]
    public async Task VerifyDatSignature_DefaultConstructor_NoSidecar_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "default-strict.dat");
        File.WriteAllText(path, "content");

        var handler = new FixedStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        // No explicit strictSidecarValidation argument: must default to strict (true).
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        Assert.False(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    [Fact]
    public async Task VerifyDatSignature_DefaultConstructor_NoUrl_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "default-strict-no-url.dat");
        File.WriteAllText(path, "content");

        // Default ctor (no httpClient, no strict flag) — must fail closed without sidecar/URL.
        using var svc = new DatSourceService(_tempDir);
        Assert.False(await svc.VerifyDatSignatureAsync(path, sourceUrl: "", expectedSha256: null));
    }

    [Fact]
    public async Task VerifyDatSignature_StrictMode_NoUrl_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "strict-no-url.dat");
        File.WriteAllText(path, "strict");

        using var svc = new DatSourceService(_tempDir, strictSidecarValidation: true);
        Assert.False(await svc.VerifyDatSignatureAsync(path, "", null));
    }

    [Fact]
    public async Task VerifyDatSignature_StrictMode_Sidecar404_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "strict-404.dat");
        File.WriteAllText(path, "strict-404");

        var handler = new FixedStatusHandler(HttpStatusCode.NotFound);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: true);

        Assert.False(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    [Fact]
    public async Task VerifyDatSignature_StrictMode_MalformedSidecar_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "strict-malformed.dat");
        File.WriteAllText(path, "strict-malformed");

        var handler = new MalformedSidecarHandler();
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: true);

        Assert.False(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    [Fact]
    public async Task VerifyDatSignature_StrictMode_SidecarNetworkFailure_ReturnsFalse()
    {
        var path = Path.Combine(_tempDir, "strict-network.dat");
        File.WriteAllText(path, "strict-network");

        var handler = new ThrowingHttpHandler();
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: true);

        Assert.False(await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat"));
    }

    [Fact]
    public async Task VerifyDatSignature_SidecarHashMismatch_ReturnsFalse_Issue9()
    {
        var path = Path.Combine(_tempDir, "sidecar-mismatch.dat");
        File.WriteAllText(path, "trusted-content");

        // Sidecar exists and is parseable but intentionally wrong -> must fail-closed
        var handler = new SidecarMismatchHandler();
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var ok = await svc.VerifyDatSignatureAsync(path, "https://example.invalid/test.dat");

        Assert.False(ok);
    }

    private sealed class FixedStatusHandler(HttpStatusCode status) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(status));
    }

    private sealed class SidecarMismatchHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("0000000000000000000000000000000000000000000000000000000000000000")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    // === Path-Traversal Tests ==========================================

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

    [Fact]
    public async Task DownloadDatAsync_VerificationFailure_PreservesExistingTargetFile()
    {
        var existingPath = Path.Combine(_tempDir, "existing.dat");
        File.WriteAllText(existingPath, "trusted-old-content");

        var newContent = "<?xml version=\"1.0\"?><datafile><header><name>new</name></header></datafile>";
        var handler = new ContentHandler(newContent);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var wrongSha256 = new string('0', 64);
        var result = await svc.DownloadDatAsync("https://example.invalid/test.dat", "existing.dat", wrongSha256);

        Assert.Null(result);
        Assert.True(File.Exists(existingPath));
        Assert.Equal("trusted-old-content", File.ReadAllText(existingPath));
    }

    [Fact]
    public async Task DownloadDatAsync_ContentLengthExceedsLimit_ReturnsNull()
    {
        var handler = new OversizedContentLengthHandler();
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatAsync("https://example.invalid/huge.dat", "huge.dat");

        Assert.Null(result);
        Assert.False(File.Exists(Path.Combine(_tempDir, "huge.dat")));
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
            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content))
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
            return Task.FromResult(resp);
        }
    }

    // === DownloadDatByFormatAsync (zip-dat) Tests =======================

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
    public async Task DownloadDatByFormatAsync_ZipDat_ExistingTarget_CreatesBackupAndReplaces()
    {
        var oldContent = "<datafile><header><name>Old</name></header></datafile>";
        var newContent = "<datafile><header><name>New</name></header></datafile>";
        var targetPath = Path.Combine(_tempDir, "redump-ps1.dat");
        File.WriteAllText(targetPath, oldContent);

        var zipBytes = CreateZipWithDat("Sony - PlayStation.dat", newContent);
        var handler = new ByteContentHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatByFormatAsync(
            "https://redump.org/datfile/ps1/", "redump-ps1.dat", "zip-dat");

        Assert.Equal(targetPath, result);
        Assert.Equal(newContent, File.ReadAllText(targetPath));
        var backups = Directory.GetFiles(_tempDir, "redump-ps1.dat.*.bak");
        var backup = Assert.Single(backups);
        Assert.Equal(oldContent, File.ReadAllText(backup));
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

    [Fact]
    public void ImportLocalDatPacks_ExistingTarget_CreatesBackupThenCleansUp()
    {
        var sourceDir = Path.Combine(_tempDir, "source");
        Directory.CreateDirectory(sourceDir);

        var sourceDat = Path.Combine(sourceDir, "Nintendo - Game Boy.dat");
        File.WriteAllText(sourceDat, "new-pack-content");

        var targetPath = Path.Combine(_tempDir, "nointro-gb.dat");
        File.WriteAllText(targetPath, "old-pack-content");

        var catalog = new List<DatCatalogEntry>
        {
            new()
            {
                Id = "nointro-gb",
                Group = "No-Intro",
                System = "Nintendo - Game Boy",
                Format = "nointro-pack",
                PackMatch = "Nintendo*"
            }
        };

        using var svc = new DatSourceService(_tempDir);
        var imported = svc.ImportLocalDatPacks(sourceDir, catalog);

        Assert.Equal(1, imported);
        Assert.Equal("new-pack-content", File.ReadAllText(targetPath));
        // .bak is cleaned up after successful import
        Assert.False(File.Exists(targetPath + ".bak"));
    }

    [Fact]
    public async Task DownloadDatByFormatAsync_ZipDat_ZipSlipEntry_ReturnsNull()
    {
        var zipBytes = CreateZipWithEntries(("../escape.dat", "<datafile/>"), ("safe.dat", "<datafile/>"));
        var handler = new ByteContentHandler(zipBytes);
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        var result = await svc.DownloadDatByFormatAsync(
            "https://example.invalid/", "redump-ps1.dat", "zip-dat");

        Assert.Null(result);
    }

    [Fact]
    public void ReplaceWithBackup_CopyFailure_RestoresPreviousDestination()
    {
        var destinationPath = Path.Combine(_tempDir, "replace-target.dat");
        File.WriteAllText(destinationPath, "original-content");

        var sourceDirectory = Path.Combine(_tempDir, "source-directory");
        Directory.CreateDirectory(sourceDirectory);

        var method = typeof(DatSourceService).GetMethod("ReplaceWithBackup", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);

        var exception = Assert.Throws<TargetInvocationException>(() => method!.Invoke(null, new object[] { sourceDirectory, destinationPath }));
        Assert.NotNull(exception.InnerException);
        Assert.True(exception.InnerException is IOException or UnauthorizedAccessException);
        Assert.True(File.Exists(destinationPath));
        Assert.Equal("original-content", File.ReadAllText(destinationPath));
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

    private static byte[] CreateZipWithEntries(params (string Name, string Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }
        return ms.ToArray();
    }

    // === Content-Type / Login-Page Detection Tests ======================

    [Fact]
    public async Task DownloadDatAsync_TextPlain_Succeeds_GitHubRawUrl()
    {
        // GitHub raw URLs serve .dat files as text/plain -- must NOT be rejected
        var content = "<?xml version=\"1.0\"?><datafile/>";
        var handler = new ContentTypeHandler(content, "text/plain");
        using var httpClient = new HttpClient(handler);
        // F-DAT-01: this test focuses on Content-Type handling, not sidecar verification.
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: false);

        var result = await svc.DownloadDatAsync("https://example.invalid/test.dat", "github-raw.dat");
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
    }

    [Fact]
    public async Task DownloadDatAsync_TextXml_Succeeds()
    {
        var content = "<?xml version=\"1.0\"?><datafile/>";
        var handler = new ContentTypeHandler(content, "text/xml");
        using var httpClient = new HttpClient(handler);
        // F-DAT-01: this test focuses on Content-Type handling, not sidecar verification.
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient, strictSidecarValidation: false);

        var result = await svc.DownloadDatAsync("https://example.invalid/test.dat", "xml-dat.dat");
        Assert.NotNull(result);
        Assert.True(File.Exists(result));
    }

    [Fact]
    public async Task DownloadDatAsync_TextHtml_ThrowsLoginPage()
    {
        var content = "<html><body>Please login</body></html>";
        var handler = new ContentTypeHandler(content, "text/html");
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DownloadDatAsync("https://example.invalid/test.dat", "login.dat"));
    }

    [Fact]
    public async Task DownloadDatByFormatAsync_ZipDat_HtmlResponse_ThrowsLoginPage()
    {
        var content = "<html><body>Please login to Redump</body></html>";
        var handler = new ContentTypeHandler(content, "text/html");
        using var httpClient = new HttpClient(handler);
        using var svc = new DatSourceService(_tempDir, httpClient: httpClient);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DownloadDatByFormatAsync("https://redump.org/datfile/ps1/", "test.dat", "zip-dat"));
    }

    private sealed class ContentTypeHandler(string content, string mediaType) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? "";
            if (uri.EndsWith(".sha256"))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var resp = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(content))
            };
            resp.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(mediaType);
            return Task.FromResult(resp);
        }
    }

    private sealed class MalformedSidecarHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? string.Empty;
            if (uri.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("not-a-valid-sha256")
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class ThrowingHttpHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new HttpRequestException("network down");
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

    private sealed class OversizedContentLengthHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri?.ToString() ?? "";
            if (uri.EndsWith(".sha256", StringComparison.OrdinalIgnoreCase))
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));

            var content = new ByteArrayContent([0x01]);
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/xml");
            content.Headers.ContentLength = (50L * 1024 * 1024) + 1;

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = content
            });
        }
    }
}
