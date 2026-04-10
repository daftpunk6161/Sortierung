using Romulus.Contracts;
using Romulus.Infrastructure.Audit;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Tests for AuditSigningService static/pure methods: BuildSignaturePayload,
/// NormalizeRollbackAction (via reflection), BuildPendingOperationKey, SanitizeCsvField,
/// and ComputeHmacSha256 determinism.
/// </summary>
public sealed class AuditSigningServiceCoverageTests
{
    #region BuildSignaturePayload

    [Fact]
    public void BuildSignaturePayload_ReturnsCanonicalFormat()
    {
        var result = AuditSigningService.BuildSignaturePayload("audit.csv", "abc123", 42, "2026-01-01T00:00:00Z");
        Assert.Equal("v1|audit.csv|abc123|42|2026-01-01T00:00:00Z", result);
    }

    [Fact]
    public void BuildSignaturePayload_EmptyValues_StillFormats()
    {
        var result = AuditSigningService.BuildSignaturePayload("", "", 0, "");
        Assert.Equal("v1|||0|", result);
    }

    #endregion

    #region SanitizeCsvField

    [Fact]
    public void SanitizeCsvField_SafeString_Unchanged()
    {
        Assert.Equal("hello", AuditSigningService.SanitizeCsvField("hello"));
    }

    [Fact]
    public void SanitizeCsvField_FormulaPrefix_Escaped()
    {
        Assert.Equal("\"=SUM(A1)\"", AuditSigningService.SanitizeCsvField("=SUM(A1)"));
    }

    [Fact]
    public void SanitizeCsvField_Plus_Escaped()
    {
        Assert.Equal("\"+cmd\"", AuditSigningService.SanitizeCsvField("+cmd"));
    }

    [Fact]
    public void SanitizeCsvField_At_Escaped()
    {
        Assert.Equal("\"@import\"", AuditSigningService.SanitizeCsvField("@import"));
    }

    #endregion

    #region ComputeHmacSha256 determinism

    [Fact]
    public void ComputeHmacSha256_Deterministic_SameKey()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hmac_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var keyPath = Path.Combine(tempDir, "hmac.key");

        try
        {
            var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
            var svc = new AuditSigningService(fs, keyFilePath: keyPath);

            var sig1 = svc.ComputeHmacSha256("test payload");
            var sig2 = svc.ComputeHmacSha256("test payload");

            Assert.Equal(sig1, sig2);
            Assert.True(sig1.Length > 0);
            Assert.Matches("^[0-9a-f]+$", sig1);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void ComputeHmacSha256_DifferentPayloads_DifferentSignatures()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"hmac_test2_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var keyPath = Path.Combine(tempDir, "hmac.key");

        try
        {
            var fs = new Romulus.Infrastructure.FileSystem.FileSystemAdapter();
            var svc = new AuditSigningService(fs, keyFilePath: keyPath);

            var sig1 = svc.ComputeHmacSha256("payload A");
            var sig2 = svc.ComputeHmacSha256("payload B");

            Assert.NotEqual(sig1, sig2);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    #endregion
}
