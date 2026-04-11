using Romulus.Contracts.Ports;

namespace Romulus.Core.SetParsing;

internal static class SetParserIoResolver
{
    private static readonly Lazy<ISetParserIo> DefaultIo = new(CreateDefaultIo, LazyThreadSafetyMode.ExecutionAndPublication);

    internal static ISetParserIo Resolve(ISetParserIo? io)
        => io ?? DefaultIo.Value;

    private static ISetParserIo CreateDefaultIo()
    {
        try
        {
            var adapterType = Type.GetType("Romulus.Infrastructure.IO.SetParserIo, Romulus.Infrastructure", throwOnError: false);
            if (adapterType is not null && Activator.CreateInstance(adapterType) is ISetParserIo adapter)
                return adapter;
        }
        catch
        {
            // Fall back to explicit injection path.
        }

        return new UnconfiguredSetParserIo();
    }

    private sealed class UnconfiguredSetParserIo : ISetParserIo
    {
        private const string Message = "Set parser I/O is not configured. Inject ISetParserIo from Infrastructure before invoking parser logic.";

        public bool Exists(string path)
            => throw new InvalidOperationException(Message);

        public IEnumerable<string> ReadLines(string path)
            => throw new InvalidOperationException(Message);
    }
}
