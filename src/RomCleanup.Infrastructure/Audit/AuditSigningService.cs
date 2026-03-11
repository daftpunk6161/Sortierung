using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RomCleanup.Contracts.Models;
using RomCleanup.Contracts.Ports;

namespace RomCleanup.Infrastructure.Audit;

/// <summary>
/// HMAC-SHA256 audit signing, metadata sidecars, and rollback.
/// Port of RunHelpers.Audit.ps1.
/// </summary>
public sealed class AuditSigningService
{
    private static readonly byte[] SessionKey = GenerateSessionKey();
    private readonly IFileSystem _fs;
    private readonly Action<string>? _log;

    public AuditSigningService(IFileSystem fs, Action<string>? log = null)
    {
        _fs = fs;
        _log = log;
    }

    /// <summary>
    /// Generate a 32-byte session key (in-memory only — BUG-015 fix).
    /// </summary>
    private static byte[] GenerateSessionKey()
    {
        var key = new byte[32];
        RandomNumberGenerator.Fill(key);
        return key;
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
    /// Compute HMAC-SHA256 of a text string using the session key.
    /// </summary>
    public static string ComputeHmacSha256(string text)
    {
        var textBytes = Encoding.UTF8.GetBytes(text);
        var hash = HMACSHA256.HashData(SessionKey, textBytes);
        return Convert.ToHexStringLower(hash);
    }

    /// <summary>
    /// Write an audit metadata sidecar (.meta.json) with HMAC signature.
    /// </summary>
    public string? WriteMetadataSidecar(string auditCsvPath, int rowCount)
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

            var metadata = new AuditMetadata
            {
                Version = "v1",
                AuditFileName = auditFileName,
                CsvSha256 = csvSha256,
                RowCount = rowCount,
                CreatedUtc = createdUtc,
                HmacSha256 = hmac
            };

            var metaPath = auditCsvPath + ".meta.json";
            var json = JsonSerializer.Serialize(metadata, new JsonSerializerOptions { WriteIndented = true });
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

        // Verify HMAC
        var payload = BuildSignaturePayload(metadata.AuditFileName, metadata.CsvSha256, metadata.RowCount, metadata.CreatedUtc);
        var expectedHmac = ComputeHmacSha256(payload);
        if (!string.Equals(expectedHmac, metadata.HmacSha256, StringComparison.OrdinalIgnoreCase))
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

        // Process rows in reverse order (undo last moves first)
        for (int i = lines.Length - 1; i >= 1; i--)
        {
            var line = lines[i];
            if (string.IsNullOrWhiteSpace(line)) continue;

            totalRows++;
            var fields = ParseCsvLine(line);
            if (fields.Length < 4) continue;

            // Fields: RootPath, OldPath, NewPath, Action, Category, Hash, Reason, Timestamp
            var oldPath = fields.Length > 1 ? fields[1] : "";
            var newPath = fields.Length > 2 ? fields[2] : "";
            var action = fields.Length > 3 ? fields[3] : "";

            // Only rollback MOVE actions
            if (!string.Equals(action, "MOVE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(action, "MOVED", StringComparison.OrdinalIgnoreCase))
                continue;

            eligible++;

            // Safety: check the current location (newPath) is within allowed roots
            var inAllowedCurrent = allowedCurrentRoots.Any(r =>
                newPath.StartsWith(r, StringComparison.OrdinalIgnoreCase));
            var inAllowedRestore = allowedRestoreRoots.Any(r =>
                oldPath.StartsWith(r, StringComparison.OrdinalIgnoreCase));

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

                    if (_fs.MoveItemSafely(newPath, oldPath))
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
    public static string SanitizeCsvField(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;

        // Prefix with single quote if starts with dangerous characters
        if (value.Length > 0 && value[0] is '=' or '+' or '-' or '@')
            value = "'" + value;

        // Quote if contains comma, newline, or double-quote
        if (value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r'))
            return "\"" + value.Replace("\"", "\"\"") + "\"";

        return value;
    }

    private static void AppendRollbackRow(string path, string action, string from, string to, string status)
    {
        var timestamp = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
        var line = $"{SanitizeCsvField(timestamp)},{SanitizeCsvField(action)},{SanitizeCsvField(from)},{SanitizeCsvField(to)},{SanitizeCsvField(status)}\n";
        File.AppendAllText(path, line, Encoding.UTF8);
    }

    private static string[] ParseCsvLine(string line)
    {
        var fields = new List<string>();
        bool inQuotes = false;
        var current = new StringBuilder();

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (inQuotes)
            {
                if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else if (c == '"')
                {
                    inQuotes = false;
                }
                else
                {
                    current.Append(c);
                }
            }
            else
            {
                if (c == '"')
                {
                    inQuotes = true;
                }
                else if (c == ',')
                {
                    fields.Add(current.ToString());
                    current.Clear();
                }
                else
                {
                    current.Append(c);
                }
            }
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }
}
