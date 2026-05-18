using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Romulus.Api;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Profiles;
using Romulus.Infrastructure.Safety;
using Xunit;

namespace Romulus.Tests;

public sealed class ApiCriticalPathRegressionTests : IDisposable
{
    private readonly string _tempRoot;

    public ApiCriticalPathRegressionTests()
    {
        _tempRoot = Path.Combine(Path.GetTempPath(), "Romulus_ApiCriticalPath_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempRoot);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempRoot))
            Directory.Delete(_tempRoot, recursive: true);
    }

    [Fact]
    public void MoveConfirmationGate_RequiresExactCaseSensitiveMoveToken()
    {
        Assert.False(MoveConfirmationGate.RequiresConfirmation(null));
        Assert.False(MoveConfirmationGate.RequiresConfirmation(RunConstants.ModeDryRun));
        Assert.True(MoveConfirmationGate.RequiresConfirmation("move"));

        Assert.True(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeDryRun, null));
        Assert.False(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, null));
        Assert.False(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, "move"));
        Assert.False(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, "yes"));
        Assert.True(MoveConfirmationGate.IsValidConfirmationToken(RunConstants.ModeMove, MoveConfirmationGate.ConfirmationToken));
    }

    [Fact]
    public void ApiClientIdentity_TrustsForwardedForOnlyFromLoopbackPeer()
    {
        var loopbackContext = NewHttpContext(IPAddress.Loopback, "203.0.113.5, 10.0.0.1");
        Assert.Equal("203.0.113.5", ApiClientIdentity.ResolveRateLimitClientId(loopbackContext, trustForwardedFor: true));

        var remoteContext = NewHttpContext(IPAddress.Parse("198.51.100.2"), "203.0.113.5");
        Assert.Equal("198.51.100.2", ApiClientIdentity.ResolveRateLimitClientId(remoteContext, trustForwardedFor: true));

        var untrustedForwardedContext = NewHttpContext(IPAddress.Loopback, "203.0.113.5");
        Assert.Equal("127.0.0.1", ApiClientIdentity.ResolveRateLimitClientId(untrustedForwardedContext, trustForwardedFor: false));

        var unknownContext = new DefaultHttpContext();
        Assert.Equal("unknown", ApiClientIdentity.ResolveRateLimitClientId(unknownContext, trustForwardedFor: true));
    }

    [Fact]
    public void RateLimiter_UsesInjectedFixedWindowClockAndResetsOnlyAfterWindow()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 5, 17, 12, 0, 0, TimeSpan.Zero));
        var limiter = new RateLimiter(maxRequestsPerWindow: 2, TimeSpan.FromSeconds(10), clock);

        Assert.True(limiter.TryAcquire("client-a"));
        Assert.True(limiter.TryAcquire("client-a"));
        Assert.False(limiter.TryAcquire("client-a"));

        clock.Advance(TimeSpan.FromSeconds(10));
        Assert.False(limiter.TryAcquire("client-a"));

        clock.Advance(TimeSpan.FromTicks(1));
        Assert.True(limiter.TryAcquire("client-a"));

        var disabled = new RateLimiter(maxRequestsPerWindow: 0, TimeSpan.FromSeconds(1), clock);
        Assert.True(disabled.TryAcquire("client-a"));
        Assert.True(disabled.TryAcquire("client-a"));
    }

    [Fact]
    public async Task ApiRunConfigurationMapper_AbsentBooleansUseProfileDefaultsAndExplicitFalseOverrides()
    {
        var dataDir = Path.Combine(_tempRoot, "data");
        Directory.CreateDirectory(dataDir);
        File.WriteAllText(Path.Combine(dataDir, RunProfilePaths.BuiltInProfilesFileName), BuiltInProfilesJson);

        var profileStore = new JsonRunProfileStore(new RunProfilePathOptions
        {
            DirectoryPath = Path.Combine(_tempRoot, "profiles")
        });
        var profileService = new RunProfileService(profileStore, dataDir);
        var materializer = new RunConfigurationMaterializer(new RunConfigurationResolver(profileService));
        var settings = new RomulusSettings
        {
            Dat = new DatSettings
            {
                UseDat = false,
                HashType = "MD5"
            }
        };

        using var absentJson = JsonDocument.Parse("""{ "profileId": "api-presence-profile" }""");
        var absentRequest = new RunRequest
        {
            Roots = [Path.Combine(_tempRoot, "roms")],
            ProfileId = "api-presence-profile"
        };

        var absent = await ApiRunConfigurationMapper.ResolveAsync(
            absentRequest,
            absentJson.RootElement,
            settings,
            materializer);

        Assert.Equal("api-presence-profile", absent.Request.ProfileId);
        Assert.True(absent.Request.SortConsole);
        Assert.True(absent.Request.EnableDat);
        Assert.True(absent.Request.EnableDatAudit);
        Assert.True(absent.Request.OnlyGames);
        Assert.False(absent.Request.KeepUnknownWhenOnlyGames);
        Assert.True(absent.Request.AggressiveJunk);
        Assert.Equal("SHA1", absent.Request.HashType);
        Assert.NotNull(absent.Request.Extensions);
        Assert.Equal([".nes", ".zip"], absent.Request.Extensions!);

        using var explicitJson = JsonDocument.Parse(
            """
            {
              "profileId": "api-presence-profile",
              "removeJunk": false,
              "sortConsole": false,
              "enableDat": false,
              "enableDatAudit": false,
              "onlyGames": false,
              "keepUnknownWhenOnlyGames": true,
              "aggressiveJunk": false
            }
            """);
        var explicitRequest = new RunRequest
        {
            Roots = [Path.Combine(_tempRoot, "roms")],
            ProfileId = "api-presence-profile",
            RemoveJunk = false,
            SortConsole = false,
            EnableDat = false,
            EnableDatAudit = false,
            OnlyGames = false,
            KeepUnknownWhenOnlyGames = true,
            AggressiveJunk = false
        };

        var explicitResult = await ApiRunConfigurationMapper.ResolveAsync(
            explicitRequest,
            explicitJson.RootElement,
            settings,
            materializer);

        Assert.False(explicitResult.Request.RemoveJunk);
        Assert.False(explicitResult.Request.SortConsole);
        Assert.False(explicitResult.Request.EnableDat);
        Assert.False(explicitResult.Request.EnableDatAudit);
        Assert.False(explicitResult.Request.OnlyGames);
        Assert.True(explicitResult.Request.KeepUnknownWhenOnlyGames);
        Assert.False(explicitResult.Request.AggressiveJunk);
    }

    [Fact]
    public void DashboardBootstrap_DoesNotExposeAllowedRootPaths()
    {
        var allowedRoot = Path.Combine(_tempRoot, "allowed");
        Directory.CreateDirectory(allowedRoot);

        var response = DashboardDataBuilder.BuildBootstrap(
            new HeadlessApiOptions
            {
                AllowRemoteClients = true,
                DashboardEnabled = true,
                PublicBaseUrl = "https://romulus.example",
                AllowedRoots = [allowedRoot]
            },
            new AllowedRootPathPolicy([allowedRoot]),
            version: "1.2.3-test");

        Assert.True(response.AllowRemoteClients);
        Assert.True(response.AllowedRootsEnforced);
        Assert.Empty(response.AllowedRoots);
        Assert.Equal("https://romulus.example", response.PublicBaseUrl);
        Assert.Equal("1.2.3-test", response.Version);
    }

    [Fact]
    public async Task DashboardDatStatus_GroupsDatFilesCountsCatalogAndFlagsAllowedRoot()
    {
        var dataDir = Path.Combine(_tempRoot, "data");
        var datRoot = Path.Combine(_tempRoot, "dat");
        var nesDir = Path.Combine(datRoot, "NES");
        Directory.CreateDirectory(dataDir);
        Directory.CreateDirectory(nesDir);

        File.WriteAllText(Path.Combine(dataDir, "dat-catalog.json"),
            """
            [
              { "id": "nointro-nes", "group": "No-Intro", "system": "Nintendo NES", "url": "", "format": "nointro-pack", "consoleKey": "NES" },
              { "id": "redump-psx", "group": "Redump", "system": "Sony PlayStation", "url": "https://example.com/psx.dat", "format": "zip-dat", "consoleKey": "PSX" }
            ]
            """);

        var rootDat = Path.Combine(datRoot, "root.dat");
        var nesDat = Path.Combine(nesDir, "Nintendo - NES.xml");
        File.WriteAllText(rootDat, "clrmamepro ()");
        File.WriteAllText(nesDat, "<datafile />");
        File.SetLastWriteTimeUtc(rootDat, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
        File.SetLastWriteTimeUtc(nesDat, DateTime.UtcNow);

        var allowed = await DashboardDataBuilder.BuildDatStatusAsync(
            datRoot,
            dataDir,
            new AllowedRootPathPolicy([datRoot]),
            CancellationToken.None);

        Assert.True(allowed.Configured);
        Assert.True(allowed.WithinAllowedRoots);
        Assert.Equal(datRoot, allowed.DatRoot);
        Assert.Equal(2, allowed.TotalFiles);
        Assert.Equal(2, allowed.CatalogEntries);
        Assert.Equal(1, allowed.OldFileCount);
        Assert.Contains("365", allowed.StaleWarning, StringComparison.Ordinal);
        Assert.Equal(1, Assert.Single(allowed.Consoles, item => item.Console == "NES").FileCount);
        Assert.Equal(1, Assert.Single(allowed.Consoles, item => item.Console == "root").FileCount);

        var denied = await DashboardDataBuilder.BuildDatStatusAsync(
            datRoot,
            dataDir,
            new AllowedRootPathPolicy([Path.Combine(_tempRoot, "other-root")]),
            CancellationToken.None);

        Assert.False(denied.WithinAllowedRoots);
    }

    [Fact]
    public void ApiRunResultMapper_UsesProjectionAndSeparatesConversionPlansFromBlockedItems()
    {
        var winner = new RomCandidate
        {
            MainPath = @"C:\roms\Game (EU).zip",
            GameKey = "game",
            Category = FileCategory.Game,
            DatMatch = true
        };
        var loser = new RomCandidate
        {
            MainPath = @"C:\roms\Game (US).zip",
            GameKey = "game",
            Category = FileCategory.Game
        };
        var junk = new RomCandidate
        {
            MainPath = @"C:\roms\Bad Dump.zip",
            GameKey = "bad-dump",
            Category = FileCategory.Junk
        };
        var conversionReport = new ConversionReport
        {
            TotalPlanned = 4,
            Converted = 1,
            Skipped = 1,
            Errors = 1,
            Blocked = 1,
            RequiresReview = 0,
            TotalSavedBytes = 2048,
            Results =
            [
                new ConversionResult(@"C:\roms\Disc.iso", @"C:\roms\Disc.chd", ConversionOutcome.Success)
                {
                    Safety = ConversionSafety.Safe,
                    VerificationResult = VerificationStatus.Verified
                },
                new ConversionResult(@"C:\roms\Already.chd", @"C:\roms\Already.chd", ConversionOutcome.Skipped, "already-target")
                {
                    Safety = ConversionSafety.Safe,
                    VerificationResult = VerificationStatus.NotAttempted
                },
                new ConversionResult(@"C:\roms\Lossy.gcz", null, ConversionOutcome.Blocked, "lossy-review-required")
                {
                    Safety = ConversionSafety.Risky
                },
                new ConversionResult(@"C:\roms\Broken.iso", null, ConversionOutcome.Error, "tool-exit-1", ExitCode: 1)
                {
                    Safety = ConversionSafety.Blocked,
                    VerificationResult = VerificationStatus.VerifyFailed
                }
            ]
        };
        var result = new RunResult
        {
            Status = RunConstants.StatusCompletedWithErrors,
            ExitCode = RunOutcome.CompletedWithErrors.ToExitCode(),
            TotalFilesScanned = 3,
            GroupCount = 1,
            WinnerCount = 1,
            LoserCount = 1,
            AllCandidates = [winner, loser, junk],
            DedupeGroups = [new DedupeGroup { GameKey = "game", Winner = winner, Losers = [loser] }],
            ConvertedCount = 1,
            ConvertSkippedCount = 1,
            ConvertBlockedCount = 1,
            ConvertErrorCount = 1,
            ConvertVerifyPassedCount = 1,
            ConvertVerifyFailedCount = 1,
            ConvertSavedBytes = 2048,
            ConversionReport = conversionReport,
            Preflight = OperationResult.Ok().AddWarning("preflight retained"),
            FailedPhaseName = "Convert",
            FailedPhaseStatus = RunConstants.StatusFailed,
            DurationMs = 123
        };

        var projection = RunProjectionFactory.Create(result);
        var mapped = ApiRunResultMapper.Map(result, projection, RunConstants.ModeMove);

        Assert.Equal(RunConstants.ModeMove, mapped.Mode);
        Assert.Equal(projection.Status, mapped.Status);
        Assert.Equal(projection.Keep, mapped.Keep);
        Assert.Equal(projection.Dupes, mapped.Losers);
        Assert.Equal(projection.FailCount, mapped.FailCount);
        Assert.Equal("Convert", mapped.FailedPhaseName);
        Assert.Equal(RunConstants.StatusFailed, mapped.FailedPhaseStatus);
        Assert.Equal(["preflight retained"], mapped.PreflightWarnings);

        var group = Assert.Single(mapped.DedupeGroups);
        Assert.Equal("game", group.GameKey);
        Assert.Equal(winner.MainPath, group.Winner.MainPath);
        Assert.Equal(loser.MainPath, Assert.Single(group.Losers).MainPath);

        Assert.Equal(2, mapped.ConversionPlans.Length);
        Assert.Contains(mapped.ConversionPlans, item =>
            item.SourcePath == @"C:\roms\Disc.iso"
            && item.TargetExtension == ".chd"
            && item.Outcome == nameof(ConversionOutcome.Success)
            && item.Verification == nameof(VerificationStatus.Verified));
        Assert.Contains(mapped.ConversionPlans, item =>
            item.SourcePath == @"C:\roms\Already.chd"
            && item.Outcome == nameof(ConversionOutcome.Skipped));

        Assert.Equal(2, mapped.ConversionBlocked.Length);
        Assert.Contains(mapped.ConversionBlocked, item =>
            item.SourcePath == @"C:\roms\Lossy.gcz"
            && item.Reason == "lossy-review-required"
            && item.Safety == nameof(ConversionSafety.Risky));
        Assert.Contains(mapped.ConversionBlocked, item =>
            item.SourcePath == @"C:\roms\Broken.iso"
            && item.Reason == "tool-exit-1"
            && item.Safety == nameof(ConversionSafety.Blocked));
    }

    private static DefaultHttpContext NewHttpContext(IPAddress? remoteIp, string? forwardedFor)
    {
        var context = new DefaultHttpContext();
        context.Connection.RemoteIpAddress = remoteIp;
        if (!string.IsNullOrWhiteSpace(forwardedFor))
            context.Request.Headers["X-Forwarded-For"] = forwardedFor;

        return context;
    }

    private const string BuiltInProfilesJson =
        """
        [
          {
            "version": 1,
            "id": "api-presence-profile",
            "name": "API Presence Profile",
            "description": "Verifies API property presence mapping.",
            "builtIn": true,
            "tags": ["api"],
            "settings": {
              "mode": "DryRun",
              "removeJunk": true,
              "onlyGames": true,
              "keepUnknownWhenOnlyGames": false,
              "aggressiveJunk": true,
              "sortConsole": true,
              "enableDat": true,
              "enableDatAudit": true,
              "extensions": [".zip", ".nes"],
              "hashType": "SHA1"
            }
          }
        ]
        """;

    private sealed class ManualTimeProvider(DateTimeOffset initialUtcNow) : ITimeProvider
    {
        public DateTimeOffset UtcNow { get; private set; } = initialUtcNow;

        public void Advance(TimeSpan delta)
        {
            UtcNow += delta;
        }
    }
}
