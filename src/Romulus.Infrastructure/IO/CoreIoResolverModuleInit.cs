using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Romulus.Core.Classification;
using Romulus.Core.SetParsing;

namespace Romulus.Infrastructure.IO;

[SuppressMessage("Usage", "CA2255", Justification = "Registers core I/O default adapters once at assembly load.")]
internal static class CoreIoResolverModuleInit
{
    [ModuleInitializer]
    internal static void Initialize()
    {
        ClassificationIoResolver.ConfigureDefault(static () => new ClassificationIo());
        SetParserIoResolver.ConfigureDefault(static () => new SetParserIo());
    }
}

