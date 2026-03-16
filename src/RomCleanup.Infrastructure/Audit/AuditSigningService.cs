using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// HMAC-SHA256 audit signing, metadata sidecars, and rollback.
/// Port of RunHelpers.Audit.ps1.
/// </summary>
public sealed class AuditSigningService
{
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

            // Try to load from file
            if (!string.IsNullOrEmpty(_keyFilePath) && File.Exists(_keyFilePath))
            {
                try
                {
                    var hex = File.ReadAllText(_keyFilePath, Encoding.UTF8).Trim();
                    _persistedKey = Convert.FromHexString(hex);
                    return _persistedKey;
                }
                catch
                {
                    _log?.Invoke("Failed to load HMAC key file, generating new key");
                }
            }

            // Generate new key
            var key = new byte[32];
            RandomNumberGenerator.Fill(key);
            _persistedKey = key;

            // Persist to file if path configured
            if (!string.IsNullOrEmpty(_keyFilePath))
            {
                try
                {
                    var dir = Path.GetDirectoryName(_keyFilePath);
                    if (!string.IsNullOrEmpty(dir))
                        Directory.CreateDirectory(dir);
                    // V2-SEC-H01: Atomic write via temp+rename to prevent corrupt key on crash
                    var tmpPath = _keyFilePath + ".tmp";
                    File.WriteAllText(tmpPath, Convert.ToHexStringLower(key), Encoding.UTF8);
                    File.Move(tmpPath, _keyFilePath, overwrite: true);

                    // Restrict file permissions to current user only (Windows-only API)
                    if (OperatingSystem.IsWindows())
                    {
                        try
                        {
                            var fi = new FileInfo(_keyFilePath);
                            var security = fi.GetAccessControl();
                            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                            var currentUser = System.Security.Principal.WindowsIdentity.GetCurrent().Name;
                            security.AddAccessRule(new System.Security.AccessControl.FileSystemAccessRule(
                                currentUser,
                                System.Security.AccessControl.FileSystemRights.FullControl,
                                System.Security.AccessControl.AccessControlType.Allow));
                            fi.SetAccessControl(security);
                        }
                        catch
                        {
                            _log?.Invoke("Could not restrict HMAC key file permissions — manual ACL recommended");
                        }
                    }
                    // V2-SEC-M02: Set Unix file permissions to owner-only (0600)
                    else if (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
                    {
                        try { File.SetUnixFileMode(_keyFilePath, UnixFileMode.UserRead | UnixFileMode.UserWrite); }
                        catch { _log?.Invoke("Could not set HMAC key file permissions to 0600"); }
                    }
                }
                catch (Exception ex)
                {
                    _log?.Invoke($"Failed to persist HMAC key: {ex.Message}");
                }
            }

            return _persistedKey;
        }
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

    /// <summary>
    /// Build the signature payload string in the canonical format.
    /// </summary>
    public static string BuildSignaturePayload(string auditFileName, string csvSha256, int rowCount, string createdUtc)
        => $"v1|{auditFileName}|{csvSha256}|{rowCount}|{createdUtc}";

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
            var createdUtc = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
            var payload = BuildSignaturePayload(auditFileName, csvSha256, rowCount, createdUtc);
            var hmac = ComputeHmacSha256(payload);

            var auditMetadata = new AuditMetadata
            {
                Version = "v1",
                AuditFileName = auditFileName,
                CsvSha256 = csvSha256,
                RowCount = rowCount,
                CreatedUtc = createdUtc,
                HmacSha256 = hmac,
                AdditionalMetadata = ToJsonExtensionData(metadata)
            };

            var metaPath = auditCsvPath + ".meta.json";
            var json = JsonSerializer.Serialize(auditMetadata, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(metaPath, json, Encoding.UTF8);

            _log?.Invoke($"Audit sidecar written: {metaPath}");
            return metaPath;
        }
        catch (Exception ex)
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

        // Verify CSV hash
        var actualSha256 = ComputeFileSha256(auditCsvPath);
        if (!string.Equals(actualSha256, metadata.CsvSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"CSV hash mismatch: expected {metadata.CsvSha256}, got {actualSha256}");

        // Verify HMAC (constant-time comparison to prevent timing attacks)
        var payload = BuildSignaturePayload(metadata.AuditFileName, metadata.CsvSha256, metadata.RowCount, metadata.CreatedUtc);
        var expectedHmac = ComputeHmacSha256(payload);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(expectedHmac),
                Encoding.UTF8.GetBytes(metadata.HmacSha256 ?? "")))
            throw new InvalidDataException("HMAC signature verification failed — audit file may have been tampered with");

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
        if (!File.Exists(auditCsvPath))
        {
            return new AuditRollbackResult
            {
                AuditCsvPath = auditCsvPath,
                DryRun = dryRun
            };
        }

        // Verify audit file integrity before executing rollback
        var metaPath = auditCsvPath + ".meta.json";
        if (File.Exists(metaPath))
        {
            try
            {
                VerifyMetadataSidecar(auditCsvPath);
            }
            catch (Exception ex)
            {
                _log?.Invoke($"Audit integrity check failed: {ex.Message}");
                return new AuditRollbackResult
                {
                    AuditCsvPath = auditCsvPath,
                    DryRun = dryRun,
                    Failed = 1
                };
            }
        }

        var lines = File.ReadAllLines(auditCsvPath, Encoding.UTF8);
        if (lines.Length <= 1) // header only
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

        if (!dryRun)
        {
            rollbackAuditPath = Path.ChangeExtension(auditCsvPath, ".rollback-audit.csv");
            File.WriteAllText(rollbackAuditPath, "Timestamp,Action,OldPath,NewPath,Status\n", Encoding.UTF8);
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
        for (int i = lines.Length - 1; i >= 1; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            totalRows++;
            var fields = AuditCsvParser.ParseCsvLine(line);
            if (fields.Length < 4) continue;

            // Fields: RootPath, OldPath, NewPath, Action, Category, Hash, Reason, Timestamp
            var oldPath = fields.Length > 1 ? fields[1] : "";
            var newPath = fields.Length > 2 ? fields[2] : "";
            var action = fields.Length > 3 ? fields[3] : "";

            // Rollback MOVE and JUNK_REMOVE actions (Issue #22)
            if (!string.Equals(action, "MOVE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "MOVED", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "JUNK_REMOVE", StringComparison.OrdinalIgnoreCase))
                continue;

            eligible++;

            // Safety: check the current location (newPath) is within allowed roots
            var fullNewPath = Path.GetFullPath(newPath).Normalize(NormalizationForm.FormC);
            var fullOldPath = Path.GetFullPath(oldPath).Normalize(NormalizationForm.FormC);
            var inAllowedCurrent = normalizedCurrentRoots.Any(nr =>
                fullNewPath.StartsWith(nr, StringComparison.OrdinalIgnoreCase));
            var inAllowedRestore = normalizedRestoreRoots.Any(nr =>
                fullOldPath.StartsWith(nr, StringComparison.OrdinalIgnoreCase));

            if (!inAllowedCurrent || !inAllowedRestore)
            {
                skippedUnsafe++;
                continue;
            }

            // Check current file/dir exists at newPath
            if (!File.Exists(newPath) && !Directory.Exists(newPath))
            {
                skippedMissingDest++;
                continue;
            }

            // Check no collision at oldPath
            if (File.Exists(oldPath) || Directory.Exists(oldPath))
            {
                skippedCollision++;
                continue;
            }

            if (dryRun)
            {
                dryRunPlanned++;
                _log?.Invoke($"DRYRUN rollback: {newPath} -> {oldPath}");
            }
            else
            {
                try
                {
                    // Ensure parent directory exists
                    var parentDir = Path.GetDirectoryName(oldPath);
                    if (parentDir is not null)
                        _fs.EnsureDirectory(parentDir);

                    if (_fs.MoveItemSafely(newPath, oldPath) is not null)
                    {
                        rolledBack++;
                        _log?.Invoke($"Rolled back: {newPath} -> {oldPath}");
                        AppendRollbackRow(rollbackAuditPath!, "ROLLBACK", newPath, oldPath, "OK");
                    }
                    else
                    {
                        failed++;
                        AppendRollbackRow(rollbackAuditPath!, "ROLLBACK", newPath, oldPath, "MOVE_FAILED");
                    }
                }
                catch (Exception ex)
                {
                    failed++;
                    _log?.Invoke($"Rollback failed: {newPath} -> {oldPath}: {ex.Message}");
                    AppendRollbackRow(rollbackAuditPath!, "ROLLBACK", newPath, oldPath, $"ERROR: {ex.Message}");
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
            RollbackAuditPath = rollbackAuditPath
        };
    }

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
        File.AppendAllText(path, line, Encoding.UTF8);
    }
}
