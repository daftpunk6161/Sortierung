using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Romulus.Infrastructure.Orchestration;

[SuppressMessage("Usage", "CA2255", Justification = "Module initializer registers shared version scoring profile once at process start.")]
internal static class VersionScoringModuleInit
{
    [ModuleInitializer]
    internal static void Init() => VersionScoringProfile.EnsureRegistered();
}
