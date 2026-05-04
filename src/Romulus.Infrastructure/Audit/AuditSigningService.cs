using System.Globalization;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.FileSystem;

namespace Romulus.Infrastructure.Audit;

/// <summary>
/// HMAC-SHA256 audit signing, metadata sidecars, and rollback.
/// Port of RunHelpers.Audit.ps1.
/// </summary>
public sealed class AuditSigningService
{
    private const int HmacKeyLengthBytes = 32;
    private const string CurrentMetadataVersion = "v2";
    private static readonly object LedgerLock = new();

    private byte[]? _persistedKey;
    private readonly object _keyLock = new();
    private readonly IFileSystem _fs;
    private readonly Action<string>? _log;
    private readonly string? _keyFilePath;

    public AuditSigningService(IFileSystem fs, Action<string>? log = null, string? keyFilePath = null)
    {
        _fs = fs;
        _log = log;
        _keyFilePath = keyFilePath;
    }

    /// <summary>
    /// Get or create the HMAC signing key. If a key file path is configured,
    /// the key is persisted to disk so signatures survive app restarts.
    /// </summary>
    private byte[] GetSigningKey()
    {
        lock (_keyLock)
        {
            if (_persistedKey is not null)
                return _persistedKey;

            if (!string.IsNullOrEmpty(_keyFilePath) && File.Exists(_keyFilePath))
                return _persistedKey = LoadExistingSigningKey(_keyFilePath);

            var key = new byte[32];
            RandomNumberGenerator.Fill(key);

            if (!string.IsNullOrEmpty(_keyFilePath))
            {
                try
                {
                    WriteNewSigningKeyFileSecurely(_keyFilePath, key);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.SecurityException)
                {
                    try { if (File.Exists(_keyFilePath)) File.Delete(_keyFilePath); }
                    catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException) { }
                    try
                    {
                        var tmpPath = _keyFilePath + ".tmp";
                        if (File.Exists(tmpPath))
                            File.Delete(tmpPath);
                    }
                    catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException) { }

                    _persistedKey = null;
                    throw new InvalidOperationException("HMAC key file cannot be secured", ex);
                }
            }

            _persistedKey = key;
            return _persistedKey;
        }
    }

    private byte[] LoadExistingSigningKey(string keyFilePath)
    {
        try
        {
            EnsureSigningKeyFileSecurity(keyFilePath);
            var hex = File.ReadAllText(keyFilePath, Encoding.UTF8).Trim();
            var key = Convert.FromHexString(hex);
            if (key.Length != HmacKeyLengthBytes)
                throw new InvalidDataException($"HMAC key must be {HmacKeyLengthBytes} bytes.");

            return key;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException or InvalidDataException)
        {
            QuarantineInvalidKeyFile(keyFilePath);
            throw new InvalidOperationException("HMAC key file is missing, corrupt, or has unsafe permissions.", ex);
        }
    }

    private static void WriteNewSigningKeyFileSecurely(string keyFilePath, byte[] key)
    {
        var dir = Path.GetDirectoryName(keyFilePath);
        if (!string.IsNullOrEmpty(dir))
            Directory.CreateDirectory(dir);

        var tempPath = Path.Combine(
            string.IsNullOrWhiteSpace(dir) ? Directory.GetCurrentDirectory() : dir,
            $".{Path.GetFileName(keyFilePath)}.{Environment.ProcessId}.{Guid.NewGuid():N}.tmp");

        try
        {
            var content = Encoding.UTF8.GetBytes(Convert.ToHexStringLower(key));
            if (OperatingSystem.IsWindows())
            {
                var security = BuildCurrentUserOnlyFileSecurity();
                using var stream = System.IO.FileSystemAclExtensions.Create(
                    new FileInfo(tempPath),
                    FileMode.CreateNew,
                    FileSystemRights.Read | FileSystemRights.Write,
                    FileShare.None,
                    4096,
                    FileOptions.WriteThrough,
                    security);
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }
            else
            {
                using var stream = new FileStream(tempPath, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    File.SetUnixFileMode(tempPath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                stream.Write(content);
                stream.Flush(flushToDisk: true);
            }

            File.Move(tempPath, keyFilePath, overwrite: false);
            EnsureSigningKeyFileSecurity(keyFilePath);
        }
        finally
        {
            try
            {
                if (File.Exists(tempPath))
                    File.Delete(tempPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private static FileSecurity BuildCurrentUserOnlyFileSecurity()
    {
        var currentUser = WindowsIdentity.GetCurrent().User
                          ?? throw new InvalidOperationException("Could not resolve current Windows identity SID.");
        var security = new FileSecurity();
        security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        security.AddAccessRule(new FileSystemAccessRule(
            currentUser,
            FileSystemRights.FullControl,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow));
        return security;
    }

    private static void EnsureSigningKeyFileSecurity(string keyFilePath)
    {
        if (OperatingSystem.IsWindows())
        {
            var fileInfo = new FileInfo(keyFilePath);
            var security = fileInfo.GetAccessControl();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            var currentUser = WindowsIdentity.GetCurrent().User
                              ?? throw new InvalidOperationException("Could not resolve current Windows identity SID.");

            var rules = security.GetAccessRules(includeExplicit: true, includeInherited: false, typeof(SecurityIdentifier));
            var hasCurrentUserRule = false;
            foreach (FileSystemAccessRule rule in rules)
            {
                if (rule.IdentityReference.Equals(currentUser)
                    && rule.AccessControlType == AccessControlType.Allow
                    && (rule.FileSystemRights & FileSystemRights.Read) != 0
                    && (rule.FileSystemRights & FileSystemRights.Write) != 0)
                {
                    hasCurrentUserRule = true;
                }
            }

            if (!hasCurrentUserRule)
            {
                security.AddAccessRule(new FileSystemAccessRule(
                    currentUser,
                    FileSystemRights.FullControl,
                    InheritanceFlags.None,
                    PropagationFlags.None,
                    AccessControlType.Allow));
            }

            fileInfo.SetAccessControl(security);
            return;
        }

        if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
        {
            var mode = File.GetUnixFileMode(keyFilePath);
            var unsafeBits =
                UnixFileMode.GroupRead | UnixFileMode.GroupWrite | UnixFileMode.GroupExecute |
                UnixFileMode.OtherRead | UnixFileMode.OtherWrite | UnixFileMode.OtherExecute;
            if ((mode & unsafeBits) != 0)
            {
                File.SetUnixFileMode(keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite);
                mode = File.GetUnixFileMode(keyFilePath);
                if ((mode & unsafeBits) != 0)
                    throw new UnauthorizedAccessException("HMAC key file is readable or writable by group/other.");
            }
        }
    }

    private static void QuarantineInvalidKeyFile(string keyFilePath)
    {
        if (!File.Exists(keyFilePath))
            return;

        var dir = Path.GetDirectoryName(keyFilePath) ?? Directory.GetCurrentDirectory();
        var quarantineDir = Path.Combine(dir, "quarantine");
        Directory.CreateDirectory(quarantineDir);
        var quarantinePath = Path.Combine(
            quarantineDir,
            $"{Path.GetFileName(keyFilePath)}.{DateTime.UtcNow:yyyyMMddHHmmssfff}.{Guid.NewGuid():N}.bad");
        File.Move(keyFilePath, quarantinePath, overwrite: false);
    }

    /// <summary>
    /// Compute SHA256 hash of a file.
    /// </summary>
    public static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        var hash = SHA256.HashData(stream);
        return Convert.ToHexStringLower(hash);
    }

    private static string ComputeAuditPathSha256(string auditCsvPath)
    {
        var normalizedPath = Path.GetFullPath(auditCsvPath).Normalize(NormalizationForm.FormC);
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(normalizedPath));
        return Convert.ToHexStringLower(hash);
    }

    private static string ComputeKeyId(byte[] key)
    {
        var hash = SHA256.HashData(key);
        return Convert.ToHexStringLower(hash.AsSpan(0, 16));
    }

    private string? GetLedgerPath()
    {
        if (string.IsNullOrWhiteSpace(_keyFilePath))
            return null;

        return _keyFilePath + ".ledger.jsonl";
    }

    private string? ReadLatestLedgerHmac(string auditPathSha256)
    {
        var ledgerPath = GetLedgerPath();
        if (ledgerPath is null || !File.Exists(ledgerPath))
            return null;

        lock (LedgerLock)
        {
            string? latest = null;
            foreach (var line in File.ReadLines(ledgerPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonSerializer.Deserialize<AuditLedgerEntry>(line);
                if (entry is not null
                    && string.Equals(entry.AuditPathSha256, auditPathSha256, StringComparison.Ordinal)
                    && !string.IsNullOrWhiteSpace(entry.CurrentSidecarHmac))
                {
                    latest = entry.CurrentSidecarHmac;
                }
            }

            return latest;
        }
    }

    private void AppendLedgerEntry(
        string auditPathSha256,
        string currentSidecarHmac,
        string previousSidecarHmac,
        string keyId,
        string createdUtc)
    {
        var ledgerPath = GetLedgerPath();
        if (ledgerPath is null)
            return;

        var ledgerDir = Path.GetDirectoryName(ledgerPath);
        if (!string.IsNullOrWhiteSpace(ledgerDir))
            Directory.CreateDirectory(ledgerDir);

        var entry = new AuditLedgerEntry(
            Version: CurrentMetadataVersion,
            AuditPathSha256: auditPathSha256,
            CurrentSidecarHmac: currentSidecarHmac,
            PreviousSidecarHmac: previousSidecarHmac,
            KeyId: keyId,
            CreatedUtc: createdUtc);
        var line = JsonSerializer.Serialize(entry) + "\n";

        lock (LedgerLock)
        {
            AtomicFileWriter.AppendText(ledgerPath, line, Encoding.UTF8);
        }
    }

    private void VerifyLedgerLatestEntry(AuditMetadata metadata)
    {
        var ledgerPath = GetLedgerPath();
        if (ledgerPath is null)
            return;

        if (!File.Exists(ledgerPath))
            throw new InvalidDataException("Audit ledger is missing.");

        AuditLedgerEntry? latestForAudit = null;
        lock (LedgerLock)
        {
            foreach (var line in File.ReadLines(ledgerPath, Encoding.UTF8))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                var entry = JsonSerializer.Deserialize<AuditLedgerEntry>(line);
                if (entry is not null
                    && string.Equals(entry.AuditPathSha256, metadata.AuditPathSha256, StringComparison.Ordinal))
                {
                    latestForAudit = entry;
                }
            }
        }

        if (latestForAudit is null)
            throw new InvalidDataException("Audit sidecar is not present in the append-only ledger.");

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(latestForAudit.CurrentSidecarHmac),
                Encoding.UTF8.GetBytes(metadata.HmacSha256 ?? "")))
        {
            throw new InvalidDataException("Audit sidecar replay detected: ledger contains a newer checkpoint.");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(latestForAudit.PreviousSidecarHmac),
                Encoding.UTF8.GetBytes(metadata.PreviousSidecarHmac ?? "")))
        {
            throw new InvalidDataException("Audit ledger predecessor mismatch.");
        }

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(latestForAudit.KeyId),
                Encoding.UTF8.GetBytes(metadata.KeyId ?? "")))
        {
            throw new InvalidDataException("Audit ledger key id mismatch.");
        }
    }

    private sealed record AuditLedgerEntry(
        string Version,
        string AuditPathSha256,
        string CurrentSidecarHmac,
        string PreviousSidecarHmac,
        string KeyId,
        string CreatedUtc);

    /// <summary>
    /// Build the signature payload string in the canonical format.
    /// </summary>
    public static string BuildSignaturePayload(string auditFileName, string csvSha256, int rowCount, string createdUtc)
        => $"v1|{auditFileName}|{csvSha256}|{rowCount}|{createdUtc}";

    public static string BuildSignaturePayloadV2(
        string auditFileName,
        string auditPathSha256,
        string csvSha256,
        int rowCount,
        string createdUtc,
        string keyId,
        string previousSidecarHmac)
        => $"v2|{auditFileName}|{auditPathSha256}|{csvSha256}|{rowCount}|{createdUtc}|{keyId}|{previousSidecarHmac}";

    /// <summary>
    /// Compute HMAC-SHA256 of a text string using the persisted signing key.
    /// Uses constant-time comparison internally for verification.
    /// </summary>
    public string ComputeHmacSha256(string text)
    {
        var key = GetSigningKey();
        var textBytes = Encoding.UTF8.GetBytes(text);
        var hash = HMACSHA256.HashData(key, textBytes);
        return Convert.ToHexStringLower(hash);
    }

    public string GetSigningKeyId()
    {
        var key = GetSigningKey();
        return ComputeKeyId(key);
    }

    /// <summary>
    /// Write an audit metadata sidecar (.meta.json) with HMAC signature.
    /// </summary>
    public string? WriteMetadataSidecar(string auditCsvPath, int rowCount, IDictionary<string, object>? metadata = null)
    {
        if (!File.Exists(auditCsvPath))
        {
            _log?.Invoke($"Audit CSV not found: {auditCsvPath}");
            return null;
        }

        try
        {
            var csvSha256 = ComputeFileSha256(auditCsvPath);
            var auditFileName = Path.GetFileName(auditCsvPath);
            var auditPathSha256 = ComputeAuditPathSha256(auditCsvPath);
            var createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var key = GetSigningKey();
            var keyId = ComputeKeyId(key);
            var previousSidecarHmac = ReadLatestLedgerHmac(auditPathSha256) ?? "";
            var payload = BuildSignaturePayloadV2(
                auditFileName,
                auditPathSha256,
                csvSha256,
                rowCount,
                createdUtc,
                keyId,
                previousSidecarHmac);
            var hmac = ComputeHmacSha256(payload);

            var auditMetadata = new AuditMetadata
            {
                Version = CurrentMetadataVersion,
                AuditFileName = auditFileName,
                AuditPathSha256 = auditPathSha256,
                CsvSha256 = csvSha256,
                RowCount = rowCount,
                CreatedUtc = createdUtc,
                KeyId = keyId,
                PreviousSidecarHmac = previousSidecarHmac,
                HmacSha256 = hmac,
                AdditionalMetadata = ToJsonExtensionData(metadata)
            };

            var metaPath = auditCsvPath + ".meta.json";
            var json = JsonSerializer.Serialize(auditMetadata, new JsonSerializerOptions { WriteIndented = true });

            // Sidecar+ledger must form an atomic pair. If the ledger append fails after the
            // sidecar was already written, every subsequent VerifyMetadataSidecar throws
            // "sidecar not present in the append-only ledger" and rollback is permanently
            // blocked with AUDIT_INTEGRITY_BROKEN even though the audit CSV is intact.
            // Snapshot any pre-existing sidecar so we can restore it on failure instead of
            // leaving a phantom file that does not match the ledger.
            byte[]? previousSidecarBytes = null;
            if (File.Exists(metaPath))
            {
                try { previousSidecarBytes = File.ReadAllBytes(metaPath); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    previousSidecarBytes = null;
                }
            }

            AtomicFileWriter.WriteAllText(metaPath, json, Encoding.UTF8);

            try
            {
                AppendLedgerEntry(auditPathSha256, hmac, previousSidecarHmac, keyId, createdUtc);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                _log?.Invoke($"Audit ledger append failed; reverting sidecar to keep integrity chain consistent: {ex.Message}");

                try
                {
                    if (previousSidecarBytes is not null)
                    {
                        // Restore previous good sidecar so the ledger's last entry still matches.
                        AtomicFileWriter.WriteAllText(metaPath, Encoding.UTF8.GetString(previousSidecarBytes), Encoding.UTF8);
                    }
                    else if (File.Exists(metaPath))
                    {
                        File.Delete(metaPath);
                    }
                }
                catch (Exception cleanupEx) when (cleanupEx is IOException or UnauthorizedAccessException)
                {
                    _log?.Invoke($"Failed to revert phantom sidecar after ledger failure: {cleanupEx.Message}");
                }

                return null;
            }

            _log?.Invoke($"Audit sidecar written: {metaPath}");
            return metaPath;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"Failed to write audit sidecar: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Verify an audit metadata sidecar against the CSV.
    /// Throws if verification fails.
    /// </summary>
    public bool VerifyMetadataSidecar(string auditCsvPath)
    {
        var metaPath = auditCsvPath + ".meta.json";
        if (!File.Exists(metaPath))
            throw new FileNotFoundException("Audit sidecar not found", metaPath);

        var json = File.ReadAllText(metaPath, Encoding.UTF8);
        var metadata = JsonSerializer.Deserialize<AuditMetadata>(json);
        if (metadata is null)
            throw new InvalidDataException("Failed to deserialize audit sidecar");

        if (!string.Equals(metadata.Version, CurrentMetadataVersion, StringComparison.Ordinal))
            throw new InvalidDataException("Unsupported audit sidecar version. Regenerate the audit checkpoint.");

        var expectedAuditPathSha256 = ComputeAuditPathSha256(auditCsvPath);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedAuditPathSha256),
                Encoding.UTF8.GetBytes(metadata.AuditPathSha256 ?? "")))
        {
            throw new InvalidDataException("Audit sidecar path binding mismatch.");
        }

        if (!string.Equals(metadata.AuditFileName, Path.GetFileName(auditCsvPath), StringComparison.Ordinal))
            throw new InvalidDataException("Audit sidecar file name mismatch.");

        var key = GetSigningKey();
        var expectedKeyId = ComputeKeyId(key);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedKeyId),
                Encoding.UTF8.GetBytes(metadata.KeyId ?? "")))
        {
            throw new InvalidDataException("Audit sidecar key id mismatch.");
        }

        // Verify CSV hash (constant-time comparison to prevent timing attacks — SEC-AUDIT-01)
        var actualSha256 = ComputeFileSha256(auditCsvPath);
        var actualBytes = Encoding.UTF8.GetBytes(actualSha256.ToLowerInvariant());
        var expectedBytes = Encoding.UTF8.GetBytes((metadata.CsvSha256 ?? "").ToLowerInvariant());
        if (!CryptographicOperations.FixedTimeEquals(actualBytes, expectedBytes))
            throw new InvalidDataException($"CSV hash mismatch: expected {metadata.CsvSha256}, got {actualSha256}");

        // Verify HMAC (constant-time comparison to prevent timing attacks)
        var payload = BuildSignaturePayloadV2(
            metadata.AuditFileName,
            metadata.AuditPathSha256 ?? "",
            metadata.CsvSha256 ?? "",
            metadata.RowCount,
            metadata.CreatedUtc,
            metadata.KeyId ?? "",
            metadata.PreviousSidecarHmac ?? "");
        var expectedHmac = ComputeHmacSha256(payload);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHmac),
                Encoding.UTF8.GetBytes(metadata.HmacSha256 ?? "")))
            throw new InvalidDataException("HMAC signature verification failed — audit file may have been tampered with");

        VerifyLedgerLatestEntry(metadata);

        _log?.Invoke($"Audit sidecar verified: {metaPath}");
        return true;
    }

    /// <summary>
    /// Perform audit rollback: reverse moves recorded in the audit CSV.
    /// Supports dry-run mode. Creates a .rollback-audit.csv for the rollback trail.
    /// </summary>
    public AuditRollbackResult Rollback(
        string auditCsvPath,
        IReadOnlyList<string> allowedRestoreRoots,
        IReadOnlyList<string> allowedCurrentRoots,
        bool dryRun = true)
    {
        var metaPath = auditCsvPath + ".meta.json";
        if (!File.Exists(auditCsvPath))
        {
            if (File.Exists(metaPath))
            {
                var metadata = ReadMetadataForFailure(metaPath);
                return new AuditRollbackResult
                {
                    AuditCsvPath = auditCsvPath,
                    DryRun = dryRun,
                    Failed = Math.Max(1, metadata?.RowCount ?? 0),
                    Tampered = true,
                    IntegrityError = "AUDIT_CSV_MISSING_WITH_SIDECAR"
                };
            }

            return new AuditRollbackResult
            {
                AuditCsvPath = auditCsvPath,
                DryRun = dryRun,
                IntegrityError = "AUDIT_CSV_MISSING"
            };
        }

        // SEC-ROLLBACK-03: Verify audit file integrity before rollback (dry-run and execute).
        // Preview/Execute must make the same safety decision to keep parity deterministic.
        if (File.Exists(metaPath))
        {
            try
            {
                VerifyMetadataSidecar(auditCsvPath);
            }
            catch (Exception ex) when (ex is FileNotFoundException or InvalidDataException or IOException or UnauthorizedAccessException or JsonException)
            {
                _log?.Invoke($"Audit integrity check failed: {ex.Message}");
                return new AuditRollbackResult
                {
                    AuditCsvPath = auditCsvPath,
                    DryRun = dryRun,
                    Failed = CountAuditDataRows(auditCsvPath),
                    Tampered = true,
                    IntegrityError = "AUDIT_INTEGRITY_BROKEN"
                };
            }
        }
        else
        {
            _log?.Invoke("Rollback blocked: No integrity sidecar (.meta.json) found. Cannot verify audit integrity.");
            return new AuditRollbackResult
            {
                AuditCsvPath = auditCsvPath,
                DryRun = dryRun,
                Failed = CountAuditDataRows(auditCsvPath),
                Tampered = true,
                IntegrityError = "AUDIT_SIDECAR_MISSING"
            };
        }

        if (!HasAuditDataRows(auditCsvPath))
        {
            return new AuditRollbackResult
            {
                AuditCsvPath = auditCsvPath,
                DryRun = dryRun
            };
        }

        int totalRows = 0, eligible = 0, skippedUnsafe = 0;
        int rolledBack = 0, dryRunPlanned = 0;
        int skippedMissingDest = 0, skippedCollision = 0, failed = 0;
        string? rollbackAuditPath = null;
        string? rollbackTrailPath = null;
        var restoredPaths = new List<string>();
        var plannedPaths = new List<string>();
        var processedPendingOperationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var suppressedPendingOperationKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var pendingConvertSourceRollbacks = new List<bool>();

        if (!dryRun)
        {
            rollbackAuditPath = Path.ChangeExtension(auditCsvPath, ".rollback-audit.csv");
            AtomicFileWriter.WriteAllText(rollbackAuditPath, "Timestamp,Action,OldPath,NewPath,Status\n", Encoding.UTF8);

            rollbackTrailPath = Path.ChangeExtension(auditCsvPath, ".rollback-trail.csv");
            AtomicFileWriter.WriteAllText(rollbackTrailPath, "RestoredPath,RestoredFrom,OriginalAction,Timestamp\n", Encoding.UTF8);
        }

        // Pre-cache normalized root paths to avoid Path.GetFullPath per root per row (expensive on UNC)
        // Issue #21: NFC normalization for macOS HFS+ paths
        var normalizedCurrentRoots = allowedCurrentRoots
            .Select(r => Path.GetFullPath(r).Normalize(NormalizationForm.FormC)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToArray();
        var normalizedRestoreRoots = allowedRestoreRoots
            .Select(r => Path.GetFullPath(r).Normalize(NormalizationForm.FormC)
                .TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar)
            .ToArray();

        // Process rows in reverse order (undo last moves first)
        foreach (var line in ReadAuditRowsReverse(auditCsvPath))
        {
            if (string.IsNullOrWhiteSpace(line)) continue;

            totalRows++;
            string[] fields;
            try
            {
                fields = AuditCsvParser.ParseCsvLine(line);
            }
            catch (InvalidDataException)
            {
                skippedUnsafe++;
                _log?.Invoke("Rollback skipped (corrupt CSV row). ");
                continue;
            }

            if (fields.Length < 4) continue;

            // Fields: RootPath, OldPath, NewPath, Action, Category, Hash, Reason, Timestamp
            var oldPath = fields.Length > 1 ? fields[1] : "";
            var newPath = fields.Length > 2 ? fields[2] : "";
            var action = fields.Length > 3 ? fields[3] : "";

            if (string.Equals(action, RunConstants.AuditActions.MoveFailed, StringComparison.OrdinalIgnoreCase))
            {
                suppressedPendingOperationKeys.Add(BuildPendingOperationKey(RunConstants.AuditActions.Move, oldPath, newPath));
                continue;
            }

            if (string.Equals(action, RunConstants.AuditActions.CopyFailed, StringComparison.OrdinalIgnoreCase))
            {
                suppressedPendingOperationKeys.Add(BuildPendingOperationKey(RunConstants.AuditActions.Copy, oldPath, newPath));
                continue;
            }

            if (string.Equals(action, RunConstants.AuditActions.DatRenameFailed, StringComparison.OrdinalIgnoreCase))
            {
                suppressedPendingOperationKeys.Add(BuildPendingOperationKey(RunConstants.AuditActions.DatRename, oldPath, newPath));
                continue;
            }

            var normalizedAction = NormalizeRollbackAction(action);
            if (normalizedAction is null)
                continue;

            var isPendingMoveAction = string.Equals(action, RunConstants.AuditActions.MovePending, StringComparison.OrdinalIgnoreCase);
            var isPendingCopyAction = string.Equals(action, RunConstants.AuditActions.CopyPending, StringComparison.OrdinalIgnoreCase);
            var isPendingDatRenameAction = string.Equals(action, RunConstants.AuditActions.DatRenamePending, StringComparison.OrdinalIgnoreCase);
            var isPendingAction = isPendingMoveAction || isPendingCopyAction || isPendingDatRenameAction;
            var isConvertCreateAction = string.Equals(normalizedAction, RunConstants.AuditActions.Convert, StringComparison.OrdinalIgnoreCase);
            var isConvertSourceAction = string.Equals(normalizedAction, RunConstants.AuditActions.ConvertSource, StringComparison.OrdinalIgnoreCase);
            var isCopyAction = string.Equals(normalizedAction, RunConstants.AuditActions.Copy, StringComparison.OrdinalIgnoreCase);
            var isDatRenameAction = string.Equals(normalizedAction, RunConstants.AuditActions.DatRename, StringComparison.OrdinalIgnoreCase);

            if (pendingConvertSourceRollbacks.Count > 0 && !isConvertSourceAction && !isConvertCreateAction)
                pendingConvertSourceRollbacks.Clear();

            var pendingOperationKey = BuildPendingOperationKey(normalizedAction, oldPath, newPath);
            if (isPendingAction)
            {
                if (suppressedPendingOperationKeys.Contains(pendingOperationKey)
                    || processedPendingOperationKeys.Contains(pendingOperationKey))
                {
                    continue;
                }

                processedPendingOperationKeys.Add(pendingOperationKey);
            }
            else if (string.Equals(normalizedAction, RunConstants.AuditActions.Move, StringComparison.OrdinalIgnoreCase)
                     || isCopyAction
                     || isDatRenameAction)
            {
                processedPendingOperationKeys.Add(pendingOperationKey);
            }

            eligible++;

            // Safety: check the current location (newPath) is within allowed roots
            var fullNewPath = Path.GetFullPath(newPath).Normalize(NormalizationForm.FormC);
            var fullOldPath = Path.GetFullPath(oldPath).Normalize(NormalizationForm.FormC);
            var inAllowedCurrent = normalizedCurrentRoots.Any(nr =>
                fullNewPath.StartsWith(nr, StringComparison.OrdinalIgnoreCase));
            var inAllowedRestore = normalizedRestoreRoots.Any(nr =>
                fullOldPath.StartsWith(nr, StringComparison.OrdinalIgnoreCase));

            var requiresRestoreTarget = !isConvertCreateAction;

            if (!inAllowedCurrent || (requiresRestoreTarget && !inAllowedRestore))
            {
                skippedUnsafe++;
                if (isConvertSourceAction)
                    pendingConvertSourceRollbacks.Add(false);
                continue;
            }

            // Check current file/dir exists at newPath
            // Missing dest = recovery failure (user can't roll back this entry)
            if (!File.Exists(newPath) && !Directory.Exists(newPath))
            {
                if (isPendingAction)
                {
                    _log?.Invoke($"Rollback skipped (pending action without materialized dest): {newPath}");
                }
                else
                {
                    failed++;
                    skippedMissingDest++;
                    _log?.Invoke($"Rollback failed (missing dest): {newPath}");
                }

                if (isConvertSourceAction)
                    pendingConvertSourceRollbacks.Add(false);
                continue;
            }

            if (requiresRestoreTarget && !isCopyAction)
            {
                // Check no collision at oldPath
                if (File.Exists(oldPath) || Directory.Exists(oldPath))
                {
                    skippedCollision++;
                    if (isConvertSourceAction)
                        pendingConvertSourceRollbacks.Add(false);
                    continue;
                }
            }
            else if (isCopyAction && !File.Exists(oldPath) && !Directory.Exists(oldPath))
            {
                failed++;
                skippedMissingDest++;
                _log?.Invoke($"Rollback failed (missing copy source): {oldPath}");
                continue;
            }

            if (isConvertCreateAction && pendingConvertSourceRollbacks.Count > 0 && pendingConvertSourceRollbacks.Any(static success => !success))
            {
                _log?.Invoke($"Rollback skipped (conversion source restore incomplete): {newPath}");
                pendingConvertSourceRollbacks.Clear();
                continue;
            }

            if (dryRun)
            {
                // SEC-ROLLBACK-01: In dry run, check reparse points and account as unsafe skip
                // to keep DryRun/Execute counters semantically aligned.
                if (_fs.IsReparsePoint(newPath))
                {
                    skippedUnsafe++;
                    _log?.Invoke($"DRYRUN rollback blocked (reparse point): {newPath}");
                    if (isConvertSourceAction)
                        pendingConvertSourceRollbacks.Add(false);
                    continue;
                }

                // SEC-ROLLBACK-04b: In dry run, also check restore target parent for reparse points (Preview/Execute parity)
                var dryRunParent = requiresRestoreTarget ? Path.GetDirectoryName(oldPath) : null;
                if (dryRunParent is not null && Directory.Exists(dryRunParent) && _fs.IsReparsePoint(dryRunParent))
                {
                    skippedUnsafe++;
                    _log?.Invoke($"DRYRUN rollback blocked (restore parent is reparse point): {dryRunParent}");
                    if (isConvertSourceAction)
                        pendingConvertSourceRollbacks.Add(false);
                    continue;
                }

                dryRunPlanned++;
                plannedPaths.Add(isCopyAction || isConvertCreateAction ? newPath : oldPath);
                _log?.Invoke(isConvertCreateAction
                    ? $"DRYRUN rollback convert-delete: {newPath}"
                    : isCopyAction
                    ? $"DRYRUN rollback copy-delete: {newPath}"
                    : $"DRYRUN rollback: {newPath} -> {oldPath}");
                if (isConvertSourceAction)
                    pendingConvertSourceRollbacks.Add(true);
                else if (isConvertCreateAction)
                    pendingConvertSourceRollbacks.Clear();
            }
            else
            {
                // SEC-ROLLBACK-02: In execute, check reparse points → skip as unsafe
                if (_fs.IsReparsePoint(newPath))
                {
                    skippedUnsafe++;
                    _log?.Invoke($"Rollback skipped (reparse point): {newPath}");
                    if (isConvertSourceAction)
                        pendingConvertSourceRollbacks.Add(false);
                    continue;
                }

                try
                {
                    if (isCopyAction || isConvertCreateAction)
                    {
                        _fs.DeleteFile(newPath);
                        rolledBack++;
                        restoredPaths.Add(newPath);
                        _log?.Invoke(isConvertCreateAction
                            ? $"Rolled back conversion target: removed {newPath}"
                            : $"Rolled back copy: removed {newPath}");
                        AppendRollbackRow(
                            rollbackAuditPath!,
                            isConvertCreateAction ? "ROLLBACK_CONVERT" : "ROLLBACK_COPY",
                            newPath,
                            oldPath,
                            "OK");
                        AppendRollbackTrailRow(rollbackTrailPath!, newPath, oldPath, action);
                        if (isConvertCreateAction)
                            pendingConvertSourceRollbacks.Clear();
                    }
                    else
                    {
                        // SEC-ROLLBACK-04: Check restore target parent for reparse points
                        var parentDir = Path.GetDirectoryName(oldPath);
                        if (parentDir is not null && Directory.Exists(parentDir) && _fs.IsReparsePoint(parentDir))
                        {
                            skippedUnsafe++;
                            _log?.Invoke($"Rollback skipped (restore parent is reparse point): {parentDir}");
                            if (isConvertSourceAction)
                                pendingConvertSourceRollbacks.Add(false);
                            continue;
                        }
                        if (parentDir is not null)
                            _fs.EnsureDirectory(parentDir);

                        if (_fs.MoveItemSafely(newPath, oldPath) is not null)
                        {
                            rolledBack++;
                            restoredPaths.Add(oldPath);
                            _log?.Invoke($"Rolled back: {newPath} -> {oldPath}");
                            AppendRollbackRow(rollbackAuditPath!, "ROLLBACK", newPath, oldPath, "OK");
                            AppendRollbackTrailRow(rollbackTrailPath!, oldPath, newPath, action);
                            if (isConvertSourceAction)
                                pendingConvertSourceRollbacks.Add(true);
                        }
                        else
                        {
                            failed++;
                            AppendRollbackRow(rollbackAuditPath!, "ROLLBACK", newPath, oldPath, "MOVE_FAILED");
                            if (isConvertSourceAction)
                                pendingConvertSourceRollbacks.Add(false);
                        }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    failed++;
                    _log?.Invoke($"Rollback failed: {newPath} -> {oldPath}: {ex.Message}");
                    AppendRollbackRow(rollbackAuditPath!, "ROLLBACK", newPath, oldPath, $"ERROR: {ex.Message}");
                    if (isConvertSourceAction)
                        pendingConvertSourceRollbacks.Add(false);
                }
            }
        }

        return new AuditRollbackResult
        {
            AuditCsvPath = auditCsvPath,
            TotalRows = totalRows,
            EligibleRows = eligible,
            SkippedUnsafe = skippedUnsafe,
            RolledBack = rolledBack,
            DryRunPlanned = dryRunPlanned,
            SkippedMissingDest = skippedMissingDest,
            SkippedCollision = skippedCollision,
            Failed = failed,
            DryRun = dryRun,
            RollbackAuditPath = rollbackAuditPath,
            RollbackTrailPath = rollbackTrailPath,
            RestoredPaths = restoredPaths,
            PlannedPaths = plannedPaths
        };
    }

    private static bool HasAuditDataRows(string auditCsvPath)
    {
        foreach (var row in ReadLogicalCsvRecords(auditCsvPath).Skip(1))
        {
            if (!string.IsNullOrWhiteSpace(row))
                return true;
        }

        return false;
    }

    private static int CountAuditDataRows(string auditCsvPath)
    {
        try
        {
            return AuditCsvStore.CountAuditRows(auditCsvPath);
        }
        catch (InvalidDataException)
        {
            var count = 0;
            foreach (var line in File.ReadLines(auditCsvPath, Encoding.UTF8).Skip(1))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;

                try
                {
                    if (AuditCsvParser.ParseCsvLine(line).Length >= 4)
                        count++;
                }
                catch (InvalidDataException)
                {
                    // Corrupt rows are not roll-backable and are not counted as actionable rows.
                }
            }

            return count;
        }
    }

    private static AuditMetadata? ReadMetadataForFailure(string metaPath)
    {
        try
        {
            var json = File.ReadAllText(metaPath, Encoding.UTF8);
            return JsonSerializer.Deserialize<AuditMetadata>(json);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return null;
        }
    }

    private static IEnumerable<string> ReadAuditRowsReverse(string auditCsvPath)
    {
        var spoolPath = Path.Combine(Path.GetTempPath(), $"audit-rollback-{Guid.NewGuid():N}.spool");

        try
        {
            using (var spool = new FileStream(spoolPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(spool, Encoding.UTF8, leaveOpen: true))
            {
                foreach (var line in ReadLogicalCsvRecords(auditCsvPath).Skip(1))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    var bytes = Encoding.UTF8.GetBytes(line);
                    writer.Write(bytes.Length);
                    writer.Write(bytes);
                    writer.Write(bytes.Length);
                }
            }

            using var spoolRead = new FileStream(spoolPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            using var binaryReader = new BinaryReader(spoolRead, Encoding.UTF8, leaveOpen: true);

            var cursor = spoolRead.Length;
            while (cursor >= sizeof(int))
            {
                cursor -= sizeof(int);
                spoolRead.Position = cursor;
                var trailingLength = binaryReader.ReadInt32();

                if (trailingLength < 0 || cursor < trailingLength + sizeof(int))
                    throw new InvalidDataException("Malformed rollback spool record.");

                cursor -= trailingLength;
                spoolRead.Position = cursor;
                var payload = binaryReader.ReadBytes(trailingLength);

                cursor -= sizeof(int);
                spoolRead.Position = cursor;
                var leadingLength = binaryReader.ReadInt32();
                if (leadingLength != trailingLength)
                    throw new InvalidDataException("Rollback spool record length mismatch.");

                yield return Encoding.UTF8.GetString(payload);
            }
        }
        finally
        {
            try
            {
                if (File.Exists(spoolPath))
                    File.Delete(spoolPath);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Best effort cleanup.
            }
        }
    }

    private static IEnumerable<string> ReadLogicalCsvRecords(string auditCsvPath)
    {
        using var input = new FileStream(auditCsvPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(input, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        var current = new StringBuilder();
        var inQuotes = false;
        while (reader.Read() is var value && value >= 0)
        {
            var c = (char)value;
            current.Append(c);

            if (c == '"')
            {
                if (inQuotes && reader.Peek() == '"')
                {
                    current.Append((char)reader.Read());
                    continue;
                }

                inQuotes = !inQuotes;
            }

            if ((c == '\n' || c == '\r') && !inQuotes)
            {
                var row = current.ToString().TrimEnd('\r', '\n');
                current.Clear();
                if (row.Length > 0)
                    yield return row;
            }
        }

        if (current.Length > 0)
            yield return current.ToString();
    }

    internal static string? NormalizeRollbackAction(string action)
    {
        if (string.Equals(action, RunConstants.AuditActions.MovePending, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, RunConstants.AuditActions.Move, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, RunConstants.AuditActions.Moved, StringComparison.OrdinalIgnoreCase))
        {
            return RunConstants.AuditActions.Move;
        }

        if (string.Equals(action, RunConstants.AuditActions.CopyPending, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, RunConstants.AuditActions.Copy, StringComparison.OrdinalIgnoreCase))
        {
            return RunConstants.AuditActions.Copy;
        }

        if (string.Equals(action, RunConstants.AuditActions.DatRenamePending, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, RunConstants.AuditActions.DatRename, StringComparison.OrdinalIgnoreCase))
        {
            return RunConstants.AuditActions.DatRename;
        }

        if (string.Equals(action, RunConstants.AuditActions.JunkRemove, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, RunConstants.AuditActions.ConsoleSort, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, RunConstants.AuditActions.Convert, StringComparison.OrdinalIgnoreCase)
            || string.Equals(action, RunConstants.AuditActions.ConvertSource, StringComparison.OrdinalIgnoreCase))
        {
            return action.ToUpperInvariant();
        }

        return null;
    }

    internal static string BuildPendingOperationKey(string action, string oldPath, string newPath)
        => $"{action}|{oldPath}|{newPath}";

    /// <summary>
    /// Sanitize a CSV field to prevent CSV injection.
    /// </summary>
    public static string SanitizeCsvField(string value) => AuditCsvParser.SanitizeCsvField(value);

    private static IDictionary<string, JsonElement>? ToJsonExtensionData(IDictionary<string, object>? metadata)
    {
        if (metadata is null || metadata.Count == 0)
            return null;

        var extensionData = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in metadata)
            extensionData[entry.Key] = JsonSerializer.SerializeToElement(entry.Value);

        return extensionData;
    }

    private static void AppendRollbackRow(string path, string action, string from, string to, string status)
    {
        var timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var line = $"{SanitizeCsvField(timestamp)},{SanitizeCsvField(action)},{SanitizeCsvField(from)},{SanitizeCsvField(to)},{SanitizeCsvField(status)}\n";
        AtomicFileWriter.AppendText(path, line, Encoding.UTF8);
    }

    private static void AppendRollbackTrailRow(string path, string restoredPath, string restoredFrom, string originalAction)
    {
        var timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var line = $"{SanitizeCsvField(restoredPath)},{SanitizeCsvField(restoredFrom)},{SanitizeCsvField(originalAction)},{SanitizeCsvField(timestamp)}\n";
        AtomicFileWriter.AppendText(path, line, Encoding.UTF8);
    }
}
