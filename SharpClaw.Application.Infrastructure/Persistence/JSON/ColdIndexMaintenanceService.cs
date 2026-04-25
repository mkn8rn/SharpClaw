using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Persistence;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Periodically rebuilds cold entity indexes to self-heal any drift caused by
/// unclean shutdowns, manual file edits, or partial transaction failures.
/// <para>
/// Uses <see cref="PeriodicTimer"/> in a background loop. Logs a duration
/// warning when a rebuild cycle exceeds 30 seconds.
/// </para>
/// </summary>
internal sealed class ColdIndexMaintenanceService : IDisposable
{
    private static readonly TimeSpan SlowRebuildThreshold = TimeSpan.FromSeconds(30);

    private readonly JsonFileOptions _options;
    private readonly EncryptionOptions _encryptionOptions;
    private readonly IPersistenceFileSystem _fs;
    private readonly DirectoryLockManager? _directoryLockManager;
    private readonly ILogger<ColdIndexMaintenanceService> _logger;
    private readonly ModuleColdIndexRegistry _coldIndexRegistry;
    private readonly CancellationTokenSource _cts = new();
    private Task? _loopTask;

    public ColdIndexMaintenanceService(
        JsonFileOptions options,
        EncryptionOptions encryptionOptions,
        IPersistenceFileSystem fs,
        ILogger<ColdIndexMaintenanceService> logger,
        DirectoryLockManager? directoryLockManager = null,
        IEnumerable<IModuleColdIndexContributor>? coldIndexContributors = null)
    {
        _options = options;
        _encryptionOptions = encryptionOptions;
        _fs = fs;
        _logger = logger;
        _directoryLockManager = directoryLockManager;
        _coldIndexRegistry = new ModuleColdIndexRegistry(coldIndexContributors ?? []);
    }

    /// <summary>
    /// Starts the periodic rescan loop. If <paramref name="intervalMinutes"/> is
    /// <c>0</c>, the service is not started (startup-only mode).
    /// </summary>
    public void Start(int intervalMinutes)
    {
        if (intervalMinutes <= 0)
        {
            _logger.LogDebug("Cold index maintenance disabled (interval=0)");
            return;
        }

        var interval = TimeSpan.FromMinutes(intervalMinutes);
        _logger.LogInformation("Cold index maintenance started (interval={Interval}min)", intervalMinutes);
        _loopTask = RunLoopAsync(interval, _cts.Token);
    }

    private async Task RunLoopAsync(TimeSpan interval, CancellationToken ct)
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(ct))
            {
                await RebuildAsync(ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            // Normal shutdown
        }
    }

    /// <summary>
    /// Executes a single rebuild cycle. Exposed for testing.
    /// </summary>
    internal async Task RebuildAsync(CancellationToken ct)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            var tasks = new List<Task>();
            foreach (var coldType in _options.ColdEntityTypes)
            {
                var entityDir = _fs.CombinePath(_options.DataDirectory, coldType.Name);
                if (!_fs.DirectoryExists(entityDir))
                    continue;

                // Acquire directory lock if available
                tasks.Add(RebuildTypeAsync(coldType, entityDir, ct));
            }

            if (tasks.Count > 0)
                await Task.WhenAll(tasks);

            sw.Stop();
            if (sw.Elapsed > SlowRebuildThreshold)
            {
                _logger.LogWarning(
                    "Cold index rebuild took {Elapsed:F1}s (threshold: {Threshold}s)",
                    sw.Elapsed.TotalSeconds, SlowRebuildThreshold.TotalSeconds);
            }
            else
            {
                _logger.LogDebug("Cold index rebuild completed in {Elapsed:F1}s", sw.Elapsed.TotalSeconds);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Cold index rebuild failed after {Elapsed:F1}s", sw.Elapsed.TotalSeconds);
        }
    }

    private async Task RebuildTypeAsync(Type coldType, string entityDir, CancellationToken ct)
    {
        using var _ = _directoryLockManager is not null
            ? await _directoryLockManager.AcquireAsync(entityDir, ct)
            : null;

        // Phase J: Full-scan checksum verification before index rebuild.
        if (_options.EnableChecksums)
        {
            var mismatched = await ChecksumManifest.VerifyAllAsync(
                _fs, entityDir, _encryptionOptions.Key, _logger, ct);

            foreach (var filePath in mismatched)
            {
                _logger.LogWarning("Checksum mismatch — quarantining {Path}", filePath);
                QuarantineService.MoveToQuarantine(_fs, filePath, entityDir, _logger);
            }

            if (mismatched.Count > 0)
            {
                _logger.LogWarning(
                    "Quarantined {Count} file(s) with checksum mismatches in {Dir}",
                    mismatched.Count, entityDir);
            }
        }

        await ColdEntityIndex.RebuildIndexAsync(
            _fs, entityDir, coldType.Name, coldType,
            _encryptionOptions.Key, ColdEntityStore.JsonOptions,
            _logger, ct, _coldIndexRegistry.GetIndexedProperties());
    }

    private int _disposed;

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        try { _cts.Cancel(); } catch (ObjectDisposedException) { }
        _cts.Dispose();
    }
}
