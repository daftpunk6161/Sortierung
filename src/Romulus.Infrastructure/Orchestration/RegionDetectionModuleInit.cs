using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;

namespace Romulus.Infrastructure.Orchestration;

[SuppressMessage("Usage", "CA2255", Justification = "Module initializer registers shared region-detection profile once at process start.")]
internal static class RegionDetectionModuleInit
{
    [ModuleInitializer]
    internal static void Init() => RegionDetectionProfile.EnsureRegistered();
}
