using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Romulus.Core.Classification;
using Romulus.Core.GameKeys;
using Romulus.Infrastructure.IO;
using Romulus.Infrastructure.Orchestration;

namespace Romulus.Tests.Benchmark;

/// <summary>
/// Triggers Infrastructure's CoreIoResolverModuleInit by touching a public type
/// from Romulus.Infrastructure.IO and Romulus.Infrastructure.SetParsing,
/// then registers normalization profiles. Without this, Benchmark tests fail
/// with "Classification I/O is not configured" because no Infrastructure type
/// is otherwise referenced from this assembly's hot path.
/// </summary>
[SuppressMessage("Usage", "CA2255", Justification = "Registers default I/O adapters once at assembly load for benchmark tests.")]
internal static class ModuleInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Force Infrastructure assembly load. CoreIoResolverModuleInit registers
        // the default ClassificationIo when the Infrastructure assembly is touched.
        _ = typeof(ClassificationIo);
        _ = typeof(SetParserIo);

        var patterns = GameKeyNormalizationProfile.TagPatterns;
        var aliases = GameKeyNormalizationProfile.AlwaysAliasMap;
        if (patterns is { Count: > 0 })
            GameKeyNormalizer.RegisterDefaultPatterns(patterns, aliases);

        RegionDetectionProfile.EnsureRegistered();
        FormatScoringProfile.EnsureRegistered();
        VersionScoringProfile.EnsureRegistered();
    }
}
