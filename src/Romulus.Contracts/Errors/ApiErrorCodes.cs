namespace Romulus.Contracts.Errors;

/// <summary>
/// Centralized API error codes shared across entry points.
/// Prevents magic-string divergence — single source of truth for error-code strings
/// used in API responses, tests, and error classification.
/// </summary>
public static class ApiErrorCodes
{
    // ── AUTH ─────────────────────────────────────────────────────────

    public const string InternalError = "INTERNAL_ERROR";

    public const string AuthInvalidClientId = "AUTH-INVALID-CLIENT-ID";
    public const string AuthUnauthorized = "AUTH-UNAUTHORIZED";
    public const string AuthForbidden = "AUTH-FORBIDDEN";

    // ── RUN ──────────────────────────────────────────────────────────

    public const string RunInvalidId = "RUN-INVALID-ID";
    public const string RunNotFound = "RUN-NOT-FOUND";
    public const string RunInProgress = "RUN-IN-PROGRESS";
    public const string RunNoRoots = "RUN-NO-ROOTS";
    public const string RunRateLimit = "RUN-RATE-LIMIT";
    public const string RunInvalidOffset = "RUN-INVALID-OFFSET";
    public const string RunInvalidLimit = "RUN-INVALID-LIMIT";
    public const string RunCompareIdsRequired = "RUN-COMPARE-IDS-REQUIRED";
    public const string RunCompareNotFound = "RUN-COMPARE-NOT-FOUND";
    public const string RunInvalidContentType = "RUN-INVALID-CONTENT-TYPE";
    public const string RunBodyTooLarge = "RUN-BODY-TOO-LARGE";
    public const string RunInvalidJson = "RUN-INVALID-JSON";
    public const string RunInvalidConfig = "RUN-INVALID-CONFIG";
    public const string RunRootsRequired = "RUN-ROOTS-REQUIRED";
    public const string RunRootEmpty = "RUN-ROOT-EMPTY";
    public const string RunInvalidMode = "RUN-INVALID-MODE";
    public const string RunInvalidIdempotencyKey = "RUN-INVALID-IDEMPOTENCY-KEY";
    public const string RunTooManyRegions = "RUN-TOO-MANY-REGIONS";
    public const string RunInvalidRegion = "RUN-INVALID-REGION";
    public const string RunInvalidHashType = "RUN-INVALID-HASH-TYPE";
    public const string RunInvalidExtension = "RUN-INVALID-EXTENSION";
    public const string RunInvalidConflictPolicy = "RUN-INVALID-CONFLICT-POLICY";
    public const string RunInvalidConvertFormat = "RUN-INVALID-CONVERT-FORMAT";
    public const string RunInvalidUnknownPolicy = "RUN-INVALID-UNKNOWN-POLICY";
    public const string RunInvalidWaitTimeout = "RUN-INVALID-WAIT-TIMEOUT";
    public const string RunActiveConflict = "RUN-ACTIVE-CONFLICT";
    public const string RunIdempotencyConflict = "RUN-IDEMPOTENCY-CONFLICT";
    public const string RunPayloadTooLarge = "RUN-PAYLOAD-TOO-LARGE";
    public const string RunTooManyPaths = "RUN-TOO-MANY-PATHS";
    public const string RunInvalidReviewOffset = "RUN-INVALID-REVIEW-OFFSET";
    public const string RunInvalidReviewLimit = "RUN-INVALID-REVIEW-LIMIT";
    public const string RunRollbackNotAvailable = "RUN-ROLLBACK-NOT-AVAILABLE";
    /// <summary>POST /run with mode=Move requires the X-Confirm-Token header set to 'MOVE'.
    /// Mirrors the GUI typed-token confirmation gate for fachliche Paritaet.</summary>
    public const string RunMoveConfirmationRequired = "RUN-MOVE-CONFIRMATION-REQUIRED";

    // ── DAT ──────────────────────────────────────────────────────────

    public const string DatNotAvailable = "DAT-NOT-AVAILABLE";
    public const string DatRootNotConfigured = "DAT-ROOT-NOT-CONFIGURED";
    public const string DatRootCreateFailed = "DAT-ROOT-CREATE-FAILED";
    public const string DatInvalidJson = "DAT-INVALID-JSON";
    public const string DatCatalogNotFound = "DAT-CATALOG-NOT-FOUND";
    public const string DatCatalogLoadError = "DAT-CATALOG-LOAD-ERROR";
    public const string DatCatalogEmpty = "DAT-CATALOG-EMPTY";
    public const string DatRootNotFound = "DAT-ROOT-NOT-FOUND";
    public const string DatBodyTooLarge = "DAT-BODY-TOO-LARGE";
    public const string DatReadError = "DAT-READ-ERROR";
    public const string DatPathRequired = "DAT-PATH-REQUIRED";
    public const string DatSourceNotFound = "DAT-SOURCE-NOT-FOUND";
    public const string DatInvalidFormat = "DAT-INVALID-FORMAT";
    public const string DatImportBlocked = "DAT-IMPORT-BLOCKED";
    public const string DatImportIoError = "DAT-IMPORT-IO-ERROR";

    // ── PROVENANCE ───────────────────────────────────────────────────

    public const string ProvenanceInvalidFingerprint = "PROVENANCE-INVALID-FINGERPRINT";

    // ── POLICY ───────────────────────────────────────────────────────

    public const string PolicyInvalid = "POLICY-INVALID";
    public const string PolicyTextRequired = "POLICY-TEXT-REQUIRED";
    public const string PolicyRootsRequired = "POLICY-ROOTS-REQUIRED";
    public const string PolicyRootEmpty = "POLICY-ROOT-EMPTY";

    // ── WATCH ────────────────────────────────────────────────────────

    public const string WatchInvalidJson = "WATCH-INVALID-JSON";
    public const string WatchInvalidConfig = "WATCH-INVALID-CONFIG";
    public const string WatchRootsRequired = "WATCH-ROOTS-REQUIRED";
    public const string WatchInvalidDebounce = "WATCH-INVALID-DEBOUNCE";
    public const string WatchInvalidInterval = "WATCH-INVALID-INTERVAL";
    public const string WatchScheduleRequired = "WATCH-SCHEDULE-REQUIRED";
    public const string WatchInvalidCron = "WATCH-INVALID-CRON";
    public const string WatchRootEmpty = "WATCH-ROOT-EMPTY";
    public const string WatchInvalidMode = "WATCH-INVALID-MODE";

    // ── IO ───────────────────────────────────────────────────────────

    public const string IoRootNotFound = "IO-ROOT-NOT-FOUND";

    // ── PROFILE ──────────────────────────────────────────────────────

    public const string ProfileNotFound = "PROFILE-NOT-FOUND";
    public const string ProfileInvalid = "PROFILE-INVALID";
    public const string ProfileDeleteBlocked = "PROFILE-DELETE-BLOCKED";

    // ── WORKFLOW ──────────────────────────────────────────────────────

    public const string WorkflowNotFound = "WORKFLOW-NOT-FOUND";

    // ── COLLECTION ───────────────────────────────────────────────────

    public const string CollectionCompareInvalidLimit = "COLLECTION-COMPARE-INVALID-LIMIT";
    public const string CollectionCompareNotReady = "COLLECTION-COMPARE-NOT-READY";
    public const string CollectionMergeNotReady = "COLLECTION-MERGE-NOT-READY";
    public const string CollectionMergeApplyNotReady = "COLLECTION-MERGE-APPLY-NOT-READY";
    public const string CollectionMergeInvalidLimit = "COLLECTION-MERGE-INVALID-LIMIT";
    public const string CollectionMergeTargetRequired = "COLLECTION-MERGE-TARGET-REQUIRED";
    public const string CollectionMergeRollbackAuditRequired = "COLLECTION-MERGE-ROLLBACK-AUDIT-REQUIRED";
    public const string CollectionMergeRollbackAuditNotFound = "COLLECTION-MERGE-ROLLBACK-AUDIT-NOT-FOUND";
    public const string CollectionMergeRollbackRootsUnavailable = "COLLECTION-MERGE-ROLLBACK-ROOTS-UNAVAILABLE";
}
