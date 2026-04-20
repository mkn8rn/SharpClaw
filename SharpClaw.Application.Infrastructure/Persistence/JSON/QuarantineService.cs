using Microsoft.Extensions.Logging;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Handles quarantine and auto-purge of corrupt or unreadable entity files.
/// <para>
/// <b>Phase F deliverables:</b>
/// <list type="bullet">
///   <item><b>RGAP-14:</b> Exponential backoff retry (3 attempts: 100 ms → 500 ms → 2 s)
///         before quarantine. Handles transient I/O failures such as antivirus locks
///         or network storage hiccups.</item>
///   <item>Move corrupt files to <c>_quarantine/{entityType}/</c> with a timestamp suffix.</item>
///   <item>Startup purge of quarantined files older than <see cref="JsonFileOptions.QuarantineMaxAgeDays"/>.</item>
/// </list>
/// </para>
/// </summary>
internal static class QuarantineService
{
    /// <summary>
    /// Quarantine subdirectory name, placed inside each entity-type directory.
    /// </summary>
    internal const string QuarantineDir = "_quarantine";

    /// <summary>
    /// Exponential backoff delays between retry attempts (RGAP-14).
    /// Three attempts total: immediate → 100 ms → 500 ms → quarantine.
    /// </summary>
    private static readonly int[] RetryDelaysMs = [100, 500, 2000];

    /// <summary>
    /// Reads a file with exponential backoff retries for transient I/O errors.
    /// If all retries fail, the file is quarantined and a <see cref="ReadResult{T}"/>
    /// error variant is returned instead of throwing.
    /// </summary>
    /// <param name="fs">File system abstraction.</param>
    /// <param name="path">Full path to the entity JSON file.</param>
    /// <param name="entityDir">The entity-type directory (for quarantine placement).</param>
    /// <param name="key">AES-256-GCM decryption key.</param>
    /// <param name="logger">Logger for warnings and errors.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <param name="snapshotFallback">
    /// Optional snapshot fallback (Phase I). When non-null and all retries are exhausted,
    /// the service attempts to restore the entity file from the latest snapshot before
    /// quarantining. On successful restore, the read is reattempted once.
    /// </param>
    /// <param name="dataDirectory">
    /// Root data directory, required when <paramref name="snapshotFallback"/> is non-null.
    /// </param>
    /// <returns>
    /// An <see cref="OwnedMemory"/> on success, or <c>null</c> with a quarantine
    /// action performed when the file is unrecoverable.
    /// </returns>
    internal static async Task<ReadBytesResult> ReadBytesWithRetryAsync(
        IPersistenceFileSystem fs,
        string path,
        string entityDir,
        byte[] key,
        ILogger logger,
        CancellationToken ct,
        ISnapshotFallback? snapshotFallback = null,
        string? dataDirectory = null)
    {
        Exception lastException;

        // First attempt (immediate, no delay).
        try
        {
            var owned = await JsonFileEncryption.ReadBytesAsync(fs, path, key, ct);
            return ReadBytesResult.Ok(owned);
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            lastException = ex;
            logger.LogWarning(ex, "Read attempt 1/3 failed for {Path}", path);
        }

        // Retry attempts with exponential backoff.
        for (var i = 0; i < RetryDelaysMs.Length; i++)
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(RetryDelaysMs[i], ct);

            try
            {
                var owned = await JsonFileEncryption.ReadBytesAsync(fs, path, key, ct);
                logger.LogInformation("Read succeeded on retry {Attempt}/3 for {Path}", i + 2, path);
                return ReadBytesResult.Ok(owned);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                lastException = ex;
                logger.LogWarning(ex, "Read attempt {Attempt}/3 failed for {Path}", i + 2, path);
            }
        }

        // Phase I: Attempt snapshot restoration before quarantining.
        if (snapshotFallback is not null && dataDirectory is not null)
        {
            try
            {
                var restored = await snapshotFallback.TryRestoreAsync(path, dataDirectory, ct);
                if (restored)
                {
                    // Reattempt read after restoration.
                    try
                    {
                        var owned = await JsonFileEncryption.ReadBytesAsync(fs, path, key, ct);
                        logger.LogWarning("Read succeeded after snapshot restoration for {Path}", path);
                        return ReadBytesResult.Ok(owned);
                    }
                    catch (Exception ex)
                    {
                        lastException = ex;
                        logger.LogError(ex, "Read failed even after snapshot restoration for {Path}", path);
                    }
                }
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Snapshot fallback failed for {Path}", path);
            }
        }

        // All retries (and snapshot fallback) exhausted — quarantine the file.
        var isIoError = lastException is IOException or UnauthorizedAccessException;
        MoveToQuarantine(fs, path, entityDir, logger);

        return isIoError
            ? ReadBytesResult.Io(lastException, path)
            : ReadBytesResult.Corrupt(lastException, path);
    }

    /// <summary>
    /// Moves a corrupt or unreadable file into the quarantine directory with
    /// a timestamp suffix to prevent overwriting previous quarantined versions.
    /// </summary>
    internal static void MoveToQuarantine(
        IPersistenceFileSystem fs, string filePath, string entityDir, ILogger logger)
    {
        try
        {
            var quarantineDir = fs.CombinePath(entityDir, QuarantineDir);
            fs.CreateDirectory(quarantineDir);

            var fileName = fs.GetFileName(filePath);
            var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
            var quarantinedName = $"{Path.GetFileNameWithoutExtension(fileName)}_{timestamp}{Path.GetExtension(fileName)}";
            var quarantinedPath = fs.CombinePath(quarantineDir, quarantinedName);

            fs.MoveFile(filePath, quarantinedPath);
            logger.LogWarning("Quarantined corrupt file {Source} → {Destination}", filePath, quarantinedPath);
        }
        catch (Exception ex)
        {
            // If quarantine itself fails, log and delete the original file
            // to prevent repeated failures on every startup cycle.
            logger.LogError(ex, "Failed to quarantine {Path}; deleting to prevent loop", filePath);
            try { fs.DeleteFile(filePath); }
            catch { /* best effort */ }
        }
    }

    /// <summary>
    /// Purges quarantined files older than <paramref name="maxAgeDays"/> across
    /// all entity-type directories. Called during startup.
    /// </summary>
    /// <param name="fs">File system abstraction.</param>
    /// <param name="dataDirectory">Root data directory.</param>
    /// <param name="maxAgeDays">
    /// Maximum age in days. Files older than this are deleted.
    /// <c>0</c> means keep forever (no purge).
    /// </param>
    /// <param name="logger">Logger for purge activity.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>The number of files purged.</returns>
    internal static int PurgeExpiredQuarantineFiles(
        IPersistenceFileSystem fs,
        string dataDirectory,
        int maxAgeDays,
        ILogger logger,
        CancellationToken ct)
    {
        if (maxAgeDays <= 0 || !fs.DirectoryExists(dataDirectory))
            return 0;

        var cutoff = DateTimeOffset.UtcNow.AddDays(-maxAgeDays);
        var purged = 0;

        foreach (var entityDir in fs.GetDirectories(dataDirectory))
        {
            ct.ThrowIfCancellationRequested();

            var quarantineDir = fs.CombinePath(entityDir, QuarantineDir);
            if (!fs.DirectoryExists(quarantineDir))
                continue;

            foreach (var file in fs.GetFiles(quarantineDir, "*.json"))
            {
                ct.ThrowIfCancellationRequested();

                // Parse timestamp from filename: {id}_{yyyyMMdd_HHmmss_fff}.json
                if (TryParseQuarantineTimestamp(fs.GetFileName(file), out var timestamp) &&
                    timestamp < cutoff)
                {
                    try
                    {
                        fs.DeleteFile(file);
                        purged++;
                    }
                    catch (Exception ex)
                    {
                        logger.LogWarning(ex, "Failed to purge quarantined file {Path}", file);
                    }
                }
            }
        }

        if (purged > 0)
            logger.LogInformation("Purged {Count} expired quarantined file(s)", purged);

        return purged;
    }

    /// <summary>
    /// Attempts to parse the quarantine timestamp from a filename.
    /// Expected format: <c>{guid}_{yyyyMMdd_HHmmss_fff}.json</c>.
    /// </summary>
    internal static bool TryParseQuarantineTimestamp(string fileName, out DateTimeOffset timestamp)
    {
        timestamp = default;

        // Strip extension: "{guid}_{yyyyMMdd_HHmmss_fff}"
        var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
        if (nameWithoutExt is null)
            return false;

        // The timestamp is the last 19 chars: "yyyyMMdd_HHmmss_fff"
        const int timestampLength = 19; // "20250101_120000_000"
        if (nameWithoutExt.Length < timestampLength + 1) // +1 for separator '_'
            return false;

        var timestampStr = nameWithoutExt[^timestampLength..];
        return DateTimeOffset.TryParseExact(
            timestampStr,
            "yyyyMMdd_HHmmss_fff",
            System.Globalization.CultureInfo.InvariantCulture,
            System.Globalization.DateTimeStyles.AssumeUniversal,
            out timestamp);
    }

    /// <summary>
    /// Internal result type for <see cref="ReadBytesWithRetryAsync"/>
    /// to convey success/failure without allocating a full <see cref="ReadResult{T}"/>.
    /// </summary>
    internal readonly struct ReadBytesResult : IDisposable
    {
        public readonly OwnedMemory? Data;
        public readonly Exception? Exception;
        public readonly string? FilePath;
        public readonly ReadBytesOutcome Outcome;

        private ReadBytesResult(OwnedMemory? data, Exception? ex, string? path, ReadBytesOutcome outcome)
        {
            Data = data;
            Exception = ex;
            FilePath = path;
            Outcome = outcome;
        }

        internal static ReadBytesResult Ok(OwnedMemory data) => new(data, null, null, ReadBytesOutcome.Success);
        internal static ReadBytesResult Corrupt(Exception ex, string path) => new(null, ex, path, ReadBytesOutcome.Corrupted);
        internal static ReadBytesResult Io(Exception ex, string path) => new(null, ex, path, ReadBytesOutcome.IoError);

        public bool IsSuccess => Outcome == ReadBytesOutcome.Success;

        public void Dispose() => Data?.Dispose();
    }

    internal enum ReadBytesOutcome { Success, Corrupted, IoError }
}
