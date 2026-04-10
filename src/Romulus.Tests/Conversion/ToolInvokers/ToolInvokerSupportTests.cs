using System.Security.Cryptography;
using Romulus.Contracts.Models;
using Romulus.Infrastructure.Conversion.ToolInvokers;
using Xunit;

namespace Romulus.Tests.Conversion.ToolInvokers;

public sealed class ToolInvokerSupportTests : IDisposable
{
    private readonly string _root;

    public ToolInvokerSupportTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "Romulus.ToolInvokerSupportTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void ReadSafeCommandToken_EmptyOrWhitespace_ReturnsNull()
    {
        Assert.Null(ToolInvokerSupport.ReadSafeCommandToken(string.Empty));
        Assert.Null(ToolInvokerSupport.ReadSafeCommandToken("   "));
    }

    [Fact]
    public void ReadSafeCommandToken_PathSeparator_ReturnsNull()
    {
        Assert.Null(ToolInvokerSupport.ReadSafeCommandToken("..\\createcd"));
        Assert.Null(ToolInvokerSupport.ReadSafeCommandToken("/bin/createcd"));
    }

    [Fact]
    public void ReadSafeCommandToken_ValidWithArgs_ReturnsFirstToken()
    {
        var token = ToolInvokerSupport.ReadSafeCommandToken("createdvd --fast --level 9");

        Assert.Equal("createdvd", token);
    }

    [Fact]
    public void FixedTimeHashEquals_IsCaseInsensitive()
    {
        Assert.True(ToolInvokerSupport.FixedTimeHashEquals("ABCDEF", "abcdef"));
        Assert.False(ToolInvokerSupport.FixedTimeHashEquals("abc", "abd"));
    }

    [Fact]
    public void ValidateToolConstraints_FileMissing_ReturnsNotFoundError()
    {
        var requirement = new ToolRequirement { ToolName = "chdman" };

        var result = ToolInvokerSupport.ValidateToolConstraints(Path.Combine(_root, "missing.exe"), requirement);

        Assert.Equal("tool-not-found-on-disk", result);
    }

    [Fact]
    public void ValidateToolConstraints_HashMismatch_ReturnsMismatch()
    {
        var toolPath = CreateFile("tool.exe", [1, 2, 3, 4]);
        var requirement = new ToolRequirement
        {
            ToolName = "chdman",
            ExpectedHash = new string('0', 64)
        };

        var result = ToolInvokerSupport.ValidateToolConstraints(toolPath, requirement);

        Assert.Equal("tool-hash-mismatch", result);
    }

    [Fact]
    public void ValidateToolConstraints_HashMatch_ReturnsNull()
    {
        var toolPath = GetExistingExecutablePath();
        var requirement = new ToolRequirement
        {
            ToolName = "chdman",
            ExpectedHash = ComputeSha256(toolPath)
        };

        var result = ToolInvokerSupport.ValidateToolConstraints(toolPath, requirement);

        Assert.Null(result);
    }

    [Fact]
    public void ValidateToolConstraints_MinVersionInvalid_ReturnsInvalid()
    {
        var toolPath = GetExistingExecutablePath();
        var requirement = new ToolRequirement
        {
            ToolName = "chdman",
            MinVersion = "not-a-version"
        };

        var result = ToolInvokerSupport.ValidateToolConstraints(toolPath, requirement);

        Assert.Equal("tool-minversion-invalid", result);
    }

    [Fact]
    public void ValidateToolConstraints_MinVersionTooHigh_ReturnsTooOld()
    {
        var toolPath = GetExistingExecutablePath();
        var requirement = new ToolRequirement
        {
            ToolName = "chdman",
            MinVersion = "99.0.0.0"
        };

        var result = ToolInvokerSupport.ValidateToolConstraints(toolPath, requirement);

        Assert.Equal("tool-version-too-old", result);
    }

    [Fact]
    public void IsLikelyCdImage_OnlyKnownExtensionsAreAccepted()
    {
        var path = CreateSizedFile("disc.chd", 100);

        Assert.False(ToolInvokerSupport.IsLikelyCdImage(path));
    }

    [Fact]
    public void IsLikelyCdImage_ZeroLengthIso_ReturnsFalse()
    {
        var path = CreateSizedFile("disc.iso", 0);

        Assert.False(ToolInvokerSupport.IsLikelyCdImage(path));
    }

    [Fact]
    public void IsLikelyCdImage_BelowThreshold_ReturnsTrue()
    {
        var path = CreateSizedFile("disc.iso", ToolInvokerSupport.CdImageThresholdBytes - 1);

        Assert.True(ToolInvokerSupport.IsLikelyCdImage(path));
    }

    [Fact]
    public void IsLikelyCdImage_AtOrAboveThreshold_ReturnsFalse()
    {
        var atThreshold = CreateSizedFile("disc-at.iso", ToolInvokerSupport.CdImageThresholdBytes);
        var aboveThreshold = CreateSizedFile("disc-over.iso", ToolInvokerSupport.CdImageThresholdBytes + 1);

        Assert.False(ToolInvokerSupport.IsLikelyCdImage(atThreshold));
        Assert.False(ToolInvokerSupport.IsLikelyCdImage(aboveThreshold));
    }

    [Fact]
    public void ResolveEffectiveChdmanCommand_SmallIso_DowngradesToCreatecd()
    {
        var path = CreateSizedFile("small.iso", ToolInvokerSupport.CdImageThresholdBytes - 1);

        var command = ToolInvokerSupport.ResolveEffectiveChdmanCommand("createdvd", path);

        Assert.Equal("createcd", command);
    }

    [Fact]
    public void ResolveEffectiveChdmanCommand_NonCreatedvdOrLargeImage_Unchanged()
    {
        var largePath = CreateSizedFile("large.iso", ToolInvokerSupport.CdImageThresholdBytes + 1);

        Assert.Equal("createdvd", ToolInvokerSupport.ResolveEffectiveChdmanCommand("createdvd", largePath));
        Assert.Equal("createcd", ToolInvokerSupport.ResolveEffectiveChdmanCommand("createcd", largePath));
    }

    [Theory]
    [InlineData("chdman", 30)]
    [InlineData("7z", 10)]
    [InlineData("dolphintool", 20)]
    [InlineData("psxtract", 20)]
    [InlineData("nkit", 30)]
    [InlineData("unecm", 10)]
    [InlineData("unknown-tool", 15)]
    public void ResolveToolTimeout_UsesExpectedDefaults(string toolName, int expectedMinutes)
    {
        var timeout = ToolInvokerSupport.ResolveToolTimeout(toolName);

        Assert.Equal(TimeSpan.FromMinutes(expectedMinutes), timeout);
    }

    [Fact]
    public void SourceNotFound_ResultFieldsAreStable()
    {
        var result = ToolInvokerSupport.SourceNotFound();

        Assert.False(result.Success);
        Assert.Equal("source-not-found", result.StdErr);
        Assert.Equal(VerificationStatus.NotAttempted, result.Verification);
    }

    [Fact]
    public void ToolNotFound_ResultIncludesToolName()
    {
        var result = ToolInvokerSupport.ToolNotFound("nkit");

        Assert.False(result.Success);
        Assert.Equal("tool-not-found:nkit", result.StdErr);
        Assert.Equal(VerificationStatus.VerifyNotAvailable, result.Verification);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, true);
        }
        catch
        {
        }
    }

    private string CreateFile(string name, byte[] bytes)
    {
        var path = Path.Combine(_root, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private string CreateSizedFile(string name, long length)
    {
        var path = Path.Combine(_root, name);
        using var stream = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        stream.SetLength(length);
        return path;
    }

    private static string GetExistingExecutablePath()
    {
        var winDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var candidates = new[]
        {
            Path.Combine(winDir, "System32", "cmd.exe"),
            Path.Combine(winDir, "System32", "where.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
                return candidate;
        }

        throw new InvalidOperationException("No suitable executable found for version/hash constraints.");
    }

    private static string ComputeSha256(string path)
    {
        using var sha256 = SHA256.Create();
        using var stream = File.OpenRead(path);
        var hash = sha256.ComputeHash(stream);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }
}
