namespace Romulus.Infrastructure.Dat;

/// <summary>
/// Background prewarm: walks the DAT root, parses every DAT through the
/// <see cref="IDatEntryCache"/> so subsequent run starts hit the cache instead
/// of reparsing 50-100 MB XML files. Idempotent and safe to call repeatedly;
/// cached entries return in microseconds, only stale or new files are reparsed.
/// </summary>
public sealed class DatPrewarmService
{
    private static readonly string[] DatExtensions = { ".dat", ".xml" };

    private readonly IDatEntryCache _cache;
    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private CancellationTokenSource? _activeCts;

    public DatPrewarmService(IDatEntryCache cache, Action<string>? log = null)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _log = log;
    }

    /// <summary>
    /// Kick off a background prewarm for the given DAT root. Cancels any prior
    /// in-flight prewarm so a DatRoot change does not leave two scans racing.
    /// Returns immediately; the actual work runs on the thread pool.
    /// </summary>
    public Task StartAsync(string datRoot, string hashType = "SHA1", CancellationToken externalToken = default)
    {
        if (string.IsNullOrWhiteSpace(datRoot) || !Directory.Exists(datRoot))
            return Task.CompletedTask;

        CancellationTokenSource cts;
        lock (_gate)
        {
            try { _activeCts?.Cancel(); }
            catch (ObjectDisposedException) { /* prior CTS already disposed */ }
            _activeCts?.Dispose();
            cts = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            _activeCts = cts;
        }

        return Task.Run(() => RunPrewarm(datRoot, hashType, cts.Token), cts.Token);
    }

    private void RunPrewarm(string datRoot, string hashType, CancellationToken token)
    {
        IEnumerable<string> files;
        try
        {
            files = Directory.EnumerateFiles(datRoot, "*.*", SearchOption.AllDirectories)
                .Where(p => DatExtensions.Contains(Path.GetExtension(p), StringComparer.OrdinalIgnoreCase));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log?.Invoke($"[DAT-Prewarm] Verzeichnis nicht lesbar: {ex.Message}");
            return;
        }

        var startedUtc = DateTime.UtcNow;
        int total = 0, hits = 0, parsed = 0, skipped = 0;
        var lastReportUtc = startedUtc;

        foreach (var datPath in files)
        {
            if (token.IsCancellationRequested)
                break;

            total++;

            // Only consume parser cycles when the cache actually misses. A miss
            // returns an empty payload here because we never invoke the parser
            // directly; the run-time DatRepositoryAdapter (with the same cache)
            // will populate it lazily on first real access. To prewarm we need
            // the adapter to do the work, so call it via a single-entry dummy
            // map - keep the lookup cheap by using a fresh adapter per file so
            // the 100MB size guard still applies.
            if (_cache.TryGet(datPath, hashType, out _))
            {
                hits++;
            }
            else
            {
                try
                {
                    // Per-file parse via fresh adapter so cache.Set is invoked.
                    var adapter = new DatRepositoryAdapter(log: null, toolRunner: null, cache: _cache);
                    _ = adapter.LoadDatPayload(datPath, hashType);
                    parsed++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    skipped++;
                    _log?.Invoke($"[DAT-Prewarm] {Path.GetFileName(datPath)} uebersprungen: {ex.Message}");
                }
            }

            // Rate-limited progress so the UI log stays readable for huge libraries.
            var now = DateTime.UtcNow;
            if ((now - lastReportUtc).TotalSeconds >= 5)
            {
                _log?.Invoke($"[DAT-Prewarm] {total} DAT(s) verarbeitet (Cache: {hits}, neu geparst: {parsed})");
                lastReportUtc = now;
            }
        }

        if (token.IsCancellationRequested)
        {
            _log?.Invoke($"[DAT-Prewarm] abgebrochen nach {total} DAT(s)");
            return;
        }

        var elapsed = (DateTime.UtcNow - startedUtc).TotalSeconds;
        _log?.Invoke($"[DAT-Prewarm] fertig in {elapsed:F1}s: {total} DAT(s) (Cache: {hits}, neu geparst: {parsed}, uebersprungen: {skipped})");
    }
}
