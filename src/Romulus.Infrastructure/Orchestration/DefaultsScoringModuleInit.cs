using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Romulus.Infrastructure.Orchestration;

[SuppressMessage("Usage", "CA2255", Justification = "Module initializer registers shared defaults-scoring profile once at process start.")]
internal static class DefaultsScoringModuleInit
{
    [ModuleInitializer]
    internal static void Init() => DefaultsScoringProfile.EnsureRegistered();
}
