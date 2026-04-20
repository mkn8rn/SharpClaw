namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Provides snapshot-based entity restoration for the quarantine retry chain (Phase I / Phase F cross-ref).
/// <para>
/// When <see cref="QuarantineService.ReadBytesWithRetryAsync"/> exhausts all retries, it invokes
/// <see cref="TryRestoreAsync"/> before quarantining. If the latest snapshot contains the entity file,
/// it is restored to the original path and the read is reattempted.
/// </para>
/// </summary>
public interface ISnapshotFallback
{
    /// <summary>
    /// Attempts to restore a single entity file from the latest snapshot.
    /// </summary>
    /// <param name="entityFilePath">Full path to the missing/corrupt entity JSON file.</param>
    /// <param name="dataDirectory">Root data directory.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns><c>true</c> if the file was successfully restored; <c>false</c> otherwise.</returns>
    Task<bool> TryRestoreAsync(string entityFilePath, string dataDirectory, CancellationToken ct);
}
