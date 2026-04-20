using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Periodic full-state ZIP snapshots for fast disaster recovery baseline (Phase I).
/// <para>
/// <b>Deliverables:</b>
/// <list type="bullet">
///   <item>Create ZIP of all entity directories with all locks held for consistency.</item>
///   <item>Retention: keep only the latest <see cref="JsonFileOptions.SnapshotRetentionCount"/> snapshots.</item>
///   <item>Restore single entity file from latest snapshot (<see cref="ISnapshotFallback"/>).</item>
///   <item>Config: <c>EnableSnapshots</c>, <c>SnapshotIntervalHours</c>, <c>SnapshotRetentionCount</c>.</item>
/// </list>
/// </para>
/// </summary>
internal sealed class SnapshotService : ISnapshotFallback
{
    internal const string SnapshotsDirectory = "_snapshots";
    internal const string FilePrefix = "snapshot_";
    internal const string FileExtension = ".zip";
    private const string DateFormat = "yyyyMMdd_HHmmss";

    private readonly IPersistenceFileSystem _fs;
    private readonly JsonFileOptions _options;
    private readonly DirectoryLockManager _lockManager;
    private readonly FlushQueue? _flushQueue;
    private readonly ILogger _logger;

    internal SnapshotService(
        IPersistenceFileSystem fs,
        JsonFileOptions options,
        DirectoryLockManager lockManager,
        ILogger<SnapshotService> logger,
        FlushQueue? flushQueue = null)
    {
        _fs = fs;
        _options = options;
        _lockManager = lockManager;
        _flushQueue = flushQueue;
        _logger = logger;
    }

    /// <summary>
    /// Creates a ZIP snapshot of all entity directories under <see cref="JsonFileOptions.DataDirectory"/>.
    /// Acquires all directory locks for consistency.
    /// </summary>
    /// <returns>Full path to the created snapshot file, or <c>null</c> if snapshots are disabled.</returns>
    internal async Task<string?> CreateSnapshotAsync(CancellationToken ct)
    {
        if (!_options.EnableSnapshots)
            return null;

        var dataDir = _options.DataDirectory;
        if (!_fs.DirectoryExists(dataDir))
            return null;

        var snapshotsDir = _fs.CombinePath(dataDir, SnapshotsDirectory);
        _fs.CreateDirectory(snapshotsDir);

        var timestamp = DateTimeOffset.UtcNow.UtcDateTime.ToString(DateFormat, System.Globalization.CultureInfo.InvariantCulture);
        var snapshotPath = _fs.CombinePath(snapshotsDir, $"{FilePrefix}{timestamp}{FileExtension}");

        // Phase K (RGAP-5): Drain pending async flushes before acquiring locks
        // so the snapshot captures the latest disk state.
        if (_flushQueue is not null)
            await _flushQueue.DrainAsync(ct);

        // Acquire all locks to ensure consistency during snapshot.
        await _lockManager.AcquireAllAsync(ct);
        try
        {
            // Build the ZIP in memory, then write atomically.
            using var ms = new MemoryStream();
            using (var archive = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
            {
                foreach (var entityDir in _fs.GetDirectories(dataDir))
                {
                    ct.ThrowIfCancellationRequested();
                    var dirName = Path.GetFileName(entityDir);

                    // Skip internal directories.
                    if (dirName.StartsWith('_'))
                        continue;

                    foreach (var file in _fs.GetFiles(entityDir, "*.json"))
                    {
                        ct.ThrowIfCancellationRequested();
                        var fileName = _fs.GetFileName(file);

                        // Skip internal files (checksums, indexes).
                        if (fileName.StartsWith('_'))
                            continue;

                        var entryName = $"{dirName}/{fileName}";
                        var entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);

                        using var owned = await _fs.ReadAllBytesAsync(file, ct);
                        await using var entryStream = entry.Open();
                        await entryStream.WriteAsync(owned.Memory, ct);
                    }
                }
            }

            ms.Position = 0;
            await _fs.WriteAllBytesAsync(snapshotPath, ms.ToArray(), ct);
            _logger.LogInformation("Created snapshot {Path}", snapshotPath);
        }
        finally
        {
            // Release all locks by disposing and re-creating is not viable;
            // AcquireAllAsync acquires existing semaphores, we need to release them.
            // The lock manager's AcquireAll/Dispose pattern is for shutdown only.
            // For snapshot, we release by disposing each lock individually.
            // Since AcquireAllAsync doesn't return disposables, we use a workaround:
            // We'll rely on the DirectoryLockManager releasing via a helper.
            ReleaseLocks();
        }

        EnforceRetention(snapshotsDir);
        return snapshotPath;
    }

    /// <summary>
    /// Gets the full path to the latest snapshot file, or <c>null</c> if none exists.
    /// </summary>
    internal string? GetLatestSnapshotPath()
    {
        var snapshotsDir = _fs.CombinePath(_options.DataDirectory, SnapshotsDirectory);
        if (!_fs.DirectoryExists(snapshotsDir))
            return null;

        return _fs.GetFiles(snapshotsDir, $"*{FileExtension}")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    /// <summary>
    /// Restores a single entity file from the latest snapshot (Phase F cross-ref).
    /// </summary>
    public async Task<bool> TryRestoreAsync(string entityFilePath, string dataDirectory, CancellationToken ct)
    {
        var latestSnapshot = GetLatestSnapshotPath();
        if (latestSnapshot is null || !_fs.FileExists(latestSnapshot))
            return false;

        // Derive the relative entry name from the entity file path.
        // entityFilePath: {dataDirectory}/{EntityType}/{id}.json → EntityType/id.json
        var relative = Path.GetRelativePath(dataDirectory, entityFilePath).Replace('\\', '/');

        try
        {
            using var owned = await _fs.ReadAllBytesAsync(latestSnapshot, ct);
            using var ms = new MemoryStream(owned.Span.ToArray());
            using var archive = new ZipArchive(ms, ZipArchiveMode.Read);

            var entry = archive.GetEntry(relative);
            if (entry is null)
                return false;

            await using var entryStream = entry.Open();
            using var buffer = new MemoryStream();
            await entryStream.CopyToAsync(buffer, ct);

            // Ensure directory exists.
            var dir = Path.GetDirectoryName(entityFilePath)!;
            _fs.CreateDirectory(dir);

            await _fs.WriteAllBytesAsync(entityFilePath, buffer.ToArray(), ct);
            _logger.LogWarning("Restored {File} from snapshot {Snapshot}", entityFilePath, latestSnapshot);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore {File} from snapshot", entityFilePath);
            return false;
        }
    }

    /// <summary>
    /// Enforces snapshot retention by deleting oldest snapshots beyond the configured count.
    /// </summary>
    internal void EnforceRetention(string? snapshotsDir = null)
    {
        snapshotsDir ??= _fs.CombinePath(_options.DataDirectory, SnapshotsDirectory);
        if (!_fs.DirectoryExists(snapshotsDir))
            return;

        var files = _fs.GetFiles(snapshotsDir, $"*{FileExtension}")
            .OrderByDescending(f => f, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var keep = Math.Max(1, _options.SnapshotRetentionCount);
        for (var i = keep; i < files.Count; i++)
        {
            try
            {
                _fs.DeleteFile(files[i]);
                _logger.LogInformation("Deleted old snapshot {Path}", files[i]);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to delete old snapshot {Path}", files[i]);
            }
        }
    }

    /// <summary>
    /// Releases all locks acquired by <see cref="DirectoryLockManager.AcquireAllAsync"/>.
    /// This is a workaround since AcquireAllAsync doesn't return disposables.
    /// We release each semaphore in the lock manager's internal dictionary.
    /// </summary>
    private void ReleaseLocks()
    {
        // The lock manager's AcquireAllAsync acquires all existing semaphores.
        // We need to release them. Since we can't access internals directly,
        // we use AcquireAsync for each known directory then immediately release.
        // Actually, we'll use a simpler approach: the snapshot holds the locks
        // for the duration of the ZIP creation. After creating, we need a way
        // to release. We'll add a ReleaseAll helper on DirectoryLockManager.
        _lockManager.ReleaseAll();
    }
}
