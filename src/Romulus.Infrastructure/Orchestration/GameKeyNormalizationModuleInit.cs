using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Romulus.Infrastructure.Orchestration;

/// <summary>
/// Module initializer that registers the rules.json-based pattern factory with
/// <see cref="Romulus.Core.GameKeys.GameKeyNormalizer"/> so that the convenience
/// Normalize(string) overload resolves patterns lazily from rules.json.
/// </summary>
[SuppressMessage("Usage", "CA2255", Justification = "Module initializer registers shared game key normalization profile exactly once at process start.")]
internal static class GameKeyNormalizationModuleInit
{
    [ModuleInitializer]
    internal static void Init() => GameKeyNormalizationProfile.EnsureRegistered();
}
