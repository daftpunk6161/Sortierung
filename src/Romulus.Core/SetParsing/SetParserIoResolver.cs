using Romulus.Contracts.Ports;

namespace Romulus.Core.SetParsing;

public static class SetParserIoResolver
{
    private static readonly object DefaultGate = new();
    private static Lazy<ISetParserIo> _defaultIo = new(CreateUnconfiguredIo, LazyThreadSafetyMode.ExecutionAndPublication);

    public static void ConfigureDefault(Func<ISetParserIo> factory)
    {
        ArgumentNullException.ThrowIfNull(factory);

        lock (DefaultGate)
            _defaultIo = new Lazy<ISetParserIo>(factory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    internal static ISetParserIo Resolve(ISetParserIo? io)
        => io ?? _defaultIo.Value;

    private static ISetParserIo CreateUnconfiguredIo()
        => new UnconfiguredSetParserIo();

    private sealed class UnconfiguredSetParserIo : ISetParserIo
    {
        private const string Message = "Set parser I/O is not configured. Inject ISetParserIo from Infrastructure before invoking parser logic.";

        public bool Exists(string path)
            => throw new InvalidOperationException(Message);

        public IEnumerable<string> ReadLines(string path)
            => throw new InvalidOperationException(Message);
    }
}
