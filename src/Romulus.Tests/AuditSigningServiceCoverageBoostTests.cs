using Romulus.Contracts;
using Romulus.Contracts.Models;
using Romulus.Contracts.Ports;
using Romulus.Infrastructure.Audit;
using Romulus.Infrastructure.FileSystem;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage boost for AuditSigningService: NormalizeRollbackAction mapping, BuildPendingOperationKey,
/// Rollback edge cases (JunkRemove action, collision detection, missing dest), sidecar error paths.
/// Targets ~148 uncovered lines.
/// </summary>
public sealed class AuditSigningServiceCoverageBoostTests : IDisposable
{
    private readonly string _root;
    private readonly AuditSigningService _sut;

    public AuditSigningServiceCoverageBoostTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "AuditSign_Cov_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_root);
        _sut = new AuditSigningService(new FileSystemAdapter());
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, true); } catch { }
    }

    // ══════ NormalizeRollbackAction ═══════════════════════════════

    [Theory]
    [InlineData("MOVE_PENDING", "MOVE")]
    [InlineData("COPY_PENDING", "COPY")]
    [InlineData("CONVERT", "CONVERT")]
    [InlineData("CONVERT_SOURCE", "CONVERT_SOURCE")]
    [InlineData("CONSOLE_SORT", "CONSOLE_SORT")]
    [InlineData("DAT_RENAME", "DAT_RENAME")]
    [InlineData("JUNK_REMOVE", "JUNK_REMOVE")]
    public void NormalizeRollbackAction_KnownActions_ReturnsExpected(string input, string expected)
    {
        var result = AuditSigningService.NormalizeRollbackAction(input);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData("UNKNOWN_ACTION")]
    [InlineData("")]
    [InlineData("DELETE")]
    public void NormalizeRollbackAction_UnknownActions_ReturnsNull(string input)
    {
        var result = AuditSigningService.NormalizeRollbackAction(input);
        Assert.Null(result);
    }

    [Fact]
    public void NormalizeRollbackAction_CaseInsensitive()
    {
        Assert.Equal("MOVE", AuditSigningService.NormalizeRollbackAction("move_pending"));
        Assert.Equal("CONVERT", AuditSigningService.NormalizeRollbackAction("convert"));
        Assert.Equal("JUNK_REMOVE", AuditSigningService.NormalizeRollbackAction("junk_remove"));
    }

    // ══════ BuildPendingOperationKey ═══════════════════════════════

    [Fact]
    public void BuildPendingOperationKey_DeterministicOutput()
    {
        var key1 = AuditSigningService.BuildPendingOperationKey("Move", "/old/path", "/new/path");
        var key2 = AuditSigningService.BuildPendingOperationKey("Move", "/old/path", "/new/path");
        Assert.Equal(key1, key2);
    }

    [Fact]
    public void BuildPendingOperationKey_DifferentActionsProduceDifferentKeys()
    {
        var keyMove = AuditSigningService.BuildPendingOperationKey("Move", "/old", "/new");
        var keyCopy = AuditSigningService.BuildPendingOperationKey("Copy", "/old", "/new");
        Assert.NotEqual(keyMove, keyCopy);
    }

    [Fact]
    public void BuildPendingOperationKey_DifferentPathsProduceDifferentKeys()
    {
        var key1 = AuditSigningService.BuildPendingOperationKey("Move", "/a/file.rom", "/b/file.rom");
        var key2 = AuditSigningService.BuildPendingOperationKey("Move", "/a/other.rom", "/b/other.rom");
        Assert.NotEqual(key1, key2);
    }

    // ══════ Rollback edge cases ═══════════════════════════════════

    [Fact]
    public void Rollback_JunkRemoveAction_DryRun_PlansRestore()
    {
        var csvPath = CreateAuditCsv("junk-audit.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_root},{_root}\\game.rom,{_root}\\.trash\\game.rom,JUNK_REMOVE,JUNK,,junk-detected,2025-01-01T00:00:00Z");

        // Create the file at NewPath (the trash location) to make it eligible for restore
        CreateFile(".trash\\game.rom", "content");

        // Create sidecar for integrity check
        _sut.WriteMetadataSidecar(csvPath, 1);

        var result = _sut.Rollback(csvPath,
            [_root], [_root],
            dryRun: true);

        Assert.True(result.DryRun);
        Assert.True(result.TotalRows >= 1);
        Assert.True(result.EligibleRows >= 1);
        Assert.True(result.DryRunPlanned >= 1);
    }

    [Fact]
    public void Rollback_CollisionDetection_SkipsWhenTargetExists()
    {
        // Move from old to new, try to rollback, but old already exists (collision)
        CreateFile("og-game.rom", "original-content");
        var newPath = CreateFile("moved\\game.rom", "moved-content");

        var csvPath = CreateAuditCsv("collision-audit.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_root},{_root}\\og-game.rom,{newPath},MOVE_PENDING,GAME,,dedupe,2025-01-01T00:00:00Z");

        // Create sidecar for integrity check
        _sut.WriteMetadataSidecar(csvPath, 1);

        var result = _sut.Rollback(csvPath,
            [_root], [_root],
            dryRun: true);

        // Should detect collision and skip
        Assert.True(result.SkippedCollision >= 1);
    }

    [Fact]
    public void Rollback_MissingDestFile_SkippedAsMissingDest()
    {
        var csvPath = CreateAuditCsv("missing-audit.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_root},{_root}\\game.rom,{_root}\\moved\\game.rom,MOVE,GAME,,dedupe,2025-01-01T00:00:00Z");

        // Create sidecar for integrity check
        _sut.WriteMetadataSidecar(csvPath, 1);

        // NewPath does NOT exist → missing dest → failed
        var result = _sut.Rollback(csvPath,
            [_root], [_root],
            dryRun: false);

        Assert.True(result.Failed >= 1);
        Assert.True(result.SkippedMissingDest >= 1);
    }

    [Fact]
    public void Rollback_HeaderOnlyCsv_ReturnsEmptyResult()
    {
        var csvPath = CreateAuditCsv("header-only.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");

        // Create sidecar for integrity check
        _sut.WriteMetadataSidecar(csvPath, 0);

        var result = _sut.Rollback(csvPath,
            [_root], [_root],
            dryRun: true);

        Assert.Equal(0, result.TotalRows);
        Assert.Equal(0, result.EligibleRows);
    }

    [Fact]
    public void Rollback_NonExistentCsv_ReturnsDefault()
    {
        var result = _sut.Rollback(
            Path.Combine(_root, "nonexistent.csv"),
            [_root], [_root], dryRun: true);

        Assert.Equal(0, result.TotalRows);
    }

    [Fact]
    public void Rollback_MultipleMixedActions_CountsCorrectly()
    {
        CreateFile("trash\\a.rom", "a");
        CreateFile("trash\\b.rom", "b");

        var csvPath = CreateAuditCsv("mixed-actions.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_root},{_root}\\a.rom,{_root}\\trash\\a.rom,MOVE_PENDING,GAME,,dedupe,2025-01-01T00:00:00Z",
            $"{_root},{_root}\\b.rom,{_root}\\trash\\b.rom,JUNK_REMOVE,JUNK,,junk,2025-01-01T00:00:00Z",
            $"{_root},{_root}\\c.rom,{_root}\\trash\\c.rom,NOT_A_REAL_ACTION,GAME,,unknown,2025-01-01T00:00:00Z");

        // Create sidecar for integrity check
        _sut.WriteMetadataSidecar(csvPath, 3);

        var result = _sut.Rollback(csvPath,
            [_root], [_root],
            dryRun: true);

        Assert.True(result.TotalRows >= 3);
        Assert.True(result.EligibleRows >= 2); // MOVE_PENDING + JUNK_REMOVE
    }

    // ══════ WriteMetadataSidecar edge cases ════════════════════════

    [Fact]
    public void WriteMetadataSidecar_NonExistentCsv_ReturnsNull()
    {
        var result = _sut.WriteMetadataSidecar(
            Path.Combine(_root, "nonexistent.csv"), 0);
        Assert.Null(result);
    }

    [Fact]
    public void WriteMetadataSidecar_ValidCsv_CreatesSidecar()
    {
        var csvPath = CreateAuditCsv("valid.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_root},{_root}\\a.rom,{_root}\\b.rom,MOVE_PENDING,GAME,,test,2025-01-01T00:00:00Z");

        var sidecarPath = _sut.WriteMetadataSidecar(csvPath, 1);

        Assert.NotNull(sidecarPath);
        Assert.True(File.Exists(sidecarPath));
    }

    [Fact]
    public void WriteAndVerify_RoundTrip_Succeeds()
    {
        var csvPath = CreateAuditCsv("roundtrip.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_root},{_root}\\a.rom,{_root}\\b.rom,MOVE_PENDING,GAME,,test,2025-01-01T00:00:00Z");

        _sut.WriteMetadataSidecar(csvPath, 1);
        var verified = _sut.VerifyMetadataSidecar(csvPath);
        Assert.True(verified);
    }

    [Fact]
    public void VerifyMetadataSidecar_TamperedCsv_ThrowsInvalidData()
    {
        var csvPath = CreateAuditCsv("tamper.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp",
            $"{_root},{_root}\\a.rom,{_root}\\b.rom,MOVE_PENDING,GAME,,test,2025-01-01T00:00:00Z");

        _sut.WriteMetadataSidecar(csvPath, 1);

        // Tamper the CSV
        File.AppendAllText(csvPath, $"\n{_root},{_root}\\c.rom,{_root}\\d.rom,MOVE_PENDING,GAME,,injected,2025-01-01T00:00:00Z");

        Assert.Throws<InvalidDataException>(() => _sut.VerifyMetadataSidecar(csvPath));
    }

    [Fact]
    public void VerifyMetadataSidecar_MissingSidecar_ThrowsFileNotFound()
    {
        var csvPath = CreateAuditCsv("no-sidecar.csv",
            "RootPath,OldPath,NewPath,Action,Category,Hash,Reason,Timestamp");

        Assert.Throws<FileNotFoundException>(() => _sut.VerifyMetadataSidecar(csvPath));
    }

    // ══════ ComputeFileSha256 ═════════════════════════════════════

    [Fact]
    public void ComputeFileSha256_Deterministic()
    {
        var filePath = CreateFile("hash-test.bin", "deterministic-content");
        var hash1 = AuditSigningService.ComputeFileSha256(filePath);
        var hash2 = AuditSigningService.ComputeFileSha256(filePath);
        Assert.Equal(hash1, hash2);
        Assert.NotEmpty(hash1);
    }

    // ══════ BuildSignaturePayload ═════════════════════════════════

    [Fact]
    public void BuildSignaturePayload_CanonicalFormat()
    {
        var payload = AuditSigningService.BuildSignaturePayload("audit.csv", "abc123", 5, "2025-06-15T12:00:00Z");
        Assert.Contains("audit.csv", payload);
        Assert.Contains("abc123", payload);
        Assert.Contains("5", payload);
        Assert.Contains("2025-06-15T12:00:00Z", payload);
    }

    // ══════ SanitizeCsvField ══════════════════════════════════════

    [Theory]
    [InlineData("=cmd|calc", "\"=cmd|calc\"")]
    [InlineData("+SUM()", "\"+SUM()\"")]
    [InlineData("-formula", "\"-formula\"")]
    [InlineData("@SUM", "\"@SUM\"")]
    public void SanitizeCsvField_PreventsFormulaInjection(string input, string expected)
    {
        Assert.Equal(expected, AuditSigningService.SanitizeCsvField(input));
    }

    [Fact]
    public void SanitizeCsvField_QuotesComma()
    {
        var result = AuditSigningService.SanitizeCsvField("hello,world");
        Assert.Contains("\"", result);
    }

    [Fact]
    public void SanitizeCsvField_SafeFieldUnchanged()
    {
        Assert.Equal("normal-value", AuditSigningService.SanitizeCsvField("normal-value"));
    }

    // ══════ Helpers ════════════════════════════════════════════════

    private string CreateFile(string relativePath, string content = "data")
    {
        var path = Path.GetFullPath(Path.Combine(_root, relativePath));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
        return path;
    }

    private string CreateAuditCsv(string name, params string[] lines)
    {
        var csvPath = Path.Combine(_root, name);
        File.WriteAllLines(csvPath, lines);
        return csvPath;
    }
}
