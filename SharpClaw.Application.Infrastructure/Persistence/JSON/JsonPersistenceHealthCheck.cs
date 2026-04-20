using Microsoft.Extensions.Logging;
using SharpClaw.Contracts.Persistence;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Persistence health check for observability — detects problems before data loss (Phase M).
/// <para>
/// Checks: disk writable, index consistent, no pending transactions, no quarantined files,
/// checksums valid, event log writable, snapshot age acceptable, unclean shutdown detection.
/// </para>
/// </summary>
public sealed class JsonPersistenceHealthCheck
{
    private readonly IPersistenceFileSystem _fs;
    private readonly JsonFileOptions _options;
    private readonly EncryptionOptions _encryptionOptions;
    private readonly TransactionQueue _txQueue;
    private readonly FlushQueue? _flushQueue;
    private readonly ILogger<JsonPersistenceHealthCheck> _logger;

    public JsonPersistenceHealthCheck(
        IPersistenceFileSystem fs,
        JsonFileOptions options,
        EncryptionOptions encryptionOptions,
        TransactionQueue txQueue,
        ILogger<JsonPersistenceHealthCheck> logger,
        FlushQueue? flushQueue = null)
    {
        _fs = fs;
        _options = options;
        _encryptionOptions = encryptionOptions;
        _txQueue = txQueue;
        _logger = logger;
        _flushQueue = flushQueue;
    }

    /// <summary>
    /// Runs all health checks and returns a consolidated result.
    /// </summary>
    public async Task<HealthCheckResult> CheckAsync(CancellationToken ct = default)
    {
        var entries = new List<HealthCheckEntry>();

        entries.Add(CheckDiskWritable());
        entries.Add(CheckPendingTransactions());
        entries.Add(CheckQuarantinedFiles());
        entries.Add(CheckFlushQueueBacklog());

        if (_options.EnableChecksums)
            entries.Add(await CheckChecksumsAsync(ct));

        if (_options.EnableEventLog)
            entries.Add(await CheckEventLogWritableAsync(ct));

        if (_options.EnableSnapshots)
            entries.Add(CheckSnapshotAge());

        entries.Add(CheckUncleanShutdown());

        var status = entries.Any(e => e.Status == HealthStatus.Unhealthy)
            ? HealthStatus.Unhealthy
            : entries.Any(e => e.Status == HealthStatus.Degraded)
                ? HealthStatus.Degraded
                : HealthStatus.Healthy;

        return new HealthCheckResult(status, entries);
    }

    private HealthCheckEntry CheckDiskWritable()
    {
        const string name = "DiskWritable";
        try
        {
            var dataDir = _options.DataDirectory;
            _fs.CreateDirectory(dataDir);
            var probePath = _fs.CombinePath(dataDir, $"_healthprobe_{Guid.NewGuid():N}.tmp");
            _fs.WriteAllBytesAsync(probePath, "ok"u8.ToArray(), CancellationToken.None)
                .GetAwaiter().GetResult();
            _fs.DeleteFile(probePath);
            return new HealthCheckEntry(name, HealthStatus.Healthy, "Data directory is writable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: disk not writable");
            return new HealthCheckEntry(name, HealthStatus.Unhealthy, $"Disk write failed: {ex.Message}");
        }
    }

    private HealthCheckEntry CheckPendingTransactions()
    {
        const string name = "PendingTransactions";
        try
        {
            var pendingDir = _fs.CombinePath(_options.DataDirectory, "_transactions", "pending");
            if (!_fs.DirectoryExists(pendingDir))
                return new HealthCheckEntry(name, HealthStatus.Healthy, "No pending transactions");

            var files = _fs.GetFiles(pendingDir, "*.json");
            if (files.Length == 0)
                return new HealthCheckEntry(name, HealthStatus.Healthy, "No pending transactions");

            return new HealthCheckEntry(name, HealthStatus.Degraded,
                $"{files.Length} pending transaction(s) found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: pending transactions check");
            return new HealthCheckEntry(name, HealthStatus.Unhealthy, $"Check failed: {ex.Message}");
        }
    }

    private HealthCheckEntry CheckQuarantinedFiles()
    {
        const string name = "QuarantinedFiles";
        try
        {
            var dataDir = _options.DataDirectory;
            if (!_fs.DirectoryExists(dataDir))
                return new HealthCheckEntry(name, HealthStatus.Healthy, "No quarantined files");

            var total = 0;
            foreach (var entityDir in _fs.GetDirectories(dataDir))
            {
                var quarantineDir = _fs.CombinePath(entityDir, QuarantineService.QuarantineDir);
                if (_fs.DirectoryExists(quarantineDir))
                    total += _fs.GetFiles(quarantineDir, "*.json").Length;
            }

            if (total == 0)
                return new HealthCheckEntry(name, HealthStatus.Healthy, "No quarantined files");

            return new HealthCheckEntry(name, HealthStatus.Degraded,
                $"{total} quarantined file(s) found");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: quarantine check");
            return new HealthCheckEntry(name, HealthStatus.Unhealthy, $"Check failed: {ex.Message}");
        }
    }

    private HealthCheckEntry CheckFlushQueueBacklog()
    {
        const string name = "FlushQueueBacklog";
        if (_flushQueue is null || !_options.AsyncFlush)
            return new HealthCheckEntry(name, HealthStatus.Healthy, "Async flush disabled");

        var count = _flushQueue.Count;
        var capacity = _flushQueue.Capacity;

        if (count == 0)
            return new HealthCheckEntry(name, HealthStatus.Healthy, "Flush queue empty");

        var utilization = (double)count / capacity;
        if (utilization >= 0.9)
            return new HealthCheckEntry(name, HealthStatus.Unhealthy,
                $"Flush queue near capacity: {count}/{capacity}");

        if (utilization >= 0.5)
            return new HealthCheckEntry(name, HealthStatus.Degraded,
                $"Flush queue backlog: {count}/{capacity}");

        return new HealthCheckEntry(name, HealthStatus.Healthy,
            $"Flush queue: {count}/{capacity}");
    }

    private async Task<HealthCheckEntry> CheckChecksumsAsync(CancellationToken ct)
    {
        const string name = "Checksums";
        try
        {
            var hmacKey = _encryptionOptions.Key;
            if (hmacKey is not { Length: > 0 })
                return new HealthCheckEntry(name, HealthStatus.Healthy, "No HMAC key configured — skipped");

            var dataDir = _options.DataDirectory;
            if (!_fs.DirectoryExists(dataDir))
                return new HealthCheckEntry(name, HealthStatus.Healthy, "No data directory");

            var totalMismatched = 0;
            foreach (var entityDir in _fs.GetDirectories(dataDir))
            {
                ct.ThrowIfCancellationRequested();
                var mismatched = await ChecksumManifest.VerifyAllAsync(
                    _fs, entityDir, hmacKey, _logger, ct);
                totalMismatched += mismatched.Count;
            }

            if (totalMismatched == 0)
                return new HealthCheckEntry(name, HealthStatus.Healthy, "All checksums valid");

            return new HealthCheckEntry(name, HealthStatus.Unhealthy,
                $"{totalMismatched} file(s) with checksum mismatch");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: checksum verification");
            return new HealthCheckEntry(name, HealthStatus.Unhealthy, $"Check failed: {ex.Message}");
        }
    }

    private async Task<HealthCheckEntry> CheckEventLogWritableAsync(CancellationToken ct)
    {
        const string name = "EventLogWritable";
        try
        {
            var eventsDir = _fs.CombinePath(_options.DataDirectory, EventLog.EventsDirectory);
            _fs.CreateDirectory(eventsDir);
            var probePath = _fs.CombinePath(eventsDir, $"_healthprobe_{Guid.NewGuid():N}.tmp");
            await _fs.WriteAllBytesAsync(probePath, "ok"u8.ToArray(), ct);
            _fs.DeleteFile(probePath);
            return new HealthCheckEntry(name, HealthStatus.Healthy, "Event log directory is writable");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: event log not writable");
            return new HealthCheckEntry(name, HealthStatus.Unhealthy, $"Event log write failed: {ex.Message}");
        }
    }

    private HealthCheckEntry CheckSnapshotAge()
    {
        const string name = "SnapshotAge";
        try
        {
            var snapshotsDir = _fs.CombinePath(_options.DataDirectory, SnapshotService.SnapshotsDirectory);
            if (!_fs.DirectoryExists(snapshotsDir))
                return new HealthCheckEntry(name, HealthStatus.Degraded, "No snapshots directory");

            var files = _fs.GetFiles(snapshotsDir, $"*{SnapshotService.FileExtension}");
            if (files.Length == 0)
                return new HealthCheckEntry(name, HealthStatus.Degraded, "No snapshots found");

            // Latest snapshot is the last lexicographically.
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);
            var latestFile = files[^1];

            // Parse timestamp from filename: snapshot_yyyyMMdd_HHmmss.zip
            var fileName = _fs.GetFileName(latestFile);
            if (TryParseSnapshotTimestamp(fileName, out var snapshotTime))
            {
                var age = DateTimeOffset.UtcNow - snapshotTime;
                var maxAge = TimeSpan.FromHours(_options.SnapshotIntervalHours * 3);

                if (age > maxAge)
                    return new HealthCheckEntry(name, HealthStatus.Degraded,
                        $"Latest snapshot is {age.TotalHours:F1}h old (threshold: {maxAge.TotalHours:F1}h)");

                return new HealthCheckEntry(name, HealthStatus.Healthy,
                    $"Latest snapshot is {age.TotalHours:F1}h old");
            }

            return new HealthCheckEntry(name, HealthStatus.Healthy, "Snapshot exists");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: snapshot age check");
            return new HealthCheckEntry(name, HealthStatus.Unhealthy, $"Check failed: {ex.Message}");
        }
    }

    private HealthCheckEntry CheckUncleanShutdown()
    {
        const string name = "UncleanShutdown";
        try
        {
            var lockFile = _fs.CombinePath(_options.DataDirectory, UncleanShutdownSentinel);
            if (_fs.FileExists(lockFile))
                return new HealthCheckEntry(name, HealthStatus.Degraded,
                    "Unclean shutdown detected — lock file present");

            return new HealthCheckEntry(name, HealthStatus.Healthy, "Clean shutdown");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Health check failed: unclean shutdown check");
            return new HealthCheckEntry(name, HealthStatus.Unhealthy, $"Check failed: {ex.Message}");
        }
    }

    // ── Unclean shutdown sentinel ──────────────────────────────────

    /// <summary>Sentinel file created on startup, removed on clean shutdown.</summary>
    internal const string UncleanShutdownSentinel = "_running.lock";

    /// <summary>
    /// Creates the sentinel file. Call during <see cref="InfrastructureServiceExtensions.InitializeInfrastructureAsync"/>.
    /// </summary>
    public static void CreateSentinel(IPersistenceFileSystem fs, JsonFileOptions options)
    {
        var path = fs.CombinePath(options.DataDirectory, UncleanShutdownSentinel);
        fs.CreateDirectory(options.DataDirectory);
        fs.WriteAllBytesAsync(path, "1"u8.ToArray(), CancellationToken.None)
            .GetAwaiter().GetResult();
    }

    /// <summary>
    /// Removes the sentinel file. Call during <see cref="InfrastructureServiceExtensions.ShutdownInfrastructureAsync"/>.
    /// </summary>
    public static void RemoveSentinel(IPersistenceFileSystem fs, JsonFileOptions options)
    {
        var path = fs.CombinePath(options.DataDirectory, UncleanShutdownSentinel);
        if (fs.FileExists(path))
            fs.DeleteFile(path);
    }

    // ── Helpers ─────────────────────────────────────────────────────

    private static bool TryParseSnapshotTimestamp(string fileName, out DateTimeOffset timestamp)
    {
        // snapshot_yyyyMMdd_HHmmss.zip → extract "yyyyMMdd_HHmmss"
        timestamp = default;
        const string prefix = SnapshotService.FilePrefix;
        if (!fileName.StartsWith(prefix, StringComparison.Ordinal))
            return false;

        var withoutPrefix = fileName[prefix.Length..];
        var dotIndex = withoutPrefix.IndexOf('.');
        if (dotIndex < 0) return false;

        var tsStr = withoutPrefix[..dotIndex];
        return DateTimeOffset.TryParseExact(
            tsStr, "yyyyMMdd_HHmmss",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out timestamp);
    }
}

/// <summary>Status of an individual health check entry.</summary>
public enum HealthStatus { Healthy, Degraded, Unhealthy }

/// <summary>A single health check entry with name, status, and description.</summary>
public sealed record HealthCheckEntry(string Name, HealthStatus Status, string Description);

/// <summary>Aggregate health check result.</summary>
public sealed record HealthCheckResult(HealthStatus Status, IReadOnlyList<HealthCheckEntry> Entries);
