using Romulus.Contracts.Models;
using Romulus.Infrastructure.Analysis;
using Xunit;

namespace Romulus.Tests;

/// <summary>
/// Coverage tests for CollectionCompareService pure static normalization helpers:
/// NormalizeScope, NormalizeExtensions, NormalizeSourceId, NormalizeLabel,
/// ResolveEffectiveEnrichmentFingerprint.
/// </summary>
public sealed class CollectionCompareNormalizationCoverageTests
{
    // ── NormalizeSourceId ────────────────────────────────────────────

    [Fact]
    public void NormalizeSourceId_Null_ReturnsSource()
    {
        Assert.Equal("source", CollectionCompareService.NormalizeSourceId(null));
    }

    [Fact]
    public void NormalizeSourceId_Empty_ReturnsSource()
    {
        Assert.Equal("source", CollectionCompareService.NormalizeSourceId(""));
    }

    [Fact]
    public void NormalizeSourceId_Whitespace_ReturnsSource()
    {
        Assert.Equal("source", CollectionCompareService.NormalizeSourceId("   "));
    }

    [Fact]
    public void NormalizeSourceId_ValidId_ReturnsTrimmed()
    {
        Assert.Equal("my-collection", CollectionCompareService.NormalizeSourceId("  my-collection  "));
    }

    // ── NormalizeLabel ──────────────────────────────────────────────

    [Fact]
    public void NormalizeLabel_NullLabel_FallsBackToSourceId()
    {
        Assert.Equal("my-src", CollectionCompareService.NormalizeLabel(null, "my-src"));
    }

    [Fact]
    public void NormalizeLabel_EmptyLabel_FallsBackToSourceId()
    {
        Assert.Equal("my-src", CollectionCompareService.NormalizeLabel("", "my-src"));
    }

    [Fact]
    public void NormalizeLabel_WhitespaceLabel_FallsBackToSourceId()
    {
        Assert.Equal("my-src", CollectionCompareService.NormalizeLabel("   ", "my-src"));
    }

    [Fact]
    public void NormalizeLabel_NullLabelNullSourceId_ReturnsSource()
    {
        Assert.Equal("source", CollectionCompareService.NormalizeLabel(null, null));
    }

    [Fact]
    public void NormalizeLabel_ValidLabel_ReturnsTrimmed()
    {
        Assert.Equal("My Collection", CollectionCompareService.NormalizeLabel("  My Collection  ", "ignored"));
    }

    // ── NormalizeExtensions ─────────────────────────────────────────

    [Fact]
    public void NormalizeExtensions_EmptyList_ReturnsDefaults()
    {
        var result = CollectionCompareService.NormalizeExtensions(Array.Empty<string>());
        Assert.NotEmpty(result);
        Assert.All(result, ext => Assert.StartsWith(".", ext));
    }

    [Fact]
    public void NormalizeExtensions_WithDotPrefix_PreservesDot()
    {
        var result = CollectionCompareService.NormalizeExtensions(new[] { ".zip", ".7z" });
        Assert.Contains(".zip", result);
        Assert.Contains(".7z", result);
    }

    [Fact]
    public void NormalizeExtensions_WithoutDotPrefix_AddsDot()
    {
        var result = CollectionCompareService.NormalizeExtensions(new[] { "zip", "7z" });
        Assert.Contains(".zip", result);
        Assert.Contains(".7z", result);
    }

    [Fact]
    public void NormalizeExtensions_MixedCase_LowerCased()
    {
        var result = CollectionCompareService.NormalizeExtensions(new[] { ".ZIP", ".Bin" });
        Assert.Contains(".zip", result);
        Assert.Contains(".bin", result);
    }

    [Fact]
    public void NormalizeExtensions_Duplicates_Deduped()
    {
        var result = CollectionCompareService.NormalizeExtensions(new[] { ".zip", ".ZIP", "zip" });
        Assert.Single(result, ".zip");
    }

    [Fact]
    public void NormalizeExtensions_WhitespaceEntries_Filtered()
    {
        var result = CollectionCompareService.NormalizeExtensions(new[] { ".zip", "", "  ", ".7z" });
        Assert.Equal(2, result.Length);
        Assert.Contains(".zip", result);
        Assert.Contains(".7z", result);
    }

    [Fact]
    public void NormalizeExtensions_TrimmedBeforeNormalization()
    {
        var result = CollectionCompareService.NormalizeExtensions(new[] { "  .zip  ", "  7z  " });
        Assert.Contains(".zip", result);
        Assert.Contains(".7z", result);
    }

    // ── NormalizeScope ──────────────────────────────────────────────

    [Fact]
    public void NormalizeScope_EmptyRoots_ResultsInEmptyNormalizedRoots()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "test",
            Roots = Array.Empty<string>()
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Empty(result.Roots);
    }

    [Fact]
    public void NormalizeScope_NormalizesSourceId()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "  my-id  ",
            Roots = Array.Empty<string>()
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Equal("my-id", result.SourceId);
    }

    [Fact]
    public void NormalizeScope_NullSourceId_DefaultsToSource()
    {
        var scope = new CollectionSourceScope
        {
            Roots = Array.Empty<string>()
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.False(string.IsNullOrWhiteSpace(result.SourceId));
    }

    [Fact]
    public void NormalizeScope_NormalizesLabel()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "src",
            Label = "  My Label  ",
            Roots = Array.Empty<string>()
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Equal("My Label", result.Label);
    }

    [Fact]
    public void NormalizeScope_NullLabel_FallsBackToSourceId()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "my-src",
            Roots = Array.Empty<string>()
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Equal("my-src", result.Label);
    }

    [Fact]
    public void NormalizeScope_NormalizesExtensions()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "test",
            Roots = Array.Empty<string>(),
            Extensions = new[] { "ZIP", ".bin" }
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Contains(".zip", result.Extensions);
        Assert.Contains(".bin", result.Extensions);
    }

    [Fact]
    public void NormalizeScope_WhitespaceRoots_Filtered()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "test",
            Roots = new[] { "", "  ", @"C:\valid\root" }
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Single(result.Roots);
    }

    [Fact]
    public void NormalizeScope_DuplicateRoots_Deduped()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "NormScopeTest_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        try
        {
            var scope = new CollectionSourceScope
            {
                SourceId = "test",
                Roots = new[] { tmpDir, tmpDir, tmpDir }
            };

            var result = CollectionCompareService.NormalizeScope(scope);
            Assert.Single(result.Roots);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void NormalizeScope_TrimsEnrichmentFingerprint()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "test",
            Roots = Array.Empty<string>(),
            EnrichmentFingerprint = "  fp-123  "
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Equal("fp-123", result.EnrichmentFingerprint);
    }

    [Fact]
    public void NormalizeScope_NullEnrichmentFingerprint_Empty()
    {
        var scope = new CollectionSourceScope
        {
            SourceId = "test",
            Roots = Array.Empty<string>(),
            EnrichmentFingerprint = null!
        };

        var result = CollectionCompareService.NormalizeScope(scope);
        Assert.Equal(string.Empty, result.EnrichmentFingerprint);
    }

    [Fact]
    public void NormalizeScope_FingerprintMismatch_SetsMarker()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "NormScopeFP_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        try
        {
            var scope = new CollectionSourceScope
            {
                SourceId = "test",
                Roots = new[] { tmpDir },
                RootFingerprint = "deliberately-wrong-fingerprint"
            };

            var result = CollectionCompareService.NormalizeScope(scope);
            Assert.Equal("__root-fingerprint-mismatch__", result.RootFingerprint);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    [Fact]
    public void NormalizeScope_MatchingFingerprint_Preserved()
    {
        var tmpDir = Path.Combine(Path.GetTempPath(), "NormScopeOK_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tmpDir);
        try
        {
            // First, get the computed fingerprint
            var initScope = new CollectionSourceScope
            {
                SourceId = "test",
                Roots = new[] { tmpDir }
            };
            var normalized = CollectionCompareService.NormalizeScope(initScope);
            var computedFp = normalized.RootFingerprint;

            // Now set matching fingerprint
            var scope = new CollectionSourceScope
            {
                SourceId = "test",
                Roots = new[] { tmpDir },
                RootFingerprint = computedFp
            };
            var result = CollectionCompareService.NormalizeScope(scope);

            Assert.Equal(computedFp, result.RootFingerprint);
            Assert.NotEqual("__root-fingerprint-mismatch__", result.RootFingerprint);
        }
        finally
        {
            Directory.Delete(tmpDir, recursive: true);
        }
    }

    // ── ResolveEffectiveEnrichmentFingerprint ──────────────────────

    [Fact]
    public void ResolveEffectiveEnrichmentFingerprint_ScopeHasFingerprint_AllMatch_ReturnsFp()
    {
        var scope = new CollectionSourceScope { EnrichmentFingerprint = "fp-1" };
        var entries = new[]
        {
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-1" },
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-1" }
        };

        var result = CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries);
        Assert.Equal("fp-1", result);
    }

    [Fact]
    public void ResolveEffectiveEnrichmentFingerprint_ScopeHasFingerprint_NotAllMatch_ReturnsNull()
    {
        var scope = new CollectionSourceScope { EnrichmentFingerprint = "fp-1" };
        var entries = new[]
        {
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-1" },
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-2" }
        };

        var result = CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveEffectiveEnrichmentFingerprint_NoScopeFingerprint_UniformEntries_ReturnsFp()
    {
        var scope = new CollectionSourceScope();
        var entries = new[]
        {
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-A" },
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-A" }
        };

        var result = CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries);
        Assert.Equal("fp-A", result);
    }

    [Fact]
    public void ResolveEffectiveEnrichmentFingerprint_NoScopeFingerprint_MixedEntries_ReturnsNull()
    {
        var scope = new CollectionSourceScope();
        var entries = new[]
        {
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-A" },
            new CollectionIndexEntry { EnrichmentFingerprint = "fp-B" }
        };

        var result = CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveEffectiveEnrichmentFingerprint_NoScopeFingerprint_EmptyEntries_ReturnsNull()
    {
        var scope = new CollectionSourceScope();
        var entries = Array.Empty<CollectionIndexEntry>();

        // No entries → empty fingerprints → length != 1 → null
        var result = CollectionCompareService.ResolveEffectiveEnrichmentFingerprint(scope, entries);
        Assert.Null(result);
    }
}
