using System.Buffers;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Abstracts file-system operations used by the JSON persistence layer.
/// <para>
/// Read methods return <see cref="IMemoryOwner{T}"/> backed by
/// <see cref="ArrayPool{T}.Shared"/> so callers can deserialize from a
/// pooled buffer and return it via <see cref="IDisposable.Dispose"/>,
/// eliminating per-read <c>byte[]</c> allocations.
/// </para>
/// <para>
/// Named <c>IPersistenceFileSystem</c> (not <c>IFileSystem</c>) to
/// avoid ambiguity with <c>System.IO.Abstractions.IFileSystem</c>.
/// </para>
/// </summary>
public interface IPersistenceFileSystem
{
    /// <summary>
    /// Reads the entire contents of a file into a pooled buffer.
    /// The caller must dispose the returned <see cref="IMemoryOwner{T}"/>
    /// to return the buffer to the pool. The <see cref="OwnedMemory.Length"/>
    /// property gives the actual byte count (the underlying array may be larger).
    /// </summary>
    Task<OwnedMemory> ReadAllBytesAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Writes raw bytes to a file, creating or overwriting it.
    /// </summary>
    Task WriteAllBytesAsync(string path, ReadOnlyMemory<byte> data, CancellationToken ct = default);

    /// <summary>
    /// Writes a UTF-8 string to a file, creating or overwriting it.
    /// </summary>
    Task WriteAllTextAsync(string path, string text, CancellationToken ct = default);

    /// <summary>
    /// Reads the entire file as a UTF-8 string.
    /// </summary>
    Task<string> ReadAllTextAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Returns <c>true</c> if the file exists.
    /// </summary>
    bool FileExists(string path);

    /// <summary>
    /// Deletes a file. No-op if the file does not exist.
    /// </summary>
    void DeleteFile(string path);

    /// <summary>
    /// Moves (renames) a file. Overwrites the destination if it exists.
    /// </summary>
    void MoveFile(string sourcePath, string destinationPath, bool overwrite = true);

    /// <summary>
    /// Returns <c>true</c> if the directory exists.
    /// </summary>
    bool DirectoryExists(string path);

    /// <summary>
    /// Creates a directory (and all parent directories) if it does not exist.
    /// </summary>
    void CreateDirectory(string path);

    /// <summary>
    /// Returns the full paths of files in a directory matching the search pattern.
    /// </summary>
    string[] GetFiles(string directoryPath, string searchPattern);

    /// <summary>
    /// Returns the full paths of subdirectories in a directory.
    /// </summary>
    string[] GetDirectories(string directoryPath);

    /// <summary>
    /// Combines path segments using the platform separator.
    /// </summary>
    string CombinePath(params ReadOnlySpan<string> segments);

    /// <summary>
    /// Returns the file name (with extension) from a path.
    /// </summary>
    string GetFileName(string path);

    /// <summary>
    /// Flushes a file's contents and metadata to durable storage (fsync).
    /// No-op for in-memory implementations.
    /// </summary>
    Task FlushFileAsync(string path, CancellationToken ct = default);

    /// <summary>
    /// Appends UTF-8 text to a file, creating it if it does not exist.
    /// </summary>
    Task AppendAllTextAsync(string path, string text, CancellationToken ct = default);
}

/// <summary>
/// A pool-backed byte buffer with an explicit length.
/// The underlying array (from <see cref="ArrayPool{T}.Shared"/>) may be
/// larger than <see cref="Length"/>; only the first <see cref="Length"/>
/// bytes are valid. Dispose returns the buffer to the pool.
/// </summary>
public sealed class OwnedMemory : IDisposable
{
    private byte[]? _array;
    private readonly bool _pooled;

    /// <summary>Actual byte count of valid data.</summary>
    public int Length { get; }

    /// <summary>The valid data as a <see cref="ReadOnlyMemory{T}"/>.</summary>
    public ReadOnlyMemory<byte> Memory => _array.AsMemory(0, Length);

    /// <summary>The valid data as a <see cref="ReadOnlySpan{T}"/>.</summary>
    public ReadOnlySpan<byte> Span => _array.AsSpan(0, Length);

    /// <summary>
    /// Creates a pooled <see cref="OwnedMemory"/>. The array will be
    /// returned to <see cref="ArrayPool{T}.Shared"/> on dispose.
    /// </summary>
    public OwnedMemory(byte[] array, int length)
    {
        _array = array;
        Length = length;
        _pooled = true;
    }

    private OwnedMemory(byte[] array, int length, bool pooled)
    {
        _array = array;
        Length = length;
        _pooled = pooled;
    }

    /// <summary>
    /// Creates an <see cref="OwnedMemory"/> that wraps an existing array
    /// without pooling — the array is NOT returned on dispose.
    /// Used by <see cref="InMemoryPersistenceFileSystem"/> and tests.
    /// </summary>
    public static OwnedMemory WrapUnpooled(byte[] data)
        => new(data, data.Length, pooled: false);

    public void Dispose()
    {
        var arr = Interlocked.Exchange(ref _array, null);
        if (arr is not null && _pooled)
            ArrayPool<byte>.Shared.Return(arr);
    }
}
