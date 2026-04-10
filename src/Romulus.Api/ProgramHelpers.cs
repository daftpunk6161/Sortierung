using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Romulus.Contracts;
using Romulus.Contracts.Errors;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Api;
using Romulus.Infrastructure.Analysis;
using Romulus.Infrastructure.Dat;
using Romulus.Infrastructure.Index;
using Romulus.Infrastructure.Orchestration;
using Romulus.Infrastructure.Review;
using Romulus.Infrastructure.Safety;

public partial class Program
{
    // V2-H10: API version from assembly metadata, not hardcoded
    internal static readonly string ApiVersion =
        typeof(Program).Assembly.GetName().Version?.ToString(2) ?? "1.0";

    internal static bool FixedTimeEquals(string expected, string? actual)
    {
        if (actual is null) return false;
        // HMAC both values to normalize length before comparison,
        // eliminating the length oracle from FixedTimeEquals.
        var key = Encoding.UTF8.GetBytes(expected);
        var a = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(expected));
        var b = HMACSHA256.HashData(key, Encoding.UTF8.GetBytes(actual));
        return CryptographicOperations.FixedTimeEquals(a, b);
    }

    internal static bool FixedTimeEqualsAny(IReadOnlyList<string> expectedKeys, string? actual)
    {
        if (actual is null || expectedKeys.Count == 0)
            return false;

        var matched = false;
        foreach (var key in expectedKeys)
            matched |= FixedTimeEquals(key, actual);

        return matched;
    }

    internal static List<string> ParseApiKeys(string? configuredValue)
    {
        if (string.IsNullOrWhiteSpace(configuredValue))
            return [];

        return configuredValue
            .Split([',', ';', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries)
            .Select(static key => key.Trim())
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    internal static string BuildRateLimitBucketId(string apiKey)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(apiKey));
        return "api-key:" + Convert.ToHexString(hash.AsSpan(0, 8));
    }

    internal static void SafeConsoleWriteLine(string message)
    {
        try
        {
            Console.WriteLine(message);
        }
        catch (ObjectDisposedException)
        {
            // Some tests temporarily replace/dispose Console.Out. Logging must never break request handling.
        }
    }

    internal static async Task WriteSseEvent(Stream stream, Encoding encoding, string eventName, object data)
    {
        // SEC: Prevent SSE event injection via newlines in event name
        var safeEventName = SanitizeSseEventName(eventName);
        var json = JsonSerializer.Serialize(data);
        var payload = $"event: {safeEventName}\ndata: {json}\n\n";
        await stream.WriteAsync(encoding.GetBytes(payload));
        await stream.FlushAsync();
    }

    internal static string? SanitizeCorrelationId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Length > 64) return null;
        // Only allow printable ASCII, no control chars / newlines / whitespace besides space
        foreach (var ch in raw)
        {
            if (ch < 0x21 || ch > 0x7E) return null; // reject control chars, newlines, non-ASCII
        }
        return raw;
    }

    internal static string? SanitizeClientBindingId(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        if (raw.Length > 64) return null;
        foreach (var ch in raw)
        {
            if (!(char.IsLetterOrDigit(ch) || ch is '-' or '_' or '.'))
                return null;
        }

        return raw;
    }

    internal static bool IsAnonymousEndpoint(PathString path)
    {
        return path.Equals("/healthz", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/dashboard/bootstrap", StringComparison.OrdinalIgnoreCase);
    }

    internal static string ResolveCorsOrigin(string mode, string customOrigin)
    {
        return mode switch
        {
            "local-dev" => "http://localhost:3000",
            "strict-local" => "http://127.0.0.1",
            "custom" => IsValidCorsOrigin(customOrigin) ? customOrigin : "http://127.0.0.1",
            _ => "http://127.0.0.1"
        };
    }

    internal static bool IsValidCorsOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin))
            return false;

        if (!Uri.TryCreate(origin, UriKind.Absolute, out var uri))
            return false;

        return uri.Scheme is "http" or "https" && !string.IsNullOrWhiteSpace(uri.Host);
    }

    internal static string GetClientBindingId(HttpContext context, bool trustForwardedFor)
    {
        if (context.Items.TryGetValue("ClientBindingId", out var existing) && existing is string cached && !string.IsNullOrWhiteSpace(cached))
            return cached;

        var rawClientId = context.Request.Headers["X-Client-Id"].FirstOrDefault();
        var resolved = SanitizeClientBindingId(rawClientId)
            ?? ApiClientIdentity.ResolveRateLimitClientId(context, trustForwardedFor);
        context.Items["ClientBindingId"] = resolved;
        return resolved;
    }

    internal static bool CanAccessRun(RunRecord run, string requesterClientId)
    {
        if (string.IsNullOrWhiteSpace(run.OwnerClientId))
            return true;

        return string.Equals(run.OwnerClientId, requesterClientId, StringComparison.Ordinal);
    }

    internal static ApiRunList BuildRunList(IReadOnlyList<RunRecord> runs, int offset = 0, int? limit = null)
    {
        var safeOffset = Math.Min(offset, runs.Count);
        var pageSize = limit ?? Math.Max(runs.Count - safeOffset, 0);
        var pageRuns = limit is null
            ? runs.Skip(safeOffset).ToArray()
            : runs.Skip(safeOffset).Take(limit.Value).ToArray();

        return new ApiRunList
        {
            Total = runs.Count,
            Offset = offset,
            Limit = pageSize,
            Returned = pageRuns.Length,
            HasMore = safeOffset + pageRuns.Length < runs.Count,
            Runs = pageRuns.Select(run => run.ToDto()).ToArray()
        };
    }

    internal static ApiRunHistoryList BuildRunHistoryList(CollectionRunHistoryPage page)
    {
        return new ApiRunHistoryList
        {
            Total = page.Total,
            Offset = page.Offset,
            Limit = page.Limit,
            Returned = page.Returned,
            HasMore = page.HasMore,
            Runs = page.Runs
                .Select(snapshot => new ApiRunHistoryEntry
            {
                RunId = snapshot.RunId,
                StartedUtc = snapshot.StartedUtc,
                CompletedUtc = snapshot.CompletedUtc,
                Mode = snapshot.Mode,
                Status = snapshot.Status,
                RootCount = snapshot.RootCount,
                RootFingerprint = snapshot.RootFingerprint,
                DurationMs = snapshot.DurationMs,
                TotalFiles = snapshot.TotalFiles,
                CollectionSizeBytes = snapshot.CollectionSizeBytes,
                Games = snapshot.Games,
                Dupes = snapshot.Dupes,
                Junk = snapshot.Junk,
                DatMatches = snapshot.DatMatches,
                ConvertedCount = snapshot.ConvertedCount,
                FailCount = snapshot.FailCount,
                SavedBytes = snapshot.SavedBytes,
                ConvertSavedBytes = snapshot.ConvertSavedBytes,
                HealthScore = snapshot.HealthScore
            })
                .ToArray()
        };
    }

    internal static async Task<ApiReviewQueue> BuildReviewQueueAsync(
        RunRecord run,
        PersistedReviewDecisionService? reviewDecisionService,
        int offset = 0,
        int? limit = null,
        CancellationToken ct = default)
    {
        var core = run.CoreRunResult;
        if (core is null)
        {
            return new ApiReviewQueue
            {
                RunId = run.RunId,
                Total = 0,
                Offset = offset,
                Limit = limit ?? 0,
                Returned = 0,
                HasMore = false,
                Items = Array.Empty<ApiReviewItem>()
            };
        }

        var projectedArtifacts = RunArtifactProjection.Project(core);
        var projectedCandidates = projectedArtifacts.AllCandidates;

        var approvedPaths = reviewDecisionService is null
            ? new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            : await reviewDecisionService.GetApprovedPathSetAsync(
                projectedCandidates.Select(static candidate => candidate.MainPath).ToArray(),
                ct);

        var items = projectedCandidates
            .Where(c => c.SortDecision is SortDecision.Review or SortDecision.Blocked or SortDecision.Unknown)
            .OrderBy(c => c.ConsoleKey, StringComparer.OrdinalIgnoreCase)
            .ThenBy(c => c.MainPath, StringComparer.OrdinalIgnoreCase)
            .Select(c => new ApiReviewItem
            {
                MainPath = c.MainPath,
                FileName = Path.GetFileName(c.MainPath),
                ConsoleKey = c.ConsoleKey,
                SortDecision = c.SortDecision.ToString(),
                DecisionClass = c.DecisionClass.ToString(),
                EvidenceTier = c.EvidenceTier.ToString(),
                PrimaryMatchKind = c.PrimaryMatchKind.ToString(),
                PlatformFamily = c.PlatformFamily.ToString(),
                MatchLevel = c.MatchEvidence.Level.ToString(),
                MatchReasoning = c.MatchEvidence.Reasoning,
                DetectionConfidence = c.DetectionConfidence,
                Approved = run.IsReviewPathApproved(c.MainPath) || approvedPaths.Contains(c.MainPath)
            })
            .ToArray();

        var safeOffset = Math.Min(offset, items.Length);
        var pageSize = limit ?? Math.Max(items.Length - safeOffset, 0);
        var pageItems = limit is null
            ? items.Skip(safeOffset).ToArray()
            : items.Skip(safeOffset).Take(limit.Value).ToArray();

        return new ApiReviewQueue
        {
            RunId = run.RunId,
            Total = items.Length,
            Offset = offset,
            Limit = pageSize,
            Returned = pageItems.Length,
            HasMore = safeOffset + pageItems.Length < items.Length,
            Items = pageItems
        };
    }

    internal static string SanitizeSseEventName(string eventName)
    {
        // SSE event names must be single-line printable ASCII
        foreach (var ch in eventName)
        {
            if (ch is '\n' or '\r' or ':') return "error";
        }
        return eventName;
    }

    internal static IResult CreateArtifactDownloadResult(
        string? artifactPath,
        string contentType,
        string fallbackFileName,
        string unavailableCode,
        string unavailableMessage,
        string runId)
    {
        if (string.IsNullOrWhiteSpace(artifactPath) || !File.Exists(artifactPath))
            return ApiError(409, unavailableCode, unavailableMessage, runId: runId);

        var downloadName = Path.GetFileName(artifactPath);
        if (string.IsNullOrWhiteSpace(downloadName))
            downloadName = fallbackFileName;

        return Results.File(artifactPath, contentType, downloadName);
    }

    internal static IResult? ValidateRootSecurity(string root, AllowedRootPathPolicy allowedRootPolicy)
    {
        try
        {
            var dirInfo = new DirectoryInfo(root);
            if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
            {
                return ApiError(400, SecurityErrorCodes.RootReparsePoint, $"Symlink/junction not allowed as root: {root}", ErrorKind.Critical);
            }
        }
        catch (Exception ex)
        {
            return ApiError(400, SecurityErrorCodes.RootAttributeCheckFailed,
                $"Cannot verify attributes for root: {root} ({ex.GetType().Name})", ErrorKind.Critical);
        }

        var full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar);
        if (SafetyValidator.IsProtectedSystemPath(full))
            return ApiError(400, SecurityErrorCodes.SystemDirectoryRoot, $"System directory not allowed: {root}", ErrorKind.Critical);
        if (SafetyValidator.IsDriveRoot(full))
            return ApiError(400, SecurityErrorCodes.DriveRootNotAllowed, $"Drive root not allowed: {root}", ErrorKind.Critical);
        if (allowedRootPolicy.IsEnforced && !allowedRootPolicy.IsPathAllowed(full))
        {
            return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, $"Root is outside configured AllowedRoots: {root}", ErrorKind.Critical);
        }

        return null;
    }

    internal static IResult? ValidateCollectionScopeSecurity(
        CollectionSourceScope scope,
        string fieldName,
        AllowedRootPathPolicy allowedRootPolicy,
        bool requireExistingRoots)
    {
        if (scope.Roots.Count == 0)
            return ApiError(400, $"COLLECTION-{fieldName.ToUpperInvariant()}-ROOTS-REQUIRED", $"{fieldName}.roots[] is required.");

        foreach (var root in scope.Roots)
        {
            if (string.IsNullOrWhiteSpace(root))
                return ApiError(400, $"COLLECTION-{fieldName.ToUpperInvariant()}-ROOT-EMPTY", $"Empty root path in {fieldName}.roots.");

            var pathError = ValidateRootSecurity(root, allowedRootPolicy);
            if (pathError is not null)
                return pathError;

            if (requireExistingRoots && !Directory.Exists(root))
                return ApiError(400, "IO-ROOT-NOT-FOUND", $"Root not found: {root}");
        }

        return null;
    }

    internal static IResult? ValidateCollectionMergeRequest(CollectionMergeRequest request, AllowedRootPathPolicy allowedRootPolicy)
    {
        var leftValidation = ValidateCollectionScopeSecurity(request.CompareRequest.Left, "left", allowedRootPolicy, requireExistingRoots: true);
        if (leftValidation is not null)
            return leftValidation;

        var rightValidation = ValidateCollectionScopeSecurity(request.CompareRequest.Right, "right", allowedRootPolicy, requireExistingRoots: true);
        if (rightValidation is not null)
            return rightValidation;

        if (request.CompareRequest.Limit < 1 || request.CompareRequest.Limit > 5000)
            return ApiError(400, "COLLECTION-MERGE-INVALID-LIMIT", "compareRequest.limit must be an integer between 1 and 5000.");

        if (string.IsNullOrWhiteSpace(request.TargetRoot))
            return ApiError(400, "COLLECTION-MERGE-TARGET-REQUIRED", "targetRoot is required.");

        return ValidatePathSecurity(request.TargetRoot.Trim(), "targetRoot", allowedRootPolicy);
    }

    internal static IResult? ValidatePathSecurity(string path, string fieldName, AllowedRootPathPolicy? allowedRootPolicy = null)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;

        string full;
        try { full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar); }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException or System.Security.SecurityException) { return ApiError(400, SecurityErrorCodes.InvalidPath, $"Invalid path for {fieldName}.", ErrorKind.Critical); }

        // Block reparse points
        try
        {
            if (Directory.Exists(full))
            {
                var dirInfo = new DirectoryInfo(full);
                if ((dirInfo.Attributes & FileAttributes.ReparsePoint) != 0)
                    return ApiError(400, SecurityErrorCodes.ReparsePoint, $"Symlink/junction not allowed for {fieldName}.", ErrorKind.Critical);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
        {
            return ApiError(400, SecurityErrorCodes.AttributeCheckFailed, $"Cannot verify attributes for {fieldName}: {ex.GetType().Name}.", ErrorKind.Critical);
        }

        // Block system directories (single source of truth: SafetyValidator)
        if (SafetyValidator.IsProtectedSystemPath(full))
            return ApiError(400, SecurityErrorCodes.SystemDirectory, $"System directory not allowed for {fieldName}.", ErrorKind.Critical);

        // Block drive root
        if (SafetyValidator.IsDriveRoot(full))
            return ApiError(400, SecurityErrorCodes.DriveRoot, $"Drive root not allowed for {fieldName}.", ErrorKind.Critical);

        if (allowedRootPolicy?.IsEnforced == true && !allowedRootPolicy.IsPathAllowed(full))
        {
            return ApiError(400, SecurityErrorCodes.OutsideAllowedRoots, $"Path for {fieldName} is outside configured AllowedRoots.", ErrorKind.Critical);
        }

        return null;
    }

    internal static async Task<IResult> HandleRunCompletenessAsync(
        string runId,
        HttpContext context,
        RunLifecycleManager manager,
        AllowedRootPathPolicy allowedRootPolicy,
        bool trustForwardedFor,
        CancellationToken ct)
    {
        if (!Guid.TryParse(runId, out _))
            return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

        var run = manager.Get(runId);
        if (run is null)
            return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

        if (!CanAccessRun(run, GetClientBindingId(context, trustForwardedFor)))
            return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

        if (run.Status == "running")
            return ApiError(409, "RUN-IN-PROGRESS", "Run still in progress.", runId: runId);

        var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
            ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        var runRoots = run.Roots ?? Array.Empty<string>();
        if (runRoots.Length == 0)
            return ApiError(400, "RUN-NO-ROOTS", "Run has no roots configured.", runId: runId);

        foreach (var runRoot in runRoots)
        {
            var pathError = ValidatePathSecurity(runRoot, "roots", allowedRootPolicy);
            if (pathError is not null)
                return pathError;
        }

        var effectiveDatRoot = run.DatRoot ?? settings.Dat?.DatRoot;
        if (!string.IsNullOrWhiteSpace(effectiveDatRoot))
        {
            var pathError = ValidatePathSecurity(effectiveDatRoot, "datRoot", allowedRootPolicy);
            if (pathError is not null)
                return pathError;
        }

        var runOptions = new Romulus.Contracts.Models.RunOptions
        {
            Roots = runRoots,
            EnableDat = true,
            DatRoot = effectiveDatRoot,
            Extensions = run.Extensions
        };

        using var env = new RunEnvironmentFactory().Create(runOptions);
        if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
            return ApiError(400, "DAT-NOT-AVAILABLE", "No DAT index available. Configure DatRoot in settings.", runId: runId);

        var report = await CompletenessReportService.BuildAsync(
            env.DatIndex,
            runOptions.Roots,
            env.CollectionIndex,
            runOptions.Extensions,
            run.CoreRunResult is { } frontendExportRunResult
                ? RunArtifactProjection.Project(frontendExportRunResult).AllCandidates
                : null,
            ct);

        return Results.Ok(new
        {
            runId,
            source = report.Source,
            sourceItemCount = report.SourceItemCount,
            entries = report.Entries.Select(e => new
            {
                e.ConsoleKey,
                e.TotalInDat,
                e.Verified,
                e.MissingCount,
                e.Percentage,
                missingGames = e.MissingGames.Take(100).ToArray(),
                truncated = e.MissingGames.Count > 100
            }).ToArray(),
            totalInDat = report.Entries.Sum(e => e.TotalInDat),
            totalVerified = report.Entries.Sum(e => e.Verified),
            totalMissing = report.Entries.Sum(e => e.MissingCount),
            overallPercentage = report.Entries.Sum(e => e.TotalInDat) > 0
                ? Math.Round(100.0 * report.Entries.Sum(e => e.Verified) / report.Entries.Sum(e => e.TotalInDat), 1)
                : 0.0
        });
    }

    internal static async Task<IResult> HandleRunFixDatAsync(
        string runId,
        string? outputPath,
        string? name,
        HttpContext context,
        RunLifecycleManager manager,
        AllowedRootPathPolicy allowedRootPolicy,
        bool trustForwardedFor,
        CancellationToken ct)
    {
        if (!Guid.TryParse(runId, out _))
            return ApiError(400, "RUN-INVALID-ID", "Invalid run ID format.");

        var run = manager.Get(runId);
        if (run is null)
            return ApiError(404, "RUN-NOT-FOUND", "Run not found.", runId: runId);

        if (!CanAccessRun(run, GetClientBindingId(context, trustForwardedFor)))
            return ApiError(403, "AUTH-FORBIDDEN", "Run belongs to a different client.", ErrorKind.Critical, runId: runId);

        if (run.Status == "running")
            return ApiError(409, "RUN-IN-PROGRESS", "Run still in progress.", runId: runId);

        if (string.IsNullOrWhiteSpace(outputPath))
            return ApiError(400, "FIXDAT-OUTPUT-REQUIRED", "outputPath is required.", runId: runId);

        var outputPathError = ValidatePathSecurity(outputPath.Trim(), "outputPath", allowedRootPolicy);
        if (outputPathError is not null)
            return outputPathError;

        var dataDir = RunEnvironmentBuilder.TryResolveDataDir()
            ?? Path.Combine(Directory.GetCurrentDirectory(), "data");
        var settings = RunEnvironmentBuilder.LoadSettings(dataDir);

        var runRoots = run.Roots ?? Array.Empty<string>();
        if (runRoots.Length == 0)
            return ApiError(400, "RUN-NO-ROOTS", "Run has no roots configured.", runId: runId);

        foreach (var runRoot in runRoots)
        {
            var pathError = ValidatePathSecurity(runRoot, "roots", allowedRootPolicy);
            if (pathError is not null)
                return pathError;
        }

        var effectiveDatRoot = run.DatRoot ?? settings.Dat?.DatRoot;
        if (!string.IsNullOrWhiteSpace(effectiveDatRoot))
        {
            var pathError = ValidatePathSecurity(effectiveDatRoot, "datRoot", allowedRootPolicy);
            if (pathError is not null)
                return pathError;
        }

        var runOptions = new Romulus.Contracts.Models.RunOptions
        {
            Roots = runRoots,
            EnableDat = true,
            DatRoot = effectiveDatRoot,
            Extensions = run.Extensions
        };

        using var env = new RunEnvironmentFactory().Create(runOptions);
        if (env.DatIndex is null || env.DatIndex.TotalEntries == 0)
            return ApiError(400, "DAT-NOT-AVAILABLE", "No DAT index available. Configure DatRoot in settings.", runId: runId);

        var report = await CompletenessReportService.BuildAsync(
            env.DatIndex,
            runOptions.Roots,
            env.CollectionIndex,
            runOptions.Extensions,
            run.CoreRunResult is { } fixDatRunResult
                ? RunArtifactProjection.Project(fixDatRunResult).AllCandidates
                : null,
            ct);

        var generatedUtc = DateTime.UtcNow;
        var datName = string.IsNullOrWhiteSpace(name)
            ? $"Romulus-FixDAT-{runId}"
            : name.Trim();

        var fixDat = DatAnalysisService.BuildFixDatFromCompleteness(env.DatIndex, report, datName, generatedUtc);

        string safeOutputPath;
        try
        {
            safeOutputPath = SafetyValidator.EnsureSafeOutputPath(outputPath.Trim(), allowUnc: false);
        }
        catch (InvalidOperationException ex)
        {
            return ApiError(400, SecurityErrorCodes.InvalidPath, $"Invalid outputPath: {ex.Message}", ErrorKind.Critical, runId: runId);
        }

        var directory = Path.GetDirectoryName(safeOutputPath);
        if (!string.IsNullOrWhiteSpace(directory))
            Directory.CreateDirectory(directory);

        await File.WriteAllTextAsync(safeOutputPath, fixDat.XmlContent, Encoding.UTF8, ct);

        return Results.Ok(new
        {
            runId,
            outputPath = safeOutputPath,
            fixDat.DatName,
            fixDat.ConsoleCount,
            fixDat.MissingGames,
            fixDat.MissingRoms,
            consoles = fixDat.Consoles
        });
    }

    internal static async Task<(T? Value, IResult? Error)> ReadJsonBodyAsync<T>(
        HttpContext context,
        string codePrefix,
        CancellationToken ct)
    {
        if (context.Request.ContentLength is > 1_048_576)
            return (default, ApiError(400, $"{codePrefix}-BODY-TOO-LARGE", "Request body too large (max 1MB)."));

        string body;
        try
        {
            using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
            // Limit read to 1MB + 1 byte to detect oversized chunked bodies
            // (same pattern as POST /runs body reading).
            var buffer = new char[1_048_577];
            var charsRead = await reader.ReadBlockAsync(buffer, 0, buffer.Length);
            if (charsRead > 1_048_576)
                return (default, ApiError(400, $"{codePrefix}-BODY-TOO-LARGE", "Request body too large (max 1MB)."));
            body = new string(buffer, 0, charsRead);
        }
        catch (IOException)
        {
            return (default, ApiError(400, $"{codePrefix}-READ-ERROR", "Failed to read request body."));
        }

        if (string.IsNullOrWhiteSpace(body))
            return (default, ApiError(400, $"{codePrefix}-INVALID-JSON", "Invalid JSON."));

        try
        {
            var value = JsonSerializer.Deserialize<T>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return value is null
                ? (default, ApiError(400, $"{codePrefix}-INVALID-JSON", "Invalid JSON."))
                : (value, null);
        }
        catch (JsonException)
        {
            return (default, ApiError(400, $"{codePrefix}-INVALID-JSON", "Invalid JSON."));
        }
    }

    internal static IResult ApiError(
        int statusCode,
        string code,
        string message,
        ErrorKind kind = ErrorKind.Recoverable,
        string? runId = null,
        IDictionary<string, object>? meta = null)
    {
        return Results.Json(CreateErrorResponse(code, message, kind, runId, meta), statusCode: statusCode);
    }

    internal static Task WriteApiError(
        HttpContext context,
        int statusCode,
        string code,
        string message,
        ErrorKind kind = ErrorKind.Recoverable,
        string? runId = null,
        IDictionary<string, object>? meta = null)
    {
        context.Response.StatusCode = statusCode;
        return context.Response.WriteAsJsonAsync(CreateErrorResponse(code, message, kind, runId, meta));
    }

    internal static OperationErrorResponse CreateErrorResponse(
        string code,
        string message,
        ErrorKind kind = ErrorKind.Recoverable,
        string? runId = null,
        IDictionary<string, object>? meta = null)
    {
        return new OperationErrorResponse(new OperationError(code, message, kind, "API"), runId, meta);
    }

    internal static IDictionary<string, object> CreateMeta(params (string Key, object? Value)[] entries)
    {
        var meta = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
        {
            if (value is not null)
                meta[key] = value;
        }

        return meta;
    }

    internal static (string Code, string Message) MapRunConfigurationError(string message)
    {
        if (message.Contains("protected system path", StringComparison.OrdinalIgnoreCase))
            return (SecurityErrorCodes.SystemDirectory, message);

        if (message.Contains("drive root", StringComparison.OrdinalIgnoreCase))
            return (SecurityErrorCodes.DriveRoot, message);

        if (message.Contains("UNC path", StringComparison.OrdinalIgnoreCase) ||
            message.Contains(" is invalid:", StringComparison.OrdinalIgnoreCase))
        {
            return (SecurityErrorCodes.InvalidPath, message);
        }

        if (message.Contains("Invalid region", StringComparison.OrdinalIgnoreCase))
            return ("RUN-INVALID-REGION", message);

        if (message.Contains("Invalid extension", StringComparison.OrdinalIgnoreCase))
            return ("RUN-INVALID-EXTENSION", message);

        if (message.Contains("Invalid hashType", StringComparison.OrdinalIgnoreCase))
            return ("RUN-INVALID-HASH-TYPE", message);

        if (message.Contains("Invalid convertFormat", StringComparison.OrdinalIgnoreCase))
            return ("RUN-INVALID-CONVERT-FORMAT", message);

        if (message.Contains("Invalid conflictPolicy", StringComparison.OrdinalIgnoreCase))
            return ("RUN-INVALID-CONFLICT-POLICY", message);

        if (message.Contains("Invalid mode", StringComparison.OrdinalIgnoreCase))
            return ("RUN-INVALID-MODE", message);

        if (message.Contains("Workflow '", StringComparison.OrdinalIgnoreCase))
            return ("RUN-WORKFLOW-NOT-FOUND", message);

        if (message.Contains("Profile '", StringComparison.OrdinalIgnoreCase))
            return ("RUN-PROFILE-NOT-FOUND", message);

        return ("RUN-INVALID-CONFIG", message);
    }

    internal static (string Code, string Message) MapWatchConfigurationError(string message)
    {
        if (message.Contains("protected system path", StringComparison.OrdinalIgnoreCase))
            return (SecurityErrorCodes.SystemDirectory, message);

        if (message.Contains("drive root", StringComparison.OrdinalIgnoreCase))
            return (SecurityErrorCodes.DriveRoot, message);

        if (message.Contains("UNC path", StringComparison.OrdinalIgnoreCase) ||
            message.Contains(" is invalid:", StringComparison.OrdinalIgnoreCase))
        {
            return (SecurityErrorCodes.InvalidPath, message);
        }

        if (message.Contains("Invalid region", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-INVALID-REGION", message);

        if (message.Contains("Invalid extension", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-INVALID-EXTENSION", message);

        if (message.Contains("Invalid hashType", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-INVALID-HASH-TYPE", message);

        if (message.Contains("Invalid convertFormat", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-INVALID-CONVERT-FORMAT", message);

        if (message.Contains("Invalid conflictPolicy", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-INVALID-CONFLICT-POLICY", message);

        if (message.Contains("Invalid mode", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-INVALID-MODE", message);

        if (message.Contains("Workflow '", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-WORKFLOW-NOT-FOUND", message);

        if (message.Contains("Profile '", StringComparison.OrdinalIgnoreCase))
            return ("WATCH-PROFILE-NOT-FOUND", message);

        return ("WATCH-INVALID-CONFIG", message);
    }
}

internal sealed class ApiFrontendExportRequest
{
    public string? Frontend { get; set; }
    public string? OutputPath { get; set; }
    public string? CollectionName { get; set; }
    public string[]? Roots { get; set; }
    public string[]? Extensions { get; set; }
    public string? RunId { get; set; }
}
