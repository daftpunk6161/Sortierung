using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Romulus.Infrastructure.Orchestration;

[SuppressMessage("Usage", "CA2255", Justification = "Module initializer registers shared format scoring profile once at process start.")]
internal static class FormatScoringModuleInit
{
    [ModuleInitializer]
    internal static void Init() => FormatScoringProfile.EnsureRegistered();
}
