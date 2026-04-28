using System.Threading;

namespace Romulus.UI.Avalonia.Runtime;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly Mutex _mutex;
    private readonly bool _ownsMutex;
    private bool _disposed;

    private SingleInstanceGuard(Mutex mutex, bool ownsMutex)
    {
        _mutex = mutex;
        _ownsMutex = ownsMutex;
    }

    public bool IsAcquired => _ownsMutex;

    public static SingleInstanceGuard Acquire(string mutexName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(mutexName);

        var mutex = new Mutex(initiallyOwned: true, mutexName, out var createdNew);
        return new SingleInstanceGuard(mutex, createdNew);
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        if (_ownsMutex)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (ApplicationException)
            {
                // Ignore release failures when ownership is already lost.
            }
        }

        _mutex.Dispose();
        _disposed = true;
    }
}
