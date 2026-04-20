using System.Collections.Concurrent;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Provides per-directory exclusive locking using <see cref="SemaphoreSlim(1,1)"/>.
/// RGAP-3: <c>SemaphoreSlim</c> is fully async-safe, unlike
/// <c>ReaderWriterLockSlim</c> which has thread-affinity and cannot be used
/// with <c>async</c>/<c>await</c>.
/// </summary>
public sealed class DirectoryLockManager : IDisposable
{
    private readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new(StringComparer.OrdinalIgnoreCase);
    private volatile bool _disposed;

    /// <summary>
    /// Acquires the exclusive lock for the given directory path.
    /// Returns an <see cref="IDisposable"/> that releases the lock on dispose.
    /// </summary>
    public async Task<IDisposable> AcquireAsync(string directoryPath, CancellationToken ct = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var sem = _locks.GetOrAdd(directoryPath, _ => new SemaphoreSlim(1, 1));
        await sem.WaitAsync(ct);
        return new LockRelease(sem);
    }

    /// <summary>
    /// Acquires all known locks (for graceful shutdown).
    /// Must be called before disposing to ensure no in-flight I/O.
    /// </summary>
    public async Task AcquireAllAsync(CancellationToken ct = default)
    {
        // Snapshot keys to avoid modification during enumeration.
        foreach (var kvp in _locks.ToArray())
        {
            await kvp.Value.WaitAsync(ct);
        }
    }

    /// <summary>
    /// Releases all currently acquired locks without disposing the manager.
    /// Used by <see cref="SnapshotService"/> after snapshot creation.
    /// </summary>
    public void ReleaseAll()
    {
        foreach (var kvp in _locks.ToArray())
        {
            try { kvp.Value.Release(); } catch (SemaphoreFullException) { }
        }
    }

    /// <summary>
    /// Releases all acquired locks and marks the manager as disposed.
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        foreach (var kvp in _locks)
        {
            try { kvp.Value.Release(); } catch (SemaphoreFullException) { }
            kvp.Value.Dispose();
        }

        _locks.Clear();
    }

    private readonly struct LockRelease(SemaphoreSlim semaphore) : IDisposable
    {
        public void Dispose() => semaphore.Release();
    }
}
