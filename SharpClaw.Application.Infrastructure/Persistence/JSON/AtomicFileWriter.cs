using System.Text;

namespace SharpClaw.Infrastructure.Persistence.JSON;

/// <summary>
/// Performs crash-safe writes using the write-to-tmp + optional fsync + atomic rename pattern.
/// No partial writes ever reach the final filename.
/// </summary>
internal static class AtomicFileWriter
{
    /// <summary>
    /// Atomically writes raw bytes to <paramref name="path"/> via a temporary file.
    /// </summary>
    public static async Task WriteAsync(
        IPersistenceFileSystem fs, string path, ReadOnlyMemory<byte> data,
        bool fsync, CancellationToken ct = default)
    {
        var tmpPath = path + ".tmp";

        await fs.WriteAllBytesAsync(tmpPath, data, ct);

        if (fsync)
            await fs.FlushFileAsync(tmpPath, ct);

        fs.MoveFile(tmpPath, path, overwrite: true);
    }

    /// <summary>
    /// Atomically writes a UTF-8 string to <paramref name="path"/> via a temporary file.
    /// </summary>
    public static async Task WriteTextAsync(
        IPersistenceFileSystem fs, string path, string text,
        bool fsync, CancellationToken ct = default)
    {
        var data = Encoding.UTF8.GetBytes(text);
        await WriteAsync(fs, path, data, fsync, ct);
    }
}
